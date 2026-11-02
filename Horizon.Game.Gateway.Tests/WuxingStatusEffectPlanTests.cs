using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Horizon.Game.Sim.Skills;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 状态效果**计划层**（方案 §15）的口径闸门。
/// <para>
/// 这一层现在还没有真消费方：把效果写进 <c>CombatState.ActiveEffects</c> 那一步在 Grain 侧，
/// 而 Grain 侧正被"HP 单权威"那条线改（并行会话）。所以本组只钉"计划本身对不对"，
/// 并额外钉住两条最容易被顺手写错的线：
/// ① 有了计划器**不等于**效果已兑现（账本不许动）；
/// ② 数据没配时长时，走的是**写明出处的引擎常量**，而不是"当成永久"（现容器的
///    <c>IsExpired</c> 把 <c>Duration &lt;= 0</c> 判成永久，直通下去就是永久削甲）。
/// </para>
/// </summary>
public class WuxingStatusEffectPlanTests
{
    [Fact]
    public void ControlUsesItsOwnSeconds_NotTheSharedDuration()
    {
        var plan = WuxingStatusEffects.Plan(@"{""stunSec"":1.2,""durationSec"":8}");

        var stun = Assert.Single(plan);
        Assert.Equal(WuxingStatusEffects.StatusKind.Stun, stun.Kind);
        Assert.Equal(1.2, stun.DurationSec, 3);              // 定身 1.2 秒，不是 8 秒
        Assert.True(stun.DurationFromData);
        Assert.True(stun.ToTarget, "控制挂在被打的人身上");
        Assert.Equal(0d, stun.MagnitudePct);                 // 控制没有幅度，不许填一个猜出来的百分比
    }

    [Fact]
    public void MissingOrNonPositiveMagnitudePlansNothing_InsteadOfTreatingItAsZeroPercent()
    {
        Assert.Empty(WuxingStatusEffects.Plan(@"{""vulnerability"":0,""durationSec"":5}"));
        Assert.Empty(WuxingStatusEffects.Plan(@"{""vulnerability"":-0.3,""durationSec"":5}"));
        Assert.Empty(WuxingStatusEffects.Plan(@"{""enemyArmorDown"":""三成""}"));   // 非数值当没配
    }

    /// <summary>数据里 <c>enemyArmorDown</c> 与 <c>vulnerability</c> 各只有 1 行、且都没配 durationSec。</summary>
    [Fact]
    public void MissingDurationFallsBackToTheEngineConstant_AndSaysSo()
    {
        var plan = WuxingStatusEffects.Plan(@"{""vulnerability"":0.15}");

        var vuln = Assert.Single(plan);
        Assert.Equal(WuxingStatusEffects.DefaultProportionalDurationSeconds, vuln.DurationSec, 3);
        Assert.False(vuln.DurationFromData, "必须能看出这个时长是引擎给的，不是内容配的");

        var withData = Assert.Single(WuxingStatusEffects.Plan(@"{""vulnerability"":0.15,""durationSec"":3}"));
        Assert.Equal(3d, withData.DurationSec, 3);
        Assert.True(withData.DurationFromData);
    }

    [Fact]
    public void DamageReductionOnSelfIsNotAttachedToTheTarget()
    {
        var self = Assert.Single(WuxingStatusEffects.Plan(@"{""damageTakenDown"":0.25,""durationSec"":6}"));

        Assert.Equal(WuxingStatusEffects.StatusKind.DamageTakenDown, self.Kind);
        Assert.False(self.ToTarget, "这是给自己穿的减伤，挂错人等于凭空给对手减伤");
        Assert.Equal(0.25, self.MagnitudePct, 3);
    }

    /// <summary>13 行 DOT 都只配了 ratio + 总时长，间隔归引擎常量；配了 tickEvery/tickSec 就以数据为准。</summary>
    [Fact]
    public void DotNeedsRatioAndTotalSeconds_AndItsCadenceFallsBackToTheEngine()
    {
        Assert.Empty(WuxingStatusEffects.Plan(@"{""dotRatio"":0.25}"));               // 没有总时长
        Assert.Empty(WuxingStatusEffects.Plan(@"{""dotSec"":4}"));                     // 没有倍率

        var implied = Assert.Single(WuxingStatusEffects.Plan(@"{""dotRatio"":0.25,""dotSec"":4}"));
        Assert.Equal(WuxingStatusEffects.StatusKind.Dot, implied.Kind);
        Assert.Equal(4d, implied.DurationSec, 3);                                     // 总时长来自 dotSec
        Assert.Equal(WuxingStatusEffects.DefaultDotTickSeconds, implied.TicksEverySec, 3);
        Assert.Equal(0.25d, implied.DamageRatio, 3);
        Assert.True(implied.ToTarget, "DOT 挂在被打的人身上");

        var explicitTick = Assert.Single(WuxingStatusEffects.Plan(
            @"{""dotRatio"":0.2,""dotSec"":6,""tickEvery"":0.5}"));
        Assert.Equal(0.5d, explicitTick.TicksEverySec, 3);
    }

