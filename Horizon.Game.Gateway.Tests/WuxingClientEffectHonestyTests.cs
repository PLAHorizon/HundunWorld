using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Horizon.Game.Sim.Skills;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 客户端"招式说明"的诚实口径与服务端效果账本必须同源（方案 §14.7 G25 / G33）。
/// <para>
/// <b>要防的不是写错，而是写歪之后没人知道</b>：数据里写着"定身 2 秒""持续伤害 4 秒"，
/// 而服务端今天只结算破甲与回复。玩家照着说明去体验，发现什么都没发生——这条链上没有任何一行报错。
/// 现在客户端会在说明里点名"实装中，暂未生效：定身、持续伤害"，
/// 于是"哪些效果已经真生效"这一事实第一次同时存在于两端。
/// </para>
/// <para>
/// 既然是两处声明，就必须钉一致性（本仓已有同款先例：世界 BOSS 掉落表基址在 Silo 与 Sim 各写一次，
/// 由 <c>WuxingBossLootTests</c> 断言两边同一个数）。这里钉三件事：
/// ① 客户端声称"已结算"的效果集合，与服务端账本里 <see cref="EffectRole.Applied"/> 的键**一一对应**；
/// ② 说明文本真的被页面消费（否则前一条是死代码）；
/// ③ 产物里出现的每一个效果类型都被登记过——要么已结算，要么有待生效的中文名。
/// 服务端新兑现一条效果而客户端没跟上，或反过来客户端提前吹，都会当场红。
/// </para>
/// </summary>
public class WuxingClientEffectHonestyTests
{
    private const string TreeFile = @"HundunWorld/Source/Game/Combat/Skills/WuxingSkillTree.cs";
    private const string PageFile = @"HundunWorld/Source/Game/UI/Ink/Pages/Character/SkillTreePage.cs";
    private const string ArtifactFile = @"HundunWorld/Content/GameData/Skills/skills.json";

    /// <summary>
    /// 服务端账本键 → 客户端产物里的效果类型名。
    /// <c>hpCost</c> 不在表里：它是"代价"，走的是耗气/气血那几行显示，本来就不是一个附加效果类型。
    /// </summary>
    private static readonly Dictionary<string, string> AppliedKeyToClientType = new()
    {
        ["pen"] = "Penetration",
        ["healPct"] = "Heal",
    };

    private static readonly string[] KeysWithoutClientEffectType = { "hpCost" };

