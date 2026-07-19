using Microsoft.Extensions.Logging;
using Horizon.Game.Gateway.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// 简单的消息路由器实现
    /// </summary>
    public class MessageRouter : IMessageRouter
    {
        private readonly ILogger<MessageRouter> _logger;
        private bool _isRunning;
        private readonly MessageRouterStatistics _statistics = new();

        public MessageRouter(ILogger<MessageRouter> logger)
        {
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("消息路由器启动");
            _isRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("消息路由器停止");
            _isRunning = false;
            return Task.CompletedTask;
        }

       

        public MessageRouterStatistics GetStatistics()
        {
            long messagesPerSecond = 0;
            
            // 实时计算每秒消息数（简化实现）
            // 实际应用中可以使用滑动窗口计算
            if (_statistics.TotalMessages > 0)
            {
                // 这里可以根据时间窗口计算，暂时返回估计值
                messagesPerSecond = _statistics.TotalMessages / Math.Max(1, DateTime.UtcNow.Second);
            }
            
            return new MessageRouterStatistics
            {
                TotalMessages = _statistics.TotalMessages,
                RoutingErrors = _statistics.RoutingErrors,
                MessagesPerSecond = messagesPerSecond,
                AverageResponseTime = 0,
                ErrorRate = _statistics.TotalMessages > 0 ? (double)_statistics.RoutingErrors / _statistics.TotalMessages * 100 : 0
            };
        }

        public async Task RouteMessageAsync(byte[] message, IGameConnection connection)
        {
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// 简单的负载均衡器实现
    /// </summary>
    public class LoadBalancer : ILoadBalancer
    {
        private readonly ILogger<LoadBalancer> _logger;
        private bool _isRunning;
        private readonly LoadBalancerStatistics _statistics = new();

        public LoadBalancer(ILogger<LoadBalancer> logger)
        {
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("负载均衡器启动");
            _isRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("负载均衡器停止");
            _isRunning = false;
            return Task.CompletedTask;
        }

        public string? SelectBestSilo(string messageType)
        {
            if (!_isRunning)
                return null;

            _statistics.TotalRequests++;
            
            // 简单实现，返回默认Silo
            return "localhost:11111";
        }

        public LoadBalancerStatistics GetStatistics()
        {
            return new LoadBalancerStatistics
            {
                ActiveSilos = 1,
                TotalRequests = _statistics.TotalRequests,
                AverageLoad = 0.5,
                BalancingErrors = _statistics.BalancingErrors
            };
        }
    }

    /// <summary>
    /// 简单的会话管理器实现
    /// </summary>
    public class SessionManager : ISessionManager
    {
        private readonly ILogger<SessionManager> _logger;
        private bool _isRunning;
        private readonly SessionManagerStatistics _statistics = new();
        
        // 会话存储
        private readonly Dictionary<string, IGameSession> _sessions = new();
        private readonly Dictionary<long, string> _userIdToSessionId = new();
        private readonly object _lock = new();

        public SessionManager(ILogger<SessionManager> logger)
        {
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("会话管理器启动");
            _isRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("会话管理器停止");
            _isRunning = false;
            return Task.CompletedTask;
        }

        public Task<IGameSession> CreateSessionAsync(IGameConnection connection, long userId)
        {
            if (!_isRunning)
                throw new InvalidOperationException("会话管理器未运行");

            lock (_lock)
            {
                // 检查是否已存在该用户的会话
                if (_userIdToSessionId.TryGetValue(userId, out var existingSessionId))
                {
                    // 移除旧会话
                    _logger.LogWarning("用户 {UserId} 已有会话 {SessionId}，将被新会话替换", userId, existingSessionId);
                    _sessions.Remove(existingSessionId);
                }
                
                _statistics.TotalSessions++;
                
                var session = new GameSession(Guid.NewGuid().ToString(), userId, connection);
                
                // 存储会话
                _sessions[session.SessionId] = session;
                _userIdToSessionId[userId] = session.SessionId;
                
                _logger.LogInformation("创建会话: {SessionId}, UserId={UserId}", session.SessionId, userId);
                
                return Task.FromResult<IGameSession>(session);
            }
        }

        public IGameSession? GetSession(string sessionId)
        {
            lock (_lock)
            {
                if (_sessions.TryGetValue(sessionId, out var session))
                {
                    // 更新活跃时间
                    session.LastActiveTime = DateTime.UtcNow;
                    _logger.LogDebug("获取会话: {SessionId}", sessionId);
                    return session;
                }
                
                _logger.LogWarning("会话不存在: {SessionId}", sessionId);
                return null;
            }
        }

        public IGameSession? GetSessionByUserId(long userId)
        {
            lock (_lock)
            {
                if (_userIdToSessionId.TryGetValue(userId, out var sessionId))
                {
                    return GetSession(sessionId);
                }
                
                _logger.LogWarning("用户会话不存在: UserId={UserId}", userId);
                return null;
            }
        }

        public Task RemoveSessionAsync(string sessionId)
        {
            lock (_lock)
            {
                if (_sessions.TryGetValue(sessionId, out var session))
                {
                    // 从会话存储中移除
                    _sessions.Remove(sessionId);
                    
                    // 从用户ID映射中移除
                    _userIdToSessionId.Remove(session.UserId);
                    
                    _logger.LogInformation("移除会话: {SessionId}, UserId={UserId}", sessionId, session.UserId);
                }
                else
                {
                    _logger.LogWarning("尝试移除不存在的会话: {SessionId}", sessionId);
                }
            }
            
            return Task.CompletedTask;
        }

        public SessionManagerStatistics GetStatistics()
        {
            int activeSessions;
            int authenticatedSessions = 0;
            double averageSessionDuration = 0;
            
            lock (_lock)
            {
                activeSessions = _sessions.Count;
                
                if (activeSessions > 0)
                {
                    var now = DateTime.UtcNow;
                    double totalDuration = 0;
                    
                    foreach (var session in _sessions.Values)
                    {
                        if (session.IsAuthenticated)
                        {
                            authenticatedSessions++;
                        }
                        
                        totalDuration += (now - session.CreatedTime).TotalSeconds;
                    }
                    
                    averageSessionDuration = totalDuration / activeSessions;
                }
            }
            
            return new SessionManagerStatistics
            {
                ActiveSessions = activeSessions,
                TotalSessions = _statistics.TotalSessions,
                AuthenticatedSessions = authenticatedSessions,
                AverageSessionDuration = averageSessionDuration,
                SessionErrors = _statistics.SessionErrors
            };
        }
    }

    /// <summary>
    /// 简单的游戏会话实现
    /// </summary>
    public class GameSession : IGameSession
    {
        public string SessionId { get; }
        public long UserId { get; }
        public IGameConnection Connection { get; }
        public DateTime CreatedTime { get; }
        public DateTime LastActiveTime { get; set; }
        public bool IsAuthenticated { get; set; }
        public System.Collections.Generic.Dictionary<string, object> Data { get; }

        public GameSession(string sessionId, long userId, IGameConnection connection)
        {
            SessionId = sessionId;
            UserId = userId;
            Connection = connection;
            CreatedTime = DateTime.UtcNow;
            LastActiveTime = DateTime.UtcNow;
            Data = new System.Collections.Generic.Dictionary<string, object>();
        }

        public async Task SendMessageAsync(byte[] message)
        {
            await Connection.SendAsync(message);
            // 修复 BUG：移除 SendMessageAsync 中的 LastActiveTime 更新（与 GameConnection.SendAsync 修复一致）。
            // LastActiveTime 应该只在接收客户端数据时更新（GameNetworkServer.OnDataReceived），
            // 准确反映客户端的活跃状态。发送数据时也更新会导致：
            // 1) TCP 半关闭时服务器发送数据可能成功（TCP 缓冲区未满），LastActiveTime 被错误更新，
            //    CheckDisconnectedConnections 的空闲超时检测无法检测到客户端断线
            // 2) CharacterPresenceMonitorHostedService 检查 LastActiveTime 时误判为在线，
            //    导致 Redis 异常时无法清理僵尸连接，引发"角色被误判离线"BUG
        }

        public async Task CloseAsync(string reason = "")
        {
            await Connection.CloseAsync(reason);
        }
    }
}
