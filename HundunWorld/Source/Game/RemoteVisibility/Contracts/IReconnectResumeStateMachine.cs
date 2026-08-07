using System;

namespace HundunWorld.Game.RemoteVisibility.Contracts;

/// <summary>
/// 重连恢复相位（spec 6.4 规则 2：Connected 本身不构成恢复完成）。
/// </summary>
public enum ReconnectResumePhase
{
    /// <summary>空闲/正常在线。</summary>
    Idle = 0,

    /// <summary>断线重连中。</summary>
    Reconnecting = 1,

    /// <summary>协商已真实送达（ReconnectResumePacket 已被服务端接收）。</summary>
    NegotiationSent = 2,

    /// <summary>观测基线重建中（服务端续连基线/快照到达并应用）。</summary>
    BaselineRebuilding = 3,

    /// <summary>恢复完成：观测与资格均就绪。</summary>
    RecoveryComplete = 4,

    /// <summary>恢复失败 → 回退全量握手。</summary>
    RecoveryFailed = 5,
}

/// <summary>
/// 重连恢复相位快照：每次相位迁移生成一条，满足 spec 6.4 规则 4 时序可追溯。
/// </summary>
public readonly record struct ReconnectResumePhaseSnapshot(
    ReconnectResumePhase Phase,
    DateTimeOffset ChangedAt,
    ulong RelatedCharacterId);

/// <summary>
/// 重连恢复状态机：维护"协商真实送达 → 基线重建 → 恢复完成"相位迁移，
/// 输出 <see cref="RecoveryComplete"/> 统一恢复信号（观测与资格时序收敛）。
/// </summary>
public interface IReconnectResumeStateMachine
{
    /// <summary>当前重连恢复相位。</summary>
    ReconnectResumePhase CurrentPhase { get; }

    /// <summary>是否已达成恢复完成（观测与资格均就绪）。</summary>
    bool IsRecoveryComplete { get; }

    /// <summary>相位变化事件（含时间戳，供观测编排与日志输出）。</summary>
    event Action<ReconnectResumePhaseSnapshot> PhaseChanged;

    /// <summary>重连成功且协商真实送达后进入基线重建（由 ReconnectResumeSender 通知）。</summary>
    void OnNegotiationDelivered();

    /// <summary>协商发送失败（重试/回退决策入口）。</summary>
    void OnNegotiationFailed(ResumeFailReason reason);

    /// <summary>续连基线/首份全量快照已应用（由 SnapshotApplySystem 通知）。</summary>
    void OnBaselineSnapshotApplied();

    /// <summary>可见性审计核对通过（应见必见）确认恢复完成；未收敛时宣告失败。</summary>
    void OnRecoveryVerified(bool converged);

    /// <summary>断线/重连开始（由编排层在断线检测时调用）。</summary>
    void OnDisconnected();

    /// <summary>重连恢复完成后的资格收敛信号（由编排层在 RecoveryComplete 时驱动资格状态机）。</summary>
    void OnReconnectRecoveryComplete();
}