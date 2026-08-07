using System;
using Horizon.Game.Message.Enums;
using HundunWorld.Game.Network;
using HundunWorld.Game.RemoteVisibility.Contracts;

namespace HundunWorld.Game.RemoteVisibility;

/// <summary>
/// 断线/重连观测编排：断线冻结视觉（不销毁 Actor）、恢复完成触发全量核对补建、
/// 重连中保持现状（spec 2.1.3(5)）。
/// </summary>
public sealed class RemoteObservationCoordinator : IRemoteObservationCoordinator
{
    private readonly IReconnectResumeStateMachine _stateMachine;
    private readonly IRemoteVisibilityAudit _audit;
    private readonly NetworkManager _networkManager;

    /// <summary>FlaxActorSyncSystem 暂停开关（由装配方注入设置器）。</summary>
    private Action<bool>? _setActorPaused;

    /// <summary>恢复插值系统动作（由装配方注入，可空）。</summary>
    private Action? _resumeInterpolation;

    /// <summary>已启动标志。</summary>
    private bool _started;

    /// <summary>
    /// 初始化观测编排。
    /// </summary>
    public RemoteObservationCoordinator(
        IReconnectResumeStateMachine stateMachine,
        IRemoteVisibilityAudit audit,
        NetworkManager networkManager)
    {
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
    }

    /// <summary>注入 FlaxActorSyncSystem 暂停开关设置器。</summary>
    public RemoteObservationCoordinator WithActorPauseControl(Action<bool> setter)
    {
        _setActorPaused = setter ?? throw new ArgumentNullException(nameof(setter));
        return this;
    }

    /// <summary>注入恢复插值系统动作。</summary>
    public RemoteObservationCoordinator WithInterpolationResume(Action action)
    {
        _resumeInterpolation = action ?? throw new ArgumentNullException(nameof(action));
        return this;
    }

    /// <inheritdoc />
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;

        // 订阅重连恢复状态机相位变化（spec 2.1.2 事件驱动）。
        _stateMachine.PhaseChanged += OnPhaseChanged;

        // 订阅连接状态事件（断线 → 冻结观测）。
        if (_networkManager != null)
        {
            _networkManager.ConnectionStatusChanged += OnConnectionStatusChanged;
        }
    }

    private void OnConnectionStatusChanged(ConnectionStatus status)
    {
        try
        {
            switch (status)
            {
                case ConnectionStatus.Disconnected:
                    OnConnectionLost();
                    break;
                case ConnectionStatus.Reconnecting:
                    OnReconnecting();
                    break;
            }
        }
        catch (Exception ex)
        {
            // 编排动作异常仅记日志，保持观测链路既有行为（spec 5.6.1 规则 4）。
            System.Diagnostics.Debug.WriteLine($"[RemoteObservationCoordinator] 连接状态编排异常被隔离: {ex.Message}");
        }
    }

    private void OnPhaseChanged(ReconnectResumePhaseSnapshot snapshot)
    {
        try
        {
            switch (snapshot.Phase)
            {
                case ReconnectResumePhase.RecoveryComplete:
                    OnRecoveryCompleted();
                    break;
                case ReconnectResumePhase.Reconnecting:
                    OnReconnecting();
                    break;
                case ReconnectResumePhase.RecoveryFailed:
                    OnReconnecting(); // 恢复失败回退：保持冻结等待全量握手重建。
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RemoteObservationCoordinator] 相位编排异常被隔离: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public void OnConnectionLost()
    {
        // 断线冻结视觉（IsPaused=true，不销毁 Actor，spec 1.5 与存量行为一致）。
        try
        {
            _setActorPaused?.Invoke(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RemoteObservationCoordinator] 冻结视觉异常被隔离: {ex.Message}");
        }

        // 暂停可见性审计告警，避免断线期间误报。
        _audit.Paused = true;

        // 通知状态机进入重连相位。
        _stateMachine.OnDisconnected();
    }

    /// <inheritdoc />
    public void OnRecoveryCompleted()
    {
        // 恢复完成：解除冻结 + 恢复插值 + 全量核对补建（spec 5.6.1 规则 3b）。
        try
        {
            _setActorPaused?.Invoke(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RemoteObservationCoordinator] 恢复视觉异常被隔离: {ex.Message}");
        }

        try
        {
            _resumeInterpolation?.Invoke();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RemoteObservationCoordinator] 恢复插值异常被隔离: {ex.Message}");
        }

        _audit.Paused = false;

        // 全量核对补建：即使恢复期间 ECS 世界与 Actor 均缺失，也在基线重建完成后立即补建。
        _audit.RunReconciliation();
    }

    /// <inheritdoc />
    public void OnReconnecting()
    {
        // 重连中保持现状（不暂停系统，spec 2.1.3(5)）。
    }
}