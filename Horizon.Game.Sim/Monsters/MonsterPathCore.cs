using System;

namespace Horizon.Game.Sim.Monsters;

/// <summary>
/// 服务端导航网格（决策 D5：<b>一期走高度图网格 A*</b>，二期再叠 NavMesh 多边形图）。
/// </summary>
/// <remarks>
/// <b>为什么寻路必须在服务端</b>：权威位置在服务端、客户端只做插值（本工程已有三层插值缺陷的教训：
/// 远程角色闪移/位置回退）。若各客户端自己算怪的路径，多玩家会看到同一只怪走<b>不同</b>的轨迹。
/// <para>
/// <b>为什么是纯数据结构 + 纯函数</b>：<c>Horizon.Game.Sim</c> 不能引用 Flax（它同时被 Orleans 与 DS 消费，
/// 且要求位级可复现）。所以"从地形/NavMesh 造这张图"留在宿主（Silo/DsHost），本层只消费。
/// 客户端侧的 Flax <c>Navigation.FindPath</c> 只服务玩家点击移动
/// （<c>HundunWorld/Source/Game/PlayerController.cs:1193</c>），不参与怪物权威。
/// </para>
/// </remarks>
public struct MonsterNavGrid
{
    /// <summary>格数（X 方向）。</summary>
    public int Width;

    /// <summary>格数（Y 方向，水平面第二轴；服务端 Z-up，水平面 = XY）。</summary>
    public int Height;

    /// <summary>格子边长（米）。取 1.0 与既有 AOI chunk(16m) 尺度相容。</summary>
    public float CellSize;

    /// <summary>网格原点世界坐标 X（格 (0,0) 的中心）。</summary>
    public float OriginX;

    /// <summary>网格原点世界坐标 Y。</summary>
    public float OriginY;

    /// <summary>
    /// 每格通行代价：0 = 不可通行（墙/水/悬崖），10 = 标准，&gt;10 = 难行（泥地/斜坡）。
    /// 长度必须等于 <c>Width × Height</c>。
    /// </summary>
    public byte[] CellCost;

    /// <summary>格数总量是否自洽（宿主装配后第一道自检；不自洽时所有查询直接判"无路"）。</summary>
    public readonly bool IsValid =>
        Width > 0 && Height > 0 && CellSize > 0f && CellCost != null && CellCost.Length == Width * Height;

    /// <summary>标准代价（10 = 一格直线移动的代价单位，整数以免浮点漂移）。</summary>
    public const int BaseCellCost = 10;

    /// <summary>
    /// "难走但可走"的格子代价（陡坡、灌木、浅水）。**它不是墙**：墙是 0（不可走）。
    /// <para>地面走的怪会为绕开它付出双倍代价；飞行的怪不受地形代价约束，读路径时按
    /// <see cref="CostOf(int,bool)"/> 把它折回基准代价。这条区分就是 <c>follow_type</c> 的
    /// "直线"与"飞行"之间唯一量得出来的差别——以前两者在移动上完全同形。</para>
    /// </summary>
    public const int HardCellCost = 20;

    /// <summary>
    /// 不可走（墙、深水、建筑内部）：<b>0 是哨兵值而不是"很便宜的格子"</b>——
    /// 可走性判据就是 <c>代价 &gt; 0</c>，所以任何一档移动方式（含飞行）都不能穿它。
    /// 命名出来是为了让"0"不再被读成漏配。
    /// </summary>
    public const int BlockedCellCost = 0;

    /// <summary>
    /// 逐格普查：不可走 / 难行 / 总格数。
    /// <para>装载方（DsHost）拿它念一行运行期回执。<b>"这张图什么都没挡住"必须读得出来</b>：
    /// 一张全 10 的网格是合法文件、<see cref="IsValid"/> 为真、所有路径查询都返回"走直线"，
    /// 于是"开了导航位的怪还是只会走直线"与"导航图没装"在玩家那边同形，
    /// 而日志原本只会说"已装入 WxH"——那是一句真话，但它挡着的是个空东西。</para>
    /// </summary>
    public readonly (int Blocked, int Hard, int Total) Census()
    {
        var costs = CellCost;
        if (costs is null) return (0, 0, 0);
        var blocked = 0;
        var hard = 0;
        for (var i = 0; i < costs.Length; i++)
        {
            if (costs[i] == BlockedCellCost) blocked++;
            else if (costs[i] == HardCellCost) hard++;
        }
        return (blocked, hard, costs.Length);
    }

