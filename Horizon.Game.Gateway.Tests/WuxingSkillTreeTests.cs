using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Horizon.Game.Message.Network;
using Horizon.Model.GameModel;
using Horizon.Orleans.Grains;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 五行技能树的规则与数据回归用例（docs/SKILL_WUXING_TREE_DESIGN.md 的可执行部分）。
/// <para>
/// 分三层：① 相生/相克与循环节的纯规则逐值；② 阶梯倍率接入后的伤害基座黄金值；
/// ③ 交付物本身——服务端种子 SQL 与客户端 skills.json 必须是同一份数据的两个投影，
/// 任一侧重抄/漏改都要在这里被抓住，而不是等 PIE 里发现"某一行怎么少了一式"。
/// </para>
/// </summary>
public class WuxingSkillTreeTests
{
    // ===================================================================
    // ① 相生相克
    // ===================================================================

    [Theory]
    [InlineData(1, 3)]   // 金生水
    [InlineData(3, 2)]   // 水生木
    [InlineData(2, 4)]   // 木生火
    [InlineData(4, 5)]   // 火生土
    [InlineData(5, 1)]   // 土生金
    [InlineData(0, 0)]   // 无属性不成链
    public void GenerateOf_FollowsWuxingCycle(int element, int expected)
        => Assert.Equal(expected, CombatCalculator.GenerateOf(element));

    [Fact]
    public void GenerateOf_ClosesFiveStepCycle()
    {
        // "循环往复"的骨架：从金出发按相生走五步必须回到金，且中途不重复
        int current = 1;
        var visited = new List<int> { current };
        for (int i = 0; i < 4; i++)
        {
            current = CombatCalculator.GenerateOf(current);
            visited.Add(current);
        }

        Assert.Equal(new[] { 1, 3, 2, 4, 5 }, visited);
        Assert.Equal(1, CombatCalculator.GenerateOf(current));
    }

    [Theory]
    [InlineData(1, 2)]   // 金克木
    [InlineData(2, 5)]   // 木克土
    [InlineData(5, 3)]   // 土克水
    [InlineData(3, 4)]   // 水克火
    [InlineData(4, 1)]   // 火克金
    public void CounteredElement_FollowsRestrainingCycle(int element, int expected)
        => Assert.Equal(expected, CombatCalculator.CounteredElement(element));

    [Theory]
    [InlineData(1, 2, true)]    // 金克木
    [InlineData(2, 1, false)]   // 木不克金（反方向不算）
    [InlineData(1, 1, false)]   // 同属不算克制
    [InlineData(0, 2, false)]   // 无属性打谁都不破势
    [InlineData(1, 0, false)]   // 守方没有护体行也不破势
    public void Counters_IsDirectional(int attack, int defend, bool expected)
        => Assert.Equal(expected, CombatCalculator.Counters(attack, defend));

    [Fact]
    public void GenerateAndCounter_UseDifferentRowsSoCycleAndDuelDoNotCollapse()
    {
        // 防呆：若哪天有人把两张表写成同一张，"顺生"与"破势"会同时命中同一行，
        // 循环节与克制就成了一回事，§5.1/§5.2 的区分立刻失效。
        for (int element = 1; element <= 5; element++)
            Assert.NotEqual(CombatCalculator.GenerateOf(element), CombatCalculator.CounteredElement(element));
    }

    // ===================================================================
    // ② 循环节（周天）黄金值
    // ===================================================================

    private static readonly DateTime T0 = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void StepCycle_AnchorCastLaysTheFirstLayer()
    {
        // §5.1：节数数的是"本轮走过几行"，起手那行自己就是第一行。
        // 若这里回退成 0，五行各打一式只能叠到 4 层，周天圆满永远触发不了（D4 强制兼修落空）。
        var step = CombatCalculator.StepWuxingCycle(0, 0, default, 0, 1, T0);

        Assert.Equal(1, step.Stack);
        Assert.Equal(1, step.LastElement);
        Assert.Equal(1.03f, step.Multiplier, 3);  // 每层 +3%
        Assert.False(step.Completed);
    }

    [Fact]
    public void StepCycle_GenerateStacksAndCountsCompletionAtFive()
    {
        int stack = 0, last = 1, completions = 0;
        var expire = default(DateTime);
        var now = T0;
        var multipliers = new List<float>();

        // 金 → 水 → 木 → 火 → 土：五式打完必须周天圆满一次并清零
        for (int i = 0; i < 5; i++)
        {
            now = now.AddSeconds(0.5);
            var step = CombatCalculator.StepWuxingCycle(stack, last, expire, completions, i == 0 ? 1 : CombatCalculator.GenerateOf(last), now);
            stack = step.Stack;
            last = step.LastElement;
            expire = step.ExpireAt;
            completions = step.CompletionCount;
            multipliers.Add(step.Multiplier);
        }

        Assert.Equal(0, stack);
        Assert.Equal(1, completions);
        Assert.Equal(new[] { 1.03f, 1.06f, 1.09f, 1.12f }, multipliers.Take(4).Select(m => (float)Math.Round(m, 3)));
        // 满层那一击按"清零后的 0 层 + 圆满 ×1.25"计
        Assert.Equal(1.25f, (float)Math.Round(multipliers[4], 3));
    }

    [Fact]
    public void StepCycle_SameElementAndReverseGenerationNeverBreakStack()
    {
        var stacked = CombatCalculator.StepWuxingCycle(3, 1, T0.AddSeconds(1), 0, 1, T0.AddSeconds(1));
        var reversed = CombatCalculator.StepWuxingCycle(3, 1, T0.AddSeconds(1), 0, 5, T0.AddSeconds(1)); // 土不生金之逆：金→土

        Assert.Equal(3, stacked.Stack);       // 同属不清零、不加层
        Assert.Equal(3, reversed.Stack);      // 逆生同样保留已有层数
        Assert.False(stacked.Completed);
        Assert.False(reversed.Completed);
    }

    [Fact]
    public void StepCycle_WindowTimeoutRestartsChain()
    {
        // 超出 3 秒窗口才打，先前叠的层必须作废（"不断决"不等于无限免检）
        var step = CombatCalculator.StepWuxingCycle(4, 1, T0.AddSeconds(1), 0, 3, T0.AddSeconds(10));

        Assert.Equal(1, step.Stack);
        Assert.Equal(1.03f, step.Multiplier, 3);
    }

    [Fact]
    public void StepCycle_NeutralSkillLeavesChainUntouched()
    {
        var step = CombatCalculator.StepWuxingCycle(2, 4, T0.AddSeconds(1), 7, 0, T0.AddSeconds(9));

        Assert.Equal(2, step.Stack);
        Assert.Equal(4, step.LastElement);
        Assert.Equal(T0.AddSeconds(1), step.ExpireAt);
        Assert.Equal(7, step.CompletionCount);
        Assert.Equal(1.06f, step.Multiplier, 3);
    }

    // ===================================================================
    // ③ 阶梯倍率接入后的基座黄金值
    // ===================================================================

    private static SkillTemplateEntity Ladder(int grade, double multiplier) => new()
    {
        SkillGrade = grade,
        DamageMultiplier = multiplier,
    };

    [Fact]
    public void LadderFactor_KeepsLegacyRowsExactlyAsBefore()
    {
        // 未入阶梯（旧技能/未种子）必须逐值退回接入前的 AttackPower × 0.6，
        // 否则一次接线就把没参与改动的老技能暗砍 2.4 倍。
        Assert.Equal(0.6f, CombatCalculator.SkillLadderFactor(null, out var none), 3);
        Assert.Equal(1f, none, 3);

        Assert.Equal(0.6f, CombatCalculator.SkillLadderFactor(Ladder(0, 1.0), out var legacy), 3);
        Assert.Equal(1f, legacy, 3);
    }

