using System.Collections.Generic;
using System.Linq;
using Horizon.Game.Sim.Monsters;

namespace Horizon.MonsterCli;

/// <summary>
/// 内容普查的一行输入（从库里投影出来的最小事实，刻意不依赖 EF 实体类型）。
/// </summary>
/// <param name="MonsterId">怪物编号（只用于把人能读懂的名字对上）。</param>
/// <param name="Name">显示名。</param>
/// <param name="SkillMounts">这只怪挂了几行技能。</param>
/// <param name="UseSkills">能力位算完之后**会不会施法**（必须问 <c>MonsterAiFlags.Resolve</c>，不许自己按档位猜）。</param>
/// <param name="UseNavigation">会不会走导航图。</param>
/// <param name="UseAssist">会不会求援。</param>
/// <param name="LootBindings">这只怪挂了几张掉落表。</param>
/// <param name="LootEntries">逐件条目总数（表上的行不数进这里——那是"掷几次"而不是"掉什么"）。</param>
/// <param name="HasMoney">有没有任一绑定配了非零金币区间。</param>
/// <param name="HasPatrolRoute">行为表或某个刷怪点上挂了巡逻线。</param>
/// <param name="HasCorpseWindow">尸体停留秒数 &gt; 0（= 拾取窗口那格真的被配过）。</param>
/// <param name="HasText">配了起手喊话或起手动作。</param>
/// <param name="LootWindowSeconds">**可拾取窗口**秒数（0 = 尸体一出现就取不了）。与"尸体停留"是两格。</param>
/// <param name="NormalLootEntries">普通件（条件=无条件）的候选条目数——一具尸体只能放 18 件。</param>
/// <param name="GroupLootEntries">挂在互斥组（groupId&gt;0）里的条目数：这才是"魔兽那种从 N 件里必出 1 件"。</param>
/// <param name="ConditionalLootEntries">按条件出账的条目数（AC 的 <c>needs_quest</c> 一族）。</param>
public readonly record struct MonsterAuditRow(
    int MonsterId, string Name, int SkillMounts, bool UseSkills, bool UseNavigation, bool UseAssist,
    int LootBindings, int LootEntries, bool HasMoney, bool HasPatrolRoute, bool HasCorpseWindow, bool HasText,
    float LootWindowSeconds = 0f, int NormalLootEntries = 0,
    int GroupLootEntries = 0, int ConditionalLootEntries = 0,
    int AiProfile = 0, int FollowType = 0);

/// <summary>普查结果：读数 + 要念给人听的那几句。</summary>
public sealed class MonsterAuditResult
{
    public int Published { get; set; }
    public int SkillMounts { get; set; }
    public int MonstersWithSkills { get; set; }
    public int SkillCapable { get; set; }
    public int NavCapable { get; set; }
    public int AssistCapable { get; set; }
    public int LootBindings { get; set; }
    public int LootEntries { get; set; }
    public int MonstersWithLoot { get; set; }
    public int MoneyCapable { get; set; }
    public int RouteCapable { get; set; }
    public int CorpseCapable { get; set; }
    public int TextCapable { get; set; }
    /// <summary>指向"模板表里不存在的怪"的子表行数（孤儿）。</summary>
    public int OrphanRows { get; set; }

    /// <summary>有掉落但**可拾取窗口 ≤ 0** 的怪数（尸体一出现就取不了：内容配了、玩家永远拿不到）。</summary>
    public int LootWindowClosedWithDrops { get; set; }

    /// <summary>普通件候选数超过一具尸体上限（18）的怪数：运行期按先到先得，多出来的被丢弃。</summary>
    public int LootCandidatesOverCap { get; set; }

    /// <summary>挂在互斥组里的条目数（"从 N 件里必出 1 件"靠的就是它）。</summary>
    public int LootGroupEntries { get; set; }

    /// <summary>按条件出账的条目数（AC 的 needs_quest 一族）。</summary>
    public int LootConditionalEntries { get; set; }

    /// <summary>内容里用到的 AI 档位个数（0..3 那一格只有 1 个值时，档位表只被验证了一档）。</summary>
    public int DistinctAiProfiles { get; set; }

    /// <summary>内容里用到的移动方式个数（四档：步行/直线/飞行/站桩）。</summary>
    public int DistinctFollowTypes { get; set; }

    /// <summary>逐条发现（校验器的错误与警告 + 这几句"内容为零"的总账）。</summary>
    public List<string> Findings { get; } = new List<string>();
}