    /// <summary>对角代价的定点分子（14 ≈ 10×√2，取整，与 octile 启发一致）。</summary>
    public const int DiagonalCostNum = 14;

    /// <summary>世界坐标 → 格索引；越界返回 -1。</summary>
    public readonly int CellIndexOf(float x, float y)
    {
        if (!IsValid) return -1;
        var cx = (int)Math.Round((x - OriginX) / CellSize);
        var cy = (int)Math.Round((y - OriginY) / CellSize);
        if (cx < 0 || cy < 0 || cx >= Width || cy >= Height) return -1;
        return cy * Width + cx;
    }

    /// <summary>格索引 → 世界坐标（格中心）。</summary>
    public readonly void CellCenter(int cell, out float x, out float y)
    {
        var cx = cell % Width;
        var cy = cell / Width;
        x = OriginX + cx * CellSize;
        y = OriginY + cy * CellSize;
    }

    /// <summary>该格是否可走。</summary>
    public readonly bool IsWalkable(int cell)
    {
        if (!IsValid || cell < 0 || cell >= CellCost.Length) return false;
        return CellCost[cell] > 0;
    }

    /// <summary>该格代价；不可走返回 0。</summary>
    public readonly int CostOf(int cell)
    {
        if (!IsValid || cell < 0 || cell >= CellCost.Length) return 0;
        return CellCost[cell];
    }

    /// <summary>
    /// 按移动方式取代价。<paramref name="ignoreHardPenalty"/> = true（飞行档）时，
    /// "难走但可走"的格子折回基准代价——它仍然可走，只是不该让飞行单位绕路。
    /// 不可走（0）对谁都是不可走：这格区分的是"贵"和"不能去"，不能合并。
    /// </summary>
    public readonly int CostOf(int cell, bool ignoreHardPenalty)
    {
        var cost = CostOf(cell);
        if (ignoreHardPenalty && cost == HardCellCost) return BaseCellCost;
        return cost;
    }

    /// <summary>把世界坐标钳进网格内最近的格（目标点在网格外时用，避免"追出图就发呆"）。</summary>
    public readonly int ClampToWorldCell(float x, float y)
    {
        if (!IsValid) return -1;
        var cx = (int)Math.Round((x - OriginX) / CellSize);
        var cy = (int)Math.Round((y - OriginY) / CellSize);
        if (cx < 0) cx = 0;
        if (cy < 0) cy = 0;
        if (cx >= Width) cx = Width - 1;
        if (cy >= Height) cy = Height - 1;
        return cy * Width + cx;
    }
}

/// <summary>
/// A* 的工作缓冲（宿主按网格尺寸分配一次、复用；内核不做堆分配，否则会打乱 tick 抖动与确定性内存布局）。
/// </summary>
public sealed class MonsterNavScratch
{
    /// <summary>起点到该格的最短代价。</summary>
    public int[] GScore = Array.Empty<int>();

    /// <summary>回溯路径用的父格；-1 = 无。</summary>
    public int[] Parent = Array.Empty<int>();

    /// <summary>0 = 未访问，1 = 开放表，2 = 已闭。</summary>
    public byte[] State = Array.Empty<byte>();

    /// <summary>f 值（G+H），堆排序主键。</summary>
    public int[] FScore = Array.Empty<int>();

    /// <summary>h 值，堆排序次键（同 f 时偏向更接近目标的格）。</summary>
    public int[] HScore = Array.Empty<int>();

    /// <summary>二叉堆（存格索引）。</summary>
    public int[] Heap = Array.Empty<int>();

    /// <summary>堆内元素数。</summary>
    public int HeapSize;

    /// <summary>已分配容量（格数）。</summary>
    public int Capacity;

    /// <summary>确保容量覆盖 <paramref name="cellCount"/> 格；不足则整体重建（装配期一次，运行期稳定）。</summary>
    public void EnsureCapacity(int cellCount)
    {
        if (cellCount <= 0) return;
        if (Capacity >= cellCount && GScore.Length >= cellCount) return;
        GScore = new int[cellCount];
        Parent = new int[cellCount];
        State = new byte[cellCount];
        FScore = new int[cellCount];
        HScore = new int[cellCount];
        Heap = new int[cellCount];
        Capacity = cellCount;
        HeapSize = 0;
    }
}

