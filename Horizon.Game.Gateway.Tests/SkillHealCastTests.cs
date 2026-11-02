using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.GameModel;
using Horizon.Orleans.Grains;
using Horizon.Orleans.Interface;
using Horizon.Orleans.Interface.World;
using Horizon.Share.Dtos.Games;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans.Runtime;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 效果账本里第一条被真正结算的效果：<c>healPct</c>（G26）。
/// <para>
/// 断言刻意打在**跨 Grain 的那一次调用参数**上（回血量 = 上限 × 比例，走 <c>RequestHpChangeAsync</c>），
/// 而不是打在返回文案上——文案是结果，参数才是"效果真的发生了"的证据。
/// 生效量取服务端返回的 <c>ActualDelta</c>：已达上限时被挡掉的那部分不能算成治疗量，
/// 否则血条没动、回执却写"回复 300 点"，这是最容易被忽略的一种假反馈。
/// </para>
/// </summary>
public class SkillHealCastTests
{
    private const int HealSkillId = 30301;       // 水行·天阶·初（回血式在数据里就落在水行）
    private const int PlainSkillId = 30101;
    private const int CostSkillId = 40402;       // 火·丹雀衔烛·焚身：数据里唯一带 hpCost 的一式（0.1）
    private const ulong CasterId = 4401UL;

    private static (SkillGrain grain, Mock<ICharacterGrain> character, List<(int delta, DamageType type)> hpCalls)
        CreateGrain(string? healEffectsJson, float maxHealth, float currentHealth)
    {
        var grain = (SkillGrain)RuntimeHelpers.GetUninitializedObject(typeof(SkillGrain));
        GrainTestHarness.InjectField(grain, "_logger", NullLogger<SkillGrain>.Instance);

        var state = new SkillState
        {
            LearnedSkills = new Dictionary<int, SkillInfo>
            {
                [HealSkillId] = new SkillInfo { SkillId = HealSkillId, Level = 1, Cooldown = 0, Range = 0f },
                [PlainSkillId] = new SkillInfo { SkillId = PlainSkillId, Level = 1, Cooldown = 0, Range = 0f },
                [CostSkillId] = new SkillInfo { SkillId = CostSkillId, Level = 1, Cooldown = 0, Range = 0f },
            },
            SkillCooldowns = new Dictionary<int, DateTime>(),
            SkillDependencies = new Dictionary<int, List<int>>(),
        };
        var persistent = new Mock<IPersistentState<SkillState>>();
        persistent.SetupGet(s => s.State).Returns(state);
        persistent.Setup(s => s.WriteStateAsync()).Returns(Task.CompletedTask);
        persistent.Setup(s => s.ClearStateAsync()).Returns(Task.CompletedTask);
        GrainTestHarness.InjectField(grain, "_skillState", persistent.Object);

        GrainTestHarness.InjectField(grain, "_skillTemplateContext",
            (IDataContext<GameEntityContext, SkillTemplateEntity, int>?)null);
        GrainTestHarness.InjectField(grain, "_templates", new Dictionary<int, SkillTemplateEntity>
        {
            [HealSkillId] = new SkillTemplateEntity
            {
                Id = HealSkillId,
                SkillName = "沧海回澜",
                WuxingElement = 3,
                MaxLevel = 3,
                SkillEffects = healEffectsJson,
            },
            [PlainSkillId] = new SkillTemplateEntity
            {
                Id = PlainSkillId,
                SkillName = "凝水成冰",
                WuxingElement = 3,
                MaxLevel = 3,
                SkillEffects = "{\"rangeType\":\"Single\",\"aoeRadius\":0}",   // 没有 healPct
            },
            [CostSkillId] = new SkillTemplateEntity
            {
                Id = CostSkillId,
                SkillName = "丹雀衔烛·焚身",
                WuxingElement = 4,
                MaxLevel = 3,
                SkillEffects = "{\"hpCost\":0.1,\"rangeType\":\"Single\"}",
            },
        });

        var hpCalls = new List<(int, DamageType)>();
        var character = new Mock<ICharacterGrain>();
        character.Setup(c => c.GetCharacterInfoAsync(It.IsAny<GameQueryDto>()))
            .ReturnsAsync(new CharacterInfo
            {
                CharacterId = (long)CasterId,
                MaxHealth = maxHealth,
                CurrentHealth = currentHealth,
                IsAlive = true,
            });
        // 服务端只认权威返回的实际变化量：这里让它按请求全额生效，另两条测试单独造"被挡掉"的情形
        character.Setup(c => c.RequestHpChangeAsync(It.IsAny<int>(), It.IsAny<ulong>(), It.IsAny<DamageType>()))
            .ReturnsAsync((int delta, ulong _, DamageType type) =>
            {
                hpCalls.Add((delta, type));
                return new HpChangeResult(delta, (int)(currentHealth + delta), (int)maxHealth, false, false);
            });

        GrainTestHarness.InjectField(grain, "_characterGrainProvider",
            new Func<ulong, ICharacterGrain>(_ => character.Object));

        return (grain, character, hpCalls);
    }