    [Theory]
    [InlineData(1, 1, 1.10, 0.275)]     // 白·初
    [InlineData(5, 3, 3.68, 0.92)]      // 天·极
    [InlineData(6, 1, 4.01, 1.0025)]    // 道·初（超越天阶）
    [InlineData(6, 3, 4.76, 1.19)]      // 道·极
    public void LadderFactor_ScalesLadderRowsOnQuarterBase(int grade, int stage, double multiplier, double expectedFactor)
    {
        // 传进来的三个档位必须落在 §3 那条公式的同一个 k 上，否则黄金值只是四个孤立常数
        int k = 3 * (grade - 1) + (stage - 1);
        Assert.Equal(multiplier, Math.Round(1.10 * Math.Pow(1.09, k), 2), 2);

        float factor = CombatCalculator.SkillLadderFactor(Ladder(grade, multiplier), out float readMultiplier);

        Assert.Equal(0.25f, factor, 4);
        Assert.Equal((float)multiplier, readMultiplier, 3);
        Assert.Equal((float)expectedFactor, factor * readMultiplier, 4);
    }

    [Fact]
    public void LadderFactor_ZeroMultiplierDoesNotSilenceSkill()
    {
        // 列值被写成 0 时按 1.0 处理，不能让任何一式忽然变成零伤害
        float factor = CombatCalculator.SkillLadderFactor(Ladder(3, 0.0), out float multiplier);

        Assert.Equal(0.25f, factor, 4);
        Assert.Equal(1f, multiplier, 4);
    }

    // ===================================================================
    // ④ 交付物一致性：服务端种子 ↔ 客户端目录 ↔ 阶梯公式
    // ===================================================================

    [Fact]
    public void SeedAndClientCatalog_CarryTheSameNinetyOneSkills()
    {
        var root = FindRepoRoot();
        string sql = ReadSeedSql(root);
        string catalogJson = File.ReadAllText(
            Path.Combine(root, "HundunWorld", "Content", "GameData", "Skills", "skills.json"));

        var skillRows = ParseInsertRows(sql, SkillTemplateTable);
        var bookRows = Regex.Matches(sql, $@"INSERT INTO {SkillBookTable}").Count;
        var itemRows = ParseInsertRows(sql, ItemTemplateTable);

        Assert.Equal(91, skillRows.Count);
        Assert.Equal(91, bookRows);
        // 91 本秘籍 + 10 种五行灵材（§14.2 地阶以上研读时消耗）+ 5 件五行信物（换该域好感）
        Assert.Equal(106, itemRows.Count);

        var ids = skillRows.Select(r => int.Parse(r.Vals[r.Cols.IndexOf("skill_id")])).ToList();
        Assert.Equal(91, ids.Distinct().Count());

        // 每行 15 式（5 阶 × 3 境），道阶另有 15 式，混沌 1 式
        foreach (var element in new[] { 1, 2, 3, 4, 5 })
        {
            var perElement = skillRows
                .Select(r => r.Vals[r.Cols.IndexOf("skill_id")])
                .Count(id => int.Parse(id) / 10000 == element);
            Assert.Equal(15, perElement);

            // "分天地玄黄白 5 个阶段，每阶段分 3 个境界"这句话要钉在**形状**上，不只是数量：
            // 5×3=15 与"某阶重复、某阶缺一式"同样是 15 行，而后者会让整条阶在树上永远空一格。
            // id 契约 = 行*10000 + 阶*100 + 境，所以这里直接从 id 反解出来逐格点验。
            var cells = new HashSet<string>();
            foreach (var r in skillRows)
            {
                int id = int.Parse(r.Vals[r.Cols.IndexOf("skill_id")]);
                if (id / 10000 != element) continue;
                int grade = id / 100 % 100, realm = id % 100;
                Assert.True(grade >= 1 && grade <= 5, $"技能 {id} 的阶位越界：{grade}");
                Assert.True(realm >= 1 && realm <= 3, $"技能 {id} 的境界越界：{realm}");
                Assert.True(cells.Add($"{grade}-{realm}"),
                    $"五行 {element} 的 {grade} 阶 {realm} 境重复出现（有一格必然空着，树页会缺一式）");
            }
            Assert.Equal(15, cells.Count);      // 5 阶 × 3 境，一格不多一格不少
        }

        // 秘籍物品 id 必须等于 800000 + 技能 id：客户端 ItemCatalog 与 InventoryGrain 都按这个段判定
        var bookItems = itemRows.Where(i => int.Parse(i.Vals[i.Cols.IndexOf("item_type")]) == 6).ToList();
        var materialItems = itemRows.Where(i => int.Parse(i.Vals[i.Cols.IndexOf("item_type")]) == 3).ToList();
        Assert.Equal(91, bookItems.Count);
        Assert.Equal(10, materialItems.Count);          // 五行各一粗一炼

        foreach (var item in bookItems)
        {
            int itemId = int.Parse(item.Vals[item.Cols.IndexOf("item_id")]);
            var skillId = ids.Single(id => itemId == 800000 + id);
            Assert.True(skillId > 0);
        }

        // §14 门槛引用的每一样灵材都必须在种子里真有这一行。引用一个不存在的 item_id，
        // 现场表现是"玩家永远凑不齐材料"，从任何报错里都看不出来。
        var materialIds = materialItems
            .Select(i => int.Parse(i.Vals[i.Cols.IndexOf("item_id")]))
            .ToHashSet();
        Assert.Equal(10, materialIds.Count);
        foreach (var row in skillRows)
        {
            var cost = WuxingEvolveRules.Parse(Unquote(row.Vals[row.Cols.IndexOf("learn_cost")]));
            foreach (var need in cost.Materials ?? System.Array.Empty<WuxingEvolveRules.MaterialNeed>())
            {
                Assert.Contains(need.ItemId, materialIds);
                Assert.True(need.Count > 0, $"灵材 {need.ItemId} 的数量必须为正");
            }
            if (cost.FavorMin > 0)
            {
                // 带域的好感门槛只能指向五行之一；allDomains 式（混沌）不带 domain
                Assert.True(cost.FavorAllDomains
                    ? cost.FavorDomain == 0
                    : cost.FavorDomain >= 1 && cost.FavorDomain <= 5,
                    $"技能 {row.Vals[row.Cols.IndexOf("skill_id")]} 的好感域非法：{cost.FavorDomain}");
            }
        }

        // 好感度必须有"玩得出来"的来源：门槛要求到的每一域，都要有一件用掉就加该域好感的信物，
        // 且 use_effect 里的域号必须与信物自己的域一致——错位会让"刷金域好感"加到木域上。
        var tokenItems = itemRows.Where(i => int.Parse(i.Vals[i.Cols.IndexOf("item_id")]) is >= 910011 and <= 910015).ToList();
        Assert.Equal(5, tokenItems.Count);
        var favorDomainsRequired = skillRows
            .Select(r => WuxingEvolveRules.Parse(Unquote(r.Vals[r.Cols.IndexOf("learn_cost")])))
            .Where(c => c.FavorMin > 0 && !c.FavorAllDomains)
            .Select(c => c.FavorDomain).ToHashSet();
        var tokenDomains = new Dictionary<int, int>();
        foreach (var token in tokenItems)
        {
            int itemId = int.Parse(token.Vals[token.Cols.IndexOf("item_id")]);
            int element = int.Parse(token.Vals[token.Cols.IndexOf("element")]);       // 装备侧 0..4
            Assert.Equal(2, int.Parse(token.Vals[token.Cols.IndexOf("item_type")]));  // 2=消耗品，才能被"使用"
            var effect = JsonDocument.Parse(Unquote(token.Vals[token.Cols.IndexOf("use_effect")])).RootElement;
            Assert.Equal("favor", effect.GetProperty("kind").GetString());
            int domain = effect.GetProperty("domain").GetInt32();
            tokenDomains[itemId] = domain;
            Assert.Equal(element + 1, domain);                                         // 信物自己的域与效果作用的域必须同一行
            Assert.True(effect.GetProperty("delta").GetInt32() > 0);
        }
        foreach (var required in favorDomainsRequired)
            Assert.Contains(required, tokenDomains.Values);                             // 没有出路的域＝玄阶在那一域永远学不了

        // §14.2 的矩阵是真的落到了行上：玄阶起有好感、地阶起有材料、道阶起有基础属性
        var gradeOf = skillRows.ToDictionary(
            r => int.Parse(r.Vals[r.Cols.IndexOf("skill_id")]),
            r => int.Parse(r.Vals[r.Cols.IndexOf("skill_grade")]));
        foreach (var row in skillRows)
        {
            int id = int.Parse(row.Vals[row.Cols.IndexOf("skill_id")]);
            var cost = WuxingEvolveRules.Parse(Unquote(row.Vals[row.Cols.IndexOf("learn_cost")]));
            Assert.Equal(gradeOf[id] >= 5 || gradeOf[id] == 3, cost.FavorMin > 0);
            Assert.Equal(gradeOf[id] >= 4, (cost.Materials?.Count ?? 0) > 0);
            Assert.Equal(gradeOf[id] >= 6, cost.HealthNeed > 0 || cost.StrengthNeed > 0);
            // 白/黄的书就是任务与刷怪产出的，不该再叠一层材料消耗
            if (gradeOf[id] <= 2) Assert.Equal(0, cost.Materials?.Count ?? 0);
        }

        // 演化式融合的两式必须真实存在，且都是天阶同境行
        foreach (var row in skillRows)
        {
            int id = int.Parse(row.Vals[row.Cols.IndexOf("skill_id")]);
            int grade = int.Parse(row.Vals[row.Cols.IndexOf("skill_grade")]);
            if (grade != 6) continue;

            int stage = int.Parse(row.Vals[row.Cols.IndexOf("grade_stage")]);
            using var doc = JsonDocument.Parse(Unquote(row.Vals[row.Cols.IndexOf("learn_cost")]));
            var consume = doc.RootElement.GetProperty("consume").EnumerateArray()
                .Select(e => e.GetInt32()).ToList();

            Assert.Equal(2, consume.Count);
            foreach (var source in consume)
            {
                Assert.Contains(source, ids);
                Assert.Equal(5, (source / 100) % 100);           // 阶位=天阶
                Assert.Equal(stage, source % 100);             // 与被融式同境
            }
        }

        // 客户端目录：同一批技能、同一条阶梯公式
        using var catalog = JsonDocument.Parse(catalogJson);
        var skills = catalog.RootElement.GetProperty("Skills").EnumerateArray().ToList();
        Assert.Equal(91, skills.Count);
        Assert.Equal(ids.OrderBy(x => x),
            skills.Select(s => s.GetProperty("id").GetInt32()).OrderBy(x => x));

        foreach (var skill in skills)
        {
            int grade = skill.GetProperty("grade").GetInt32();
            int stage = skill.GetProperty("gradeStage").GetInt32();
            if (grade == 7) continue;   // 混沌是终式，不在 1..5 阶的公式带上

            int k = 3 * (grade - 1) + (stage - 1);
            double expected = Math.Round(1.10 * Math.Pow(1.09, k), 2);
            Assert.Equal(expected, skill.GetProperty("damageMultiplier").GetDouble(), 2);
        }
    }

