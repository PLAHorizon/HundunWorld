using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Horizon.Game.Gateway.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// 连接管理器实现
    /// </summary>
    public class ConnectionManager : IConnectionManager
    {
        private readonly ILogger<ConnectionManager> _logger;
        private readonly IOptionsMonitor<GatewayOptions> _gatewayOptions;
        
        private readonly ConcurrentDictionary<string, IGameConnection> _connections = new();
        private readonly ConcurrentDictionary<long, string> _userConnections = new();
        
        private readonly ConnectionManagerStatistics _statistics = new();
        private readonly NetworkStatistics _networkStatistics = new();
        private readonly object _statsLock = new();
        
        private Timer? _cleanupTimer;

        public ConnectionManager(
            ILogger<ConnectionManager> logger,
            IOptionsMonitor<GatewayOptions> gatewayOptions)
        {
            _logger = logger;
            _gatewayOptions = gatewayOptions;
            
            // 启动清理定时器
            _cleanupTimer = new Timer(CleanupConnections, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        /// <summary>
        /// 添加连接
        /// </summary>
        public async Task<bool> AddConnectionAsync(IGameConnection connection)
        {
            try
            {
                if (_connections.Count >= _gatewayOptions.CurrentValue.MaxConnections)
                {
                    _logger.LogWarning("达到最大连接数限制: {MaxConnections}", _gatewayOptions.CurrentValue.MaxConnections);
                    await connection.CloseAsync("服务器连接数已满");
                    return false;
                }

                if (_connections.TryAdd(connection.ConnectionId, connection))
                {
                    // 注册连接关闭事件
                    connection.Closed += OnConnectionClosed;
                    
                    lock (_statsLock)
                    {
                        _statistics.TotalConnections++;
                        _statistics.ActiveConnections = _connections.Count;
                        _statistics.PeakConnections = Math.Max(_statistics.PeakConnections, _statistics.ActiveConnections);
                    }

                    _logger.LogInformation("新连接已添加: {ConnectionId} from {RemoteAddress}", 
                        connection.ConnectionId, connection.RemoteAddress);
                    
                    return true;
                }

                _logger.LogWarning("添加连接失败，连接ID已存在: {ConnectionId}", connection.ConnectionId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加连接时发生错误: {ConnectionId}", connection.ConnectionId);
                return false;
            }
        }

        /// <summary>
        /// 移除连接
        /// </summary>
        public async Task<bool> RemoveConnectionAsync(string connectionId)
        {
            try
            {
                if (_connections.TryRemove(connectionId, out var connection))
                {
                    // 如果连接有关联的用户，移除用户映射
                    if (connection.UserId.HasValue)
                    {
                        _userConnections.TryRemove(connection.UserId.Value, out _);
                    }

                    // 取消注册事件
                    connection.Closed -= OnConnectionClosed;

                    lock (_statsLock)
                    {
                        _statistics.TotalDisconnections++;
                        _statistics.ActiveConnections = _connections.Count;
                        
                        // 计算平均连接时长
                        var duration = DateTime.UtcNow - connection.ConnectedTime;
                        _statistics.AverageConnectionDuration = 
                            (_statistics.AverageConnectionDuration * (_statistics.TotalDisconnections - 1) + duration.TotalSeconds) / 
                            _statistics.TotalDisconnections;
                    }

                    _logger.LogInformation("连接已移除: {ConnectionId}", connectionId);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "移除连接时发生错误: {ConnectionId}", connectionId);
                return false;
            }
        }

        /// <summary>
        /// 获取连接
        /// </summary>
        public IGameConnection? GetConnection(string connectionId)
        {
            _connections.TryGetValue(connectionId, out var connection);
            return connection;
        }

        /// <summary>
        /// 获取所有连接
        /// </summary>
        public IEnumerable<IGameConnection> GetAllConnections()
        {
            return _connections.Values.ToList();
        }

        /// <summary>
        /// 根据用户ID获取连接
        /// </summary>
        public IGameConnection? GetConnectionByUserId(long userId)
        {
            if (_userConnections.TryGetValue(userId, out var connectionId))
            {
                return GetConnection(connectionId);
            }
            return null;
        }

        /// <summary>
        /// 广播消息给所有连接
        /// </summary>
        public async Task BroadcastAsync(byte[] message)
        {
            var connections = GetAllConnections().ToList();
            var tasks = new List<Task>();

            foreach (var connection in connections)
            {
                if (connection.IsConnected)
                {
                    tasks.Add(SendToConnectionSafeAsync(connection, message));
                }
            }

            await Task.WhenAll(tasks);
            
            lock (_statsLock)
            {
                _networkStatistics.MessagesSent += connections.Count;
                _networkStatistics.BytesSent += message.Length * connections.Count;
            }

            _logger.LogDebug("广播消息发送完成: {ConnectionCount} 个连接, {MessageSize} 字节", 
                connections.Count, message.Length);
        }

        /// <summary>
        /// 按条件筛选并广播消息
        /// </summary>
        public async Task BroadcastAsync(byte[] message, Func<IGameConnection, bool> predicate)
        {
            var connections = GetAllConnections().Where(predicate).ToList();
            var tasks = new List<Task>();

            foreach (var connection in connections)
            {
                if (connection.IsConnected)
                {
                    tasks.Add(SendToConnectionSafeAsync(connection, message));
                }
            }

            await Task.WhenAll(tasks);

            lock (_statsLock)
            {
                _networkStatistics.MessagesSent += connections.Count;
                _networkStatistics.BytesSent += message.Length * connections.Count;
            }

            _logger.LogDebug("按条件筛选广播消息发送完成: {ConnectionCount} 个连接, {MessageSize} 字节",
                connections.Count, message.Length);
        }

        /// <summary>
        /// 向指定用户组广播消息
        /// </summary>
        public async Task BroadcastToUserGroupAsync(byte[] message, IEnumerable<long> userIds)
        {
            var userIdSet = new HashSet<long>(userIds);
            var connections = GetAllConnections()
                .Where(c => c.UserId.HasValue && userIdSet.Contains(c.UserId.Value))
                .ToList();
            var tasks = new List<Task>();

            foreach (var connection in connections)
            {
                if (connection.IsConnected)
                {
                    tasks.Add(SendToConnectionSafeAsync(connection, message));
                }
            }

            await Task.WhenAll(tasks);

            lock (_statsLock)
            {
                _networkStatistics.MessagesSent += connections.Count;
                _networkStatistics.BytesSent += message.Length * connections.Count;
            }

            _logger.LogDebug("向指定用户组广播消息发送完成: {ConnectionCount} 个连接, {MessageSize} 字节",
                connections.Count, message.Length);
        }

        /// <summary>
        /// 根据连接属性筛选并广播消息
        /// </summary>
        public async Task BroadcastByPropertyAsync(byte[] message, Func<Dictionary<string, object>, bool> propertyFilter)
        {
            var connections = GetAllConnections()
                .Where(c => propertyFilter(c.Properties))
                .ToList();
            var tasks = new List<Task>();

            foreach (var connection in connections)
            {
                if (connection.IsConnected)
                {
                    tasks.Add(SendToConnectionSafeAsync(connection, message));
                }
            }

            await Task.WhenAll(tasks);

            lock (_statsLock)
            {
                _networkStatistics.MessagesSent += connections.Count;
                _networkStatistics.BytesSent += message.Length * connections.Count;
            }

            _logger.LogDebug("根据属性筛选广播消息发送完成: {ConnectionCount} 个连接, {MessageSize} 字节",
                connections.Count, message.Length);
        }

        /// <summary>
        /// 发送消息给指定连接
        /// </summary>
        public async Task SendToConnectionAsync(string connectionId, byte[] message)
        {
            var connection = GetConnection(connectionId);
            if (connection != null && connection.IsConnected)
            {
                await SendToConnectionSafeAsync(connection, message);
                
                lock (_statsLock)
                {
                    _networkStatistics.MessagesSent++;
                    _networkStatistics.BytesSent += message.Length;
                }
            }
            else
            {
                _logger.LogWarning("尝试发送消息给不存在或已断开的连接: {ConnectionId}", connectionId);
            }
        }

        /// <summary>
        /// 发送消息给指定用户
        /// </summary>
        public async Task SendToUserAsync(long userId, byte[] message)
        {
            var connection = GetConnectionByUserId(userId);
            if (connection != null && connection.IsConnected)
            {
                await SendToConnectionSafeAsync(connection, message);
                
                lock (_statsLock)
                {
                    _networkStatistics.MessagesSent++;
                    _networkStatistics.BytesSent += message.Length;
                }
            }
            else
            {
                _logger.LogWarning("尝试发送消息给不在线的用户: {UserId}", userId);
            }
        }

        /// <summary>
        /// 获取连接统计信息
        /// </summary>
        public ConnectionManagerStatistics GetStatistics()
        {
            lock (_statsLock)
            {
                _statistics.ActiveConnections = _connections.Count;
                _statistics.AuthenticatedConnections = _connections.Values.Count(c => c.IsAuthenticated);
                
                return new ConnectionManagerStatistics
                {
                    ActiveConnections = _statistics.ActiveConnections,
                    TotalConnections = _statistics.TotalConnections,
                    TotalDisconnections = _statistics.TotalDisconnections,
                    ErrorConnections = _statistics.ErrorConnections,
                    AuthenticatedConnections = _statistics.AuthenticatedConnections,
                    PeakConnections = _statistics.PeakConnections,
                    AverageConnectionDuration = _statistics.AverageConnectionDuration
                };
            }
        }

        /// <summary>
        /// 获取网络统计信息
        /// </summary>
        public NetworkStatistics GetNetworkStatistics()
        {
            lock (_statsLock)
            {
                return new NetworkStatistics
                {
                    BytesReceived = _networkStatistics.BytesReceived,
                    BytesSent = _networkStatistics.BytesSent,
                    MessagesReceived = _networkStatistics.MessagesReceived,
                    MessagesSent = _networkStatistics.MessagesSent,
                    AverageLatency = _networkStatistics.AverageLatency,
                    Errors = _networkStatistics.Errors
                };
            }
        }

        /// <summary>
        /// 清理超时连接
        /// </summary>
        public async Task CleanupTimeoutConnectionsAsync()
        {
            var timeout = TimeSpan.FromSeconds(_gatewayOptions.CurrentValue.ConnectionTimeout);
            var cutoffTime = DateTime.UtcNow - timeout;
            var timeoutConnections = new List<IGameConnection>();

            foreach (var connection in _connections.Values)
            {
                if (connection.LastActiveTime < cutoffTime)
                {
                    timeoutConnections.Add(connection);
                }
            }

            foreach (var connection in timeoutConnections)
            {
                _logger.LogInformation("清理超时连接: {ConnectionId}, 最后活跃时间: {LastActiveTime}", 
                    connection.ConnectionId, connection.LastActiveTime);
                
                await connection.CloseAsync("连接超时");
                await RemoveConnectionAsync(connection.ConnectionId);
            }

            if (timeoutConnections.Count > 0)
            {
                _logger.LogInformation("清理了 {Count} 个超时连接", timeoutConnections.Count);
            }
        }

        /// <summary>
        /// 安全发送消息给连接
        /// </summary>
        private async Task SendToConnectionSafeAsync(IGameConnection connection, byte[] message)
        {
            try
            {
                await connection.SendAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "发送消息失败: {ConnectionId}", connection.ConnectionId);
                
                lock (_statsLock)
                {
                    _networkStatistics.Errors++;
                }

                // 连接发送失败，标记为需要移除
                _ = Task.Run(async () =>
                {
                    await connection.CloseAsync("发送消息失败");
                    await RemoveConnectionAsync(connection.ConnectionId);
                });
            }
        }

        /// <summary>
        /// 连接关闭事件处理
        /// </summary>
        private async void OnConnectionClosed(object? sender, ConnectionClosedEventArgs e)
        {
            await RemoveConnectionAsync(e.ConnectionId);
            _logger.LogInformation("连接已关闭: {ConnectionId}, 原因: {Reason}", e.ConnectionId, e.Reason);
        }

        /// <summary>
        /// 定时清理连接
        /// </summary>
        private async void CleanupConnections(object? state)
        {
            try
            {
                await CleanupTimeoutConnectionsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "定时清理连接时发生错误");
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _cleanupTimer = null;
        }
    }
}