    [Fact]
    public void SlowRequiresAFractionPercent_AndToleratesMissingSecondsViaTheSameFallback()
    {
        // 正向对照先立住：这条数据确实会产生计划，下面几条"没有计划"才不是空转。
        var both = Assert.Single(WuxingStatusEffects.Plan(@"{""slowPct"":0.15,""slowSec"":2}"));
        Assert.Equal(2d, both.DurationSec, 3);
        Assert.True(both.DurationFromData);

        Assert.Empty(WuxingStatusEffects.Plan(@"{""slowPct"":1.5,""slowSec"":2}"));   // 非分数：不是"减速 150%"
        Assert.Empty(WuxingStatusEffects.Plan(@"{""slowPct"":0,""slowSec"":2}"));
        Assert.Empty(WuxingStatusEffects.Plan(@"{""slowSec"":2}"));                   // 只有时长没有幅度

        var noSec = Assert.Single(WuxingStatusEffects.Plan(@"{""pen"":0.3,""slowPct"":0.1,""stackMax"":2}"));
        Assert.False(noSec.DurationFromData);
        Assert.Equal(WuxingStatusEffects.DefaultProportionalDurationSeconds, noSec.DurationSec, 3);
    }

    [Fact]
    public void EmptyAndMalformedJsonPlanNothing_WithoutThrowing()
    {
        Assert.Empty(WuxingStatusEffects.Plan(null));
        Assert.Empty(WuxingStatusEffects.Plan(""));
        Assert.Empty(WuxingStatusEffects.Plan("not json"));
        Assert.Empty(WuxingStatusEffects.Plan("[1,2,3]"));                 // 根不是对象
        Assert.Empty(WuxingStatusEffects.Plan(@"{""stunSec"":""两秒""}"));
    }

    [Fact]
    public void LevelBenefitReadsAFlatKey_AndNestedFormSilentlyMeansZero()
    {
        // 反直觉但必须钉住：ReadNumber 只看根对象，写成 {"perLevel":{...}} 会读成"没配"→ 每级 +0%。
        Assert.Equal(0d, WuxingStatusEffects.ReadLevelDamagePct(@"{""perLevel"":{""damagePerLevel"":0.02}}", 5), 6);
        Assert.Equal(0d, WuxingStatusEffects.ReadLevelDamagePct(null, 9), 6);
        Assert.Equal(0d, WuxingStatusEffects.ReadLevelDamagePct(@"{""damagePerLevel"":0.02}", 1), 6);   // 第 1 重是基准
        Assert.Equal(0.08d, WuxingStatusEffects.ReadLevelDamagePct(@"{""damagePerLevel"":0.02}", 5), 6);
        Assert.Equal(0d, WuxingStatusEffects.ReadLevelDamagePct(@"{""damagePerLevel"":-0.02}", 5), 6);    // 负数当没配
    }

    /// <summary>
    /// 账本不许因为"写了计划器"就往前走一格：这些键此刻仍没有任何 Grain 真在结算。
    /// 接线之后应把它们挪进 <see cref="EffectRole.Applied"/>，而这条测试会当场红一次来提醒改账本
    /// （G26 的同款动作）。
    /// </summary>
    [Fact]
    public void PlanningADoesNotClaimTheKeyIsSettled()
    {
        var notApplied = WuxingSkillEffects.NotAppliedKeysIn(
            @"{""stunSec"":1,""vulnerability"":0.3,""damageTakenDown"":0.2,""durationSec"":5,""dotRatio"":0.2,""dotSec"":4,""tickEvery"":1}");

        foreach (var key in new[] { "stunSec", "vulnerability", "damageTakenDown", "dotRatio" })
            Assert.Contains(key, notApplied);
    }

    /// <summary>
    /// 计划器必须真打在种子数据上：91 行里若一行都解不出状态，这个模块就是死代码
    /// （本仓教训："有写入方但零调用方 = 功能没有入口"）。这里同时按类别点数，
    /// 保证任何一种 StatusKind 都不是没人用的分支。
    /// </summary>
    [Fact]
    public void SeedDataActuallyProducesEveryPlannedKind()
    {
        var text = File.ReadAllText(Path.Combine(FindRepoRoot(), @"scripts/sql/011_seed_wuxing_skill_tree.sql"));
        var blobs = Regex.Matches(text, @"\{[^{}]*""rangeType""[^{}]*\}")
            .Select(m => m.Value).ToHashSet();
        Assert.True(blobs.Count >= 91, $"只解出 {blobs.Count} 行效果 JSON（期望 ≥91）：锚点变了，这条会空跑");

        var counts = blobs.Select(WuxingStatusEffects.Plan).SelectMany(p => p.Select(s => s.Kind))
            .GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());

        foreach (WuxingStatusEffects.StatusKind kind in Enum.GetValues<WuxingStatusEffects.StatusKind>())
            Assert.True(counts.TryGetValue(kind, out var n) && n > 0,
                $"91 行数据里一行都解不出 {kind}：要么键名/口径变了，要么这个分支没人用（该删就别留）");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Horizon.sln"))) dir = dir.Parent;
        Assert.True(dir != null, "找不到仓库根（Horizon.sln）");
        return dir!.FullName;
    }
}
