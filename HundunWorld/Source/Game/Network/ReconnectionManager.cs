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
        /// 最大重连尝试次数
        /// </summary>
        public int MaxReconnectAttempts { get; set; } = 10;

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
        /// </summary>
        public int HeartbeatTimeoutMs { get; set; } = 10000;

        /// <summary>
        /// 心跳间隔（毫秒）
        /// </summary>
        public int HeartbeatIntervalMs { get; set; } = 3000;

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
        /// 状态变更事件
        /// </summary>
        public event Action<ReconnectState> OnStateChanged;

        #endregion

        private CancellationTokenSource _reconnectCts;
        private Timer _heartbeatTimer;
        private bool _disposed;
        private volatile bool _reconnectAttemptInProgress;
        private readonly Func<Task<bool>> _connectFunction;
        private readonly Func<Task> _disconnectFunction;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="connectFunction">连接函数，返回true表示连接成功</param>
        /// <param name="disconnectFunction">断开连接函数，重连前调用以清理旧连接残留（防止幽灵连接）</param>
        public ReconnectionManager(Func<Task<bool>> connectFunction = null, Func<Task> disconnectFunction = null)
        {
            _connectFunction = connectFunction;
            _disconnectFunction = disconnectFunction;
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
        /// 更新心跳
        /// </summary>
        public void UpdateHeartbeat()
        {
            LastHeartbeatTime = DateTime.UtcNow;
        }

        /// <summary>
        /// 检查心跳超时
        /// </summary>
        private void CheckHeartbeat(object state)
        {
            if (CurrentState != ReconnectState.Connected)
                return;

            var elapsed = (DateTime.UtcNow - LastHeartbeatTime).TotalMilliseconds;
            if (elapsed > HeartbeatTimeoutMs)
            {
                Debug.Log($"心跳超时: {elapsed:F0}ms，触发断线重连");
                HandleDisconnect();
            }
        }

        /// <summary>
        /// 处理断线
        /// </summary>
        public void HandleDisconnect()
        {
            if (CurrentState == ReconnectState.Reconnecting || CurrentState == ReconnectState.Failed || _reconnectAttemptInProgress)
                return;

            LastDisconnectTime = DateTime.UtcNow;
            ChangeState(ReconnectState.Disconnected);
            OnDisconnected?.Invoke();

            // 自动开始重连
            _ = StartReconnectAsync();
        }

        /// <summary>
        /// 开始重连
        /// </summary>
        public async Task StartReconnectAsync()
        {
            if (CurrentState == ReconnectState.Reconnecting)
                return;

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

            // 所有重连尝试都失败
            ChangeState(ReconnectState.Failed);
            OnReconnectFailed?.Invoke();
            Debug.LogError($"重连失败：已达到最大尝试次数 {MaxReconnectAttempts}");
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
            _reconnectCts?.Cancel();
            if (CurrentState == ReconnectState.Reconnecting)
            {
                ChangeState(ReconnectState.Disconnected);
            }
        }

        /// <summary>
        /// 标记已连接
        /// </summary>
        public void MarkConnected()
        {
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
                    _reconnectCts?.Cancel();
                    _reconnectCts?.Dispose();
                    _reconnectCts = null;

                    // 清理事件委托，防止外部引用残留
                    OnDisconnected = null;
                    OnReconnectAttempt = null;
                    OnReconnected = null;
                    OnReconnectFailed = null;
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
