using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Enums;
using Horizon.Model.GameModel;
using Horizon.Orleans.Grains;
using Horizon.Orleans.Interface;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 3.1 — HP 单一权威契约测试。
/// <para>
/// 验证 CombatGrain 的战斗结算（攻击/技能/受击/复活）经
/// <see cref="ICharacterGrain.RequestHpChangeAsync"/> / <see cref="ICharacterGrain.ResurrectAsync"/>
/// 回写 <c>CharacterInfo.CurrentHealth</c>（唯一持久化权威），消除 HP 双权威残留
/// （旧实现只改 CombatInfo.Health，从不回写角色档案）。
/// </para>
/// </summary>
public class HpSingleAuthorityContractTests
{
    /// <summary>构造 CombatGrain（预置 CombatParticipants 避免 DB mock；注入 characterGrainProvider）。</summary>
    private static (CombatGrain Grain, Mock<ICharacterGrain> CharacterGrain) CreateGrain(
        ulong attackerId, ulong targetId)
    {
        var state = new CombatState();
        state.CombatParticipants[attackerId] = new CombatInfo
        {
            CharacterId = attackerId,
            Health = 1000, MaxHealth = 1000,
            AttackPower = 100, Defense = 20,
            Energy = 100, MaxEnergy = 100,
            DodgeRate = 0f, BlockRate = 0f, CritRate = 0f, CritDamageMultiplier = 1f,
        };
        state.CombatParticipants[targetId] = new CombatInfo
        {
            CharacterId = targetId,
            Health = 500, MaxHealth = 1000,
            AttackPower = 100, Defense = 20,
            Energy = 100, MaxEnergy = 100,
            DodgeRate = 0f, BlockRate = 0f, CritRate = 0f, CritDamageMultiplier = 1f,
        };

        var mockState = new Mock<IPersistentState<CombatState>>();
        mockState.SetupGet(s => s.State).Returns(state);

        var mockCharacterGrain = new Mock<ICharacterGrain>();
        mockCharacterGrain
            .Setup(c => c.RequestElementalHpChangeAsync(It.IsAny<int>(), It.IsAny<ulong>(),
                It.IsAny<Horizon.Orleans.Interface.World.DamageType>(), It.IsAny<int>(),
                It.IsAny<Horizon.Game.Message.Combat.ElementStatVector>(), It.IsAny<Horizon.Game.Message.Combat.ElementStatVector>()))
            // Phase 2：本地 CombatInfo.Health 以裁决结果 CurrentHp 回写（target 初始 500），
            // 故 mock 返回真实变更后 HP，避免硬编码 0 意外触发 CombatGrain 判死路径。
            .ReturnsAsync((int delta, ulong src, Horizon.Orleans.Interface.World.DamageType t, int _,
                Horizon.Game.Message.Combat.ElementStatVector __,
                Horizon.Game.Message.Combat.ElementStatVector ___) =>
                new HpChangeResult(delta, Math.Max(0, 500 + delta), 1000, 500 + delta <= 0, false));

        // CombatGrain : Grain<CombatState> 基类默认 ctor 在无 GrainContext 时抛 NRE（Orleans 10），
        // 故用共享助手无构造创建 + 反射注入字段（GrainTestHarness 统一封装，避免启动 TestCluster 的成本）。
        var grain = (CombatGrain)System.Runtime.Serialization.FormatterServices
            .GetUninitializedObject(typeof(CombatGrain));
        GrainTestHarness.InjectField(grain, "_logger", NullLogger<CombatGrain>.Instance);
        GrainTestHarness.InjectField(grain, "_combatState", mockState.Object);
        GrainTestHarness.InjectField(grain, "_characterContext",
            new Mock<Horizon.Core.Abstract.IDataContext<Horizon.Entities.GameEntityContext, CharacterEntity, long>>().Object);
        GrainTestHarness.InjectField(grain, "_mapper", new Mock<AutoMapper.IMapper>().Object);
        GrainTestHarness.InjectField(grain, "_characterGrainProvider",
            new Func<ulong, ICharacterGrain>(_ => mockCharacterGrain.Object));

        return (grain, mockCharacterGrain);
    }