    [Fact]
    public void Seed_TemplatesCarryGradeStageAndSeriesForTreeRendering()
    {
        string sql = ReadSeedSql(FindRepoRoot());

        Assert.Contains("skill_grade", sql);
        Assert.Contains("grade_stage", sql);
        Assert.Contains("series_id", sql);
        Assert.Contains("damage_multiplier", sql);

        var skillRows = ParseInsertRows(sql, SkillTemplateTable);
        var itemRows = ParseInsertRows(sql, ItemTemplateTable);

        // 技能行必须带齐技能树页面要读的三列，否则树页画不出格子（grade/stage 定位，series 归组）
        foreach (var row in skillRows)
        {
            Assert.True(int.Parse(row.Vals[row.Cols.IndexOf("skill_grade")]) >= 1);
            Assert.True(int.Parse(row.Vals[row.Cols.IndexOf("grade_stage")]) >= 1);
            Assert.NotEmpty(row.Vals[row.Cols.IndexOf("series_id")]);
        }

        // 五行编号双轨：技能列走战斗侧（1金2木3水4火5土，0=混沌），物品列走装备侧
        // （0金1木2水3火4土，5=无）。两套序不同，逐本按"这本教哪个技能"核映射；
        // 只查范围会把"金行秘籍标成木行"这类整行错位放过去。
        var elementBySkillId = skillRows.ToDictionary(
            r => int.Parse(r.Vals[r.Cols.IndexOf("skill_id")]),
            r => int.Parse(r.Vals[r.Cols.IndexOf("wuxing_element")]));

        foreach (var item in itemRows.Where(i => int.Parse(i.Vals[i.Cols.IndexOf("item_type")]) == 6))
        {
            int itemId = int.Parse(item.Vals[item.Cols.IndexOf("item_id")]);
            int itemElement = int.Parse(item.Vals[item.Cols.IndexOf("element")]);
            int wuxing = elementBySkillId[itemId - 800000];

            Assert.Equal(wuxing == 0 ? 5 : wuxing - 1, itemElement);
        }

        // 灵材走同一套装备侧编号，且必须能堆（一次性消耗几十份的材料不能占 100 个格子）
        foreach (var item in itemRows.Where(i => int.Parse(i.Vals[i.Cols.IndexOf("item_type")]) == 3))
        {
            Assert.InRange(int.Parse(item.Vals[item.Cols.IndexOf("element")]), 0, 4);
            Assert.True(int.Parse(item.Vals[item.Cols.IndexOf("max_stack")]) > 1);
        }
    }

    // ===================================================================
    // ⑤ 演化门槛（§6.2 的后两条：周天圆满次数 / 道阶系圆满对数）
    // ===================================================================

    [Fact]
    public void LearnCostParse_ToleratesPlainCostStrings()
    {
        // learn_cost 也用来放普通式的开销描述，脏值不能让整式无法研读
        Assert.Equal(WuxingEvolveRules.LearnCost.None, WuxingEvolveRules.Parse(null));
        Assert.Equal(WuxingEvolveRules.LearnCost.None, WuxingEvolveRules.Parse(""));
        Assert.Equal(WuxingEvolveRules.LearnCost.None, WuxingEvolveRules.Parse("500金 30铁"));
        Assert.Equal(WuxingEvolveRules.LearnCost.None, WuxingEvolveRules.Parse("[1,2,3]"));

        var cost = WuxingEvolveRules.Parse("{\"pair\":1,\"consume\":[10501,30501],\"loopNeed\":12}");
        Assert.Equal(new[] { 10501, 30501 }, cost.Consume);
        Assert.Equal(12, cost.LoopNeed);
        Assert.Equal(0, cost.NeedPairsCompleted);
    }

    [Fact]
    public void EvolutionSeriesCounting_RequiresAllThreeStagesOfOnePair()
    {
        // 一对（金生水 701 系）三境齐 → 1 系；缺极境那一式就不算，凑数无用
        Assert.Equal(0, WuxingEvolveRules.CountCompleteEvolutionPairs(new[] { 70101, 70102 }));
        Assert.Equal(1, WuxingEvolveRules.CountCompleteEvolutionPairs(new[] { 70101, 70102, 70103 }));
        Assert.Equal(1, WuxingEvolveRules.CountCompleteEvolutionPairs(new[] { 70101, 70102, 70103, 70201, 70202 }));
        Assert.Equal(2, WuxingEvolveRules.CountCompleteEvolutionPairs(new[] { 70101, 70102, 70103, 70201, 70202, 70203 }));

        // 同一系里重复计数不叠加（集合语义，防"练满一式刷两遍"）
        Assert.Equal(1, WuxingEvolveRules.CountCompleteEvolutionPairs(
            new[] { 70101, 70102, 70103, 70103, 70103 }));
    }

    [Theory]
    [InlineData(12, 11, 0, 0, true)]    // 差一次周天就要被拒
    [InlineData(12, 12, 0, 0, false)]
    [InlineData(40, 40, 2, 3, true)]    // 混沌：周天够了，道阶系只满 2 对
    [InlineData(40, 40, 3, 3, false)]
    public void CycleGates_NeedBothCounters(int loopNeed, int completionCount, int completePairs, int needPairs, bool expectBlocked)
    {
        var cost = new WuxingEvolveRules.LearnCost(Array.Empty<int>(), loopNeed, needPairs);
        var blocker = WuxingEvolveRules.CheckCycleGates(cost, completionCount, completePairs);

        Assert.Equal(expectBlocked, blocker != null);
    }