    private static SkillCastMessage Request(int skillId) => new()
    {
        CasterId = CasterId,
        SkillId = skillId,
        TargetEntityId = 0,
    };

    [Fact]
    public async Task HealPercentTurnsIntoAnActualHpRequest()
    {
        var (grain, _, hpCalls) = CreateGrain("{\"healPct\":0.06,\"rangeType\":\"Self\"}", maxHealth: 2000f, currentHealth: 800f);

        var result = await grain.CastSkillAsync(Request(HealSkillId));

        Assert.True(result.Success, result.Message);
        var call = Assert.Single(hpCalls);
        Assert.Equal(120, call.delta);                    // 2000 × 6%
        Assert.Equal(DamageType.Healing, call.type);      // 走治疗语义，不是凭空改血
        Assert.Contains("120", result.Message);           // 回执把真实数字带出去，PIE 上可核对
    }

    [Fact]
    public async Task SkillWithoutHealPercentTouchesNoHp()
    {
        var (grain, _, hpCalls) = CreateGrain(null, maxHealth: 2000f, currentHealth: 800f);

        var result = await grain.CastSkillAsync(Request(PlainSkillId));

        Assert.True(result.Success, result.Message);
        Assert.Empty(hpCalls);
        Assert.Equal("技能施放成功", result.Message);
    }

    /// <summary>
    /// 满血时不跳过请求，但**回执只报服务端认下的那一口**：这里 <c>ActualDelta=0</c>，
    /// 文案必须回到普通"施放成功"。否则血条没动、游戏却说"回复 120 点"，是最难查的假反馈。
    /// </summary>
    [Fact]
    public async Task OverhealReportsWhatTheAuthorityActuallyApplied()
    {
        var (grain, character, hpCalls) = CreateGrain("{\"healPct\":0.06}", maxHealth: 2000f, currentHealth: 2000f);
        // 已满：请求照发，但权威把这一口挡成 0。
        // 注意这里必须继续记账到 hpCalls——重设 Setup 会把上面那个记录器顶掉，
        // 少一行 hpCalls.Add 就会让"Assert.Single(hpCalls)"红成"根本没调用"，把两条不同的事混成一条假失败。
        character.Setup(c => c.RequestHpChangeAsync(It.IsAny<int>(), It.IsAny<ulong>(), It.IsAny<DamageType>()))
            .ReturnsAsync((int delta, ulong _, DamageType type) =>
            {
                hpCalls.Add((delta, type));
                return new HpChangeResult(0, 2000, 2000, false, false);
            });

        var result = await grain.CastSkillAsync(Request(HealSkillId));

        Assert.True(result.Success, result.Message);
        Assert.Single(hpCalls);                                   // 请求照发（由权威裁决），不是本地自作聪明地跳过
        Assert.Equal("技能施放成功", result.Message);                 // 但回执不许写一个没发生的数字
    }

    /// <summary>上限还没建立时不许按"猜出来的上限"回血——与 G15/G24 是同一件事的正反面。</summary>
    [Fact]
    public async Task UncomputedHealthPoolBlocksTheHealEntirely()
    {
        var (grain, _, hpCalls) = CreateGrain("{\"healPct\":0.06}", maxHealth: 0f, currentHealth: 0f);

        await grain.CastSkillAsync(Request(HealSkillId));

        Assert.Empty(hpCalls);
    }

    /// <summary>比例写成 0 或负数不算治疗：数据里"配了个 0"应当与"没配"同义，都不许凭空造血。</summary>
    [Theory]
    [InlineData("{\"healPct\":0}")]
    [InlineData("{\"healPct\":-0.5}")]
    [InlineData("{\"healPct\":\"abc\"}")]
    public async Task NonPositiveOrUnparseableHealRatioDoesNothing(string json)
    {
        var (grain, _, hpCalls) = CreateGrain(json, maxHealth: 2000f, currentHealth: 500f);

        await grain.CastSkillAsync(Request(HealSkillId));

        Assert.Empty(hpCalls);
    }

    // ==================== G27：hpCost（以血为引的代价）====================