    /// <summary>
    /// 客户端那台本地效果引擎（DoT 自算伤害、自写眩晕）**今天没有施加入口**，
    /// 所以它不构成"客户端在掉血而服务端不知道"的双权威（方案 §15.7）。
    /// 这条断言现在为真，价值全在将来：有人加一个本地施加点时，它必须直接面对
    /// "数字只能来自服务端"这条约束（§15.2 K3 用户口径：Orleans 判、DS 同步、客户端只显示）。
    /// <para>
    /// 反向说清楚一点，免得被当成"鼓励保持现状"：这条不是在夸当前架构，而是钉住一个事实——
    /// <c>SkillEffectSystem</c> 的 <c>ApplyEffect/RemoveEffect/ClearAllEffects</c> 三个改状态的方法
    /// 目前零调用方，于是 <c>_activeEffects</c> 恒为空、HUD 的 buff 栏（<c>CombatHUDManager</c> /
    /// <c>BuffBarUI</c>）永远画不出东西。接线时该补的是**服务端驱动的入口**（协议 190 的处理器里
    /// 把消息交给它做纯表现），而不是在这台引擎里自己推伤害。
    /// </para>
    /// </summary>
    [Fact]
    public void ClientSideEffectEngineHasNoLocalAuthorityEntryYet()
    {
        var root = Path.Combine(FindRepoRootForTest(), @"HundunWorld/Source/Game");
        Assert.True(Directory.Exists(root), $"找不到客户端脚本目录 {root}：这条闸门正在空跑");

        // 正向对照：先证明"被排除的调用形状"确实存在可被命中的东西（引擎里真有这三个改状态的方法），
        // 否则零命中只是因为正则写错了（本仓反复踩过"拒绝路径的断言其实从没可能命中"）。
        var engine = Read(@"HundunWorld/Source/Game/Combat/Effects/SkillEffectSystem.cs");
        foreach (var signature in new[] { "public bool ApplyEffect(", "public void RemoveEffect(" })
            Assert.Contains(signature, engine);

        // 只拦"会凭空产生数值与状态"的两个入口。ClearAllEffects 不在内：
        // 它只做清理（CombatSystemManager 在重置时调它），一次也不会造出伤害或眩晕。
        // 第一次写这条时把清理也算了进去，于是闸门红在一条无害的 housekeeping 上——
        // 那种红会把人训练成"这条闸门可以忽略"，比漏报更糟。
        var mutator = new Regex(@"(SkillEffectSystem\.Instance|_effectSystem)\s*\.\s*(ApplyEffect|RemoveEffect)\s*\(");
        var hits = new List<string>();
        foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFileName(file), "SkillEffectSystem.cs", StringComparison.Ordinal)) continue;
            foreach (Match m in mutator.Matches(File.ReadAllText(file)))
                hits.Add($"{Path.GetRelativePath(root, file)}: {m.Value}");
        }

        Assert.True(hits.Count == 0,
            "客户端开始本地施加效果了：" + string.Join("；", hits) +
            "。按 §15.2/§15.7 的口径，效果的数值与时长只能由服务端算（协议 190 下行），" +
            "客户端这一侧只许显示，不许自己推伤害或自写眩晕——那会长出第二个权威，" +
            "而且表现是'客户端掉血了但服务端不知道'，两端都不会报错");
    }

    private static string FindRepoRootForTest()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Horizon.sln"))) dir = dir.Parent;
        Assert.True(dir != null, "找不到仓库根（Horizon.sln）");
        return dir!.FullName;
    }

    private static string Read(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Horizon.sln"))) dir = dir.Parent;
        Assert.True(dir != null, "找不到仓库根（Horizon.sln）");
        var full = Path.Combine(dir!.FullName, relativePath);
        Assert.True(File.Exists(full), $"缺少 {relativePath}：这条闸门正在空跑");
        return File.ReadAllText(full);
    }

    /// <summary>把客户端文件里那个"已结算"集合原样抠出来（不 import 客户端程序集，只读源码）。</summary>
    private static HashSet<string> ReadClientSettledTypes()
    {
        var text = Read(TreeFile);
        var block = Regex.Match(text,
            @"SettledEffectTypes\s*=\s*new[^{]*\{(?<body>[^}]*)\}");
        Assert.True(block.Success, "没在 WuxingSkillTree.cs 里找到 SettledEffectTypes 的初始化块：写法变了，这条闸门要同步改");

        var types = Regex.Matches(block.Groups["body"].Value, "\"([A-Za-z]+)\"")
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        Assert.True(types.Count > 0, "SettledEffectTypes 是空的：那客户端等于把所有效果都说成未生效");
        return types;
    }

    [Fact]
    public void ClientSettledTypesMatchTheServerLedgersAppliedKeys()
    {
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in WuxingSkillEffects.AppliedKeys)
        {
            Assert.True(AppliedKeyToClientType.ContainsKey(key) || KeysWithoutClientEffectType.Contains(key),
                $"服务端新把 {key} 记成已结算，但这里没决定它在客户端算什么效果类型：" +
                "要么加映射（并把它加进客户端的 SettledEffectTypes），要么显式说明它不是一个附加效果");
            if (AppliedKeyToClientType.TryGetValue(key, out var clientType)) expected.Add(clientType);
        }

        var actual = ReadClientSettledTypes();
        Assert.True(expected.SetEquals(actual),
            $"客户端说这些效果已结算 = {string.Join(",", actual.OrderBy(s => s))}，" +
            $"服务端账本里真兑现的 = {string.Join(",", expected.OrderBy(s => s))}。" +
            "两边不一致的表现是：玩家在说明里看到『已经生效』的效果其实不发生，或反过来被低估");
    }

    [Fact]
    public void DetailLineActuallyConsumesTheHonestyNote()
    {
        var page = Read(PageFile);
        Assert.Matches(@"default:\s*return\s+WuxingSkillTree\.DescribeEffects\(cfg\);", page);

        var tree = Read(TreeFile);
        Assert.Contains("（实装中，暂未生效：", tree);
        Assert.True(tree.Contains("PendingEffectLabels"), "说明里的未生效清单必须由登记表驱动，别写成固定句子");
    }

    /// <summary>
    /// 产物里出现过的每个效果类型都得有个归宿：已结算，或在待生效登记表里有中文名。
    /// 少一个的话，将来加新效果类型时它会**静默地不被提及**——正是这条链上最难发现的一种漏。
    /// </summary>
    [Fact]
    public void EveryEffectTypeInTheArtifactIsAccountedFor()
    {
        using var doc = JsonDocument.Parse(Read(ArtifactFile));
        var rows = doc.RootElement.GetProperty("Skills").EnumerateArray().ToList();
        Assert.True(rows.Count >= 91, $"skills.json 只有 {rows.Count} 行（期望 ≥91）：产物形状变了");

        var artifactTypes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
            if (row.TryGetProperty("effects", out var effects) && effects.ValueKind == JsonValueKind.Array)
                foreach (var e in effects.EnumerateArray())
                    if (e.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String)
                        artifactTypes.Add(t.GetString()!);

        var settled = ReadClientSettledTypes();
        var tree = Read(TreeFile);
        var labeled = Regex.Matches(tree, @"\""([A-Za-z]+)\|[^\""]*""")
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        Assert.True(artifactTypes.Count > 0, "产物里一个效果类型都没有：这条断言就是空跑");
        foreach (var type in artifactTypes)
            Assert.True(settled.Contains(type) || labeled.Contains(type),
                $"效果类型 {type} 出现在 skills.json 里，但既不在已结算集合、也不在待生效登记表里：" +
                "它会在说明里被完全略过，玩家看不到任何提示");
    }
}
