using System;
using System.Threading;
using System.Threading.Tasks;
using FlaxEngine;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 断线重连管理器
    /// 管理客户端断线检测、自动重连（指数退避）和状态恢复
    /// </summary>
    public class ReconnectionManager : IDisposable
    {
        /// <summary>
        /// 重连状态
        /// </summary>
        public enum ReconnectState
        {
            Unkonw,
            Connected,      // 已连接
            Disconnected,   // 断开
            Reconnecting,   // 重连中
            Failed          // 重连失败
        }

        #region 配置

        /// <summary>
        /// 最大探查尝试次数。
        /// 修复（客户端无限发送连接请求）：原值 10 次过多，5 轮探查仍无法连接网关则永久停止，
        /// 等待用户手动触发（ConnectOnDemandAsync），否则客户端永不再发送任何网络请求。
        /// </summary>
        public int MaxReconnectAttempts { get; set; } = 5;

        /// <summary>
        /// 基础重连间隔（毫秒）
        /// </summary>
        public int BaseReconnectIntervalMs { get; set; } = 1000;

        /// <summary>
        /// 最大重连间隔（毫秒）
        /// </summary>
        public int MaxReconnectIntervalMs { get; set; } = 30000;

        /// <summary>
        /// 退避乘数
        /// </summary>
        public double BackoffMultiplier { get; set; } = 1.5;

        /// <summary>
        /// 心跳超时时间（毫秒）
        /// 修复（远程角色同时不动+同时离线）：
        /// 原值 10 秒在网络抖动（心跳响应丢失）时过于敏感，即使快照持续到达也会误触发断线。
        /// 增大到 30 秒，容忍多次心跳丢失。配合 OnDataReceived 更新 LastHeartbeatTime，
        /// 只有真正收不到任何消息 30 秒才触发断线。
        /// </summary>
        public int HeartbeatTimeoutMs { get; set; } = 30000;

        /// <summary>
        /// 心跳间隔（毫秒）
        /// </summary>
        public int HeartbeatIntervalMs { get; set; } = 3000;

        /// <summary>
        /// 断线后延迟拉起网关探查的时间（毫秒）。
        /// 修复（远程角色同时不动+同时离线）：
        /// 原值 60 秒太长，断线后远程角色 60 秒不动，加上 90 秒 StaleEntityTimeout → 同时清理。
        /// 缩短到 5 秒，快速重连，减少快照通道断开时间。
        /// </summary>
        public int ReconnectDelayMs { get; set; } = 5000;

        /// <summary>
        /// 断线超时阈值（毫秒）：超过此时间仍未重连成功，停止所有自动重连尝试，
        /// 触发 <see cref="OnDisconnectTimeout"/> 事件通知上层退回角色选择界面。
        /// 退回后不再发起任何网络连接请求，直到用户主动选择进入游戏时才按需拉起连接。
        /// 默认 60 秒。
        /// </summary>
        public int DisconnectTimeoutMs { get; set; } = 60000;

        #endregion

        #region 状态

        /// <summary>
        /// 当前重连状态
        /// </summary>
        public ReconnectState CurrentState { get; internal set; } 

        /// <summary>
        /// 当前重连尝试次数
        /// </summary>
        public int CurrentAttemptCount { get; private set; }

        /// <summary>
        /// 最后断线时间
        /// </summary>
        public DateTime LastDisconnectTime { get; private set; }

        /// <summary>
        /// 最后心跳时间
        /// </summary>
        public DateTime LastHeartbeatTime { get; private set; } = DateTime.UtcNow;

        /// <summary>
        /// 最后确认的消息序列号
        /// </summary>
        public long LastAcknowledgedSequence { get; set; }

        /// <summary>
        /// 会话令牌
        /// </summary>
        public string SessionToken { get; set; } = "";

        #endregion

        #region 事件

        /// <summary>
        /// 断线事件
        /// </summary>
        public event Action OnDisconnected;

        /// <summary>
        /// 重连开始事件
        /// </summary>
        public event Action<int> OnReconnectAttempt;

        /// <summary>
        /// 重连成功事件
        /// </summary>
        public event Action OnReconnected;

        /// <summary>
        /// 重连失败事件
        /// </summary>
        public event Action OnReconnectFailed;

        /// <summary>
        /// 断线超时事件：断线超过 <see cref="DisconnectTimeoutMs"/> 仍未重连成功时触发。
        /// 上层收到此事件后应退回角色选择界面，停止所有自动重连，
        /// 等待用户主动选择进入游戏时再按需拉起连接。
        /// </summary>
        public event Action OnDisconnectTimeout;

        /// <summary>
        /// 状态变更事件
        /// </summary>
        public event Action<ReconnectState> OnStateChanged;

        #endregion

        private CancellationTokenSource _reconnectCts;
        private CancellationTokenSource _reconnectDelayCts;
        private CancellationTokenSource _disconnectTimeoutCts;
        private Timer _heartbeatTimer;
        private bool _disposed;
        private volatile bool _reconnectAttemptInProgress;
        /// <summary>断线超时后标记为 true，阻止所有自动重连，直到用户主动触发。</summary>
        private volatile bool _disconnectTimedOut;
        private readonly Func<Task<bool>> _connectFunction;
        private readonly Func<Task> _disconnectFunction;
        /// <summary>本地网络检查函数（由 NetworkManager 注入 NetworkStateMonitor.IsNetworkAvailableAsync）。</summary>
        private readonly Func<Task<bool>> _networkCheckFunction;

        /// <summary>
        /// 连续心跳超时计数。
        /// 修复（客户端无限发送连接请求）：原实现在单次心跳超时（30s）后立即触发 HandleDisconnect，
        /// 网络抖动时频繁触发探查。修复：连续 5 次心跳超时后才触发，并先检查本地网络环境，
        /// 本地网络不可用时不探查（等待 NetworkStateMonitor 通知网络恢复后再触发）。
        /// </summary>
        private int _consecutiveHeartbeatFailures;

        /// <summary>连续心跳超时次数阈值：达到此次数后才触发断线探查。</summary>
        public int HeartbeatFailureThreshold { get; set; } = 5;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="connectFunction">连接函数（仅探查），返回true表示网关可达</param>
        /// <param name="disconnectFunction">断开连接函数，重连前调用以清理旧连接残留（防止幽灵连接）</param>
        /// <param name="networkCheckFunction">本地网络检查函数，心跳连续失败后先检查本地网络环境</param>
        public ReconnectionManager(Func<Task<bool>> connectFunction = null, Func<Task> disconnectFunction = null, Func<Task<bool>> networkCheckFunction = null)
        {
            _connectFunction = connectFunction;
            _disconnectFunction = disconnectFunction;
            _networkCheckFunction = networkCheckFunction;
        }

        /// <summary>
        /// 启动心跳检测
        /// </summary>
        public void StartHeartbeat()
        {
            StopHeartbeat();
            _heartbeatTimer = new Timer(CheckHeartbeat, null, HeartbeatIntervalMs, HeartbeatIntervalMs);
        }

        /// <summary>
        /// 停止心跳检测
        /// </summary>
        public void StopHeartbeat()
        {
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
        }

        /// <summary>
        /// 更新心跳。
        /// 修复（无限重连循环 — 超时计时器被 TCP 连接成功反复取消）：
        /// 只有收到服务端应用层数据（心跳响应/快照等）才证明连接真正可用，
        /// 此时取消断线超时计时器并重置连续心跳失败计数。
        /// </summary>
        public void UpdateHeartbeat()
        {
            LastHeartbeatTime = DateTime.UtcNow;
            _consecutiveHeartbeatFailures = 0; // 收到数据 = 连接可用，重置计数
        
            // 收到服务端数据 = 连接真正可用，取消断线超时计时器
            if (_disconnectTimeoutCts != null)
            {
                CancelDisconnectTimeout();
                _disconnectTimedOut = false;
            }
        }

        /// <summary>
        /// 检查心跳超时。
        /// 修复（客户端无限发送连接请求）：
        /// 原实现在单次心跳超时（30s）后立即触发 HandleDisconnect，网络抖动时频繁触发探查。
        /// 修复：连续 5 次心跳超时后才触发，并先检查本地网络环境：
        /// - 本地网络不可用 → 不探查，等待 NetworkStateMonitor 通知网络恢复后再触发
        /// - 本地网络可用 → 触发 HandleDisconnect 进入探查流程
        /// </summary>
        private void CheckHeartbeat(object state)
        {
            if (CurrentState != ReconnectState.Connected)
                return;
        
            // 永久停止守卫：探查失败后不再自动重连
            if (_disconnectTimedOut)
                return;
        
            var elapsed = (DateTime.UtcNow - LastHeartbeatTime).TotalMilliseconds;
            if (elapsed > HeartbeatTimeoutMs)
            {
                _consecutiveHeartbeatFailures++;
                Debug.Log($"心跳超时: {elapsed:F0}ms，连续失败 {_consecutiveHeartbeatFailures}/{HeartbeatFailureThreshold}");
        
                // 未达到连续失败阈值，等待下次检查
                if (_consecutiveHeartbeatFailures < HeartbeatFailureThreshold)
                    return;
        
                // 连续 5 次心跳超时，先检查本地网络环境
                Debug.LogWarning($"心跳连续 {_consecutiveHeartbeatFailures} 次超时，检查本地网络环境...");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        bool localNetworkOk = true;
                        if (_networkCheckFunction != null)
                        {
                            localNetworkOk = await _networkCheckFunction();
                        }
        
                        if (!localNetworkOk)
                        {
                            // 本地网络不可用，不探查网关，等待 NetworkStateMonitor 通知网络恢复
                            Debug.LogWarning("[心跳检查] 本地网络不可用，不探查网关，等待网络恢复后由 NetworkStateMonitor 触发重连");
                            _consecutiveHeartbeatFailures = 0; // 重置计数，网络恢复后重新计数
                            return;
                        }
        
                        // 本地网络可用，触发断线探查
                        Debug.Log("[心跳检查] 本地网络可用，触发断线探查");
                        // 主动断开旧 TCP 连接，清理服务端残留
                        if (_disconnectFunction != null)
                        {
                            try { await _disconnectFunction(); }
                            catch (Exception ex) { Debug.LogWarning($"心跳超时后断开旧连接异常（忽略）: {ex.Message}"); }
                        }
                        HandleDisconnect();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[心跳检查] 检查本地网络时发生异常: {ex.Message}");
                    }
                });
            }
        }

        /// <summary>
        /// 处理断线。
        /// 修复（客户端无限发送连接请求）：增加 _disconnectTimedOut 永久停止守卫。
        /// 5 轮探查失败后 _disconnectTimedOut=true，此后任何路径（心跳超时/NetworkStateMonitor/TCP 断开）
        /// 触发的 HandleDisconnect 都直接返回，永不再发起自动重连，
        /// 只有用户主动调用 ConnectOnDemandAsync（ResetDisconnectTimeout）才能重新发起连接。
        /// </summary>
        public void HandleDisconnect()
        {
            // 永久停止守卫：5 轮探查失败后不再自动重连
            if (_disconnectTimedOut)
                return;

            // 修复（真正断网后无法恢复 — 重连未拉起）：
            // 原实现 guard 仅拦截 Reconnecting/Failed/_reconnectAttemptInProgress，
            // 不拦截 Disconnected 状态。真正断网时：
            // 1) CheckHeartbeat 触发 HandleDisconnect → 状态变为 Disconnected → 启动延迟重连任务 A
            // 2) OnClientDisconnected（TCP 层）随后触发 HandleDisconnect → guard 不拦截 →
            //    CancelDelayedReconnect 取消任务 A → 启动任务 B
            // 3) 若 OnClientDisconnected 先于 CheckHeartbeat 触发，则相反
            // 两次调用的副作用是延迟重连任务被取消并重启，极端情况下可能反复取消导致重连始终未拉起。
            // 修复：增加 Disconnected 状态拦截。首次 HandleDisconnect 进入 Disconnected 状态并启动延迟重连，
            // 后续重复调用直接返回，不干扰已启动的延迟重连任务。
            if (CurrentState == ReconnectState.Reconnecting
                || CurrentState == ReconnectState.Failed
                || CurrentState == ReconnectState.Disconnected
                || _reconnectAttemptInProgress)
                return;

            LastDisconnectTime = DateTime.UtcNow;
            ChangeState(ReconnectState.Disconnected);
            OnDisconnected?.Invoke();

            // 心跳失败或网关断线后，延迟 ReconnectDelayMs 再拉起网关探查，
            // 避免网络抖动时频繁探查网关。
            StartDelayedReconnect();

            // 启动断线超时计时器：超过 DisconnectTimeoutMs 仍未重连成功则停止所有自动重连，
            // 通知上层退回角色选择界面。
            StartDisconnectTimeout();
        }

        /// <summary>
        /// 启动延迟重连任务：等待 ReconnectDelayMs 后开始探查重连。
        /// 若在等待期间连接恢复（MarkConnected）或被取消，则终止延迟任务，
        /// 不再拉起网关探查（探查到网关/连接成功后即终止所有探查任务）。
        /// </summary>
        private void StartDelayedReconnect()
        {
            CancelDelayedReconnect();

            if (ReconnectDelayMs <= 0)
            {
                _ = StartReconnectAsync();
                return;
            }

            _reconnectDelayCts = new CancellationTokenSource();
            var token = _reconnectDelayCts.Token;

            Debug.Log($"断线后等待 {ReconnectDelayMs}ms 再拉起网关探查");
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(ReconnectDelayMs, token);
                    Debug.Log("断线延迟等待结束，开始拉起网关探查");
                    _ = StartReconnectAsync();
                }
                catch (TaskCanceledException)
                {
                    // 延迟任务被取消（连接已恢复或主动取消），不触发探查
                    Debug.Log("断线延迟重连任务已取消（连接已恢复或被取消），不再拉起网关探查");
                }
            }, token);
        }

        /// <summary>
        /// 启动断线超时计时器。超过 DisconnectTimeoutMs 后取消所有重连并触发 OnDisconnectTimeout。
        /// </summary>
        private void StartDisconnectTimeout()
        {
            CancelDisconnectTimeout();

            if (DisconnectTimeoutMs <= 0)
                return;

            _disconnectTimeoutCts = new CancellationTokenSource();
            var token = _disconnectTimeoutCts.Token;

            Debug.Log($"[ReconnectionManager] 启动断线超时计时器: {DisconnectTimeoutMs}ms");
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(DisconnectTimeoutMs, token);

                    // 超时：停止所有自动重连
                    _disconnectTimedOut = true;
                    CancelDelayedReconnect();
                    _reconnectCts?.Cancel();

                    if (CurrentState != ReconnectState.Connected)
                    {
                        Debug.LogWarning($"[ReconnectionManager] 断线超过 {DisconnectTimeoutMs}ms 仍未重连成功，停止自动重连，通知上层退回角色选择界面");
                        ChangeState(ReconnectState.Failed);
                        OnDisconnectTimeout?.Invoke();
                    }
                }
                catch (TaskCanceledException)
                {
                    // 计时器被取消（重连成功或主动取消），不触发超时
                }
            }, token);
        }

        /// <summary>
        /// 取消断线超时计时器（重连成功或主动取消时调用）。
        /// </summary>
        private void CancelDisconnectTimeout()
        {
            if (_disconnectTimeoutCts != null)
            {
                try { _disconnectTimeoutCts.Cancel(); } catch { }
                try { _disconnectTimeoutCts.Dispose(); } catch { }
                _disconnectTimeoutCts = null;
            }
        }

        /// <summary>
        /// 取消挂起的延迟重连任务。
        /// 在探查/连接成功、主动取消或释放时调用，确保不会重复拉起网关探查。
        /// </summary>
        private void CancelDelayedReconnect()
        {
            if (_reconnectDelayCts != null)
            {
                try { _reconnectDelayCts.Cancel(); } catch { }
                try { _reconnectDelayCts.Dispose(); } catch { }
                _reconnectDelayCts = null;
            }
        }

        /// <summary>
        /// 开始重连
        /// </summary>
        public async Task StartReconnectAsync()
        {
            if (CurrentState == ReconnectState.Reconnecting)
                return;

            // [修复] 先检查 _connectFunction 是否已连接（_connectFunction 内部已检查连接状态），
            // 避免 NetworkStateMonitor 和 ReconnectionManager 双重重连路径竞争时，一方已连接成功
            // 另一方仍启动重连循环。_connectFunction 是 NetworkManager 传入的 lambda，内部有
            // _connectionStatus == Connected 检查，但仍有小概率窗口（_connectFunction 检查时未连接，
            // 但稍后另一方连接成功）。这里直接调用 _connectFunction 尝试一次，如果已连接则直接返回。
            if (_connectFunction != null)
            {
                try
                {
                    var alreadyConnected = await _connectFunction();
                    if (alreadyConnected && MarkConnectedIfConnected())
                    {
                        Debug.Log("[StartReconnectAsync] 连接已存在（由其他路径建立），跳过重连循环");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[StartReconnectAsync] 预检查连接状态异常（忽略，进入重连循环）: {ex.Message}");
                }
            }

            // 探查启动时取消任何挂起的延迟重连任务，避免重复拉起网关探查
            CancelDelayedReconnect();

            var oldCts = _reconnectCts;
            _reconnectCts = new CancellationTokenSource();
            var token = _reconnectCts.Token;

            // Dispose old CTS after creating new one to avoid gap
            if (oldCts != null)
            {
                oldCts.Cancel();
                oldCts.Dispose();
            }

            _reconnectAttemptInProgress = true;
            ChangeState(ReconnectState.Reconnecting);
            CurrentAttemptCount = 0;

            try
            {
                while (CurrentAttemptCount < MaxReconnectAttempts && !token.IsCancellationRequested)
                {
                    CurrentAttemptCount++;

                    Debug.Log($"重连尝试 {CurrentAttemptCount}/{MaxReconnectAttempts}");
                    OnReconnectAttempt?.Invoke(CurrentAttemptCount);

                    try
                    {
                        bool success = false;

                        // [修复] 重连前先断开旧连接，清理可能残留的 TCP 套接字。
                        // 不断开直接 ConnectAsync 可能导致服务端旧连接尚未释放时新连接已建立，
                        // 形成幽灵连接（旧连接不发送数据，被服务端首包超时清理）。
                        if (_disconnectFunction != null)
                        {
                            try
                            {
                                await _disconnectFunction();
                            }
                            catch (Exception disconnectEx)
                            {
                                Debug.LogWarning($"重连前断开旧连接时发生异常（忽略并继续重连）: {disconnectEx.Message}");
                            }
                        }

                        if (_connectFunction != null)
                        {
                            success = await _connectFunction();
                        }

                        if (success)
                        {
                            ChangeState(ReconnectState.Connected);
                            CurrentAttemptCount = 0;
                            LastHeartbeatTime = DateTime.UtcNow;
                            OnReconnected?.Invoke();
                            Debug.Log("重连成功");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"重连尝试 {CurrentAttemptCount} 失败: {ex.Message}");
                    }

                    // 计算退避延迟
                    int delay = CalculateBackoffDelay(CurrentAttemptCount);
                    Debug.Log($"等待 {delay}ms 后重试...");

                    try
                    {
                        await Task.Delay(delay, token);
                    }
                    catch (TaskCanceledException)
                    {
                        return;
                    }
                }
            }
            finally
            {
                _reconnectAttemptInProgress = false;
            }

            // 所有探查尝试都失败 → 永久停止自动重连，等待用户手动触发。
            // 修复（客户端无限发送连接请求）：原实现在 MaxReconnectAttempts 用完后触发 OnReconnectFailed，
            // 但 HandleDisconnect 可能被再次触发（NetworkStateMonitor/心跳超时），导致新一轮探查循环。
            // 修复：设置 _disconnectTimedOut = true 永久阻止所有自动重连路径，
            // 只有用户主动调用 ConnectOnDemandAsync（ResetDisconnectTimeout）才能重新发起连接。
            _disconnectTimedOut = true;
            CancelDisconnectTimeout();
            ChangeState(ReconnectState.Failed);
            OnReconnectFailed?.Invoke();
            OnDisconnectTimeout?.Invoke();
            Debug.LogWarning($"探查失败：已达到最大尝试次数 {MaxReconnectAttempts}，永久停止自动重连，等待用户手动触发");
        }

        /// <summary>
        /// 检查是否已连接，若已连接则标记状态并触发事件。
        /// 用于处理 NetworkStateMonitor 和 ReconnectionManager 双重重连路径竞争的场景：
        /// 当一方已连接成功，另一方调用此方法确认并更新状态。
        /// </summary>
        /// <returns>true 表示已连接并已更新状态；false 表示未连接。</returns>
        private bool MarkConnectedIfConnected()
        {
            // 由 _connectFunction 内部检查连接状态，若已连接则返回 true
            // 此方法仅用于协调状态更新
            if (CurrentState == ReconnectState.Connected)
                return true;

            if (CurrentState == ReconnectState.Reconnecting)
                return false;

            // 不在此处强制改变状态，由 _connectFunction 返回 true 后由调用方处理
            return false;
        }

        /// <summary>
        /// 计算退避延迟
        /// </summary>
        public int CalculateBackoffDelay(int attemptCount)
        {
            var delay = BaseReconnectIntervalMs * Math.Pow(BackoffMultiplier, Math.Min(attemptCount - 1, 10));
            return Math.Min((int)delay, MaxReconnectIntervalMs);
        }

        /// <summary>
        /// 取消重连
        /// </summary>
        public void CancelReconnect()
        {
            CancelDelayedReconnect();
            CancelDisconnectTimeout();
            _reconnectCts?.Cancel();
            if (CurrentState == ReconnectState.Reconnecting)
            {
                ChangeState(ReconnectState.Disconnected);
            }
        }

        /// <summary>
        /// 重置断线超时标记。用户主动触发进入游戏时调用，
        /// 允许重新发起连接请求（按需连接模式）。
        /// </summary>
        public void ResetDisconnectTimeout()
        {
            _disconnectTimedOut = false;
            CancelDisconnectTimeout();
        }

        /// <summary>
        /// 是否已触发断线超时（自动重连已被禁止）。
        /// </summary>
        public bool IsDisconnectTimedOut => _disconnectTimedOut;

        /// <summary>
        /// 标记 TCP 层已连接。
        /// 修复（无限重连循环）：不在此处取消断线超时计时器。
        /// TCP 连接成功不代表连接真正可用（可能无数据流动，服务端首包超时后关闭）。
        /// 只有在 UpdateHeartbeat()（收到服务端应用层数据）时才取消超时计时器，
        /// 防止"TCP连接→无数据→服务端关闭→HandleDisconnect→TCP连接→取消超时→循环"。
        /// </summary>
        public void MarkConnected()
        {
            // 取消延迟重连任务（TCP 已连接，不需要再等待延迟）
            CancelDelayedReconnect();
            // 注意：不取消 CancelDisconnectTimeout()，等 UpdateHeartbeat() 收到数据后再取消

            // [修复] 重试循环中不覆盖 CurrentState（保持 Reconnecting），
            // 防止短暂连接成功后 OnClientDisconnected 触发级联 HandleDisconnect。
            // StartReconnectAsync 退出循环时统一设置最终状态。
            if (_reconnectAttemptInProgress)
            {
                LastHeartbeatTime = DateTime.UtcNow;
                return;
            }

            ChangeState(ReconnectState.Connected);
            CurrentAttemptCount = 0;
            LastHeartbeatTime = DateTime.UtcNow;
        }

        /// <summary>
        /// 获取重连状态摘要
        /// </summary>
        public string GetStatusSummary()
        {
            return $"状态: {CurrentState}\n" +
                   $"尝试次数: {CurrentAttemptCount}/{MaxReconnectAttempts}\n" +
                   $"最后断线: {(LastDisconnectTime == default ? "无" : LastDisconnectTime.ToString("HH:mm:ss"))}\n" +
                   $"最后心跳: {LastHeartbeatTime:HH:mm:ss}\n" +
                   $"最后序列号: {LastAcknowledgedSequence}";
        }

        /// <summary>
        /// 变更状态
        /// </summary>
        private void ChangeState(ReconnectState newState)
        {
            if (CurrentState == newState)
                return;

            CurrentState = newState;
            OnStateChanged?.Invoke(newState);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    StopHeartbeat();
                    CancelDelayedReconnect();
                    CancelDisconnectTimeout();
                    _reconnectCts?.Cancel();
                    _reconnectCts?.Dispose();
                    _reconnectCts = null;

                    // 清理事件委托，防止外部引用残留
                    OnDisconnected = null;
                    OnReconnectAttempt = null;
                    OnReconnected = null;
                    OnReconnectFailed = null;
                    OnDisconnectTimeout = null;
                    OnStateChanged = null;
                }
                _disposed = true;
            }
        }

        /// <summary>
        /// 析构函数
        /// </summary>
        ~ReconnectionManager()
        {
            Dispose(false);
        }
    }
}
