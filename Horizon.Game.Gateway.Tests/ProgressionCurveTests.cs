using Horizon.Orleans.Grains;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 成长曲线回归（§14 等级门槛能不能被走到的地基）。
/// <para>
/// 这套断言钉的是"节奏"：门槛值写在技能表里（玄阶 21 级等），一旦曲线被改动，
/// 玩家从 1 级爬到某个门槛所需的经验量会整体位移。没有黄金值，改曲线就是一次无人知晓的平衡变更。
/// </para>
/// </summary>
public class ProgressionCurveTests
{
    [Theory]
    [InlineData(1, 100)]
    [InlineData(2, 125)]
    [InlineData(10, 325)]
    [InlineData(20, 575)]
    public void ExpToNext_IsLinearAndMonotonic(int level, long expected)
        => Assert.Equal(expected, ProgressionCurve.ExpToNext(level));

    [Fact]
    public void ExpToNext_NeverDropsBelowFirstStep()
    {
        // 脏等级（0/负数）不许算出比 1→2 更便宜的需求，否则刷一级比刷经验条还快
        Assert.Equal(100L, ProgressionCurve.ExpToNext(0));
        Assert.Equal(100L, ProgressionCurve.ExpToNext(-7));
    }

    [Fact]
    public void ApplyExp_BelowThresholdOnlyAccumulates()
    {
        var (level, exp, gained) = ProgressionCurve.ApplyExp(1, 0, 80);
        Assert.Equal(1, level);
        Assert.Equal(80, exp);
        Assert.Equal(0, gained);
    }

    [Fact]
    public void ApplyExp_CarriesOverflowIntoNextLevel()
    {
        // 1 级需 100：给 130 应升到 2 级并留下 30 点余量
        var (level, exp, gained) = ProgressionCurve.ApplyExp(1, 0, 130);
        Assert.Equal(2, level);
        Assert.Equal(30, exp);
        Assert.Equal(1, gained);
    }

    [Fact]
    public void ApplyExp_CanSkipSeveralLevelsAndCountsThem()
    {
        // 1→2 需 100、2→3 需 125、3→4 需 150，合计 375：给 400 应到 4 级、余 25、连升 3 级
        var (level, exp, gained) = ProgressionCurve.ApplyExp(1, 0, 400);
        Assert.Equal(4, level);
        Assert.Equal(25, exp);
        Assert.Equal(3, gained);
    }

    [Fact]
    public void ApplyExp_StopsAtLevelCapAndStopsAccumulating()
    {
        var (level, exp, gained) = ProgressionCurve.ApplyExp(ProgressionCurve.MaxLevel, 0, 100_000);
        Assert.Equal(ProgressionCurve.MaxLevel, level);
        Assert.Equal(0, exp);              // 满级不攒一个永远花不掉的数
        Assert.Equal(0, gained);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    public void ApplyExp_IgnoresNonPositiveAmount(long amount)
    {
        var (level, exp, gained) = ProgressionCurve.ApplyExp(7, 40, amount);
        Assert.Equal(7, level);
        Assert.Equal(40, exp);
        Assert.Equal(0, gained);
    }

    [Fact]
    public void ApplyExp_ReachesTheHighestSkillGateLevelOnThisCurve()
    {
        // 与技能表的口径对齐：方案 §2.1 的阶梯顶是道阶 Lv300（混沌终式同 300），
        // 而 learn_level_require 实测最大就是 300。曲线的上限必须恰好覆盖它——
        // 低了就"演化到超越天阶"在服务端永远不可能满足，高了就是没人读的空等级。
        Assert.Equal(300, ProgressionCurve.MaxLevel);

        long cumulative = 0;
        for (int lv = 1; lv < ProgressionCurve.MaxLevel; lv++)
            cumulative += ProgressionCurve.ExpToNext(lv);

        var (level, exp, gained) = ProgressionCurve.ApplyExp(1, 0, cumulative);
        Assert.Equal(ProgressionCurve.MaxLevel, level);
        Assert.Equal(0, exp);                       // 满级不攒余量
        Assert.Equal(299, gained);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(30, 1)]
    [InlineData(31, 2)]      // 黄阶起
    [InlineData(76, 3)]      // 玄阶起
    [InlineData(131, 4)]
    [InlineData(201, 5)]
    [InlineData(300, 6)]     // 道阶起，也是等级轴顶
    [InlineData(999, 6)]
    public void RealmForLevel_FollowsTheDesignThresholds(int level, int expectedRealm)
        => Assert.Equal(expectedRealm, ProgressionCurve.RealmForLevel(level));

    [Theory]
    [InlineData(20, 3, true)]    // Lv20 还是白阶，玄阶(3)的门过不去
    [InlineData(76, 3, false)]
    [InlineData(300, 6, false)]
    [InlineData(299, 6, true)]
    [InlineData(1, 0, false)]    // 表里没填境界门槛 = 不判
    [InlineData(1, -1, false)]
    public void RealmBlocker_GatesExactlyAtTheStageStart(int level, int require, bool expectBlocked)
    {
        var blocker = ProgressionCurve.RealmBlocker(level, require);
        if (expectBlocked)
        {
            Assert.NotNull(blocker);
            Assert.StartsWith("境界不足", blocker);
        }
        else
        {
            Assert.Null(blocker);
        }
    }

    [Fact]
    public void RealmBlocker_NamesTheLevelThePlayerIsMissing()
    {
        // 文案要能直接行动：告诉玩家差的是哪一阶、那一阶从几级起，而不是只报一个内部编号
        var blocker = Assert.IsType<string>(ProgressionCurve.RealmBlocker(50, 3));
        Assert.Contains("3 阶", blocker);
        Assert.Contains("Lv76", blocker);
        Assert.Contains("2 阶", blocker);
    }

    [Fact]
    public void EveryStageGateIsReachableOnThisCurve()
    {
        // 方案 §2.1 的六阶起等级必须都在上限以内，否则某一阶的门槛永远读不到
        for (int realm = 1; realm < ProgressionCurve.StageStartLevels.Length; realm++)
        {
            int start = ProgressionCurve.StartLevelForRealm(realm);
            Assert.True(start <= ProgressionCurve.MaxLevel, $"第 {realm} 阶起等级 {start} 超出上限");
            Assert.Equal(realm, ProgressionCurve.RealmForLevel(start));
        }
    }
}
