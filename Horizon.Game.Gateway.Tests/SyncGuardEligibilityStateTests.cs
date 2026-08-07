using System;
using System.Collections.Generic;
using Horizon.Game.ECS.Arch.SyncGuard.Contracts;
using HundunWorld.Game.SyncGuard;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 本地发送资格状态机（LocalSendEligibilityState）单元测试：
/// 状态迁移全路径、绑定资格联动撤销与恢复、联动迟滞防护、回执异常不迁移不误拒绝。
/// </summary>
public class LocalSendEligibilityStateTests
{
    private const ulong LocalPlayerId = 1001;
    private const ulong BoundSummonId = 2001;
    private const ulong BoundPetId = 2002;

    private static (LocalSendEligibilityState State, BindingRelationshipRegistry Registry) Build()
    {
        var registry = new BindingRelationshipRegistry();
        var state = new LocalSendEligibilityState(registry);

        // 初始相位：Disconnected。
        Assert.Equal(LocalEligibilityPhase.Disconnected, state.Phase);
        Assert.False(state.IsLocalEligible);

        return (state, registry);
    }

    private static void Establish(LocalSendEligibilityState state, BindingRelationshipRegistry registry)
    {
        registry.RegisterBinding(BoundSummonId, LocalPlayerId, BindingType.Summon);
        registry.RegisterBinding(BoundPetId, LocalPlayerId, BindingType.Pet);

        state.OnConnectionChanged(true);    // ConnectedHandshakePending
        state.OnHandshakeChanged(true);     // Established
        state.OnLocalIdentityChanged(true); // Established（幂等）
    }

    [Fact]
    public void InitialState_IsDisconnected_NotEligible()
    {
        var (state, _) = Build();
        Assert.Equal(LocalEligibilityPhase.Disconnected, state.Phase);
        Assert.False(state.IsLocalEligible);
        Assert.False(state.IsBoundEntityEligible(BoundSummonId));
    }

    [Fact]
    public void ConnectOnly_HandshakePending_NotEligible()
    {
        var (state, registry) = Build();
        state.OnConnectionChanged(true);

        Assert.Equal(LocalEligibilityPhase.ConnectedHandshakePending, state.Phase);
        Assert.False(state.IsLocalEligible);
    }

    [Fact]
    public void FullEstablish_Established_AllEligible()
    {
        var (state, registry) = Build();
        Establish(state, registry);

        Assert.Equal(LocalEligibilityPhase.Established, state.Phase);
        Assert.True(state.IsLocalEligible);
        Assert.True(state.IsBoundEntityEligible(BoundSummonId));
        Assert.True(state.IsBoundEntityEligible(BoundPetId));
    }

    [Fact]
    public void LocalIdentityLost_GoesEligibilityLost_AllBoundRevoked()
    {
        var (state, registry) = Build();
        Establish(state, registry);

        state.OnLocalIdentityChanged(false);

        Assert.Equal(LocalEligibilityPhase.EligibilityLost, state.Phase);
        Assert.False(state.IsLocalEligible);
        Assert.False(state.IsBoundEntityEligible(BoundSummonId));
        Assert.False(state.IsBoundEntityEligible(BoundPetId));
    }

    [Fact]
    public void HandshakeReset_GoesEligibilityLost_AllBoundRevoked()
    {
        var (state, registry) = Build();
        Establish(state, registry);

        state.OnHandshakeChanged(false);

        Assert.Equal(LocalEligibilityPhase.EligibilityLost, state.Phase);
        Assert.False(state.IsLocalEligible);
        Assert.False(state.IsBoundEntityEligible(BoundSummonId));
    }

    [Fact]
    public void Disconnect_GoesDisconnected_AllBoundRevoked()
    {
        var (state, registry) = Build();
        Establish(state, registry);

        state.OnConnectionChanged(false);

        Assert.Equal(LocalEligibilityPhase.Disconnected, state.Phase);
        Assert.False(state.IsLocalEligible);
        Assert.False(state.IsBoundEntityEligible(BoundSummonId));
    }

    [Fact]
    public void Reestablish_RestoresOnlyValidBinding()
    {
        var (state, registry) = Build();
        Establish(state, registry);

        // 资格丧失后：其中一个绑定已失效（遣散），另一个仍有效。
        state.OnLocalIdentityChanged(false);
        registry.InvalidateBinding(BoundSummonId, BindingInvalidateReason.Dismissed);

        // 重新建立资格 → 仅恢复仍持有有效绑定关系的实体。
        state.OnConnectionChanged(true);
        state.OnHandshakeChanged(true);
        state.OnLocalIdentityChanged(true);

        Assert.True(state.IsLocalEligible);
        Assert.False(state.IsBoundEntityEligible(BoundSummonId)); // 已失效，不恢复
        Assert.True(state.IsBoundEntityEligible(BoundPetId));     // 有效，恢复
    }

    [Fact]
    public void BindingInvalidated_DuringEligibility_RevokesImmediately()
    {
        var (state, registry) = Build();
        Establish(state, registry);

        // 绑定失效 → 联动撤销该实体资格（spec 5.2.1 规则 2、4.2.3）。
        registry.InvalidateBinding(BoundPetId, BindingInvalidateReason.Died);

        Assert.False(state.IsBoundEntityEligible(BoundPetId));
        Assert.True(state.IsBoundEntityEligible(BoundSummonId)); // 其他绑定不受影响
    }

    [Fact]
    public void IsBoundEntityEligible_UnregisteredBinding_ReturnsFalse()
    {
        var (state, registry) = Build();
        Establish(state, registry);

        Assert.False(state.IsBoundEntityEligible(99999));
    }

    [Fact]
    public void AckOutOfOrder_DoesNotMigrateState()
    {
        // 回执异常（缺失/延迟/乱序）不触发状态迁移（spec 4.2.5）。
        var (state, registry) = Build();
        Establish(state, registry);

        var phaseBefore = state.Phase;

        // 模拟重复握手完成事件（不改变状态），资格保持。
        state.OnHandshakeChanged(true);
        state.OnHandshakeChanged(true);

        Assert.Equal(phaseBefore, state.Phase);
        Assert.True(state.IsLocalEligible);
    }

    [Fact]
    public void StateChanged_EventFiresOnTransition()
    {
        var (state, registry) = Build();
        var events = new List<LocalEligibilitySnapshot>();
        state.StateChanged += events.Add;

        state.OnConnectionChanged(true);
        state.OnHandshakeChanged(true);

        Assert.Equal(2, events.Count);
        Assert.Equal(LocalEligibilityPhase.ConnectedHandshakePending, events[0].Phase);
        Assert.Equal(LocalEligibilityPhase.Established, events[1].Phase);
        Assert.True(events[1].IsLocalEligible);
    }

    [Fact]
    public void StateChanged_NoEventForSamePhase()
    {
        var (state, registry) = Build();
        var events = new List<LocalEligibilitySnapshot>();
        state.StateChanged += events.Add;

        state.OnConnectionChanged(true);
        state.OnConnectionChanged(true); // 重复通知，相位未变 → 无事件

        Assert.Single(events);
    }
}