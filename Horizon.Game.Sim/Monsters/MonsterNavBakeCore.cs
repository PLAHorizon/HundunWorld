using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Horizon.Game.Sim.Monsters;

/// <summary>烘焙结果。</summary>
public sealed class MonsterNavBakeResult
{
    /// <summary>可直接落盘的网格文本（与 <c>MonsterNavGridLoader</c> 同一格式）。</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>可走格数。</summary>
    public int WalkableCells { get; set; }

    /// <summary>总格数（= Width × Height）。</summary>
    public int TotalCells { get; set; }

    /// <summary>宽度（格）。</summary>
    public int Width { get; set; }

    /// <summary>高度（格）。</summary>
    public int Height { get; set; }

    /// <summary>问题清单（非致命但必须让人看见的事，比如"整张图全阻塞"）。</summary>
    public List<string> Problems { get; } = new List<string>();

    /// <summary>可走比例（诊断与断言用）。</summary>
    public float WalkableRatio => TotalCells > 0 ? (float)WalkableCells / TotalCells : 0f;
}

/// <summary>
/// 导航网格烘焙（把"逐格能不能走"的采样结果装配成服务端那份 <c>MONSTERNAV</c> 文件）。
/// <para>
/// <b>为什么这段数学放在 <c>Horizon.Game.Sim</c> 而不是插件里</b>：文件格式的读写两侧必须逐字节同源——
/// 读侧是 <c>MonsterNavGridLoader</c>（<c>Horizon.Game.Core</c>），写侧若各自拼字符串，
/// 漂掉一个空格的表现是"文件写得出来、加载时安静地报解析失败"，而编辑器里只会看到一句红字。
/// 采样（问引擎"这一点能不能走"）留在客户端插件，<b>装配</b>留在这里：
/// 这样"掩码 → 网格"的口径可以离开门禁跑不到的进程真跑断言。
/// </para>
/// <para>
/// <b>坐标系口径</b>（与全工程一致，别改）：服务端 Z-up、水平面 = XY；Flax 是 Y-up、地面平面 = XZ。
/// 所以这里的 <c>OriginX/OriginY</c> 是**服务端**水平坐标，插件采样时自己做换轴。
/// </para>
/// </summary>
public static class MonsterNavBakeCore
{
    /// <summary>可走格的代价（与 <see cref="MonsterNavGrid.BaseCellCost"/> 同值：10 = 一格标准移动）。</summary>
    public const byte WalkableCost = (byte)MonsterNavGrid.BaseCellCost;

    /// <summary>难行格（泥地/斜坡）的代价：A* 仍会走，但会优先绕标准格。</summary>
    public const byte HardCost = (byte)MonsterNavGrid.HardCellCost;

    /// <summary>不可走格的代价。</summary>
    public const byte BlockedCost = 0;

    /// <summary>单边的最大格数（超过它多半是把整张地图的边长当成格数填了；4096×4096 = 1600 万格，内存和加载都不划算）。</summary>
    public const int MaxSide = 4096;

    /// <summary>可走比例为 0 时的判据：整张图全阻塞一定是采样失败，而不是"这片地方真的不能走"。</summary>
    public const string AllBlockedProblem = "整张网格全阻塞：一个可走格都没有。几乎必然是采样通路没生效" +
                                            "（场景里没有 NavMesh / 探针高度落在地形之外 / 换轴写反了），" +
                                            "而不是这块地真的不能走。";