    [Fact]
    public void CycleGates_NormalRowsAreNeverBlockedByCounters()
    {
        // 白/黄/玄/地/天各阶不带这两个字段 → 门槛恒为 0 → 一次周天都没圆满过也能研读
        Assert.Null(WuxingEvolveRules.CheckCycleGates(WuxingEvolveRules.LearnCost.None, 0, 0));
    }

    [Fact]
    public void Seed_EveryEvolutionRowCarriesBothCountersAndSources()
    {
        // 门槛写在 learn_cost 里，生成器漏一条就会让玩家在服务端被莫名其妙拒掉，这里按行核回去
        string sql = ReadSeedSql(FindRepoRoot());
        var skillRows = ParseInsertRows(sql, SkillTemplateTable);

        int daoRows = 0;
        foreach (var row in skillRows)
        {
            int grade = int.Parse(row.Vals[row.Cols.IndexOf("skill_grade")]);
            if (grade is not (6 or 7)) continue;

            var cost = WuxingEvolveRules.Parse(Unquote(row.Vals[row.Cols.IndexOf("learn_cost")]));
            Assert.True(cost.LoopNeed > 0, "演化式必须带周天圆满门槛");

            if (grade == 6)
            {
                daoRows++;
                Assert.Equal(2, cost.Consume.Length);
                Assert.Equal(0, cost.NeedPairsCompleted);
            }
            else
            {
                Assert.Equal(3, cost.NeedPairsCompleted);
                Assert.Equal(40, cost.LoopNeed);
            }
        }

        Assert.Equal(15, daoRows);
    }

    // ===================================================================
    // ⑥ 镇岳（§5.3：护体行由玩家指定，破势必须打在那一行上）
    // ===================================================================

    [Theory]
    [InlineData(0, 3, 3)]     // 未指定 → 退化为防御向量最高行
    [InlineData(5, 3, 5)]     // 指定土：哪怕土是我最薄的一行，护体就是土
    [InlineData(1, 0, 1)]     // 没有防御向量时指定行照样成立
    [InlineData(0, 0, 0)]     // 两边都没有 → 无护体行，谁都破不了势
    public void GuardRow_ExplicitChoiceOverridesDominantDefenseRow(int guard, int dominant, int expected)
        => Assert.Equal(expected, CombatCalculator.ResolveGuardRow(guard, dominant));

    [Fact]
    public void BreakCycleOnlyLandsOnTheChosenGuardRow()
    {
        // "换镇岳=换攻防假设"：金攻木，守方镇岳在木才破得掉；设在火就只是挨一下克。
        Assert.True(CombatCalculator.Counters(1, CombatCalculator.ResolveGuardRow(2, 5)));
        Assert.False(CombatCalculator.Counters(1, CombatCalculator.ResolveGuardRow(4, 2)));
    }

    // ===================================================================
    // ⑦ 目录下行：客户端技能树只认这几列
    // ===================================================================

    [Fact]
    public void CatalogEntry_CarriesLadderAndEvolveGatesSoServerOnlyRowsStillRender()
    {
        // 库里加了新一式而 skills.json 忘了重导时，客户端只能靠这份下行建行。
        // 少带阶位，这一式会因为 Grade=0 被技能树当作"不在五行阶梯上"直接跳过；
        // 少带 learn_cost，演化源与周天门槛就看不见，树上会显示成"随时可学"。
        var entry = SkillCatalogMapper.ToEntry(new SkillTemplateEntity
        {
            Id = 70101,
            SkillName = "金液",
            WuxingElement = 3,
            SkillType = 0,
            SkillGrade = 6,
            GradeStage = 1,
            SeriesId = "Water_6",
            DamageMultiplier = 4.01,
            PreSkillId = null,
            EnergyCostFormula = "300",
            CooldownFormula = "18",
            CastTimeFormula = "0.3",
            AttackRange = 14,
            MaxLevel = 10,
            LearnCost = "{\"pair\":1,\"consume\":[10501,30501],\"loopNeed\":12,\"affinityNeed\":75}",
        });

        Assert.Equal(70101, entry.SkillId);
        Assert.Equal(6, entry.SkillGrade);
        Assert.Equal(1, entry.GradeStage);
        Assert.Equal("Water_6", entry.SeriesId);
        Assert.Equal(4.01, entry.DamageMultiplier, 3);
        Assert.Equal(0, entry.PreSkillId);                       // 空前置折成 0，不把 null 甩给客户端
        Assert.Equal(300f, entry.EnergyCost, 1);
        Assert.Equal(18f, entry.Cooldown, 1);

        var cost = WuxingEvolveRules.Parse(entry.LearnCostJson);  // 原文透传，门槛与演化源必须还能读出来
        Assert.Equal(new[] { 10501, 30501 }, cost.Consume);
        Assert.Equal(12, cost.LoopNeed);
    }

    [Fact]
    public void CatalogEntry_ToleratesDirtyLearnCostAndNullSeries()
    {
        // 未种子的旧行：learn_cost 是自由文本、series_id 为 NULL。映射不能让整份目录因为一行脏值而抛
        var entry = SkillCatalogMapper.ToEntry(new SkillTemplateEntity
        {
            Id = 5001,
            SkillName = "岩甲术",
            SkillGrade = 0,
            LearnCost = "500金 30铁",
            SeriesId = null,
            EnergyCostFormula = "not-a-number",
            CooldownFormula = null,
        });

        Assert.Equal(0, entry.SkillGrade);
        Assert.Equal("", entry.SeriesId);
        Assert.Equal(0f, entry.EnergyCost);
        Assert.Equal(0f, entry.Cooldown);
        Assert.Equal("500金 30铁", entry.LearnCostJson);
    }

    [Theory]
    [InlineData(false, null, false)]     // 没登记秘籍的行（旧 26 技、剧情授予式）可以直接习得
    [InlineData(true, "青锋三式·启锋秘籍", true)]
    public void DirectLearnChannel_IsDecidedByBookRegistrationNotByClient(
        bool hasBook, string bookName, bool expectBlocked)
    {
        // §14.5：通道不再由客户端上报的 LearnMethod 决定。白阶也在这条规则里 ——
        // 它的书本来就是任务与刷怪产出的，绕过书就等于把 §14 的动态条件整条作废。
        var blocker = WuxingEvolveRules.DirectLearnBlocker(hasBook, bookName);
        Assert.Equal(expectBlocked, blocker != null);
        if (hasBook) Assert.Contains(bookName!, blocker!);
    }

    [Fact]
    public void DynamicGates_FailClosedWhenContextMissing()
    {
        // 带动态门槛却没有判定输入 → 拒绝。反过来（当成满足）会让"高阶无材料无好感直接学"，
        // 装配漏一次就是整条经济失效。
        var cost = WuxingEvolveRules.Parse("{\"favorNeed\":{\"domain\":1,\"min\":20}}");
        Assert.NotNull(WuxingDynamicGates.Check(cost, null));

        // 普通式不带这些键 → 完全不受影响（包括还没接装配的旧通道）
        Assert.Null(WuxingDynamicGates.Check(WuxingEvolveRules.Parse("{\"grade\":1}"), null));
    }

    [Fact]
    public void FavorabilityGate_UsesPerDomainAccumulationAndNamesTheShortfall()
    {
        var cost = WuxingEvolveRules.Parse("{\"favorNeed\":{\"domain\":3,\"min\":40}}");

        Assert.Contains("水域好感不足", WuxingDynamicGates.CheckFavorability(cost, new Dictionary<int, int> { [3] = 12 })!);
        Assert.Null(WuxingDynamicGates.CheckFavorability(cost, new Dictionary<int, int> { [3] = 40 }));
        // 完全没攒过 = 0，不是"没有这一项所以放行"
        Assert.Contains("当前 0", WuxingDynamicGates.CheckFavorability(cost, new Dictionary<int, int>())!);
        Assert.Contains("当前 0", WuxingDynamicGates.CheckFavorability(cost, null)!);
    }

