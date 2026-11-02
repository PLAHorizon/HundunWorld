using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Horizon.Game.Message.Network;
using Horizon.Model.GameModel;
using Horizon.Orleans.Grains;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans.Runtime;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 精研（<c>UpgradeSkillAsync</c>）必须给**真收益**（§15.4 用户拍板）。
/// <para>
/// 被取代的旧口径是每重把耗气乘 1.1：练到第 10 重，每次施放要多花近一倍内力。
/// 而 §6.2 要求"源式练满"才允许演化——那条前置于是变成过路费，玩家越按设计走越弱。
/// 现在每重省 2%（满重约 -17%，与 §15.9 的 B 档同尺），并且**基数从模板重算**：
/// 在上一次的存储值上连乘会让"改一次系数"变成对老角色的永久失真。
/// </para>
/// </summary>
public class SkillLevelBenefitTests
{
    private const int BaseCost = 246;              // 40503 火·丹雀衔烛 那一档的真实式子
    private const int MaxLevel = 10;

    private static SkillGrain CreateGrain(SkillState state, int skillId, string energyFormula)
    {
        var grain = (SkillGrain)RuntimeHelpers.GetUninitializedObject(typeof(SkillGrain));
        GrainTestHarness.InjectField(grain, "_logger", NullLogger<SkillGrain>.Instance);

        var persistent = new Mock<IPersistentState<SkillState>>();
        persistent.SetupGet(p => p.State).Returns(state);
        GrainTestHarness.InjectField(grain, "_skillState", persistent.Object);

        GrainTestHarness.InjectField(grain, "_templates", new Dictionary<int, SkillTemplateEntity>
        {
            [skillId] = new()
            {
                Id = skillId,
                SkillName = "测试式",
                EnergyCostFormula = energyFormula,
                MaxLevel = MaxLevel,
            },
        });
        return grain;
    }

    private static SkillState StateWith(int skillId, int level, int neiLiCost)
    {
        var state = new SkillState();
        state.LearnedSkills[skillId] = new SkillInfo
        {
            SkillId = skillId,
            SkillName = "测试式",
            Level = level,
            MaxLevel = MaxLevel,
            NeiLiCost = neiLiCost,
            Range = 12f,
        };
        return state;
    }

    /// <summary>反向对照：这条在旧口径（×1.1）下必红——先证明"变便宜"这件事真的被检测到了。</summary>
    [Fact]
    public async Task UpgradingMakesTheSkillCheaper_NotMoreExpensive()
    {
        const int skillId = 40503;
        var state = StateWith(skillId, level: 1, neiLiCost: BaseCost);
        var grain = CreateGrain(state, skillId, "246");

        Assert.True(await grain.UpgradeSkillAsync(skillId));

        int after = state.LearnedSkills[skillId].NeiLiCost;
        Assert.True(after < BaseCost,
            $"练一重之后耗气 {after} 不低于原始 {BaseCost}：精研又在收税了（旧口径是 ×1.1）");
        Assert.Equal((int)Math.Round(BaseCost * 0.98d), after);
    }

    [Fact]
    public async Task UpgradingToFullIsABenefitOfTheSameOrderAsOneGradeStep()
    {
        const int skillId = 40503;
        var state = StateWith(skillId, level: 1, neiLiCost: BaseCost);
        var grain = CreateGrain(state, skillId, "246");

        for (int i = 1; i < MaxLevel; i++) Assert.True(await grain.UpgradeSkillAsync(skillId));

        int full = state.LearnedSkills[skillId].NeiLiCost;
        Assert.Equal(205, full);                                  // 246 × 0.98^9
        Assert.True(full > BaseCost * 0.8, $"满重省了 {(1 - full / (double)BaseCost) * 100:0.#}%，过头了：别把精研做成第二套倍率");
    }

