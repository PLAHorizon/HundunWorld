using System;
using System.Collections.Generic;
using HundunWorld.Game.RemoteVisibility;
using HundunWorld.Game.RemoteVisibility.Contracts;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 重连恢复状态机（ReconnectResumeStateMachine）单元测试：覆盖 spec 6.4 规则 2/5。
/// </summary>
public class ReconnectResumeStateMachineTests
{
    private static ReconnectResumeStateMachine Build() => new();

    [Fact]
    public void InitialPhase_IsIdle()
    {
        var sm = Build();
        Assert.Equal(ReconnectResumePhase.Idle, sm.CurrentPhase);
        Assert.False(sm.IsRecoveryComplete);
    }

    [Fact]
    public void Disconnected_GoesReconnecting()
    {
        var sm = Build();
        sm.OnDisconnected();
        Assert.Equal(ReconnectResumePhase.Reconnecting, sm.CurrentPhase);
        Assert.False(sm.IsRecoveryComplete);
    }

    [Fact]
    public void FullRecovery_ReachesRecoveryComplete()
    {
        var sm = Build();
        sm.OnDisconnected();                    // Reconnecting
        sm.OnNegotiationDelivered();            // NegotiationSent
        sm.OnBaselineSnapshotApplied();         // BaselineRebuilding
        sm.OnRecoveryVerified(true);            // RecoveryComplete

        Assert.Equal(ReconnectResumePhase.RecoveryComplete, sm.CurrentPhase);
        Assert.True(sm.IsRecoveryComplete);
    }

    [Fact]
    public void ConnectedAlone_DoesNotProduceRecoveryComplete()
    {
        // spec 6.4 规则 2：Connected 本身不构成恢复完成。
        var sm = Build();
        sm.OnDisconnected();
        sm.OnNegotiationDelivered(); // 仅协商送达，无基线重建、无核对

        Assert.NotEqual(ReconnectResumePhase.RecoveryComplete, sm.CurrentPhase);
        Assert.False(sm.IsRecoveryComplete);
    }

    [Fact]
    public void NegotiationFailed_ExceedingRetries_GoesRecoveryFailed()
    {
        var sm = Build();
        sm.OnDisconnected();

        // 超过重试上限 → RecoveryFailed。
        for (int i = 0; i < sm.MaxNegotiationRetries + 2; i++)
        {
            sm.OnNegotiationFailed(ResumeFailReason.ConnectionNotReady);
        }

        Assert.Equal(ReconnectResumePhase.RecoveryFailed, sm.CurrentPhase);
        Assert.Equal(ResumeFailReason.RetryExhausted, sm.LastFailReason);
    }

    [Fact]
    public void BaselineRebuildFailure_GoesRecoveryFailed()
    {
        var sm = Build();
        sm.OnDisconnected();
        sm.OnNegotiationDelivered();
        sm.OnBaselineSnapshotApplied();

        // 应见必见核对未收敛 → RecoveryFailed。
        sm.OnRecoveryVerified(false);

        Assert.Equal(ReconnectResumePhase.RecoveryFailed, sm.CurrentPhase);
        Assert.Equal(ResumeFailReason.BaselineRebuildFailed, sm.LastFailReason);
    }

    [Fact]
    public void ReconnectDuringRecovery_RollsBackToReconnecting()
    {
        var sm = Build();
        sm.OnDisconnected();
        sm.OnNegotiationDelivered();
        sm.OnBaselineSnapshotApplied();

        // 恢复期间再断线 → 回退 Reconnecting（spec 4.2.8、5.6.3 异常 1）。
        sm.OnDisconnected();

        Assert.Equal(ReconnectResumePhase.Reconnecting, sm.CurrentPhase);
        Assert.False(sm.IsRecoveryComplete);
    }

    [Fact]
    public void PhaseChanged_EventFiresWithTimestamp()
    {
        var sm = Build();
        var events = new List<ReconnectResumePhaseSnapshot>();
        sm.PhaseChanged += events.Add;

        sm.OnDisconnected();
        sm.OnNegotiationDelivered();

        Assert.Equal(2, events.Count);
        Assert.Equal(ReconnectResumePhase.Reconnecting, events[0].Phase);
        Assert.Equal(ReconnectResumePhase.NegotiationSent, events[1].Phase);
        Assert.NotEqual(default(DateTimeOffset), events[1].ChangedAt);
        Assert.True(events[1].ChangedAt >= events[0].ChangedAt);
    }

    [Fact]
    public void SubscriberException_IsIsolated()
    {
        var sm = Build();
        sm.PhaseChanged += _ => throw new InvalidOperationException("订阅者异常");

        // 不应抛出（spec 5.6.1 规则 4 相位迁移异常收敛）。
        sm.OnDisconnected();
        Assert.Equal(ReconnectResumePhase.Reconnecting, sm.CurrentPhase);
    }

    [Fact]
    public void OnReconnectRecoveryComplete_ConfirmsWhenComplete()
    {
        var sm = Build();
        sm.OnDisconnected();
        sm.OnNegotiationDelivered();
        sm.OnBaselineSnapshotApplied();
        sm.OnRecoveryVerified(true);

        // 不抛异常。
        sm.OnReconnectRecoveryComplete();
        Assert.True(sm.IsRecoveryComplete);
    }
}