/// <summary>路径查询结果。</summary>
public enum MonsterPathResult : byte
{
    /// <summary>未查询/参数非法。</summary>
    Invalid = 0,

    /// <summary>直达（起终点同格或终点就是起点邻格，无需绕行）。</summary>
    Direct = 1,

    /// <summary>找到完整路径。</summary>
    Found = 2,

    /// <summary>目标不可达，返回**部分路径**（走到能到达的、最接近目标的格）——追击玩家上墙时不该整只怪站住。</summary>
    Partial = 3,

    /// <summary>完全不连通（起点自己就被围住）。</summary>
    Unreachable = 4,
}

/// <summary>
/// 确定性网格寻路内核（8 邻接 A*，整数代价，octile 启发）。
/// <para>
/// <b>确定性要点</b>：堆的比较是全序 <c>(f, h, cellIndex)</c>。只用 f 会让同 f 的展开顺序依赖插入历史，
/// 从而在"两条等价路径"之间随机选一条——那会让金轨迹对拍不可能稳定。
/// </para>
/// <para>
/// <b>代价单位</b>：直线 10（× 格代价/10），对角 14。启发式用全局最小格代价作下界，可采纳（admissible）。
/// </para>
/// </summary>
public static class MonsterPathCore
{
    /// <summary>版本（进金轨迹锚点）。</summary>
    public const int Version = 1;

    /// <summary>
    /// 一次寻路最多展开多少格（防大图上 A* 把一 tick 吃光）；耗尽时返回部分路径。
    /// <para>
    /// 2026-10-30 由 4096 提到 16384：真库内容烘出来的 321×74 网格（全 10 代价）上，
    /// 出生点到巡逻首点要绕过一条 3 格宽的墙——同代价等价物极多，A* 在把 f 值更小的椭圆铺完之前
    /// 就要展开五千多格，4096 会**假报 Partial**，宿主按"无路"记账，怪 5 秒后脱战回家（实测过：
    /// `到首点=Partial(89格)`，而横截面上绕行方向全是可走格）。上限只影响"要多绕一圈"的查询，
    /// 与既有小图上的选路逐字相同（同 f 同 h 的次序没动）。
    /// </para>
    /// </summary>
    public const int MaxPathCells = 16384;

