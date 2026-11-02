using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;
using Horizon.Game.Sim;
using Horizon.Orleans.Grains;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans;
using Orleans.Runtime;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 好感度阶梯的**可达性**：树上每一档 favorNeed 都必须有玩家真走得通的来源，
/// 而且"哪些口有来源上限、哪些口刻意没有"必须是写下来的事实，不是散在三处实现里的默契。
/// <para>
/// 这条测试的起因是一次算账（不是猜）。按 §14 的数据，每域能拿到的好感只有四个口：
/// </para>
/// <list type="number">
/// <item>击杀：<c>monster:{模板id}</c> 逐源计上限 <see cref="FavorabilityGrain.DefaultPerTargetCap"/>，
/// 目录里每域恰好一只日常怪 + 一只世界 BOSS = 两个源；</item>
/// <item>NPC 对话：<c>npc:{模板id}</c>，上限压到 5（低于击杀默认值），且只有三个已登记 NPC（金/木/土）；</item>
/// <item>入门任务：一次性（<c>repeat_limit = 0</c>），直接给该域 20；</item>
/// <item>信物 910011–910015：掉落产出（<c>via</c> 含 <c>loot</c>），用一份少一份，
/// 而 <see cref="FavorabilityGrain.AddAsync"/> 只在**传了 targetKey 时**才记来源上限——
/// 信物那笔账不传，所以它是唯一一个可重复累加的口。</item>
/// </list>
/// <para>
/// 前三个口加起来的天花板算出来是：火域 60、木/水/土 65–70、金域 75。
/// 而数据里道阶第三境的门槛是 85、混沌终式要求五域同达 110。
/// 也就是说**顶端完全压在信物那一条没有上限的口上**。
/// 这条口现在是对的，但它对得很安静：将来任何人"为了统一口径给信物也加个 item: 键"，
/// 表现是树上最后两档永远学不了、好感涨到 20 就再也不动，而且没有任何一行报错。
/// 所以下面既钉住当前算出来的账，也钉住那个调用点的形状。
/// </para>
/// </summary>
[Collection(MonsterContentStaticCollection.Name)]
public class WuxingFavorSupplyCeilingTests
{
    private const string SkillSeed = @"scripts/sql/011_seed_wuxing_skill_tree.sql";
    private const string EntryQuestSeed = @"scripts/sql/012_seed_wuxing_entry_quests.sql";
    private const string StoryQuestSeed = @"scripts/sql/014_seed_quest_editor.sql";
    private const string InventoryGrainSource = @"Horizon.Orleans.Grains/InventoryGrain.cs";

