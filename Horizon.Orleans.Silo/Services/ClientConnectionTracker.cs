using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Runtime;

namespace Horizon.Orleans.Silo.Services
{
    /// <summary>
    /// 客户端连接跟踪器接口
    /// </summary>
    public interface IClientConnectionTracker
    {
        void TrackConnection(string clientId, IPEndPoint clientEndpoint, string? grainType);
        void TrackDisconnection(string clientId);
        void TrackActivity(string clientId, string grainMethod);
        ClientConnectionStats GetStats();
        void LogCurrentConnections();
    }

    /// <summary>
    /// 客户端连接跟踪器实现
    /// </summary>
    public class ClientConnectionTracker : IClientConnectionTracker
    {
        private readonly ILogger<ClientConnectionTracker> _logger;
        private readonly ClientConnectionOptions _options;
        private readonly ConcurrentDictionary<string, ClientConnectionInfo> _connections = new();

        public ClientConnectionTracker(
            ILogger<ClientConnectionTracker> logger,
            IOptions<ClientConnectionOptions> options)
        {
            _logger = logger;
            _options = options.Value;
        }

        public void TrackConnection(string clientId, IPEndPoint clientEndpoint, string? grainType)
        {
            var connectionInfo = new ClientConnectionInfo
            {
                ClientId = clientId,
                ClientEndpoint = clientEndpoint?.ToString() ?? "Unknown",
                ConnectedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow,
                AccessedGrainTypes = grainType != null ? new HashSet<string> { grainType } : new HashSet<string>()
            };

            _connections.AddOrUpdate(clientId, connectionInfo, (key, existing) =>
            {
                existing.LastActivityAt = DateTime.UtcNow;
                if (grainType != null)
                {
                    lock (existing.AccessedGrainTypes)
                    {
                        existing.AccessedGrainTypes.Add(grainType);
                    }
                }
                return existing;
            });

            if (_options.EnableDetailedLogging)
            {
                _logger.LogInformation(
                    "🔗 [客户端连接] ClientId={ClientId}, Endpoint={Endpoint}, Time={Time:HH:mm:ss}",
                    clientId,
                    clientEndpoint,
                    DateTime.Now);
            }
        }

        public void TrackDisconnection(string clientId)
        {
            if (_connections.TryRemove(clientId, out var connectionInfo))
            {
                var duration = DateTime.UtcNow - connectionInfo.ConnectedAt;
                
                if (_options.EnableDetailedLogging)
                {
                    string grainTypesStr;
                    lock (connectionInfo.AccessedGrainTypes)
                    {
                        grainTypesStr = string.Join(", ", connectionInfo.AccessedGrainTypes);
                    }
                    
                    _logger.LogInformation(
                        "🔌 [客户端断开] ClientId={ClientId}, Duration={Duration:hh\\:mm\\:ss}, CallCount={CallCount}, GrainTypes={GrainTypes}",
                        clientId,
                        duration,
                        connectionInfo.CallCount,
                        grainTypesStr);
                }
            }
        }

        public void TrackActivity(string clientId, string grainMethod)
        {
            if (_connections.TryGetValue(clientId, out var connectionInfo))
            {
                connectionInfo.LastActivityAt = DateTime.UtcNow;
                System.Threading.Interlocked.Increment(ref connectionInfo.CallCount);
                
                if (_options.LogConnectionDetails)
                {
                    _logger.LogDebug(
                        "📡 [客户端活动] ClientId={ClientId}, Method={Method}, TotalCalls={TotalCalls}",
                        clientId,
                        grainMethod,
                        connectionInfo.CallCount);
                }
            }
        }

        public ClientConnectionStats GetStats()
        {
            var now = DateTime.UtcNow;
            var activeConnections = _connections.Values.ToList();

            return new ClientConnectionStats
            {
                TotalConnections = activeConnections.Count,
                ActiveInLastMinute = activeConnections.Count(c => now - c.LastActivityAt < TimeSpan.FromMinutes(1)),
                ActiveInLastFiveMinutes = activeConnections.Count(c => now - c.LastActivityAt < TimeSpan.FromMinutes(5)),
                TotalCalls = activeConnections.Sum(c => c.CallCount),
                AverageCallsPerClient = activeConnections.Count > 0 
                    ? activeConnections.Average(c => c.CallCount) 
                    : 0
            };
        }

        public void LogCurrentConnections()
        {
            if (!_options.EnableDetailedLogging) return;

            var stats = GetStats();
            
            _logger.LogInformation(
                "📊 [连接统计] 总连接数={TotalConnections}, 活跃(1分钟)={ActiveInLastMinute}, " +
                "活跃(5分钟)={ActiveInLastFiveMinutes}, 总调用次数={TotalCalls}, 平均调用={AverageCallsPerClient:F2}",
                stats.TotalConnections,
                stats.ActiveInLastMinute,
                stats.ActiveInLastFiveMinutes,
                stats.TotalCalls,
                stats.AverageCallsPerClient);

            if (_options.LogConnectionDetails && _connections.Count > 0)
            {
                _logger.LogInformation("📋 [活跃客户端列表]:");
                foreach (var conn in _connections.Values.OrderByDescending(c => c.LastActivityAt).Take(10))
                {
                    var inactiveTime = DateTime.UtcNow - conn.LastActivityAt;
                    _logger.LogInformation(
                        "   - {ClientId}: 端点={Endpoint}, 调用={CallCount}, 闲置={InactiveTime:mm\\:ss}",
                        conn.ClientId,
                        conn.ClientEndpoint,
                        conn.CallCount,
                        inactiveTime);
                }
            }
        }
    }

    public class ClientConnectionInfo
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientEndpoint { get; set; } = string.Empty;
        public DateTime ConnectedAt { get; set; }
        public DateTime LastActivityAt { get; set; }
        public int CallCount;
        public HashSet<string> AccessedGrainTypes { get; set; } = new();
    }

    public class ClientConnectionStats
    {
        public int TotalConnections { get; set; }
        public int ActiveInLastMinute { get; set; }
        public int ActiveInLastFiveMinutes { get; set; }
        public long TotalCalls { get; set; }
        public double AverageCallsPerClient { get; set; }
    }

    public class ClientConnectionOptions
    {
        public bool EnableDetailedLogging { get; set; } = true;
        public bool LogConnectionDetails { get; set; } = true;
        public TimeSpan LogInterval { get; set; } = TimeSpan.FromMinutes(1);
    }
}