    /// <summary>
    /// 求路径。
    /// </summary>
    /// <param name="grid">导航网格。</param>
    /// <param name="scratch">工作缓冲（宿主复用）。</param>
    /// <param name="startX">起点世界坐标 X。</param>
    /// <param name="startY">起点世界坐标 Y。</param>
    /// <param name="endX">终点世界坐标 X。</param>
    /// <param name="endY">终点世界坐标 Y。</param>
    /// <param name="outCells">输出：路径格索引，**不含起点**，含终点。长度建议 ≥ MaxPathCells。</param>
    /// <param name="pathLength">实际写入的格数（0 = 无路径）。调用方不得从缓冲残值猜长度——
    /// 复用缓冲里一定留着上一次更长的路径。</param>
    /// <param name="maxExploreCells">最多探索多少格（宿主按预算给；0 = 用 MaxPathCells）。</param>
    /// <returns>结果与路径。</returns>
    public static MonsterPathResult FindPath(
        in MonsterNavGrid grid,
        MonsterNavScratch scratch,
        float startX,
        float startY,
        float endX,
        float endY,
        Span<int> outCells,
        out int pathLength,
        int maxExploreCells = 0,
        bool ignoreHardPenalty = false)
    {
        pathLength = 0;
        if (!grid.IsValid || scratch == null) return MonsterPathResult.Invalid;
        var cellCount = grid.Width * grid.Height;
        if (scratch.Capacity < cellCount) return MonsterPathResult.Invalid; // 宿主没备好缓冲：大声失败而不是静默绕远

        var start = grid.CellIndexOf(startX, startY);
        if (start < 0) start = grid.ClampToWorldCell(startX, startY);
        var goal = grid.CellIndexOf(endX, endY);
        var goalExact = goal >= 0;
        if (goal < 0) goal = grid.ClampToWorldCell(endX, endY);

        if (start < 0 || goal < 0) return MonsterPathResult.Invalid;

        // 起点不可走：把怪卡在墙里比让它直线走更糟 —— 退化成"直达"（宿主会用直线位移）。
        if (!grid.IsWalkable(start)) return MonsterPathResult.Direct;

        if (start == goal)
        {
            return MonsterPathResult.Direct;
        }

        // 视线内可达：不必走 A*。这是"绕树"与"贴脸抖动"的分水岭——
        // WoW 的怪在空旷地是直线冲的，只有挡着才绕。
        if (goalExact && HasLineOfSight(in grid, startX, startY, endX, endY))
        {
            if (outCells.Length < 1) return MonsterPathResult.Invalid;
            outCells[0] = goal;
            pathLength = 1;
            return MonsterPathResult.Direct;
        }

        var budget = maxExploreCells > 0 && maxExploreCells < MaxPathCells ? maxExploreCells : MaxPathCells;

        // 初始化缓冲（只清用过的部分靠 State 标记，但为确定性一律 memset）。
        Array.Clear(scratch.State, 0, cellCount);
        Array.Clear(scratch.Parent, 0, cellCount);
        scratch.HeapSize = 0;

        var minCost = MinCostHint(in grid, ignoreHardPenalty);
        scratch.GScore[start] = 0;
        // 父链必须以 -1 终止。Array.Clear 会把 Parent 清成 0，而 0 号格（左下角）是合法格，
        // 于是回溯会一路走到 0 号格并把它接进路径 —— 怪会绕到地图角落。
        scratch.Parent[start] = -1;
        scratch.HScore[start] = Heuristic(in grid, start, goal, minCost);
        scratch.FScore[start] = scratch.HScore[start];
        scratch.State[start] = 1;
        HeapPush(scratch, start);

        var explored = 0;
        var bestCell = start;              // 部分路径用：离 goal 最近的已探索格
        var bestH = scratch.HScore[start];

        while (scratch.HeapSize > 0 && explored < budget)
        {
            var current = HeapPop(scratch);
            if (scratch.State[current] == 2) continue;
            scratch.State[current] = 2;
            explored++;

            if (current == goal)
            {
                var len = Backtrack(scratch, goal, outCells);
                pathLength = len;
                return len > 0 ? MonsterPathResult.Found : MonsterPathResult.Unreachable;
            }

            var cx = current % grid.Width;
            var cy = current / grid.Width;
            var gNow = scratch.GScore[current];

            // 8 邻接，固定展开顺序（先正后斜，序号升序）——展开顺序影响 tie 结果，必须钉死。
            for (var d = 0; d < 8; d++)
            {
                var (nx, ny, diagonal) = Neighbor(cx, cy, d);
                if (nx < 0 || ny < 0 || nx >= grid.Width || ny >= grid.Height) continue;
                var next = ny * grid.Width + nx;
                var cost = grid.CostOf(next, ignoreHardPenalty);
                if (cost <= 0) continue;
                if (diagonal)
                {
                    // 斜角不许穿墙：两条直角边都必须可走（否则怪会"贴着墙角切过去"）。
                    if (!grid.IsWalkable(cy * grid.Width + nx) || !grid.IsWalkable(ny * grid.Width + cx)) continue;
                }

                var stepCost = diagonal
                    ? (long)cost * MonsterNavGrid.DiagonalCostNum / 10L
                    : (long)cost;
                var tentative = gNow + (int)stepCost;

                if (scratch.State[next] != 0 && scratch.GScore[next] <= tentative) continue;
                if (scratch.State[next] == 2) continue; // 已闭：本实现代价一致，不再重开

                scratch.GScore[next] = tentative;
                scratch.HScore[next] = Heuristic(in grid, next, goal, minCost);
                scratch.FScore[next] = tentative + scratch.HScore[next];
                scratch.Parent[next] = current;
                scratch.State[next] = 1;
                HeapPush(scratch, next);

                if (scratch.HScore[next] < bestH)
                {
                    bestH = scratch.HScore[next];
                    bestCell = next;
                }
            }
        }

        // 预算耗尽或目标不可达：给部分路径（走到离目标最近的可达格）。
        // 起点自身即最近 → 视为不可达，宿主退化为直线/等待。
        if (bestCell == start) return MonsterPathResult.Unreachable;
        var partialLen = Backtrack(scratch, bestCell, outCells);
        pathLength = partialLen;
        return partialLen > 0 ? MonsterPathResult.Partial : MonsterPathResult.Unreachable;
    }

