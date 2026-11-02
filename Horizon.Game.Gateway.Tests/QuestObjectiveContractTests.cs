using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Sim;
using Horizon.Game.Sim.Quests;
using Horizon.Orleans.Grains;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 任务目标（objectives）这一路的契约：模板 JSON → Grain 接出来的目标 → DS 侧匹配器认得它。
/// <para>
/// 两处各自持有一个字面量 <c>kill_domain</c>（Grain 侧校验、DS 侧推进），中间没有编译期约束——
/// 所以本文件末尾那条对账测试是唯一的分叉报警器：一旦两边写岔，表现是
/// "任务目标永远推不动，玩家卡在进行中且没有任何提示"。
/// </para>
/// </summary>
[Collection(MonsterContentStaticCollection.Name)]
public class QuestObjectiveContractTests
{
    // ---------------- 匹配器：域限定 ----------------

    /// <summary>水域（域 3）目标只认水域生物。模板 1 = 墨麒麟 = 域 3。</summary>
    [Fact]
    public void DomainObjective_AdvancesOnlyBeastsOfThatDomain()
    {
        var matcher = new KillObjectiveTypeMatcher();
        Assert.Equal(3, MonsterTemplateCatalog.DomainOf(1));

        Assert.True(matcher.ShouldAdvance("kill_domain:3", "猎杀 5 只水域异兽", 1));

        // 域 1（银爪狸）与域 5（石甲狰）都不算数——这正是"杀 5 只狼 + 杀 3 只熊会串"那个老局限的解法。
        Assert.False(matcher.ShouldAdvance("kill_domain:3", "猎杀 5 只水域异兽", 4));
        Assert.False(matcher.ShouldAdvance("kill_domain:3", "猎杀 5 只水域异兽", 2));
    }

    /// <summary>未定域的生物（未登记模板）不许推进任何域目标：不猜域，宁可不推进。</summary>
    [Fact]
    public void DomainObjective_NeverAdvancesOnADomainlessBeast()
    {
        var matcher = new KillObjectiveTypeMatcher();
        Assert.Equal(0, MonsterTemplateCatalog.DomainOf(4242));
        Assert.False(matcher.ShouldAdvance("kill_domain:3", "d", 4242));
    }

    /// <summary>类型串写坏时降级方向是"不推进"，不是"当成任意击杀"。</summary>
    [Theory]
    [InlineData("kill_domain:")]
    [InlineData("kill_domain:0")]
    [InlineData("kill_domain:9")]
    [InlineData("kill_domain:abc")]
    [InlineData("kill_domain")]
    public void MalformedDomainObjective_AdvancesNothing(string objectiveType)
    {
        var matcher = new KillObjectiveTypeMatcher();
        Assert.False(matcher.ShouldAdvance(objectiveType, "d", 1),
            $"配错的 {objectiveType} 不该被当成任意击杀——那样任务会悄悄能交");
    }

    [Fact]
    public void LegacyKillObjective_StillAdvancesOnAnyBeast()
    {
        var matcher = new KillObjectiveTypeMatcher();
        Assert.True(matcher.ShouldAdvance("kill", "击杀 5 只异兽", 1));
        Assert.True(matcher.ShouldAdvance("击杀", "击杀 5 只异兽", 4));

        // 但非击杀类不许被推进
        Assert.False(matcher.ShouldAdvance("collect", "采集 3 份灵材", 1));
    }

    [Fact]
    public void DomainResolver_IsInjectable_ForBeastsOutsideCatalog()
    {
        // 内容以后会搬到配置表；解析走接口，所以那时只换实现不换这段判定。
        var matcher = new KillObjectiveTypeMatcher(new FixedDomainResolver(77, 2));
        Assert.True(matcher.ShouldAdvance("kill_domain:2", "d", 77));
        Assert.False(matcher.ShouldAdvance("kill_domain:3", "d", 77));
    }

    // ---------------- Grain 侧解析 ----------------

    [Fact]
    public void ParseObjectives_ReadsTypeDescriptionAndCount()
    {
        var json = """[{"type":"kill_domain:3","desc":"猎杀 5 只水域异兽","count":5}]""";
        var objectives = QuestGrain.ParseObjectives(json, out var rejected);

        var objective = Assert.Single(objectives);
        Assert.Equal("kill_domain:3", objective.Type);
        Assert.Equal("猎杀 5 只水域异兽", objective.Description);
        Assert.Equal(5, objective.Count);
        Assert.Equal(0, rejected);
    }

    [Theory]
    [InlineData("""[{"type":"kill_domain:9","desc":"d","count":5}]""")]     // 域越界
    [InlineData("""[{"type":"kill_domain:3","desc":"d","count":0}]""")]     // 数量非正
    [InlineData("""[{"type":"","desc":"d","count":5}]""")]                  // 类型缺失
    [InlineData("""[{"desc":"d","count":5}]""")]                            // 根本没有类型
    [InlineData("""[{"type":"kill_domain:3","desc":"d"}]""")]               // 没有数量
    [InlineData("""not json at all""")]                                     // 整份坏掉
    [InlineData("""{"type":"kill_domain:3"}""")]                            // 形状不对（对象不是数组）
    [InlineData("""[{"type":"dance:3","desc":"d","count":5}]""")]           // 语法不认识 = 没有消费方
    [InlineData("""[{"type":"kill_monster:","desc":"d","count":5}]""")]     // 带冒号却没对象 id
    [InlineData("""[{"type":"kill_monster:0","desc":"d","count":5}]""")]    // 对象 id 非正
    [InlineData("""[{"type":"talk_npc:abc","desc":"d","count":5}]""")]      // 对象 id 不是数字
    public void ParseObjectives_RejectsUnusableObjectives(string json)
    {
        var objectives = QuestGrain.ParseObjectives(json, out var rejected);

        // 拒掉的意义在于"接出来的每一条都推得动"，所以这里必须两条都断：没有目标 + 有拒绝计数。
        Assert.Empty(objectives);
        Assert.True(rejected >= 1, "被拒的目标要计入日志，不然任务少一条目标这件事无人知晓");
    }