    /// <summary>
    /// 基数必须来自模板，不能来自上一次的存储值：
    /// 否则同一份存档在新旧系数之间会漂移，且系数一改就永久失真。
    /// </summary>
    [Fact]
    public async Task CostIsRecomputedFromTheTemplateSoRepeatedDerivationDoesNotDrift()
    {
        const int skillId = 10101;
        var state = StateWith(skillId, level: 1, neiLiCost: BaseCost);
        var grain = CreateGrain(state, skillId, "246");

        await grain.UpgradeSkillAsync(skillId);
        int atLevel2 = state.LearnedSkills[skillId].NeiLiCost;

        // 手工把存储值改成"连乘口径"下的结果，再从第 2 重往上练：重算必须把它拉回模板基线上。
        state.LearnedSkills[skillId].NeiLiCost = (int)(atLevel2 * 1.1);
        await grain.UpgradeSkillAsync(skillId);

        Assert.Equal(SkillGrain.ComputeNeiLiCostAtLevel(BaseCost, 3), state.LearnedSkills[skillId].NeiLiCost);
        Assert.True(state.LearnedSkills[skillId].NeiLiCost < atLevel2,
            "第 3 重应当比第 2 重更便宜：值仍然从模板基数推，而不是在脏值上继续乘");
    }

    /// <summary>取不到模板时宁可不动，也不猜一个基数。</summary>
    [Fact]
    public async Task MissingTemplateLeavesTheStoredCostAlone()
    {
        const int knownId = 10101;
        const int unknownId = 999999;
        var state = StateWith(unknownId, level: 1, neiLiCost: 80);
        var grain = CreateGrain(state, knownId, "246");     // 字典里只有另一式

        Assert.True(await grain.UpgradeSkillAsync(unknownId));

        Assert.Equal(80, state.LearnedSkills[unknownId].NeiLiCost);   // 没被动过
        Assert.Equal(2, state.LearnedSkills[unknownId].Level);        // 但重数照常往上走
    }

    [Theory]
    [InlineData(1, 246)]           // 第 1 重就是基准，不白送
    [InlineData(10, 205)]          // 满重 -17%
    [InlineData(99, 34)]           // 98 步 ×0.98 早就掉到 1 以下的那一侧由下面那条单独测；这里只钉住曲线
    [InlineData(0, 246)]           // 脏等级按第 1 重处理，不许把系数指数算成负的
    public void PureDerivationIsIdempotentAndNeverFree(int level, int expected)
        => Assert.Equal(expected, SkillGrain.ComputeNeiLiCostAtLevel(BaseCost, level));

    /// <summary>下限真被触到时：只能到 1，绝不许变成 0（那等于凭空做出一个免费施放口）。</summary>
    [Fact]
    public void TinyCostFloorsAtOne_NeverAtZero()
    {
        Assert.Equal(1, SkillGrain.ComputeNeiLiCostAtLevel(2, 99));
        Assert.Equal(1, SkillGrain.ComputeNeiLiCostAtLevel(1, 5));
    }

    [Fact]
    public void ZeroOrNegativeBaseStaysNonPositive_InsteadOfBecomingOneCost()
    {
        // 式子本身是 0（无消耗的技能）时不许被下限抬成 1——那是凭空造出一个从来没有的收费。
        Assert.Equal(0, SkillGrain.ComputeNeiLiCostAtLevel(0, 5));
    }

    /// <summary>
    /// 旧口径（每重 ×1.1）已经把一些式子推到 <c>Level == MaxLevel</c> 的死角：
    /// 它们点不到"精研"，那笔虚高耗气就永久留在存档里。读点重推一遍把它们归位。
    /// </summary>
    [Fact]
    public void StaleInflatedCostOnAMaxedRowIsRepairedAtTheReadPoint()
    {
        const int skillId = 40503;
        var stale = new SkillInfo
        {
            SkillId = skillId, Level = MaxLevel, MaxLevel = MaxLevel,
            NeiLiCost = (int)Math.Round(BaseCost * Math.Pow(1.1d, MaxLevel - 1)),   // 旧口径练出来的 581
            Range = 12f,
        };
        var state = new SkillState();
        state.LearnedSkills[skillId] = stale;
        var grain = CreateGrain(state, skillId, "246");

        Assert.Equal(205, grain.EffectiveNeiLiCost(stale));               // 按模板基数与重数推导
        Assert.True(stale.NeiLiCost > 500, "夹具本身必须真的带着脏值，否则这条是空跑");

        // 模板取不到时不猜：照原样收，宁可保留脏值也不要凭空造一个新数字。
        var orphan = new SkillInfo { SkillId = 999999, Level = 3, MaxLevel = MaxLevel, NeiLiCost = 80 };
        Assert.Equal(80, grain.EffectiveNeiLiCost(orphan));
        Assert.Equal(0, grain.EffectiveNeiLiCost(null));
    }
}