    [Fact]
    public async Task ProcessAttackAsync_WritesBackHpDelta_ToCharacterAuthority()
    {
        var (grain, characterGrain) = CreateGrain(attackerId: 1, targetId: 2);

        await grain.ProcessAttackAsync(new AttackMessage
        {
            AttackerId = 1,
            TargetId = 2,
            Damage = 50,
            ElementType = 0,
        });

        // 3.1 契约：攻击结算必须回写 CharacterGrain（唯一持久化权威），delta = -实际结算伤害。
        // 具体值由战斗公式（CalculateBaseDamage + 五行攻击行 + 克制 + 防御减免）决定——
        // 契约重点是回写发生 + 方向正确。
        characterGrain.Verify(
            c => c.RequestElementalHpChangeAsync(It.Is<int>(d => d < 0), 1, Horizon.Orleans.Interface.World.DamageType.Physical,
                It.IsAny<int>(), It.IsAny<Horizon.Game.Message.Combat.ElementStatVector>(), It.IsAny<Horizon.Game.Message.Combat.ElementStatVector>()),
            Times.Once);

        // 单一权威的另一半：非五行入口不得同时被调用（否则一次伤害写两次 HP）。
        characterGrain.Verify(
            c => c.RequestHpChangeAsync(It.IsAny<int>(), It.IsAny<ulong>(), It.IsAny<Horizon.Orleans.Interface.World.DamageType>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessSkillCastAsync_WritesBackHpDelta_ToCharacterAuthority()
    {
        var (grain, characterGrain) = CreateGrain(attackerId: 1, targetId: 2);

        await grain.ProcessSkillCastAsync(new SkillCastMessage
        {
            CasterId = 1,
            TargetEntityId = 2,
            SkillId = 1001,
            EnergyCost = 10,
        });

        // 技能结算回写 Internal 伤害类型（delta 负值）。
        characterGrain.Verify(
            c => c.RequestElementalHpChangeAsync(It.Is<int>(d => d < 0), 1, Horizon.Orleans.Interface.World.DamageType.Internal,
                It.IsAny<int>(), It.IsAny<Horizon.Game.Message.Combat.ElementStatVector>(), It.IsAny<Horizon.Game.Message.Combat.ElementStatVector>()),
            Times.Once);
    }

    [Fact]
    public async Task TakeDamageAsync_WritesBackHpDelta_ToCharacterAuthority()
    {
        var (grain, characterGrain) = CreateGrain(attackerId: 1, targetId: 2);

        await grain.TakeDamageAsync(new DamageMessage
        {
            AttackerId = 1,
            VictimId = 2,
            Damage = 30,
            ImpactPosition = new Position(),
        });

        // 受击结算回写负伤害（方向契约：伤害必为负增量）。
        characterGrain.Verify(
            c => c.RequestElementalHpChangeAsync(It.Is<int>(d => d < 0), 1, Horizon.Orleans.Interface.World.DamageType.Physical,
                It.IsAny<int>(), It.IsAny<Horizon.Game.Message.Combat.ElementStatVector>(), It.IsAny<Horizon.Game.Message.Combat.ElementStatVector>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessResurrectAsync_SyncsAuthorityViaResurrect()
    {
        var (grain, characterGrain) = CreateGrain(attackerId: 1, targetId: 2);

        await grain.ProcessResurrectAsync(new ResurrectMessage
        {
            ResurrectedId = 2,
            ResurrectType = 1, // 完全复活
            ResurrectPosition = new Position(),
        });

        // 3.1 契约：复活必须同步 CharacterGrain 权威（ResurrectAsync = 恢复血量 + IsAlive + 位置）。
        characterGrain.Verify(
            c => c.ResurrectAsync(It.IsAny<ResurrectMessage>()),
            Times.Once);
    }

    [Fact]
    public void CharacterGrain_NoLongerExposesTakeDamage_DirectHpMutation()
    {
        // 3.1 清理：ICharacterGrain 不再暴露 TakeDamageAsync（直接扣血绕过 RequestHpChangeAsync 裁决）。
        var iface = typeof(ICharacterGrain);
        Assert.DoesNotContain(iface.GetMethods(), m => m.Name == "TakeDamageAsync");
    }

    [Fact]
    public void CharacterGrain_NoLongerExposesMoveAsync_DirectPositionWrite()
    {
        // 3.2 清理：ICharacterGrain 不再暴露 MoveAsync（老 RPC 移动写路径）。
        // 移动权威已收敛到 ZoneShardGrain + SimWorld（SyncPacketKind.Input → SubmitInputAsync →
        // 权威回放），位置只经 UpdateLastPositionAsync 从 ZoneShard 推入（Redis 读副本）。
        // 接口保留的仅是位置缓存读写（UpdateLastPositionAsync/GetLastPositionAsync），非写路径。
        var iface = typeof(ICharacterGrain);
        Assert.DoesNotContain(iface.GetMethods(), m => m.Name == "MoveAsync");

        // 位置写路径契约：接口只暴露缓存推送，不暴露任意坐标直写。
        var positionWriters = iface.GetMethods()
            .Where(m => m.Name.Contains("Position") && m.ReturnType == typeof(Task))
            .Select(m => m.Name);
        Assert.Equal(new[] { "UpdateLastPositionAsync" }, positionWriters);
    }
}
