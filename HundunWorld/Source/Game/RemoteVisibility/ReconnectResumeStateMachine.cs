using System;
using System.Threading;
using HundunWorld.Game.RemoteVisibility.Contracts;

namespace HundunWorld.Game.RemoteVisibility;

/// <summary>
/// 重连恢复状态机：维护"协商真实送达 → 基线重建 → 恢复完成"相位迁移。
/// <para>
/// <see cref="ReconnectResumePhase.Connected"/> 本身不构成恢复完成（spec 6.4 规则 2）：
/// <see cref="ReconnectResumePhase.RecoveryComplete"/> 仅在"协商真实送达 + 观测基线重建 + 应见必见核对通过"后迁移。
/// 任一环节异常收敛为 <see cref="ReconnectResumePhase.RecoveryFailed"/> 并触发回退全量握手。
/// </para>
/// </summary>
public sealed class ReconnectResumeStateMachine : IReconnectResumeStateMachine
{
    private readonly object _lock = new();
    private ReconnectResumePhase _phase = ReconnectResumePhase.Idle;
    private ulong _characterId;

    /// <inheritdoc />
    public ReconnectResumePhase CurrentPhase
    {
        get { lock (_lock) { return _phase; } }
    }

    /// <inheritdoc />
    public bool IsRecoveryComplete => CurrentPhase == ReconnectResumePhase.RecoveryComplete;

    /// <summary>最近一次失败原因（观测）。</summary>
    public ResumeFailReason? LastFailReason { get; private set; }

    /// <summary>最近一次相位变化时间。</summary>
    public DateTimeOffset PhaseChangedAt { get; private set; }

    /// <summary>协商发送重试计数（本次恢复会话）。</summary>
    private int _negotiationRetryCount;

    /// <summary>协商重试上限（spec 4.2.9、5.6.1 规则 5，建议 ≤3 次）。</summary>
    public int MaxNegotiationRetries { get; set; } = 3;

    /// <summary>基线重建等待超时（毫秒，超时未收敛宣告失败）。</summary>
    public long BaselineRebuildTimeoutMs { get; set; } = 15000;

    /// <inheritdoc />
    public event Action<ReconnectResumePhaseSnapshot>? PhaseChanged;

    /// <inheritdoc />
    public void OnDisconnected()
    {
        lock (_lock)
        {
            _negotiationRetryCount = 0;
        }
        Transition(ReconnectResumePhase.Reconnecting, "断线检测");
    }

    /// <inheritdoc />
    public void OnNegotiationDelivered()
    {
        lock (_lock)
        {
            _negotiationRetryCount = 0;
        }
        Transition(ReconnectResumePhase.NegotiationSent, "协商真实送达");
    }

    /// <inheritdoc />
    public void OnNegotiationFailed(ResumeFailReason reason)
    {
        lock (_lock)
        {
            _negotiationRetryCount++;

            if (_negotiationRetryCount > MaxNegotiationRetries)
            {
                LastFailReason = ResumeFailReason.RetryExhausted;
                Transition(ReconnectResumePhase.RecoveryFailed, $"协商重试超限({_negotiationRetryCount})");
                return;
            }
        }

        LastFailReason = reason;
        // 未超限：保持 Reconnecting，由外部按退避策略重试。
        System.Diagnostics.Debug.WriteLine($"[ReconnectResumeStateMachine] 协商发送失败(可重试): Reason={reason}, Retry={_negotiationRetryCount}");
    }

    /// <inheritdoc />
    public void OnBaselineSnapshotApplied()
    {
        var phase = CurrentPhase;
        if (phase == ReconnectResumePhase.NegotiationSent || phase == ReconnectResumePhase.Reconnecting)
        {
            Transition(ReconnectResumePhase.BaselineRebuilding, "续连基线/首份全量快照已应用");
        }
        else
        {
            // 已在 BaselineRebuilding：仅记录，不重复迁移。
            System.Diagnostics.Debug.WriteLine("[ReconnectResumeStateMachine] 基线快照持续应用（BaselineRebuilding）");
        }
    }

    /// <inheritdoc />
    public void OnRecoveryVerified(bool converged)
    {
        try
        {
            if (converged)
            {
                var phase = CurrentPhase;
                if (phase == ReconnectResumePhase.BaselineRebuilding || phase == ReconnectResumePhase.NegotiationSent)
                {
                    Transition(ReconnectResumePhase.RecoveryComplete, "应见必见核对通过");
                }
                else if (phase == ReconnectResumePhase.RecoveryComplete)
                {
                    // 已恢复完成，幂等。
                }
            }
            else
            {
                // 应见必见核对未收敛：先进入失败，由编排层回退全量握手重建。
                LastFailReason = ResumeFailReason.BaselineRebuildFailed;
                Transition(ReconnectResumePhase.RecoveryFailed, "可见性核对未收敛");
            }
        }
        catch (Exception ex)
        {
            // 相位迁移内部异常 → 按 RecoveryFailed 收敛，不抛出（spec 5.6.1 规则 4）。
            LastFailReason = ResumeFailReason.BaselineRebuildFailed;
            Transition(ReconnectResumePhase.RecoveryFailed, $"核对异常: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public void OnReconnectRecoveryComplete()
    {
        // 恢复完成后的资格收敛信号：仅确认当前相位，不改变资格判定规则。
        if (IsRecoveryComplete)
        {
            System.Diagnostics.Debug.WriteLine("[ReconnectResumeStateMachine] 恢复完成，资格收敛信号已就绪");
        }
    }

    private void Transition(ReconnectResumePhase newPhase, string reason)
    {
        ReconnectResumePhaseSnapshot snapshot;
        lock (_lock)
        {
            if (_phase == newPhase)
            {
                return;
            }

            _phase = newPhase;
            PhaseChangedAt = DateTimeOffset.UtcNow;
            snapshot = new ReconnectResumePhaseSnapshot(newPhase, PhaseChangedAt, _characterId);
        }

        System.Diagnostics.Debug.WriteLine($"[ReconnectResumeStateMachine] 相位迁移: {newPhase}（{reason}）, At={snapshot.ChangedAt:O}");
        try
        {
            PhaseChanged?.Invoke(snapshot);
        }
        catch (Exception ex)
        {
            // 订阅者异常不扩散（spec 5.6.1 规则 4）。
            System.Diagnostics.Debug.WriteLine($"[ReconnectResumeStateMachine] PhaseChanged 订阅者异常被隔离: {ex.Message}");
        }
    }
}