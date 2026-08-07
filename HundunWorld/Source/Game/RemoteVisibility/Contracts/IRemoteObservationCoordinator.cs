namespace HundunWorld.Game.RemoteVisibility.Contracts;

/// <summary>
/// 观测编排：断线/重连期间观测链路的暂停、恢复与基线重建编排（实验接口）。
/// </summary>
public interface IRemoteObservationCoordinator
{
    /// <summary>订阅连接状态与重连状态事件，编排观测链路暂停/恢复（装配时调用一次）。</summary>
    void Start();

    /// <summary>重连恢复完成时触发全量核对补建（由状态机 PhaseChanged 驱动）。</summary>
    void OnRecoveryCompleted();

    /// <summary>断线时冻结观测（IsPaused=true，不销毁 Actor）。</summary>
    void OnConnectionLost();

    /// <summary>重连中保持现状（不暂停系统）。</summary>
    void OnReconnecting();
}