    [Fact]
    public async Task HealthCostIsPaidOutOfMaxHealthBeforeTheSkillResolves()
    {
        var (grain, _, hpCalls) = CreateGrain(null, maxHealth: 2000f, currentHealth: 1500f);

        var result = await grain.CastSkillAsync(Request(CostSkillId));

        Assert.True(result.Success, result.Message);
        var call = Assert.Single(hpCalls);
        Assert.Equal(-200, call.delta);                     // 2000 × 10%，作为扣血传出去
        Assert.Equal(DamageType.Internal, call.type);       // 自伤语义，不是治疗
        Assert.Contains("200", result.Message);             // 回执带出真实数字，PIE 可核对
    }

    /// <summary>
    /// 血不够承代价时必须拒绝施放 —— 这条是"以血为引"与"自杀按钮"的分界。
    /// 恰好等于当前血也算不够（扣完就 0，0 血在权威那边就是死）。
    /// </summary>
    [Fact]
    public async Task CastIsRefusedWhenTheCostWouldDropTheCasterToZero()
    {
        var (grain, _, hpCalls) = CreateGrain(null, maxHealth: 2000f, currentHealth: 200f);

        var result = await grain.CastSkillAsync(Request(CostSkillId));

        Assert.False(result.Success);
        Assert.Contains("气血不足", result.Message);
        Assert.Empty(hpCalls);                              // 拒绝的那一次不该已经扣了血
    }

    [Fact]
    public async Task CostAboveCurrentHealthIsRefusedWithoutTouchingHp()
    {
        var (grain, _, hpCalls) = CreateGrain(null, maxHealth: 2000f, currentHealth: 120f);

        var result = await grain.CastSkillAsync(Request(CostSkillId));

        Assert.False(result.Success, result.Message);
        Assert.Empty(hpCalls);
    }

    /// <summary>上限未建立时不收费也不拦人：拦就等于让"不存在的池子"（G15）反过来惩罚玩家。</summary>
    [Fact]
    public async Task UncomputedPoolNeitherChargesNorBlocks()
    {
        var (grain, _, hpCalls) = CreateGrain(null, maxHealth: 0f, currentHealth: 0f);

        var result = await grain.CastSkillAsync(Request(CostSkillId));

        Assert.True(result.Success, result.Message);
        Assert.Empty(hpCalls);
    }

    /// <summary>代价被权威挡掉一部分时，回执只报真实发生量（这里 ActualDelta=0 → 回到普通文案）。</summary>
    [Fact]
    public async Task CostReportsOnlyWhatTheAuthorityActuallyApplied()
    {
        var (grain, character, hpCalls) = CreateGrain(null, maxHealth: 2000f, currentHealth: 1500f);
        character.Setup(c => c.RequestHpChangeAsync(It.IsAny<int>(), It.IsAny<ulong>(), It.IsAny<DamageType>()))
            .ReturnsAsync((int delta, ulong _, DamageType type) =>
            {
                hpCalls.Add((delta, type));                 // 重设 Setup 会顶掉记录闭包，这里必须继续记
                return new HpChangeResult(0, 1500, 2000, false, true);   // 被完全抵抗
            });

        var result = await grain.CastSkillAsync(Request(CostSkillId));

        Assert.True(result.Success, result.Message);
        Assert.Single(hpCalls);
        Assert.Equal("技能施放成功", result.Message);
    }

    [Fact]
    public async Task HealAndCostOnTheSameSkillAreTwoSeparateAuthoritativeCalls()
    {
        // 数据里目前没有一式同时带这两个键；这条是防"为了少一次调用而净成一个数"的口径钉子：
        // 净算会让回执说不出"付了多少、回了多少"，也会把抵抗/上限裁剪混成一团。
        var (grain, character, hpCalls) = CreateGrain("{\"healPct\":0.05,\"hpCost\":0.1}", maxHealth: 2000f, currentHealth: 1200f);
        character.Setup(c => c.RequestHpChangeAsync(It.IsAny<int>(), It.IsAny<ulong>(), It.IsAny<DamageType>()))
            .ReturnsAsync((int delta, ulong _, DamageType type) =>
            {
                hpCalls.Add((delta, type));
                return new HpChangeResult(delta, 1200 + delta, 2000, false, false);
            });

        var result = await grain.CastSkillAsync(Request(HealSkillId));   // 这份 JSON 挂在 HealSkillId 的模板上

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, hpCalls.Count);
        Assert.Equal((-200, DamageType.Internal), hpCalls[0]);
        Assert.Equal((100, DamageType.Healing), hpCalls[1]);
        Assert.Contains("自损 200", result.Message);
        Assert.Contains("回复 100", result.Message);
    }
}