    /// <summary>两点之间是否视线直通（格子 Bresenham，全整数，无浮点误差方向性）。</summary>
    public static bool HasLineOfSight(in MonsterNavGrid grid, float ax, float ay, float bx, float by)
    {
        if (!grid.IsValid) return false;
        var ca = grid.CellIndexOf(ax, ay);
        var cb = grid.CellIndexOf(bx, by);
        if (ca < 0 || cb < 0) return false;

        var x0 = ca % grid.Width;
        var y0 = ca / grid.Width;
        var x1 = cb % grid.Width;
        var y1 = cb / grid.Width;

        var dx = Math.Abs(x1 - x0);
        var dy = Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx - dy;
        var guard = 0;
        var maxSteps = dx + dy + 2;

        while (guard++ <= maxSteps)
        {
            if (!grid.IsWalkable(y0 * grid.Width + x0)) return false;
            if (x0 == x1 && y0 == y1) return true;
            var e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
        return false;
    }

    /// <summary>沿路径推进一步：朝"下一个路点格中心"移动，到阈值即推进游标。</summary>
    /// <param name="grid">导航网格。</param>
    /// <param name="path">路径格（<see cref="FindPath"/> 的输出，不含起点）。</param>
    /// <param name="pathLength">路径长度。</param>
    /// <param name="cursor">当前路点下标（引用；到点自动前进）。</param>
    /// <param name="selfX">怪当前 X（引用：直接写成新坐标，宿主无需再加 delta——走图时位移不是直线，增量法会累积误差）。</param>
    /// <param name="selfY">怪当前 Y（引用）。</param>
    /// <param name="speed">速度（米/秒）。</param>
    /// <param name="dt">步长（秒）。</param>
    /// <param name="arriveDistance">判定到点的距离（米），默认半格。</param>
    /// <returns>本 tick 是否发生了移动；false 且 <paramref name="cursor"/> ≥ <paramref name="pathLength"/> 表示已走完。</returns>
    public static bool AdvanceAlongPath(
        in MonsterNavGrid grid,
        ReadOnlySpan<int> path,
        int pathLength,
        ref int cursor,
        ref float selfX,
        ref float selfY,
        float speed,
        float dt,
        float arriveDistance = 0f)
    {
        if (!grid.IsValid || pathLength <= 0 || cursor < 0) return false;
        if (cursor >= pathLength) return false;
        if (dt <= 0f || speed <= 0f) return false;

        var threshold = arriveDistance > 0f ? arriveDistance : grid.CellSize * 0.5f;

        // 一次 tick 内可能跨过多个路点（高速怪 + 小格子）；循环消耗位移预算，避免"走一步停一步"的抖动感。
        var budget = speed * dt;
        var moved = false;
        var guard = 0;

        while (budget > 0f && cursor < pathLength && guard++ < pathLength + 2)
        {
            grid.CellCenter(path[cursor], out var wx, out var wy);
            var dx = wx - selfX;
            var dy = wy - selfY;
            var lenSq = dx * dx + dy * dy;
            if (lenSq <= 1e-8f)
            {
                cursor++;
                continue;
            }
            var len = MathF.Sqrt(lenSq);

            if (len <= threshold)
            {
                // 到点：吸附并前进游标，位移预算不结转（半格的误差，宁可略慢也不越点）。
                selfX = wx;
                selfY = wy;
                cursor++;
                moved = true;
                continue;
            }

            // 钳到剩余距离：本 tick 不越过路点（越过会让下一 tick 朝同一点回头，表现为抖动）。
            var travel = budget <= len ? budget : len;
            var inv = 1f / len;
            selfX += dx * inv * travel;
            selfY += dy * inv * travel;
            moved = true;
            break;
        }

        return moved;
    }

    /// <summary>把部分路径/完整路径的"最后可达点"换算成世界坐标（宿主用于诊断与编辑器可达性预览）。</summary>
    public static void CellToWorld(in MonsterNavGrid grid, int cell, out float x, out float y)
    {
        x = 0f;
        y = 0f;
        if (!grid.IsValid || cell < 0 || cell >= grid.CellCost.Length) return;
        grid.CellCenter(cell, out x, out y);
    }

    // ---------- 内部 ----------

    private static int Backtrack(MonsterNavScratch scratch, int endCell, Span<int> outCells)
    {
        // 先逆序填，再原地反转（不借栈：栈溢出比路径长更糟）。
        var len = 0;
        var cell = endCell;
        while (cell >= 0 && len < outCells.Length && len < MaxPathCells)
        {
            outCells[len++] = cell;
            cell = scratch.Parent[cell];
        }
        if (len <= 1) return 0; // 只有终点自己 = 起点就是终点，视作无路径

        for (var i = 0; i < len / 2; i++)
        {
            var t = outCells[i];
            outCells[i] = outCells[len - 1 - i];
            outCells[len - 1 - i] = t;
        }

        // outCells[0] 此刻是"起点之后的第一格"吗？Parent 链含起点，故反转后最后一项是终点，
        // 第一项是起点。契约是"不含起点"，所以整体左移一格。
        for (var i = 0; i < len - 1; i++) outCells[i] = outCells[i + 1];
        return len - 1;
    }

    private static int Heuristic(in MonsterNavGrid grid, int from, int to, int unitCost)
    {
        var ax = from % grid.Width;
        var ay = from / grid.Width;
        var bx = to % grid.Width;
        var by = to / grid.Width;
        var dx = Math.Abs(ax - bx);
        var dy = Math.Abs(ay - by);
        var straight = dx > dy ? dx : dy;
        var diagonal = dx + dy - 2 * straight;
        // octile：直线 unitCost、对角 unitCost×1.4；用整数 (10*unit + 4*unit/10) 形式避免浮点。
        return straight * unitCost + diagonal * unitCost * MonsterNavGrid.DiagonalCostNum / 10;
    }

    /// <summary>启发式下界用的最小格代价（图里最便宜的通路）；找不到返回标准代价。</summary>
    private static int MinCostHint(in MonsterNavGrid grid, bool ignoreHardPenalty = false)
    {
        var min = int.MaxValue;
        for (var i = 0; i < grid.CellCost.Length; i++)
        {
            var c = grid.CellCost[i];
            // 启发式必须与展开时用的代价同口径：忽略难走惩罚却仍按 20 取下界会**高估**剩余距离，
            // A* 失去最优性，表现是"路径偶尔莫名绕远"——最难查的那类错，所以这里一起折回。
            if (ignoreHardPenalty && c == MonsterNavGrid.HardCellCost) c = MonsterNavGrid.BaseCellCost;
            if (c > 0 && c < min) min = c;
        }
        return min == int.MaxValue ? MonsterNavGrid.BaseCellCost : min;
    }

    /// <summary>8 邻接的固定展开顺序（先四正后四斜）。</summary>
    private static (int Dx, int Dy, bool Diagonal) Neighbor(int cx, int cy, int direction)
    {
        return direction switch
        {
            0 => (cx + 1, cy, false),
            1 => (cx, cy + 1, false),
            2 => (cx - 1, cy, false),
            3 => (cx, cy - 1, false),
            4 => (cx + 1, cy + 1, true),
            5 => (cx - 1, cy + 1, true),
            6 => (cx - 1, cy - 1, true),
            _ => (cx + 1, cy - 1, true),
        };
    }

    private static bool HeapLess(MonsterNavScratch s, int a, int b)
    {
        // 全序比较：(f, h, cellIndex)。少了第三级，同 f 同 h 的顺序会随插入历史变，
        // 于是"两条等价路径"的选择不可复现，金轨迹对拍直接失去意义。
        if (s.FScore[a] != s.FScore[b]) return s.FScore[a] < s.FScore[b];
        if (s.HScore[a] != s.HScore[b]) return s.HScore[a] < s.HScore[b];
        return a < b;
    }

    private static void HeapPush(MonsterNavScratch s, int cell)
    {
        var i = s.HeapSize++;
        s.Heap[i] = cell;
        while (i > 0)
        {
            var p = (i - 1) / 2;
            if (!HeapLess(s, s.Heap[i], s.Heap[p])) break;
            (s.Heap[i], s.Heap[p]) = (s.Heap[p], s.Heap[i]);
            i = p;
        }
    }

    private static int HeapPop(MonsterNavScratch s)
    {
        var top = s.Heap[0];
        s.HeapSize--;
        if (s.HeapSize > 0)
        {
            s.Heap[0] = s.Heap[s.HeapSize];
            var i = 0;
            while (true)
            {
                var l = i * 2 + 1;
                var r = l + 1;
                var smallest = i;
                if (l < s.HeapSize && HeapLess(s, s.Heap[l], s.Heap[smallest])) smallest = l;
                if (r < s.HeapSize && HeapLess(s, s.Heap[r], s.Heap[smallest])) smallest = r;
                if (smallest == i) break;
                (s.Heap[i], s.Heap[smallest]) = (s.Heap[smallest], s.Heap[i]);
                i = smallest;
            }
        }
        return top;
    }
}