/// <summary>
/// "机制就绪、内容为零"这一类读数的判据。
/// <para>
/// <b>为什么要从 CLI 里抽出来</b>：普查本身要连着库才跑得动，而"什么时候该念哪一句"是纯判断。
/// 留在 <c>Program</c> 里就等于把判据放在没有闸门的地方——实测（2026-10-27）这条普查第一次跑就抓到
/// 11 只种子怪零行技能、导航位与求援位全关，而全仓每一道代码闸门都是绿的。
/// 抓法本身必须可回归，否则下一次改动把它改哑了没人知道。
/// </para>
/// <para>
/// <b>三句总账各自的触发条件都不允许"靠 0 命中当正常"</b>：
/// 零挂载 ⇒ 念"没人能施法"；有挂载但零能力位 ⇒ 念"这些行是死数据"（这是**另一种**坏，
/// 只判前者会把它读成"有内容，挺好"）；导航/求援同理。
/// </para>
/// </summary>
public static class MonsterAuditCensus
{
    public static MonsterAuditResult Build(
        IEnumerable<MonsterAuditRow> rows, IEnumerable<string>? validationFindings = null,
        IReadOnlyList<(string Table, int RowId, int MonsterId)>? orphans = null)
    {
        var result = new MonsterAuditResult();
        if (validationFindings != null) result.Findings.AddRange(validationFindings);

        // 孤儿行：指向"模板表里根本没有的怪"。它必须与"内容为零"分开念——
        // 实测生产库那一行掉落绑定挂在怪物 500001 上（模板表只有 1..11），
        // 于是导出回执写"掉落绑定 1"而 11 只真怪一行掉落都没有：一个数同时骗过了"有没有配"和"配了几条"。
        result.OrphanRows = orphans?.Count ?? 0;
        if (orphans != null && orphans.Count > 0)
        {
            var grouped = orphans.GroupBy(o => (o.Table, o.MonsterId))
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key.Table} {g.Count()} 行→怪物 {g.Key.MonsterId}")
                .Take(5);
            result.Findings.Add($"子表里有 {orphans.Count} 行指向**怪物表与 NPC 表里都不存在的怪**（{string.Join("；", grouped)}）" +
                                "⇒ 这些行永远不会被任何一只怪读到，而导出回执里那个\"掉落绑定 N\"数的可能正是它们");
        }

        var list = rows.ToList();
        result.Published = list.Count;
        // 一只都没看到 ≠ 内容干净：那说明普查没跑起来（连不上库、发布状态写错、过滤条件太窄）。
        // 这种"空结果"必须与"零发现"长得不一样，否则它会被读成通过。
        if (list.Count == 0)
        {
            result.Findings.Add("普查什么都没看到：0 只已发布的怪。这**不是**\"内容没问题\"，而是这条检查没跑起来" +
                                "（库里没有已发布内容？还是发布状态/过滤条件不对？）");
            return result;
        }

        var profiles = new List<int>();
        var followTypes = new List<int>();
        var closedWindow = new List<string>();
        var overCap = new List<string>();
        foreach (var r in list)
        {
            result.SkillMounts += r.SkillMounts;
            if (r.SkillMounts > 0) result.MonstersWithSkills++;
            if (r.UseSkills) result.SkillCapable++;
            if (r.UseNavigation) result.NavCapable++;
            if (r.UseAssist) result.AssistCapable++;
            result.LootBindings += r.LootBindings;
            result.LootEntries += r.LootEntries;
            if (r.LootBindings > 0) result.MonstersWithLoot++;
            if (r.HasMoney) result.MoneyCapable++;
            if (r.HasPatrolRoute) result.RouteCapable++;
            if (r.HasCorpseWindow) result.CorpseCapable++;
            if (r.HasText) result.TextCapable++;
            // 两格新的内容隐患（D25 与拾取窗口）：它们不是"内容为零"，而是"配了却拿不到 / 多出来的会被丢掉"。
            if (r.LootBindings > 0 && r.LootWindowSeconds <= 0f)
            {
                result.LootWindowClosedWithDrops++;
                if (closedWindow.Count < 5) closedWindow.Add(r.Name);
            }
            if (!profiles.Contains(r.AiProfile)) profiles.Add(r.AiProfile);
            if (!followTypes.Contains(r.FollowType)) followTypes.Add(r.FollowType);
            result.LootGroupEntries += r.GroupLootEntries;
            result.LootConditionalEntries += r.ConditionalLootEntries;
            if (r.NormalLootEntries > MonsterLootCore.MaxNormalLootItems)
            {
                result.LootCandidatesOverCap++;
                if (overCap.Count < 5) overCap.Add($"{r.Name}({r.NormalLootEntries})");
            }
        }