    /// <summary>
    /// 由掩码装配网格。
    /// </summary>
    /// <param name="width">格数（X 方向）。</param>
    /// <param name="height">格数（Y 方向）。</param>
    /// <param name="cellSize">每格边长（米）。</param>
    /// <param name="originX">左下角的服务端 X。</param>
    /// <param name="originY">左下角的服务端 Y。</param>
    /// <param name="mask">
    /// 每格一个字符，行优先、从 y=0 行开始：<c>1</c>/<c>w</c> = 标准可走，<c>2</c>/<c>s</c> = 难行（泥地/斜坡），
    /// <c>0</c>/<c>.</c>/<c>#</c> = 阻塞。空白字符被忽略（插件可以按行送）。
    /// 去掉空白后的长度必须恰好 = width×height，其它字符一律报错。
    /// </param>
    public static MonsterNavBakeResult FromMask(
        int width, int height, float cellSize, float originX, float originY, string mask)
    {
        var result = new MonsterNavBakeResult { Width = width, Height = height };

        if (width <= 0 || height <= 0)
        {
            result.Problems.Add($"格数非法：{width}×{height}（必须都为正）");
            return result;
        }
        if (width > MaxSide || height > MaxSide)
        {
            result.Problems.Add($"格数超过单边上限 {MaxSide}：{width}×{height}");
            return result;
        }
        if (cellSize <= 0f)
        {
            result.Problems.Add(string.Format(CultureInfo.InvariantCulture,
                "格长非法：{0}（必须 > 0 米）", cellSize));
            return result;
        }
        if (string.IsNullOrEmpty(mask))
        {
            result.Problems.Add("掩码为空：没有任何一格的采样结果");
            return result;
        }

        var need = width * height;
        var costs = new byte[need];
        var filled = 0;
        for (var i = 0; i < mask.Length && filled < need; i++)
        {
            var c = mask[i];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n') continue;
            // 只认三类记号：1/w 标准可走、2/s 难行（泥地/斜坡）、0/.# 阻塞。其它字符必须报错而不是"当成阻塞"——
            // 把拼错的记号静默算成阻塞，表现是"能走的地方少了一半"而没有任何地方说为什么。
            costs[filled] = c switch
            {
                '1' or 'w' or 'W' => WalkableCost,
                '2' or 's' or 'S' => HardCost,
                '0' or '.' or '#' => BlockedCost,
                _ => byte.MaxValue,
            };
            if (costs[filled] == byte.MaxValue)
            {
                result.Problems.Add(string.Format(CultureInfo.InvariantCulture,
                    "掩码第 {0} 个字符是 '{1}'：只接受 1/w（标准）、2/s（难行）或 0/.#（阻塞）", filled + 1, c));
                return result;
            }
            filled++;
        }

        if (filled != need)
        {
            result.Problems.Add(string.Format(CultureInfo.InvariantCulture,
                "掩码长度不符：期望 {0} 格（{1}×{2}），实得 {3}", need, width, height, filled));
            return result;
        }

        var walkable = 0;
        for (var i = 0; i < need; i++)
        {
            if (costs[i] > BlockedCost) walkable++;   // 难行格也算能走，只是贵
        }

        result.WalkableCells = walkable;
        result.TotalCells = need;

        var builder = new StringBuilder();
        builder.Append(MonsterNavGridLoaderHeader)
            .Append(' ').Append(width.ToString(CultureInfo.InvariantCulture))
            .Append(' ').Append(height.ToString(CultureInfo.InvariantCulture))
            .Append(' ').Append(cellSize.ToString("0.###", CultureInfo.InvariantCulture))
            .Append(' ').Append(originX.ToString("0.###", CultureInfo.InvariantCulture))
            .Append(' ').Append(originY.ToString("0.###", CultureInfo.InvariantCulture))
            .Append('\n');
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (x > 0) builder.Append(' ');
                builder.Append(costs[y * width + x].ToString(CultureInfo.InvariantCulture));
            }
            builder.Append('\n');
        }
        result.Text = builder.ToString();

        if (walkable == 0) result.Problems.Add(AllBlockedProblem);
        else if (walkable == need) result.Problems.Add("整张网格全可走：多半是采样把“什么都算能走”了（探针命中了不存在的默认值）");

        return result;
    }

    /// <summary>文件头的标记（必须与 <c>MonsterNavGridLoader.HeaderToken</c> 逐字相同，由对账测试钉住）。</summary>
    public const string MonsterNavGridLoaderHeader = "MONSTERNAV";
}
