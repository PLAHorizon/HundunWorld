using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Horizon.MonsterCli;

/// <summary>
/// 外观表的一行输入（从库里投影出来的最小事实，刻意不依赖 EF 实体类型）。
/// </summary>
/// <param name="MonsterId">怪物编号（决定导出顺序，避免"同样数据每次导出顺序不同"的无意义 diff）。</param>
/// <param name="Name">显示名，只用于 problems 里把人能读懂的名字带上。</param>
/// <param name="ModelKey">客户端模型键：表的主键。</param>
/// <param name="PrefabGuid">设计者指认的资产 GUID（只随表发布做诊断，客户端不靠它加载）。</param>
/// <param name="PrefabPath">内容树路径——**客户端唯一认得的加载入口**。</param>
/// <param name="ModelScale">体型缩放。</param>
/// <param name="StandState">待机姿态。</param>
public readonly record struct MonsterVisualRow(
    int MonsterId, string Name, string ModelKey, string? PrefabGuid, string? PrefabPath, double ModelScale, int StandState);

/// <summary>外观表的构建结果。</summary>
public sealed class MonsterVisualsResult
{
    /// <summary>可直接落盘的 JSON 文本。</summary>
    public string Json { get; set; } = string.Empty;

    /// <summary>进表条目数（漏配的行不算在内）。</summary>
    public int Count { get; set; }

    /// <summary>逐条点名的问题（"配了外观但客户端看不到"的那类漏配必须显形）。</summary>
    public List<string> Problems { get; } = new List<string>();

    /// <summary>
    /// "填了 prefab 路径、但探针说内容树里没有"的行数。
    /// <para>单独计数而不是只混在 Problems 里：调用方（CLI 与门禁）要能区分"没配"（11 条那种如实状态）
    /// 与"配错了"（一定会变成玩家看见的占位人形）——这两种故障的修法完全不同。</para>
    /// </summary>
    public int MissingPrefabPaths { get; set; }
}

/// <summary>
/// 客户端外观表（<c>model_key → prefab 路径/缩放/姿态</c>）的构建。
/// <para>
/// <b>为什么单独成一类而不是留在 Program 里</b>：这段判据是"哪些怪真的会换模样"的唯一裁决点，
/// 而它 normally 只在连着库的时候才跑得起来。把"投影 + 筛掉不可加载的行 + 记问题"抽成纯函数，
/// 才能在没有 SQL Server 的门禁里断言它——否则回归全靠人肉进编辑器点一次。
/// </para>
/// <para>
/// <b>键名是跨语言边界的协议</b>：读侧在 <c>HundunWorld/Source/Game/Selection/MonsterVisualCatalog.cs</c>
/// （Flax 脚本域，与本程序集互相引用不到）。它按 <c>visuals/modelKey/prefabPath/scale/standState</c>
/// 取值，且**跳过 prefabPath 为空的条目**。改名必须两侧同时改，
/// <c>MonsterEditorContractTests</c> 按字面量钉住这五个键，漂一个就红。
/// </para>
/// </summary>
public static class MonsterVisualsExport
{
    /// <summary>读侧使用的键（与 <c>MonsterVisualCatalog</c> 一一对应）。</summary>
    public static readonly string[] ClientKeys = { "visuals", "modelKey", "prefabPath", "scale", "standState" };

    /// <summary>
    /// 构建外观表。
    /// <param name="rows">库里已发布的行。</param>
    /// <param name="prefabExists">
    /// 可选的"这条路径在盘上真的有吗"探针（CLI 传内容树根，测试传假探针）。
    /// <para>为什么需要它：客户端按 <c>prefabPath</c> 现算 GUID 加载，路径写错一个字母的表现是
    /// <b>静默回退占位 Prefab</b>——编辑器里那一格明明配了东西，进游戏却全员默认人形，
    /// 而现有判据只查"有没有填"，查不出"填了个不存在的"（2026-10-27 第一次建出真怪物 prefab 时撞上的形状）。</para>
    /// <para>探针判"没命中"的行**仍然进表**（路径可能只是不在这个根下，删掉它等于替客户端做决定），
    /// 但必须逐条点名——一条会说谎的静默回退比一条红名问题难查得多。</para>
    /// </param>
    /// </summary>
    public static MonsterVisualsResult Build(IEnumerable<MonsterVisualRow> rows,
                                             Func<string, bool>? prefabExists = null)
    {
        var result = new MonsterVisualsResult();
        var visuals = new List<Dictionary<string, object?>>();
        foreach (var row in rows.OrderBy(r => r.MonsterId))
        {
            var key = (row.ModelKey ?? string.Empty).Trim();
            if (key.Length == 0)
            {
                result.Problems.Add($"怪物 {row.MonsterId}（{row.Name}）没有 model_key：客户端无从解析外观，将回退占位 Prefab");
                continue;
            }

            var guid = (row.PrefabGuid ?? string.Empty).Trim();
            var path = (row.PrefabPath ?? string.Empty).Trim();
            if (path.Length == 0)
            {
                // GUID 跨机器/跨打包不稳定（本工程实测：按路径现算 GUID 才是可靠的那条路），
                // 所以"只填了 GUID"= 编辑器里看着配好了、进游戏仍是占位人形。这种行不进表，且必须点名。
                result.Problems.Add(string.IsNullOrEmpty(guid)
                    ? $"怪物 {row.MonsterId}（{row.Name}，模型键 {key}）没有 prefab 路径：不进外观表，客户端回退占位 Prefab"
                    : $"怪物 {row.MonsterId}（{row.Name}，模型键 {key}）只填了 Guid（{guid}）没填路径：客户端按路径加载，这条不进外观表");
                continue;
            }

            // 探针只判"在不在"，不改行：填了路径却查无此资产的行仍然进表（见 Build 的注释），
            // 但逐条点名 + 计数，让"编辑器里配了、进游戏是占位"这件事在回执里就看得见。
            if (prefabExists != null && !prefabExists(path))
            {
                result.MissingPrefabPaths++;
                result.Problems.Add($"怪物 {row.MonsterId}（{row.Name}，模型键 {key}）的 prefab 路径在内容树里查不到：{path}" +
                                    "（这条仍进表，但客户端按路径加载会失败并静默回退占位 Prefab——请核对拼写或先建出该资产）");
            }

            visuals.Add(new Dictionary<string, object?>
            {
                ["modelKey"] = key,
                ["prefabPath"] = path,
                ["prefabGuid"] = guid,
                ["scale"] = row.ModelScale,
                ["standState"] = row.StandState,
            });
        }

        result.Count = visuals.Count;
        if (visuals.Count == 0)
        {
            // "一条都没配"与"配了但都不可加载"在客户端是同一个坏表现（全员占位），
            // 但在编辑器侧是两件不同的事，所以这里补一条总账，避免 problems 为空被读成"一切正常"。
            result.Problems.Add("外观表里没有任何可加载条目：进游戏后全部怪物都会回退占位 Prefab");
        }
        result.Json = Serialize(visuals, result.Problems);
        return result;
    }

    private static string Serialize(List<Dictionary<string, object?>> visuals, List<string> problems)
    {
        var payload = new Dictionary<string, object?>
        {
            ["generatedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["visuals"] = visuals,
            ["problems"] = problems,
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }
}