    /// <summary>NPC 对话的逐源上限（<c>QuestHandler.TalkFavorPerTargetCap</c>，私有常量，此处同值）。</summary>
    private const int TalkCapPerNpc = 5;

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Horizon.sln"))) dir = dir.Parent;
        Assert.True(dir != null, "找不到仓库根（Horizon.sln）");
        var full = Path.Combine(dir!.FullName, relativePath);
        Assert.True(File.Exists(full), $"缺少 {relativePath}，这条测试正在空跑");
        return File.ReadAllText(full);
    }

    private static string Seed(string relativePath) => ReadRepoFile(relativePath);

    /// <summary>数据里实际出现的 favorNeed 门槛（单域取 min，混沌取 allDomains.min）。</summary>
    private static List<int> SingleDomainGates() =>
        Regex.Matches(Seed(SkillSeed), @"""favorNeed"":\{""domain"":\d,""min"":(\d+)\}")
            .Select(m => int.Parse(m.Groups[1].Value)).ToList();

    private static List<int> AllDomainGates() =>
        Regex.Matches(Seed(SkillSeed), @"""favorNeed"":\{""allDomains"":true,""min"":(\d+)\}")
            .Select(m => int.Parse(m.Groups[1].Value)).ToList();

    [Fact]
    public void FavorGates_HaveTheShapeTheAuditAssumed()
    {
        var single = SingleDomainGates();
        var all = AllDomainGates();

        // 锚点错了这两条会静默空跑，所以先钉形状（本仓反复踩过）。
        Assert.True(single.Count >= 40, $"只从 011 解出 {single.Count} 个单域好感门槛（期望 ≥40）");
        Assert.Equal(2, all.Count);                                  // 只有混沌那一档走 allDomains
        Assert.Equal(85, single.Max());
        Assert.Equal(110, all.Max());
    }

    /// <summary>
    /// 只走"有来源上限"的口时，每域最多能攒到多少好感——**刻意算宽**：
    /// 击杀按整只目录里同域模板数（含 BOSS）、NPC 按登记数、任务把 012 与 014 的 favor 奖励全算上
    /// （哪怕 014 那几条的剧情可达性本测试并不核实）。宽算都够不到的档，就是真够不到。
    /// </summary>
    [Fact]
    public void CappedSourcesAlone_CannotReachTheTopOfTheLadder()
    {
        var monster = MonsterTemplateCatalog.RegisteredTemplates()
            .Where(t => t.WuxingDomain >= 1 && t.WuxingDomain <= 5)
            .GroupBy(t => t.WuxingDomain)
            .ToDictionary(g => g.Key, g => g.Count());
        var npcDomains = NpcTemplateCatalog.RegisteredTemplates()
            .Select(t => t.WuxingDomain)
            .Where(d => d >= 1 && d <= 5)
            .ToHashSet();

        // 入门任务：每域一条，直接给该域一笔好感（012 是按 src.d 生成 JSON 的，数字只出现在拼接串里）。
        int entryQuestFavor = ParseEntryQuestFavorPerDomain();
        var storyFavor = ParseStoryQuestFavorByDomain();

        var ceiling = new Dictionary<int, int>();
        for (int domain = 1; domain <= 5; domain++)
        {
            int kills = monster.TryGetValue(domain, out var n) ? n * FavorabilityGrain.DefaultPerTargetCap : 0;
            int talk = npcDomains.Contains(domain) ? TalkCapPerNpc : 0;
            int story = storyFavor.TryGetValue(domain, out var s) ? s : 0;
            ceiling[domain] = kills + talk + entryQuestFavor + story;
        }

        // 先证明这些数字真的来自内容目录，而不是"字典是空的所以天花板是 0"那种空跑。
        Assert.Equal(5, monster.Count);
        Assert.All(monster.Values, c => Assert.True(c >= 1));
        Assert.True(entryQuestFavor > 0);
        Assert.Equal(3, npcDomains.Count);           // 水火两域有意留白（见 NpcTemplateCatalog）
        Assert.True(ceiling[4] >= FavorabilityGrain.DefaultPerTargetCap,
            $"火域连击杀那一口都没算进来（{ceiling[4]}）：这条测试正在空跑");

        Assert.True(ceiling[4] < 75,
            $"火域只靠有上限的口已能到 {ceiling[4]}，本测试的前提（天阶那一档够不到）变了：" +
            "要么内容加了新来源，要么上限口径被改——去重新算 §14 的阶梯，别放着这条不管");
        Assert.True(ceiling.Values.All(c => c < 110),
            $"某个域靠有上限的口已达 {ceiling.Values.Max()}，混沌终式(110)不再依赖信物那条可重复的口：" +
            "这是好事，但 §14.7 的算账文字得跟着改");
    }

    /// <summary>信物必须是"会掉、且掉了能换好感"的行，否则可重复那一条口是空的。</summary>
    [Fact]
    public void TokensAreLootableAndCarryFavor()
    {
        var lines = Seed(SkillSeed).Split('\n');
        int seen = 0;
        foreach (var id in new[] { 910011, 910012, 910013, 910014, 910015 })
        {
            // 只看带列名的那一段（UPDATE）：INSERT 是按位置排值的，从里面抠"哪一列是什么"迟早抠错。
            var row = lines.SingleOrDefault(l => l.Contains("UPDATE ") && l.TrimEnd().EndsWith($"item_id = {id};"));
            Assert.NotNull(row);
            seen++;

            Assert.Contains("item_type = 2", row!);                 // 2=消耗品，才是"能用掉"的那一类
            Assert.Matches("\"via\":\\[[^\\]]*\"loot\"", row!);      // 进掉落表的闸门是 via，不是 source_type
            var rate = Regex.Match(row!, @"drop_rate = ([0-9.]+)");
            Assert.True(rate.Success && double.Parse(rate.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) > 0d,
                $"信物 {id} 的 drop_rate 不大于 0：它掉不出来，可重复好感这一条口就是空的");
            Assert.Matches(@"""kind"":""favor"",""domain"":[1-5],""delta"":[1-9]", row!);
        }
        Assert.Equal(5, seen);
    }

    /// <summary>
    /// 真账（不靠读源码）：同一笔账传不传 targetKey，行为差别就是"能不能刷满"。
    /// </summary>
    [Fact]
    public async Task CreditWithoutTargetKey_KeepsAccruing_WhileCreditWithTargetKey_Saturates()
    {
        var capped = NewGrain();
        int lastCapped = 0;
        for (int i = 0; i < 30; i++)
            lastCapped = await capped.AddAsync(4, 10, "monster:9");      // 同一个源
        Assert.Equal(FavorabilityGrain.DefaultPerTargetCap, lastCapped);   // 停在 20

        var uncapped = NewGrain();
        int total = 0;
        for (int i = 0; i < 30; i++)
            total = await uncapped.AddAsync(4, 10);                       // 信物那种两参数调用
        Assert.Equal(300, total);
        Assert.True(total >= 110, "顶端那档（混沌 110）必须能从这条口拿到，否则 §14 的门是焊死的");
    }

    /// <summary>
    /// 调用点形状：信物那笔好感必须**只传两个参数**。
    /// <para>
    /// 为什么用源码形状而不是行为断言：InventoryGrain 直接用 Orleans 的 <c>GrainFactory</c>，
    /// 脚手架里造不出那笔调用；而这条规则的风险恰恰是"顺手补一个 <c>item:</c> 键"——
    /// 补上就编译通过、测试全绿、好感涨到 20 就再也不动。锚在调用点上，改的人一眼能看到为什么。
    /// </para>
    /// </summary>
    [Fact]
    public void TokenFavorCredit_PassesNoTargetKey_OnPurpose()
    {
        var text = ReadRepoFile(InventoryGrainSource);
        var calls = Regex.Matches(text, @"favor\.AddAsync\(([^;]*?)\);");
        Assert.Single(calls);                       // 只有信物那一条；多出来就说明有人在这加了第二笔账

        var args = SplitTopLevelArgs(calls[0].Groups[1].Value);
        Assert.Equal(2, args.Count);
    }

    /// <summary>按顶层逗号拆参数（括号/引号内的逗号不算）。</summary>
    private static List<string> SplitTopLevelArgs(string argumentList)
    {
        var parts = new List<string>();
        int depth = 0;
        bool inString = false;
        var current = new System.Text.StringBuilder();
        for (int i = 0; i < argumentList.Length; i++)
        {
            char c = argumentList[i];
            if (c == '"' && (i == 0 || argumentList[i - 1] != '\\')) inString = !inString;
            if (!inString)
            {
                if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}') depth--;
                else if (c == ',' && depth == 0)
                {
                    parts.Add(current.ToString().Trim());
                    current.Clear();
                    continue;
                }
            }
            current.Append(c);
        }
        parts.Add(current.ToString().Trim());
        return parts;
    }

    private static int ParseEntryQuestFavorPerDomain()
    {
        // 012 的奖励是拼出来的：N'...,"favor:' + CAST(src.d AS NVARCHAR(2)) + N'":20,...'
        // 两处写法都有（CAST(d ...) 与 CAST(src.d ...)），所以 src. 做成可选。
        var match = Regex.Match(Seed(EntryQuestSeed),
            @"""favor:'\s*\+\s*CAST\((?:src\.)?d AS NVARCHAR\(\d+\)\)\s*\+\s*N'""\s*:\s*(\d+)");
        Assert.True(match.Success, "没从 012 里解出入门任务的好感奖励：入口变了，算账得重做");
        Assert.Contains("repeat_limit = 0", Seed(EntryQuestSeed));   // 一次性——所以才不能按"可刷"计
        return int.Parse(match.Groups[1].Value);
    }

    private static Dictionary<int, int> ParseStoryQuestFavorByDomain()
    {
        // 014 每条任务都写了两遍（UPDATE + INSERT），这里**故意不去重**：同一笔奖励按两份计入上限。
        // 这条测试要说的是"宽算都够不到"，所以宁可把天花板抬高，也不要低估。
        var result = new Dictionary<int, int>();
        foreach (System.Text.RegularExpressions.Match m in Regex.Matches(Seed(StoryQuestSeed), @"""favor:(\d)""\s*:\s*(\d+)"))
        {
            int domain = int.Parse(m.Groups[1].Value);
            result.TryGetValue(domain, out var have);
            result[domain] = have + int.Parse(m.Groups[2].Value);
        }
        return result;
    }

    private static FavorabilityGrain NewGrain()
    {
        var persistent = new Mock<IPersistentState<FavorabilityState>>();
        persistent.SetupGet(s => s.State).Returns(new FavorabilityState());
        return new FavorabilityGrain(NullLogger<FavorabilityGrain>.Instance, persistent.Object);
    }
}