        if (result.SkillMounts == 0)
            result.Findings.Add($"技能这一格全仓为零：{result.Published} 只已发布怪挂了 0 行技能挂载" +
                                $"（会施法的 {result.SkillCapable} 只）⇒ 施法条/打断这套内核在真内容里没有任何触发者");
        else if (result.SkillCapable == 0)
            result.Findings.Add($"挂了 {result.SkillMounts} 行技能却没有一只怪开着技能位（会施法的 0 只）⇒ 这些行全部是死数据");
        if (result.NavCapable == 0)
            result.Findings.Add("没有一只怪开着导航位 ⇒ 追击/巡逻/回位一律走直线（`bake-nav` 与 DS 的 A* 都无人调用）");
        if (result.AssistCapable == 0)
            result.Findings.Add("没有一只怪开着求援位 ⇒ 拉一只等于只来一只（内核 v5 那条链在真内容里没人走）");

        // 下面四句各管目标里的一条能力：掉落、金币、巡逻线、文本。
        // 判据形状与上面同一族——"内核齐了但内容为零"必须自己说出来，而不是留成一份看起来干净的零发现。
        if (result.LootEntries == 0)
            result.Findings.Add($"掉落全仓为零：{result.Published} 只已发布怪挂了 {result.LootBindings} 张掉落表、" +
                                "0 行逐件条目 ⇒ 杀怪不会掉任何东西（组互斥/逐件条件/表级重掷那套内核没有触发者）");
        else if (result.MoneyCapable == 0)
        {
            // 有掉落但没金币：金币是绑定上的另一格，两者不是一回事（切片 29 接的钱包入账链因此恒为 0）。
            result.Findings.Add($"配了 {result.LootBindings} 张掉落表却没有一只怪配金币区间 ⇒ `KillMoney` 那条链在真内容里恒为 0");
        }
        // 两条"配了却拿不到 / 多出来的被丢掉"——它们与"内容为零"是两回事，所以单独念。
        if (result.LootWindowClosedWithDrops > 0)
            result.Findings.Add($"有 {result.LootWindowClosedWithDrops} 只怪挂着掉落但**可拾取窗口是 0**（{string.Join("、", closedWindow)}）" +
                                "⇒ 尸体一出现就取不了：内容配了、运行期也掷了骰，玩家永远拿不到（要关拾取请删掉落，别用窗口 0 表达）");
        if (result.LootCandidatesOverCap > 0)
            result.Findings.Add($"有 {result.LootCandidatesOverCap} 只怪的普通件候选超过一具尸体上限 {MonsterLootCore.MaxNormalLootItems} 件" +
                                $"（{string.Join("、", overCap)}）⇒ 运行期按先到先得只放得下前 18 件，其余被丢弃并计入 ItemsDroppedOverCapacity");
        // 两条"内容形状"读数：不是错，但决定了"魔兽那种掉落"在这份内容里到底实现了多少。
        if (result.LootEntries > 0 && result.LootGroupEntries == 0)
            result.Findings.Add($"配了 {result.LootEntries} 行掉落却没有任何一件挂在互斥组里 ⇒ " +
                                "\"从这 10 件里必出 1 件\"这种魔兽常规配法在真内容里一件都没写过");
        if (result.LootEntries > 0 && result.LootConditionalEntries == 0)
            result.Findings.Add($"掉落里没有一件是按条件出账的（AC 的 needs_quest 一族）⇒ " +
                                "LootConditional → 任务持有者判定那条链在真内容里没有触发者");
        result.DistinctAiProfiles = profiles.Count;
        result.DistinctFollowTypes = followTypes.Count;
        // 只有一只怪时谈不上"多样性"：那一档会把"样本太少"误报成"内容单一"。
        if (list.Count > 1 && profiles.Count <= 1)
            result.Findings.Add($"AI 档位这一格在真内容里只用了 {profiles.Count} 个值（{string.Join(",", profiles)}）⇒ " +
                                "0..3 那张档位表只被内容验证了其中一档，其余档位的展开（能力位/战术位）只活在测试里");
        if (list.Count > 1 && followTypes.Count <= 1)
            result.Findings.Add($"移动方式这一格在真内容里只用了 {followTypes.Count} 个值（{string.Join(",", followTypes)}）⇒ " +
                                "四档（步行/直线/飞行/站桩）里三档在真内容里没有样本");
        if (result.RouteCapable == 0)            result.Findings.Add("没有一只怪挂巡逻线 ⇒ 全部只按 wanderRadius 绕出生点画圈（切片 20 那条\"沿线走\"没有内容走它）");
        if (result.CorpseCapable == 0)
            result.Findings.Add("没有一只怪配尸体停留秒 ⇒ 一死就消失，`corpse_seconds` 那格与拾取窗口都无从谈起");
        if (result.TextCapable == 0)
            result.Findings.Add("没有一只怪配起手喊话或起手动作 ⇒ 文本表那两格在真内容里全空（口径挂在设计文档 D17–D20）");

        return result;
    }
}
