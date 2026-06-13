using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Horizon.Game.Core.Configuration;

namespace Horizon.Game.Core.Security
{
    /// <summary>
    /// DOS攻击保护和速率限制系统
    /// </summary>
    public class DosProtectionService : IDisposable
    {
        private readonly ILogger<DosProtectionService> _logger;
        private readonly NetworkConfiguration _config;
        private readonly ConcurrentDictionary<string, ClientRateLimitData> _clientRateLimits = new();
        private readonly Timer _cleanupTimer;
        private volatile bool _disposed;

        public DosProtectionService(ILogger<DosProtectionService> logger, IOptions<NetworkConfiguration> config)
        {
            _logger = logger;
            _config = config.Value;
            _cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        /// <summary>
        /// 检查客户端请求是否被允许
        /// </summary>
        public Task<RateLimitResult> CheckRateLimit(string clientId, string? endpoint = null)
        {
            if (_disposed || string.IsNullOrEmpty(clientId))
                return Task.FromResult(RateLimitResult.Rejected("客户端ID无效"));

            var rateLimitData = _clientRateLimits.GetOrAdd(clientId, _ => new ClientRateLimitData(_config));
            var now = DateTime.UtcNow;

            // 检查是否被临时阻止
            if (rateLimitData.IsBlocked(now))
            {
                _logger.LogWarning("客户端 {ClientId} 当前因请求频率限制被临时封禁", clientId);
                return Task.FromResult(RateLimitResult.Rejected("客户端已被临时封禁"));
            }

            // 检查请求频率
            var requestAllowed = rateLimitData.TryAddRequest(now, endpoint);

            if (!requestAllowed)
            {
                // 检查是否达到DOS攻击阈值
                if (rateLimitData.ShouldBlock(now, _config.DosDetectionThreshold))
                {
                    rateLimitData.Block(now, TimeSpan.FromMinutes(5)); // 阻止5分钟
                    _logger.LogWarning("客户端 {ClientId} 因检测到DOS攻击模式被封禁5分钟", clientId);

                    // 异步通知安全事件
                    _ = Task.Run(() => NotifySecurityEvent(clientId, "DOS_ATTACK_DETECTED", endpoint));

                    return Task.FromResult(RateLimitResult.Rejected("检测到DOS攻击，客户端已被封禁"));
                }

                _logger.LogDebug("客户端 {ClientId} 请求频率超限，端点: {Endpoint}", clientId, endpoint);
                return Task.FromResult(RateLimitResult.Rejected("请求频率超限"));
            }

            return Task.FromResult(RateLimitResult.Allowed(rateLimitData.GetRemainingRequests(now)));
        }

        /// <summary>
        /// 记录恶意活动
        /// </summary>
        public void RecordMaliciousActivity(string clientId, string activityType, string? details = null)
        {
            if (_disposed || string.IsNullOrEmpty(clientId))
                return;

            var rateLimitData = _clientRateLimits.GetOrAdd(clientId, _ => new ClientRateLimitData(_config));
            rateLimitData.RecordMaliciousActivity(DateTime.UtcNow, activityType);

            _logger.LogWarning("检测到客户端 {ClientId} 恶意行为: {ActivityType} - {Details}",
                clientId, activityType, details);

            // 如果恶意活动频繁，立即阻止
            if (rateLimitData.GetMaliciousActivityCount(DateTime.UtcNow.AddMinutes(-5)) >= 3)
            {
                rateLimitData.Block(DateTime.UtcNow, TimeSpan.FromMinutes(10));
                _logger.LogError("客户端 {ClientId} 因重复恶意行为被封禁10分钟", clientId);

                _ = Task.Run(() => NotifySecurityEvent(clientId, "REPEATED_MALICIOUS_ACTIVITY", details));
            }
        }

        /// <summary>
        /// 获取客户端统计信息
        /// </summary>
        public ClientSecurityStats? GetClientStats(string clientId)
        {
            if (_disposed || string.IsNullOrEmpty(clientId))
                return null;

            if (!_clientRateLimits.TryGetValue(clientId, out var data))
                return null;

            var now = DateTime.UtcNow;
            return new ClientSecurityStats
            {
                ClientId = clientId,
                TotalRequests = data.GetTotalRequests(),
                RequestsInLastMinute = data.GetRequestsInTimeWindow(now.AddMinutes(-1), now),
                MaliciousActivitiesInLastHour = data.GetMaliciousActivityCount(now.AddHours(-1)),
                IsCurrentlyBlocked = data.IsBlocked(now),
                BlockedUntil = data.GetBlockedUntil(),
                RemainingRequests = data.GetRemainingRequests(now)
            };
        }

        /// <summary>
        /// 手动解除客户端阻止状态
        /// </summary>
        public bool UnblockClient(string clientId, string reason)
        {
            if (_disposed || string.IsNullOrEmpty(clientId))
                return false;

            if (_clientRateLimits.TryGetValue(clientId, out var data))
            {
                data.Unblock();
                _logger.LogInformation("客户端 {ClientId} 已被手动解封: {Reason}", clientId, reason);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 获取系统统计信息
        /// </summary>
        public DosProtectionStats GetSystemStats()
        {
            if (_disposed)
                return new DosProtectionStats();

            var now = DateTime.UtcNow;
            var stats = new DosProtectionStats
            {
                TotalClients = _clientRateLimits.Count,
                BlockedClients = 0,
                TotalRequestsInLastMinute = 0,
                TotalMaliciousActivitiesInLastHour = 0
            };

            foreach (var kvp in _clientRateLimits)
            {
                var data = kvp.Value;
                if (data.IsBlocked(now))
                    stats.BlockedClients++;

                stats.TotalRequestsInLastMinute += data.GetRequestsInTimeWindow(now.AddMinutes(-1), now);
                stats.TotalMaliciousActivitiesInLastHour += data.GetMaliciousActivityCount(now.AddHours(-1));
            }

            return stats;
        }

        /// <summary>
        /// 清理过期的客户端数据
        /// </summary>
        private void CleanupExpiredEntries(object? state)
        {
            if (_disposed)
                return;

            var cutoffTime = DateTime.UtcNow.AddHours(-24); // 保留24小时的数据
            var expiredClients = new List<string>();

            foreach (var kvp in _clientRateLimits)
            {
                if (kvp.Value.ShouldCleanup(cutoffTime))
                {
                    expiredClients.Add(kvp.Key);
                }
            }

            foreach (var clientId in expiredClients)
            {
                if (_clientRateLimits.TryRemove(clientId, out var data))
                {
                    data.Dispose();
                    _logger.LogDebug("已清理客户端 {ClientId} 的过期频率限制数据", clientId);
                }
            }

            if (expiredClients.Count > 0)
            {
                _logger.LogInformation("已清理 {Count} 条过期客户端频率限制记录", expiredClients.Count);
            }
        }

        /// <summary>
        /// 通知安全事件
        /// </summary>
        private async Task NotifySecurityEvent(string clientId, string eventType, string? details)
        {
            try
            {
                // 这里可以集成安全事件通知系统
                // 例如：发送到SIEM系统、安全日志、告警系统等
                _logger.LogCritical("安全事件: {EventType}，客户端: {ClientId} - {Details}",
                    eventType, clientId, details);

                // 模拟异步通知处理
                await Task.Delay(10);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "通知安全事件 {EventType} 时发生错误，客户端: {ClientId}",
                    eventType, clientId);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cleanupTimer?.Dispose();

            // 清理所有客户端数据
            foreach (var kvp in _clientRateLimits)
            {
                if (_clientRateLimits.TryRemove(kvp.Key, out var data))
                {
                    data.Dispose();
                }
            }

            _logger.LogInformation("DOS防护服务已释放");
        }
    }

    /// <summary>
    /// 速率限制结果
    /// </summary>
    public class RateLimitResult
    {
        public bool IsAllowed { get; private set; }
        public string? RejectReason { get; private set; }
        public int RemainingRequests { get; private set; }

        private RateLimitResult(bool isAllowed, string? rejectReason, int remainingRequests)
        {
            IsAllowed = isAllowed;
            RejectReason = rejectReason;
            RemainingRequests = remainingRequests;
        }

        public static RateLimitResult Allowed(int remainingRequests) =>
            new(true, null, remainingRequests);

        public static RateLimitResult Rejected(string reason) =>
            new(false, reason, 0);
    }

    /// <summary>
    /// 客户端安全统计信息
    /// </summary>
    public class ClientSecurityStats
    {
        public string ClientId { get; set; } = "";
        public long TotalRequests { get; set; }
        public int RequestsInLastMinute { get; set; }
        public int MaliciousActivitiesInLastHour { get; set; }
        public bool IsCurrentlyBlocked { get; set; }
        public DateTime? BlockedUntil { get; set; }
        public int RemainingRequests { get; set; }
    }

    /// <summary>
    /// DOS保护系统统计信息
    /// </summary>
    public class DosProtectionStats
    {
        public int TotalClients { get; set; }
        public int BlockedClients { get; set; }
        public long TotalRequestsInLastMinute { get; set; }
        public long TotalMaliciousActivitiesInLastHour { get; set; }
    }

    /// <summary>
    /// 客户端速率限制数据
    /// </summary>
    internal class ClientRateLimitData
    {
        private readonly NetworkConfiguration _config;
        private readonly object _lock = new();
        private readonly Queue<DateTime> _requestHistory = new();
        private readonly Queue<(DateTime Timestamp, string ActivityType)> _maliciousActivities = new();
        private DateTime? _blockedUntil;
        private long _totalRequests;

        public ClientRateLimitData(NetworkConfiguration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public bool IsBlocked(DateTime now)
        {
            lock (_lock)
            {
                return _blockedUntil.HasValue && now < _blockedUntil.Value;
            }
        }

        public bool TryAddRequest(DateTime now, string? endpoint = null)
        {
            lock (_lock)
            {
                // 清理过期的请求记录
                CleanupOldRequests(now);

                // 检查是否超过速率限制
                if (_requestHistory.Count >= _config.MaxRequestsPerMinute)
                {
                    return false;
                }

                _requestHistory.Enqueue(now);
                Interlocked.Increment(ref _totalRequests);
                return true;
            }
        }

        public bool ShouldBlock(DateTime now, int threshold)
        {
            lock (_lock)
            {
                return _requestHistory.Count >= threshold;
            }
        }

        public void Block(DateTime now, TimeSpan duration)
        {
            lock (_lock)
            {
                _blockedUntil = now.Add(duration);
            }
        }

        public void Unblock()
        {
            lock (_lock)
            {
                _blockedUntil = null;
            }
        }

        public void RecordMaliciousActivity(DateTime now, string activityType)
        {
            lock (_lock)
            {
                _maliciousActivities.Enqueue((now, activityType));

                // 保持最近1小时的记录
                while (_maliciousActivities.Count > 0 &&
                       (now - _maliciousActivities.Peek().Timestamp).TotalHours > 1)
                {
                    _maliciousActivities.Dequeue();
                }
            }
        }

        public int GetMaliciousActivityCount(DateTime since)
        {
            lock (_lock)
            {
                return _maliciousActivities.Count(a => a.Timestamp >= since);
            }
        }

        public long GetTotalRequests()
        {
            return Interlocked.Read(ref _totalRequests);
        }

        public int GetRequestsInTimeWindow(DateTime from, DateTime to)
        {
            lock (_lock)
            {
                return _requestHistory.Count(r => r >= from && r <= to);
            }
        }

        public int GetRemainingRequests(DateTime now)
        {
            lock (_lock)
            {
                CleanupOldRequests(now);
                return Math.Max(0, _config.MaxRequestsPerMinute - _requestHistory.Count);
            }
        }

        public DateTime? GetBlockedUntil()
        {
            lock (_lock)
            {
                return _blockedUntil;
            }
        }
        private void CleanupOldRequests(DateTime now)
        {
            var cutoff = now.AddMinutes(-1);
            while (_requestHistory.Count > 0 && _requestHistory.Peek() < cutoff)
            {
                _requestHistory.Dequeue();
            }
        }

        public bool ShouldCleanup(DateTime cutoffTime)
        {
            lock (_lock)
            {
                // 如果没有活动且最后一个请求时间超过截止时间，则应该清理
                return _requestHistory.Count == 0 ||
                       (_requestHistory.Count > 0 && _requestHistory.Max() < cutoffTime);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _requestHistory.Clear();
                _maliciousActivities.Clear();
            }
        }
    }
}
