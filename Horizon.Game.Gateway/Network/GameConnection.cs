using Microsoft.Extensions.Logging;
using TouchSocket.Sockets;
using Horizon.Game.Gateway.Services;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Network
{
    /// <summary>
    /// 游戏连接实现
    /// </summary>
    public class GameConnection : IGameConnection
    {
        private readonly ITcpSessionClient _client;
        private readonly ILogger _logger;
        private readonly Dictionary<string, object> _properties = new();

        /// <summary>
        /// 连接是否已损坏（发送失败、管道已关闭等）。<br/>
        /// 当 TCP 管道损坏时（如 Writing is not allowed after writer was completed），
        /// _client.Online 可能仍为 true，但发送已不可能。<br/>
        /// 设置此标志后，IsConnected 立即返回 false，CheckDisconnectedConnections
        /// 会在 5 秒内检测到并清理连接，避免僵尸连接导致角色被误判离线。
        /// </summary>
        private volatile bool _isBroken;

        /// <summary>
        /// 连续发送失败计数。<br/>
        /// 修复 BUG（A角色在线会在B角色客户端莫名离线）：<br/>
        /// 原实现在 SendAsync 中任何异常都立即 MarkAsBroken，导致 TCP 缓冲区瞬时满载
        /// （SocketException NoBufferSpaceAvailable / WouldBlock）等可恢复错误也永久杀死连接，
        /// 触发 Despawn 链路使角色从其他客户端消失。<br/>
        /// 新实现：仅致命异常（连接重置/管道断裂）立即 MarkAsBroken；
        /// 瞬时异常累计到 <see cref="MaxConsecutiveSendFailures"/> 次后才 MarkAsBroken。
        /// 成功发送后重置计数。
        /// </summary>
        private int _consecutiveSendFailures;

        /// <summary>连续发送失败阈值：超过此次数判定连接不可恢复，标记为损坏。</summary>
        private const int MaxConsecutiveSendFailures = 5;

        /// <summary>
        /// 连接ID
        /// </summary>
        public string ConnectionId => _client.Id;

        /// <summary>
        /// 用户ID（登录后设置）
        /// </summary>
        public long? UserId { get; set; }

        /// <summary>
        /// 远程地址
        /// </summary>
        public string RemoteAddress => _client.GetIPPort() ?? "Unknown";

        /// <summary>
        /// 连接时间
        /// </summary>
        public DateTime ConnectedTime { get; }

        /// <summary>
        /// 最后活跃时间
        /// </summary>
        public DateTime LastActiveTime { get; set; }

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected => !_isBroken && _client.Online;

        /// <summary>
        /// 是否已认证
        /// </summary>
        public bool IsAuthenticated { get; set; }

        /// <summary>
        /// 当前鉴权令牌（登录后设置，角色进入游戏后更新为含角色Id的令牌）
        /// </summary>
        public string AuthToken { get; set; } = "";

        /// <summary>
        /// 连接属性
        /// </summary>
        public Dictionary<string, object> Properties => _properties;

        /// <summary>
        /// 连接关闭事件
        /// </summary>
        public event EventHandler<ConnectionClosedEventArgs>? Closed;

        public GameConnection(ITcpSessionClient client, ILogger logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            ConnectedTime = DateTime.UtcNow;
            LastActiveTime = DateTime.UtcNow;

            // 监听客户端断开连接事件
            _client.Closed += OnClientDisconnected;
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        public async Task SendAsync(byte[] data)
        {
            try
            {
                if (!IsConnected)
                {
                    throw new InvalidOperationException("连接已断开");
                }

                await _client.SendAsync(data);
                // 修复 BUG：移除 SendAsync 中的 LastActiveTime 更新。
                // LastActiveTime 应该只在接收客户端数据时更新（GameNetworkServer.OnDataReceived），
                // 准确反映客户端的活跃状态。如果发送数据时也更新，会导致：
                // 1) TCP 半关闭时服务器发送数据可能成功（TCP 缓冲区未满），LastActiveTime 被错误更新，
                //    CheckDisconnectedConnections 的空闲超时检测无法检测到客户端断线
                // 2) CharacterPresenceMonitorHostedService 检查 LastActiveTime 时误判为在线，
                //    导致 Redis 异常时无法清理僵尸连接

                // 发送成功，重置连续失败计数
                if (_consecutiveSendFailures > 0)
                {
                    Interlocked.Exchange(ref _consecutiveSendFailures, 0);
                }

                _logger.LogDebug("发送数据给客户端 {ConnectionId}: {DataLength} 字节",
                    ConnectionId, data.Length);
            }
            catch (Exception ex)
            {
                // 修复核心 BUG（A角色在线会在B角色客户端莫名离线）：
                // 原实现任何异常都立即 MarkAsBroken，导致 TCP 缓冲区瞬时满载等可恢复错误
                // 也永久杀死连接，触发 Despawn 使角色从其他客户端消失。
                // 新实现：区分致命异常与瞬时异常。
                if (IsFatalSendException(ex))
                {
                    // 致命异常：连接已不可恢复（连接重置、管道断裂、Socket 已释放等）
                    _logger.LogWarning(ex,
                        "发送数据遇到致命异常，标记连接为损坏: {ConnectionId}", ConnectionId);
                    MarkAsBroken($"致命发送异常: {ex.GetType().Name}");
                    throw;
                }
                else
                {
                    // 瞬时异常：累计失败计数，超过阈值才标记为损坏
                    var failures = Interlocked.Increment(ref _consecutiveSendFailures);
                    if (failures >= MaxConsecutiveSendFailures)
                    {
                        _logger.LogWarning(ex,
                            "发送数据连续失败 {Failures}/{Max} 次，标记连接为损坏: {ConnectionId}",
                            failures, MaxConsecutiveSendFailures, ConnectionId);
                        MarkAsBroken($"连续发送失败 {failures} 次");
                    }
                    else
                    {
                        _logger.LogDebug(ex,
                            "发送数据瞬时失败（{Failures}/{Max}），不标记损坏: {ConnectionId}",
                            failures, MaxConsecutiveSendFailures, ConnectionId);
                    }
                    throw;
                }
            }
        }

        /// <summary>
        /// 判断发送异常是否为致命异常（连接不可恢复）。<br/>
        /// 致命异常立即触发 MarkAsBroken；非致命异常（瞬时）通过连续失败计数兜底。
        /// </summary>
        private static bool IsFatalSendException(Exception ex)
        {
            // Socket 已释放/已关闭
            if (ex is ObjectDisposedException) return true;

            // InvalidOperationException 且消息包含 "completed"/"disposed"/"not allowed"
            // 典型场景：Writing is not allowed after writer was completed
            if (ex is InvalidOperationException)
            {
                var msg = ex.Message;
                if (msg.Contains("completed", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("disposed", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("not allowed", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("连接已断开", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            // SocketException：根据 SocketErrorCode 判断
            if (ex is SocketException socketEx)
            {
                return socketEx.SocketErrorCode switch
                {
                    // 连接被对端重置 / 管道断裂 / 已断开
                    SocketError.ConnectionReset => true,
                    SocketError.ConnectionAborted => true,
                    SocketError.Shutdown => true,
                    SocketError.NotConnected => true,
                    SocketError.HostUnreachable => true,
                    SocketError.NetworkUnreachable => true,
                    SocketError.NetworkReset => true,
                    SocketError.TimedOut => true,  // TCP 超时（keepalive 探测失败）
                    // 瞬时错误：缓冲区满、资源暂时不可用
                    SocketError.WouldBlock => false,
                    SocketError.TryAgain => false,
                    SocketError.NoBufferSpaceAvailable => false,
                    SocketError.Interrupted => false,
                    SocketError.InProgress => false,
                    SocketError.AlreadyInProgress => false,
                    _ => false,  // 其他未知错误默认非致命，由连续失败计数兜底
                };
            }

            // IOException 可能是网络层包装的异常，检查内部异常
            if (ex is System.IO.IOException && ex.InnerException is SocketException innerSocketEx)
            {
                return IsFatalSendException(innerSocketEx);
            }

            // 其他异常默认非致命，由连续失败计数兜底
            return false;
        }

        /// <summary>
        /// 将连接标记为已损坏。<br/>
        /// 触发 Closed 事件，使 CheckDisconnectedConnections 在 5 秒内清理连接。
        /// </summary>
        /// <param name="reason">损坏原因</param>
        private void MarkAsBroken(string reason)
        {
            if (_isBroken) return; // 避免重复触发

            _isBroken = true;
            _logger.LogWarning("连接已标记为损坏: {ConnectionId}, 原因: {Reason}", ConnectionId, reason);

            // 触发 Closed 事件，使 GameNetworkServer.CleanupConnectionAsync 立即执行
            // （比等待 CheckDisconnectedConnections 的 5 秒更快）
            try
            {
                Closed?.Invoke(this, new ConnectionClosedEventArgs(ConnectionId, $"连接已损坏: {reason}"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "触发连接损坏事件时发生错误: {ConnectionId}", ConnectionId);
            }
        }

        /// <summary>
        /// 关闭连接
        /// </summary>
        public async Task CloseAsync(string reason = "")
        {
            try
            {
                // 设置 _isBroken 标志，使 IsConnected 立即返回 false
                _isBroken = true;

                if (_client.Online)
                {
                    await _client.CloseAsync(reason);
                    _logger.LogInformation("连接已关闭: {ConnectionId}, 原因: {Reason}", 
                        ConnectionId, reason);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "关闭连接时发生错误: {ConnectionId}", ConnectionId);
            }
        }

        /// <summary>
        /// 客户端断开连接事件处理
        /// </summary>
        private async Task OnClientDisconnected(ITcpSessionClient client, ClosedEventArgs e)
        {
            try
            {
                // 设置 _isBroken 标志，确保 IsConnected 返回 false
                _isBroken = true;

                // 取消注册事件，避免重复触发
                _client.Closed -= OnClientDisconnected;
                
                // 触发连接关闭事件
                Closed?.Invoke(this, new ConnectionClosedEventArgs(ConnectionId, e.Message));
                
                _logger.LogDebug("客户端连接断开事件已处理: {ConnectionId}", ConnectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理客户端断开连接事件时发生错误: {ConnectionId}", ConnectionId);
            }
        }

        /// <summary>
        /// 设置连接属性
        /// </summary>
        public void SetProperty(string key, object value)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("属性键不能为空", nameof(key));

            _properties[key] = value;
            // 修复 BUG：移除 SetProperty 中的 LastActiveTime 更新（与 SendAsync 修复一致）。
            // LastActiveTime 应该只在接收客户端数据时更新（GameNetworkServer.OnDataReceived），
            // 准确反映客户端的活跃状态。SetProperty 是服务端内部操作（如设置鉴权令牌、用户 ID），
            // 不应影响 LastActiveTime，否则会导致：
            // 1) 登录时 SetProperty 被调用，LastActiveTime 被错误更新，掩盖客户端真实活跃状态
            // 2) CheckDisconnectedConnections 的空闲超时检测无法准确检测客户端断线
            // 3) CharacterPresenceMonitorHostedService 检查 LastActiveTime 时误判为在线
        }

        /// <summary>
        /// 获取连接属性
        /// </summary>
        public object? GetProperty(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("属性键不能为空", nameof(key));

            _properties.TryGetValue(key, out var value);
            return value;
        }

        /// <summary>
        /// 移除连接属性
        /// </summary>
        public bool RemoveProperty(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("属性键不能为空", nameof(key));

            return _properties.Remove(key);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _client.Closed -= OnClientDisconnected;
            _client?.Dispose();
        }

        public override string ToString()
        {
            return $"GameConnection[{ConnectionId}] User:{UserId} Remote:{RemoteAddress} " +
                   $"Connected:{ConnectedTime:HH:mm:ss} Active:{LastActiveTime:HH:mm:ss} " +
                   $"Auth:{IsAuthenticated} Broken:{_isBroken}";
        }
    }
}