    [Fact]
    public void FavorabilityGate_AllDomains_NamesTheFirstShortfallInElementOrder()
    {
        // 混沌终式跨五域：只要有一域不够就报那一域，玩家才知道该去哪刷。
        // **必须钉"按金木水火土顺序的第一处不足"而不是"数值最低的那一域"**：两种写法在
        // "最差的恰好也是先扫到的"这种数据上看着一样，一旦被改成取最低值，
        // 服务端回绝点名的域与树页提示的域就会分叉（玩家拿"火不够"去刷火，其实卡在金）。
        var cost = WuxingEvolveRules.Parse("{\"favorNeed\":{\"allDomains\":true,\"min\":100}}");
        var favor = new Dictionary<int, int> { [1] = 50, [2] = 130, [3] = 140, [4] = 60, [5] = 101 };

        Assert.Contains("金域好感不足：需 100，当前 50", WuxingDynamicGates.CheckFavorability(cost, favor)!);
        favor[1] = 100;
        Assert.Contains("火域好感不足：需 100，当前 60", WuxingDynamicGates.CheckFavorability(cost, favor)!);
        favor[4] = 100;
        Assert.Null(WuxingDynamicGates.CheckFavorability(cost, favor));
    }

    [Fact]
    public void MaterialGate_ChecksEveryKindAndNamesTheFirstShortfall()
    {
        var cost = WuxingEvolveRules.Parse(
            "{\"materials\":[{\"itemId\":910001,\"count\":6},{\"itemId\":910006,\"count\":3}]}");
        var names = new Dictionary<int, string> { [910001] = "锐金砂", [910006] = "凝金魄" };

        Assert.Contains("锐金砂 需 6，当前 2",
            WuxingDynamicGates.CheckMaterials(cost, new Dictionary<int, int> { [910001] = 2 }, names)!);
        // 第一种齐了才轮到报第二种
        Assert.Contains("凝金魄 需 3",
            WuxingDynamicGates.CheckMaterials(cost, new Dictionary<int, int> { [910001] = 6, [910006] = 1 }, names)!);
        Assert.Null(WuxingDynamicGates.CheckMaterials(
            cost, new Dictionary<int, int> { [910001] = 6, [910006] = 3 }, names));
        // 名字表没给也不能崩，退回报 id
        Assert.Contains("物品 910001", WuxingDynamicGates.CheckMaterials(cost, null)!);
    }

    [Fact]
    public void AttributeGate_UsesTheRealAllocationFieldsAndFailsClosed()
    {
        var cost = WuxingEvolveRules.Parse("{\"attrNeed\":{\"strength\":24,\"intelligence\":48}}");

        Assert.Null(WuxingDynamicGates.CheckAttributes(cost,
            new AttributeAllocationData { Strength = 30, Intelligence = 60 }));
        Assert.Contains("神识不足：需 48，当前 10", WuxingDynamicGates.CheckAttributes(
            cost, new AttributeAllocationData { Strength = 30, Intelligence = 10 })!);
        // 读不到属性时不能放行——与"缺上下文"同一口径
        Assert.NotNull(WuxingDynamicGates.CheckAttributes(cost, null));
        // 不带属性门槛的行不受影响
        Assert.Null(WuxingDynamicGates.CheckAttributes(WuxingEvolveRules.LearnCost.None, null));
    }

    [Fact]
    public void GateParse_ReadsAllThreeNewFamiliesFromLearnCost()
    {
        var cost = WuxingEvolveRules.Parse(
            "{\"grade\":6,\"consume\":[10502,30502],\"loopNeed\":20," +
            "\"favorNeed\":{\"domain\":2,\"min\":80}," +
            "\"materials\":[{\"itemId\":910002,\"count\":5}],\"attrNeed\":{\"health\":36}}");

        Assert.Equal(2, cost.FavorDomain);
        Assert.Equal(80, cost.FavorMin);
        Assert.False(cost.FavorAllDomains);
        Assert.Single(cost.Materials!);
        Assert.Equal(910002, cost.Materials![0].ItemId);
        Assert.Equal(5, cost.Materials![0].Count);
        Assert.Equal(36, cost.HealthNeed);
        Assert.Equal(20, cost.LoopNeed);                    // §6.2 的老键不受影响
        Assert.True(cost.NeedsDynamicContext);
        Assert.False(WuxingEvolveRules.LearnCost.None.NeedsDynamicContext);
    }