    /// <summary>
    /// 接任务侧的校验必须和任务编辑侧用**同一份语法**（<c>QuestObjectiveCodec</c>）。
    /// 这条把两边钉在一起：编辑侧新增一种目标动词而 Grain 侧没跟上时，
    /// 表现会是"编辑器写得出的任务，游戏里接不到目标"——两边都"成功"，中间凭空少一路进度。
    /// </summary>
    [Theory]
    [InlineData("kill_domain:1")]
    [InlineData("kill_domain:5")]
    [InlineData("kill_monster:4")]
    [InlineData("talk_npc:1")]
    [InlineData("collect_item:910001")]
    [InlineData("use_item:910011")]
    [InlineData("reach_zone:7")]
    [InlineData("interact_prop:12")]
    public void EveryTypeTheCodecEncodes_IsAcceptedAtLearnTime(string type)
    {
        Assert.True(QuestObjectiveCodec.TryDecode(type, out var spec), $"编辑侧应能编码 {type}");

        var json = "[{\"type\":\"" + type + "\",\"desc\":\"d\",\"count\":3}]";
        var objectives = QuestGrain.ParseObjectives(json, out var rejected);

        var objective = Assert.Single(objectives);
        Assert.Equal(type, objective.Type);
        Assert.Equal(0, rejected);
    }

    [Fact]
    public void LegacyBareKillType_StillPasses_BecauseTheMatcherHonoursIt()
    {
        var objectives = QuestGrain.ParseObjectives("""[{"type":"kill","desc":"击杀 3 只异兽","count":3}]""", out var rejected);

        var objective = Assert.Single(objectives);
        Assert.Equal("kill", objective.Type);
        Assert.Equal(0, rejected);
        Assert.True(new KillObjectiveTypeMatcher().ShouldAdvance(objective.Type, objective.Description, 1));
    }

    [Fact]
    public void ParseObjectives_DropsOnlyTheBadRowAndKeepsTheGoodOnes()
    {
        var json = """
            [{"type":"kill_domain:3","desc":"a","count":5},
             {"type":"kill_domain:8","desc":"b","count":5},
             {"type":"kill","desc":"c","count":2}]
            """;
        var objectives = QuestGrain.ParseObjectives(json, out var rejected);

        Assert.Equal(2, objectives.Count);
        Assert.Equal(1, rejected);
        Assert.Contains(objectives, o => o.Type == "kill_domain:3");
        Assert.Contains(objectives, o => o.Type == "kill");
    }

    [Fact]
    public void ParseObjectives_EmptyOrNull_MeansNoObjectivesAndNoRejection()
    {
        Assert.Empty(QuestGrain.ParseObjectives(null, out var r1));
        Assert.Equal(0, r1);
        Assert.Empty(QuestGrain.ParseObjectives("[]", out var r2));
        Assert.Equal(0, r2);
    }

    // ---------------- 两端契约对账（唯一能发现字面量分叉的测试） ----------------

    [Fact]
    public void EveryObjectiveTypeTheGrainAccepts_IsRecognizedByTheDsMatcher()
    {
        var json = """
            [{"type":"kill_domain:1","desc":"金","count":1},
             {"type":"kill_domain:2","desc":"木","count":1},
             {"type":"kill_domain:3","desc":"水","count":1},
             {"type":"kill_domain:4","desc":"火","count":1},
             {"type":"kill_domain:5","desc":"土","count":1},
             {"type":"kill","desc":"任意","count":1}]
            """;
        var objectives = QuestGrain.ParseObjectives(json, out var rejected);
        Assert.Equal(0, rejected);
        Assert.Equal(6, objectives.Count);

        var matcher = new KillObjectiveTypeMatcher();
        foreach (var row in MonsterTemplateCatalog.RegisteredTemplates())
        {
            foreach (var objective in objectives)
            {
                var shouldAdvance = matcher.ShouldAdvance(objective.Type, objective.Description, row.TemplateId);
                var expected = objective.Type == $"kill_domain:{row.WuxingDomain}" || objective.Type == "kill";
                Assert.True(shouldAdvance == expected,
                    $"目标 {objective.Type} 对模板 {row.TemplateId}（域 {row.WuxingDomain}）判定为 {shouldAdvance}，期望 {expected}");
            }
        }
    }

    private sealed class FixedDomainResolver : IMonsterDomainResolver
    {
        private readonly Dictionary<int, int> _map = new();
        public FixedDomainResolver(int templateId, int domain) => _map[templateId] = domain;
        public int ResolveDomain(int monsterTemplateId) => _map.TryGetValue(monsterTemplateId, out var d) ? d : 0;
    }
}