    // ===================================================================
    // ⑦ 刷怪产出：物品模板 → 按五行域的掉落表（§14.2 产出侧）
    // ===================================================================

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("不是 JSON", 0)]
    [InlineData("[1,2]", 0)]
    [InlineData("{\"via\":[\"quest\"],\"domain\":3}", 0)]     // 只靠任务产出，不进掉落表
    [InlineData("{\"via\":[\"loot\"],\"domain\":9}", 0)]      // 域号越界
    [InlineData("{\"via\":[\"loot\",\"quest\"],\"domain\":4}", 4)]
    public void LootDomainParse_OnlyAcceptsCombatSourcedValidDomain(string? detail, int expected)
        => Assert.Equal(expected, Horizon.Orleans.Silo.Startup.WuxingLootTables.ReadLootDomain(detail));

    [Fact]
    public void LootTables_ExcludeZeroRateAndNonCombatRows()
    {
        var rows = new List<Horizon.Model.GameModel.ItemTemplateEntity>
        {
            LootRow(910011, 2, 0.10, 2, "{\"via\":[\"loot\"],\"domain\":1}"),   // 信物：进表
            LootRow(910001, 3, 0.12, 2, "{\"via\":[\"loot\",\"gather\"],\"domain\":1}"), // 灵材：进表
            // 白阶书：生成器把它的 source_type 写成 1（任务产出），但 via 含 loot。
            // 需求要的就是"既能在任务里拿到、也能刷怪掉"，所以单值的 source_type 不许当闸门——
            // 用它当闸门会静默砍死"刷怪获得初级技能书"这一整条设计。
            LootRow(810101, 6, 0.08, 1, "{\"via\":[\"quest\",\"loot\"],\"domain\":1}", sourceType: 1),
            LootRow(870101, 6, 0d, 5, "{\"via\":[\"evolve\"],\"domain\":1}"),   // 道阶书：不可拾取
            LootRow(890001, 6, 0.005, 5, "{\"via\":[\"worldBoss\"],\"domain\":0}"), // 混沌：域号缺失，不进任何表
            LootRow(500001, 3, 0.50, 1, "{\"via\":[\"gather\"],\"domain\":2}", sourceType: 1),   // via 不含 loot：不是刷出来的东西，不许进表
            LootRow(850101, 6, 0.01, 4, "{\"via\":[\"loot\"],\"domain\":1}"),   // 天阶书：稀有度高，走保底、一次一张
        };

        var tables = Horizon.Orleans.Silo.Startup.WuxingLootTables.Build(rows);

        // 混沌那本走 worldBoss 且无域号，落进"世界 BOSS 总表"（表 ID = 100）——
        // 它不再被无声丢弃：天阶/混沌只有世界 BOSS 一路产出，而道阶演化又要求天阶练满。
        Assert.Equal(new[] { 1, 100 }, tables.Keys.ToArray());
        Assert.Equal(new[] { 810101, 850101, 910001, 910011 }, tables[1].Entries.Select(e => e.ItemId).ToArray());
        Assert.Equal(1, tables[1].TableId);                                   // 表 ID 就是五行域编号
        // 保底跟着稀有度走，而不是跟着"是不是书"走：白阶书 (rarity 1) 不该被保底照顾
        Assert.False(tables[1].Entries.Single(e => e.ItemId == 810101).IsPityEligible);
        Assert.True(tables[1].Entries.Single(e => e.ItemId == 850101).IsPityEligible);
        Assert.Equal(1, tables[1].Entries.Single(e => e.ItemId == 850101).MaxCount);
        Assert.Equal(2, tables[1].Entries.Single(e => e.ItemId == 910001).MaxCount);
        // 域号写成字符串（手工编辑过的行）不能让装配抛出来
        Assert.Equal(0, Horizon.Orleans.Silo.Startup.WuxingLootTables.ReadLootDomain("{\"via\":[\"loot\"],\"domain\":\"3\"}"));
    }

    [Fact]
    public void LootTables_BuiltFromTheShippedSeed_HaveFiveDomainsOfFifteenRows()
    {
        // 真数据对账：种子里每一域应当给出 12 本可刷的书（白/黄/玄/地 = 4 阶 × 3 境）+ 2 种灵材 + 1 件信物 = 15 条。
        // 少一条意味着生成器把某阶的 via/domain 写错了，表现是"某一域刷半天什么都不掉"。
        // 注意这里必须把 source_type 一起喂进去：这个测试曾经给它兜了个默认值 2，
        // 于是"白/黄阶书在种子里其实标的是任务产出"这件事完全照不出来。
        string sql = ReadSeedSql(FindRepoRoot());
        var rows = new List<Horizon.Model.GameModel.ItemTemplateEntity>();
        foreach (var item in ParseInsertRows(sql, ItemTemplateTable))
        {
            var cols = item.Cols;
            rows.Add(LootRow(
                int.Parse(item.Vals[cols.IndexOf("item_id")]),
                int.Parse(item.Vals[cols.IndexOf("item_type")]),
                double.Parse(item.Vals[cols.IndexOf("drop_rate")], System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(item.Vals[cols.IndexOf("rarity")]),
                Unquote(item.Vals[cols.IndexOf("source_detail")]),
                sourceType: int.Parse(item.Vals[cols.IndexOf("source_type")])));
        }

        var tables = Horizon.Orleans.Silo.Startup.WuxingLootTables.Build(rows);

        Assert.Equal(new[] { 1, 2, 3, 4, 5, 100, 101, 102, 103, 104, 105 }, tables.Keys.ToArray());
        foreach (var domain in new[] { 1, 2, 3, 4, 5 })
        {
            // 世界 BOSS 表必须恰好给出该域天阶三式：这是"超越天阶"唯一的上游产出。
            // 少一本，道阶演化的"天阶三境练满"就永远差一张，而玩家只看得到"演化条件不满足"。
            var bossIds = tables[100 + domain].Entries.Select(e => e.ItemId).ToList();
            Assert.Equal(3, bossIds.Count);
            for (var realm = 1; realm <= 3; realm++)
                Assert.Contains(800000 + domain * 10000 + 500 + realm, bossIds);

            // 混沌（无域）那本只在总表 100 里，且不许混进任何分域 BOSS 表
            Assert.DoesNotContain(890001, bossIds);
            // 每一域都必须是"有书可刷、有材可采、有信物可换好感"的闭环，少一类就是生成器写错了 via/domain
            var ids = tables[domain].Entries.Select(e => e.ItemId).ToList();
            Assert.Contains(ids, id => id >= 800000 + domain * 10000 && id < 800000 + (domain + 1) * 10000);
            Assert.Equal(2, ids.Count(id => id >= 910001 && id <= 910010));    // 粗炼两种灵材
            Assert.Single(ids, id => id >= 910011 && id <= 910015);             // 一件五行信物

            // 初级书必须刷得出来（§14.2 的第一条产出通路）：白阶 1 境那一本就在里面
            Assert.Contains(800000 + domain * 10000 + 101, ids);
            // 白~地四阶都算"可刷到的书"，天阶走 worldBoss、道阶靠演化、混沌靠世界 BOSS，都不在这 12 本里
            Assert.Equal(12, ids.Count(id => id >= 800000 + domain * 10000 && id < 800000 + (domain + 1) * 10000));
            Assert.Equal(15, ids.Count);
        }

        // 道阶书不许出现在任何掉落表里：它只能由相生演化得到。
        var all = tables.Values.SelectMany(t => t.Entries).Select(e => e.ItemId).ToHashSet();
        Assert.DoesNotContain(870101, all);

        // 混沌终式允许产出，但**只许在世界 BOSS 总表（100）里**：
        // 它一旦混进日常小怪表，"最终式靠世界 BOSS"这条设计当场作废，而且没人会报错。
        Assert.Contains(890001, tables[100].Entries.Select(e => e.ItemId));
        foreach (var kv in tables)
        {
            if (kv.Key == 100) continue;
            Assert.True(!kv.Value.Entries.Any(e => e.ItemId == 890001),
                $"混沌秘籍串进了表 {kv.Key}");
        }
    }

    /// <summary>
    /// §14 的"纯动态条件"必须在**数据**里成立，而不只是判定代码里支持这些条件。
    /// <para>
    /// 判定代码有 G1/G5 的实机两侧证据（缺时被拒 + 补齐放行），但那是逐条挑出来的行；
    /// 这里钉的是全部 91 行的分布：哪一阶该带哪种门槛、哪一阶不该带。
    /// 生成器自己有同类自检，可它跑在有人手动改种子的时刻之外——种子被手改过一位数字，
    /// 表现就是某一阶的书突然不需要好感/材料，玩法梯度塌掉而没人会去看 JSON。
    /// </para>
    /// </summary>
    [Fact]
    public void Seed_DynamicGates_HoldThePerGradeMappingForAllNinetyOneRows()
    {
        string sql = ReadSeedSql(FindRepoRoot());
        var rows = ParseInsertRows(sql, SkillTemplateTable);
        Assert.Equal(91, rows.Count);

        var favorCount = 0;
        var materialCount = 0;
        var attrCount = 0;
        var evolveCount = 0;

        foreach (var row in rows)
        {
            var cols = row.Cols;
            var id = int.Parse(row.Vals[cols.IndexOf("skill_id")]);
            var grade = int.Parse(row.Vals[cols.IndexOf("skill_grade")]);
            var json = Unquote(row.Vals[cols.IndexOf("learn_cost")]);

            using var doc = JsonDocument.Parse(json);
            var gates = doc.RootElement;
            gates.TryGetProperty("favorNeed", out var favor);
            gates.TryGetProperty("materials", out var materials);
            gates.TryGetProperty("attrNeed", out var attr);
            gates.TryGetProperty("consume", out var consume);
            gates.TryGetProperty("loopNeed", out var loopNeed);

            var hasFavor = favor.ValueKind == JsonValueKind.Object;
            var hasMaterials = materials.ValueKind == JsonValueKind.Array && materials.GetArrayLength() > 0;
            var hasAttr = attr.ValueKind == JsonValueKind.Object;
            var hasConsume = consume.ValueKind == JsonValueKind.Array && consume.GetArrayLength() > 0;
            if (hasFavor) favorCount++;
            if (hasMaterials) materialCount++;
            if (hasAttr) attrCount++;
            if (hasConsume) evolveCount++;

            switch (grade)
            {
                case <= 2:
                    // 白/黄：书由任务与刷怪给，不该再叠好感/材料门槛（§14.2 的"初级"就是这个意思）
                    Assert.True(!hasFavor && !hasMaterials && !hasAttr,
                        $"白/黄阶技能 {id} 不该带动态门槛：favor={hasFavor} materials={hasMaterials} attr={hasAttr}");
                    break;
                case 3:
                    // 玄：中级门槛是好感，且必须是**本行那一域**的好感（跨域好感不能开门）
                    Assert.True(hasFavor && !hasMaterials, $"玄阶技能 {id} 应以域好感为门槛");
                    AssertFavorDomain(id, favor, cols, row.Vals);
                    Assert.True(favor.GetProperty("min").GetInt32() >= 20, $"玄阶 {id} 好感门槛低于 20");
                    break;
                case 4:
                    // 地：高级门槛换成特殊材料（这才是"高级技能靠材料"那一档）
                    Assert.True(hasMaterials && !hasFavor, $"地阶技能 {id} 应以材料为门槛");
                    break;
                case 5:
                    // 天：材料与好感双限（越往上越贵，但仍不需门派——需求已取消）
                    Assert.True(hasMaterials && hasFavor, $"天阶技能 {id} 应同时要材料与好感");
                    Assert.True(favor.GetProperty("min").GetInt32() >= 55, $"天阶 {id} 好感门槛低于 55");
                    break;
                case 6:
                    // 道阶 = 融合式：材料 + 好感 + 基础属性 + **技能树完整性**（consume 两式、周天次数）
                    Assert.True(hasMaterials && hasFavor && hasAttr,
                        $"道阶技能 {id} 应同时要材料/好感/基础属性");
                    Assert.True(hasConsume, $"道阶技能 {id} 必须回指被融合的两式（consume）");
                    Assert.True(loopNeed.GetInt32() > 0, $"道阶技能 {id} 缺周天次数门槛");
                    AssertFavorDomain(id, favor, cols, row.Vals);
                    break;
                case 7:
                    // 混沌终式跨全域：好感必须是 allDomains，且要有"圆满对数"这条树完整性门槛
                    Assert.True(hasFavor && favor.GetProperty("allDomains").GetBoolean(),
                        $"混沌终式 {id} 的好感门槛必须覆盖全域");
                    Assert.True(gates.TryGetProperty("needPairsCompleted", out var pairs)
                                && pairs.GetInt32() >= 3,
                        $"混沌终式 {id} 的道阶系圆满对数门槛不足 3 对");
                    Assert.True(hasMaterials, $"混沌终式 {id} 应有跨全域材料门槛");
                    break;
            }

            // 门派限制已取消：任何一阶都不许再往数据里塞 profession 门槛
            Assert.True(!gates.TryGetProperty("professionNeed", out _), $"技能 {id} 又出现了门派门槛");
        }

        Assert.Equal(46, favorCount);       // 玄 15 + 天 15 + 道 15 + 混沌 1
        Assert.Equal(46, materialCount);    // 地 15 + 天 15 + 道 15 + 混沌 1
        Assert.Equal(16, attrCount);        // 道 15 + 混沌 1
        Assert.Equal(15, evolveCount);      // 只有道阶回指源技（混沌靠对数门槛，不 consume 具体式）
    }

    /// <summary>门槛里的域号必须等于这一式自己那一行的五行域——跨域好感开门会让"区域往来"失去意义。</summary>
    private static void AssertFavorDomain(int skillId, System.Text.Json.JsonElement favor,
        List<string> cols, List<string> vals)
    {
        if (!cols.Contains("wuxing_element")) return;      // 老种子没这列时不硬断（域号已另由 id 段约束）
        if (favor.TryGetProperty("allDomains", out var all) && all.ValueKind == JsonValueKind.True) return;

        var element = int.Parse(vals[cols.IndexOf("wuxing_element")]);
        Assert.True(favor.TryGetProperty("domain", out var domain), $"技能 {skillId} 的好感门槛缺域号");
        Assert.True(element == 0 || domain.GetInt32() == element,
            $"技能 {skillId}（元素 {element}）的好感门槛指向了域 {domain.GetInt32()}");
    }

    /// <summary>
    /// 两份产物必须说同一句话：服务端判定的那张表（011 → 技能模板）与客户端显示用的那份
    /// （<c>skills.json</c>）都是生成器的输出，但客户端只信后者。
    /// <para>
    /// 它们分叉的表现不是"报错"，而是**门槛显示与实际判定不一致**：树页告诉玩家"好感够了"，
    /// 他读卷却被拒；或者树页不显示某项要求，玩家根本不知道该去做什么。§14 那套动态条件的
    /// 全部可玩性都建立在"缺什么提前说"上，所以这条对账是那条需求的最后一道闸门。
    /// </para>
    /// </summary>
    [Fact]
    public void ClientSkillsJson_AgreesWithTheSeedOnIdsLevelsGradesAndEveryGate()
    {
        var root = FindRepoRoot();
        var jsonPath = Path.Combine(root, "HundunWorld", "Content", "GameData", "Skills", "skills.json");
        Assert.True(File.Exists(jsonPath), $"找不到客户端技能表 {jsonPath}");

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(jsonPath));
        var client = new System.Collections.Generic.Dictionary<int, System.Text.Json.JsonElement>();
        foreach (var row in doc.RootElement.GetProperty("Skills").EnumerateArray())
            client[row.GetProperty("id").GetInt32()] = row;

        string sql = ReadSeedSql(root);
        var seedRows = ParseInsertRows(sql, SkillTemplateTable);
        var seed = new System.Collections.Generic.Dictionary<int, (int level, int grade)>();
        foreach (var r in seedRows)
        {
            var cols = r.Cols;
            seed[int.Parse(r.Vals[cols.IndexOf("skill_id")])] =
                (int.Parse(r.Vals[cols.IndexOf("learn_level_require")]),
                 int.Parse(r.Vals[cols.IndexOf("skill_grade")]));
        }

        Assert.Equal(91, client.Count);
        Assert.True(seed.Keys.OrderBy(x => x).SequenceEqual(client.Keys.OrderBy(x => x)),
            "客户端技能表与服务端种子不是同一批 id：少一行就意味着那一式的门槛在 UI 上完全没人显示");

        var bookItemIds = new System.Collections.Generic.HashSet<int>(
            ParseInsertRows(sql, ItemTemplateTable)
                .Where(r => r.Cols.Contains("item_id"))
                .Select(r => int.Parse(r.Vals[r.Cols.IndexOf("item_id")])));

        var gatedRows = 0;
        foreach (var (id, row) in client)
        {
            var (level, grade) = seed[id];
            Assert.Equal(level, row.GetProperty("requiredLevel").GetInt32());
            Assert.Equal(grade, row.GetProperty("grade").GetInt32());

            // 秘籍物品：客户端点背包里那本书要能认得出"读它学哪一式"，id 契约是 800000+技能 id
            var cost = row.GetProperty("learnCost");
            Assert.True(cost.TryGetProperty("bookItemId", out var book), $"技能 {id} 的客户端条目缺 bookItemId");
            Assert.Equal(800000 + id, book.GetInt32());
            Assert.True(bookItemIds.Contains(book.GetInt32()),
                $"技能 {id} 指向秘籍 {book.GetInt32()}，但物品种子里没有这一件");

            // 门槛逐项对齐 §14：客户端拿不到某一族门槛，就等于那族在 UI 上隐形
            var hasFavor = cost.TryGetProperty("favorNeed", out var favor) && favor.ValueKind == System.Text.Json.JsonValueKind.Object;
            var hasMaterials = cost.TryGetProperty("materials", out var mats) && mats.ValueKind == System.Text.Json.JsonValueKind.Array && mats.GetArrayLength() > 0;
            var hasAttr = cost.TryGetProperty("attrNeed", out var attr) && attr.ValueKind == System.Text.Json.JsonValueKind.Object;
            if (hasFavor || hasMaterials || hasAttr) gatedRows++;

            switch (grade)
            {
                case 3: Assert.True(hasFavor, $"玄阶 {id}：客户端没拿到好感门槛"); break;
                case 4: Assert.True(hasMaterials, $"地阶 {id}：客户端没拿到材料门槛"); break;
                case 5: Assert.True(hasFavor && hasMaterials, $"天阶 {id}：客户端门槛不齐"); break;
                case 6:
                    Assert.True(hasFavor && hasMaterials && hasAttr, $"道阶 {id}：三类门槛必须一起显示");
                    Assert.True(cost.TryGetProperty("consume", out var consume)
                                && consume.ValueKind == System.Text.Json.JsonValueKind.Array
                                && consume.GetArrayLength() == 2,
                        $"道阶 {id} 没回指被融合的两式：树页无法显示这两式已封存，玩家会以为它们凭空消失");
                    Assert.True(cost.GetProperty("loopNeed").GetInt32() > 0, $"道阶 {id} 缺周天次数门槛");
                    break;
                case 7:
                    Assert.True(cost.TryGetProperty("needPairsCompleted", out var pairs) && pairs.GetInt32() >= 3,
                        "混沌终式没把\"圆满几对道阶\"这条门槛带给客户端");
                    Assert.True(hasFavor && hasMaterials && hasAttr, "混沌终式三类门槛不齐");
                    break;
            }
        }

        Assert.Equal(61, gatedRows);   // 玄 15 + 地 15 + 天 15 + 道 15 + 混沌 1：至少带一类动态门槛的行数
    }

    /// <summary>造一行物品模板。sourceType 默认 2（战斗产出）——掉落表只认这一类。</summary>
    private static Horizon.Model.GameModel.ItemTemplateEntity LootRow(
        int id, int itemType, double dropRate, int rarity, string sourceDetail, int sourceType = 2)
        => new()
        {
            Id = id,
            ItemType = itemType,
            DropRate = (decimal)dropRate,      // 列本身是 decimal，别让测试用 double 骗过编译器
            Rarity = (byte)rarity,             // 列是 byte
            SourceType = sourceType,
            SourceDetail = sourceDetail,
            ItemName = id.ToString(),
        };

    [Fact]
    public void QuestRewardParse_ReadsItemAndFavorKeysAndDropsDirtyOnes()
    {
        var rewards = Horizon.Orleans.Grains.QuestGrain.ParseRewards(
            "{\"item:810101\":1,\"favor:3\":20,\"exp\":80,\"gold\":\"1000\",\"bogus\":0}");

        Assert.Equal(1, rewards["item:810101"]);
        Assert.Equal(20, rewards["favor:3"]);
        Assert.Equal(80, rewards["exp"]);
        // 字符串数值与非正数一律不进字典：发奖侧把它们当"没有这条键"，而不是让整个任务接不上
        Assert.False(rewards.ContainsKey("gold"));
        Assert.False(rewards.ContainsKey("bogus"));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("not json", 0)]
    [InlineData("[1,2]", 0)]
    public void QuestRewardParse_FailsSoftOnDirtyRewardColumn(string? json, int expectedCount)
        => Assert.Equal(expectedCount, Horizon.Orleans.Grains.QuestGrain.ParseRewards(json).Count);

    /// <summary>
    /// 012 的入门任务发的是 011 建的书与信物。两个种子分文件写，最容易出的错是
    /// "任务引用的物品 id 在物品表里根本没有"——现场表现是交完任务什么也没到手，
    /// 服务端只留一条 warning，玩家和策划都对不上账（011 内部有同款自检，这里是跨文件这一层）。
    /// </summary>
    [Fact]
    public void EntryQuestRewards_ReferenceItemsThatActuallyExistInSeed()
    {
        var root = FindRepoRoot();
        var knownItems = ParseInsertRows(ReadSeedSql(root), ItemTemplateTable)
            .Select(i => int.Parse(i.Vals[i.Cols.IndexOf("item_id")]))
            .ToHashSet();
        string questSql = File.ReadAllText(Path.Combine(root, "scripts", "sql", "012_seed_wuxing_entry_quests.sql"));

        Assert.False(string.IsNullOrWhiteSpace(questSql), "012 入门任务种子不存在");

        for (int domain = 1; domain <= 5; domain++)
        {
            foreach (int stage in new[] { 1, 2, 3 })
                Assert.Contains(800000 + domain * 10000 + 100 + stage, knownItems);   // 该域白阶三本
            Assert.Contains(910010 + domain, knownItems);                              // 该域五行信物
        }

        // 种子的自检段必须真的回查物品表，否则上面那条断言改天就会失去意义
        Assert.Contains("Game_HunduShijie_ItemTemplate", questSql);
        // quest_id 是 IDENTITY 列，显式插值必须包在 IDENTITY_INSERT 里（011 就是这么翻车的）
        Assert.Contains("SET IDENTITY_INSERT dbo.Game_HunduShijie_QuestTemplate ON;", questSql);
        Assert.Contains("SET IDENTITY_INSERT dbo.Game_HunduShijie_QuestTemplate OFF;", questSql);
    }

    /// <summary>
    /// 黄金值回归：种子 91 式的 <c>damage_multiplier</c> 必须逐行等于方案 §3.1 的阶梯公式
    /// <c>round(1.10 × 1.09^k, 2)</c>（混沌终式按 §3.2 钉在 k=18）。
    /// <para>
    /// 为什么值得单独钉：倍率是 D3 之后真正决定伤害的东西（基座 0.25 × 模板倍率），
    /// 而它同时由生成器公式、种子 SQL、服务端模板三处承载。中间任何一次"顺手改个数"都不会
    /// 有报错信号——表现只是某个境界的伤害悄悄变了。这条把数据直接对回设计文档。
    /// </para>
    /// </summary>
    [Fact]
    public void Seed_LadderMultipliers_MatchTheDesignFormulaForAllNinetyOneRows()
    {
        string sql = ReadSeedSql(FindRepoRoot());
        var rows = ParseInsertRows(sql, SkillTemplateTable);
        Assert.Equal(91, rows.Count);

        // 单调性只能在"同一条技系的同一阶内"要求：id 顺序是先走完一行的一整阶再换行，
        // 换行（换技系）时 k 会归零，倍率本来就该回落——跨行比大小会把这条正确的设计当成缺陷。
        var lastInSeriesGrade = new System.Collections.Generic.Dictionary<int, double>();

        foreach (var row in rows)
        {
            var cols = row.Cols;
            int id = int.Parse(row.Vals[cols.IndexOf("skill_id")]);
            int grade = int.Parse(row.Vals[cols.IndexOf("skill_grade")]);
            double actual = double.Parse(row.Vals[cols.IndexOf("damage_multiplier")],
                System.Globalization.CultureInfo.InvariantCulture);

            Assert.True(grade >= 1 && grade <= 7, $"技能 {id} 的阶位越界：{grade}");
            int stage = id % 100;
            Assert.True(stage >= 1 && stage <= 3, $"技能 {id} 的境位越界：{stage}");

            // §3.1：基础式与道阶按展平序号 k 走同一条指数曲线；混沌终式固定在 k=18（它的 id 不带阶位信息）
            int k = grade == 7 ? 18 : 3 * (grade - 1) + (stage - 1);
            double expected = System.Math.Round(1.10 * System.Math.Pow(1.09, k), 2);
            Assert.Equal(expected, actual, 2);

            var key = id / 100;   // 技系+阶的前缀：道阶三对共用 id 段 7xxxx，按 (行,阶) 分组会把它们混成一条
            if (lastInSeriesGrade.TryGetValue(key, out var previous))
                Assert.True(actual >= previous - 1e-9,
                    $"技系前缀 {key} 内倍率回退：{previous} → {actual}（第 {id} 式）");
            lastInSeriesGrade[key] = actual;
        }
    }


    // ===================================================================
    // 工具
    // ===================================================================

    private const string SkillTemplateTable = "Game_HunduShijie_SkillTemplate";
    private const string SkillBookTable = "Game_HunduShijie_SkillBook";
    private const string ItemTemplateTable = "Game_HunduShijie_ItemTemplate";

    private static string ReadSeedSql(string repoRoot) => File.ReadAllText(
        Path.Combine(repoRoot, "scripts", "sql", "011_seed_wuxing_skill_tree.sql"));

    /// <summary>
    /// 抓种子里的单行 INSERT。种子 SQL 是生成器的产物，测试从它的实际文本反查，
    /// 而不是读一个已经折进数据库的视图——生成器改列序、漏列时这里会先响。
    /// </summary>
    private static List<(List<string> Cols, List<string> Vals)> ParseInsertRows(string sql, string table)
        => Regex.Matches(sql, $@"INSERT INTO {table} \(([^)]*)\) VALUES \(([^;]*)\);")
            .Select(m => (Cols: SplitCols(m.Groups[1].Value), Vals: SplitVals(m.Groups[2].Value)))
            .ToList();

    private static List<string> SplitCols(string raw) =>
        raw.Split(',').Select(c => c.Trim()).Where(c => c.Length > 0).ToList();

    /// <summary>按引号与括号深度切 VALUES，避免把 N'金·青锋三式·启锋' 里的逗号当分隔符。</summary>
    private static List<string> SplitVals(string raw)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        int depth = 0;
        bool inString = false;

        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == '\'')
            {
                // SQL 里 '' 是转义的单引号，不算串边界
                if (inString && i + 1 < raw.Length && raw[i + 1] == '\'')
                {
                    current.Append("''");
                    i++;
                    continue;
                }
                inString = !inString;
            }

            if (!inString)
            {
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ',' && depth == 0)
                {
                    parts.Add(current.ToString().Trim());
                    current.Clear();
                    continue;
                }
            }

            current.Append(c);
        }

        if (current.Length > 0) parts.Add(current.ToString().Trim());
        return parts;
    }

    private static string Unquote(string literal)
    {
        if (literal.StartsWith("N'", StringComparison.OrdinalIgnoreCase) && literal.EndsWith("'"))
            literal = literal.Substring(2, literal.Length - 3);
        return literal.Replace("''", "'");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Horizon.sln")))
            dir = dir.Parent;

        Assert.True(dir != null, "找不到仓库根（Horizon.sln）：请把测试装在默认输出目录里跑");
        return dir!.FullName;
    }
}
