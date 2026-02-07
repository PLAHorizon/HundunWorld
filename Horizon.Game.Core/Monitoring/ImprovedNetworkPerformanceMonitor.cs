using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Horizon.Game.Core.Configuration;

namespace Horizon.Game.Core.Monitoring
{
    /// <summary>
    /// 改进的网络性能监控器 - 解决线程安全和资源管理问题
    /// </summary>
    public class ImprovedNetworkPerformanceMonitor : IHostedService, IDisposable
    {
        private readonly ILogger<ImprovedNetworkPerformanceMonitor> _logger;
        private readonly NetworkConfiguration _config;
        private readonly ConcurrentDictionary<string, ThreadSafePerformanceMetrics> _clientMetrics = new();
        private readonly Timer _reportTimer;
        private readonly Timer _cleanupTimer;
        private readonly SemaphoreSlim _reportSemaphore = new(1, 1);
        private volatile bool _disposed = false;

        // 配置参数
        private readonly TimeSpan _reportInterval;
        private readonly TimeSpan _cleanupInterval;
        private readonly TimeSpan _clientTimeout;

        public ImprovedNetworkPerformanceMonitor(
            ILogger<ImprovedNetworkPerformanceMonitor> logger,
            IOptions<NetworkConfiguration> config)
        {
            _logger = logger;
            _config = config.Value;

            _reportInterval = TimeSpan.FromMilliseconds(_config.PerformanceReportIntervalMs);
            _cleanupInterval = TimeSpan.FromMinutes(5);
            _clientTimeout = TimeSpan.FromMinutes(10);

            _reportTimer = new Timer(ReportMetricsAsync, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _cleanupTimer = new Timer(CleanupExpiredClients, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (_disposed) return Task.CompletedTask;

            _reportTimer.Change(_reportInterval, _reportInterval);
            _cleanupTimer.Change(_cleanupInterval, _cleanupInterval);

            _logger.LogInformation("Network performance monitor started with report interval {ReportInterval}ms",
                _reportInterval.TotalMilliseconds);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (_disposed) return Task.CompletedTask;

            _reportTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _cleanupTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            _logger.LogInformation("Network performance monitor stopped");

            return Task.CompletedTask;
        }

        /// <summary>
        /// 记录TCP包损坏恢复事件（线程安全）
        /// </summary>
        public void RecordCorruptionRecovery(string clientId, int bytesSkipped, bool successful)
        {
            if (_disposed || string.IsNullOrEmpty(clientId)) return;

            var metrics = GetOrCreateClientMetrics(clientId);
            metrics.RecordCorruptionRecovery(bytesSkipped, successful);

            _logger.LogDebug("TCP corruption recovery for client {ClientId}: {Result}, bytes skipped: {BytesSkipped}",
                clientId, successful ? "SUCCESS" : "FAILED", bytesSkipped);
        }

        /// <summary>
        /// 记录消息处理错误（线程安全）
        /// </summary>
        public void RecordProcessingError(string clientId, string errorType, Exception exception)
        {
            if (_disposed || string.IsNullOrEmpty(clientId)) return;

            var metrics = GetOrCreateClientMetrics(clientId);
            metrics.RecordProcessingError();

            _logger.LogWarning(exception, "Processing error for client {ClientId}: {ErrorType}",
                clientId, errorType);
        }

        /// <summary>
        /// 记录消息处理成功（线程安全）
        /// </summary>
        public void RecordSuccessfulProcessing(string clientId, string messageType, long processingTimeMs)
        {
            if (_disposed || string.IsNullOrEmpty(clientId)) return;

            var metrics = GetOrCreateClientMetrics(clientId);
            metrics.RecordSuccessfulProcessing(processingTimeMs);

            if (processingTimeMs > _config.SlowOperationThresholdMs)
            {
                _logger.LogWarning("Slow message processing for client {ClientId}: {MessageType} took {ProcessingTime}ms",
                    clientId, messageType, processingTimeMs);
            }
        }

        /// <summary>
        /// 记录反序列化成功（线程安全）
        /// </summary>
        public void RecordSuccessfulDeserialization(string clientId, int messageLength)
        {
            if (_disposed || string.IsNullOrEmpty(clientId)) return;

            var metrics = GetOrCreateClientMetrics(clientId);
            metrics.RecordSuccessfulDeserialization(messageLength);
        }

        /// <summary>
        /// 记录协议版本不匹配事件（线程安全）
        /// </summary>
        public void RecordProtocolVersionMismatch(string clientId, int expectedVersion, int actualVersion)
        {
            if (_disposed || string.IsNullOrEmpty(clientId)) return;

            var metrics = GetOrCreateClientMetrics(clientId);
            metrics.RecordProtocolVersionMismatch();

            _logger.LogWarning("Protocol version mismatch for client {ClientId}: expected {Expected}, got {Actual}",
                clientId, expectedVersion, actualVersion);
        }

        /// <summary>
        /// 记录协议兼容性恢复事件（线程安全）
        /// </summary>
        public void RecordProtocolCompatibilityRecovery(string clientId, string protocolVersion, bool successful)
        {
            if (_disposed || string.IsNullOrEmpty(clientId)) return;

            var metrics = GetOrCreateClientMetrics(clientId);
            metrics.RecordProtocolCompatibilityRecovery(successful);

            _logger.LogInformation("Protocol compatibility recovery for client {ClientId}: {Protocol} -> {Result}",
                clientId, protocolVersion, successful ? "SUCCESS" : "FAILED");
        }

        /// <summary>
        /// 线程安全地获取或创建客户端指标
        /// </summary>
        private ThreadSafePerformanceMetrics GetOrCreateClientMetrics(string clientId)
        {
            return _clientMetrics.GetOrAdd(clientId, _ => new ThreadSafePerformanceMetrics());
        }

        /// <summary>
        /// 异步报告性能指标
        /// </summary>
        private async void ReportMetricsAsync(object? state)
        {
            if (_disposed) return;

            // 使用信号量确保只有一个报告线程在运行
            if (!await _reportSemaphore.WaitAsync(100))
            {
                _logger.LogDebug("Skipping metrics report - previous report still running");
                return;
            }

            try
            {
                await ReportMetricsInternalAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while reporting metrics");
            }
            finally
            {
                _reportSemaphore.Release();
            }
        }

        /// <summary>
        /// 内部报告指标逻辑
        /// </summary>
        private async Task ReportMetricsInternalAsync()
        {
            var activeClients = 0;
            var totalSuccessfulMessages = 0L;
            var totalProcessingErrors = 0L;
            var totalCorruptionRecoveries = 0L;

            foreach (var kvp in _clientMetrics.ToArray())
            {
                var clientId = kvp.Key;
                var metrics = kvp.Value;
                var snapshot = metrics.GetSnapshot();

                if (snapshot.LastUpdateTime < DateTime.UtcNow.Subtract(_clientTimeout))
                {
                    // 客户端已超时，将在下次清理时移除
                    continue;
                }

                activeClients++;
                totalSuccessfulMessages += snapshot.SuccessfulMessages;
                totalProcessingErrors += snapshot.ProcessingErrors;
                totalCorruptionRecoveries += snapshot.SuccessfulRecoveries;

                var totalMessages = snapshot.SuccessfulMessages + snapshot.ProcessingErrors;
                var successRate = totalMessages > 0 ? (double)snapshot.SuccessfulMessages / totalMessages * 100 : 100.0;
                var avgProcessingTime = snapshot.TotalProcessingTimeMs / Math.Max(1, snapshot.SuccessfulMessages);

                _logger.LogInformation(
                    "Client {ClientId} metrics: " +
                    "Success Rate: {SuccessRate:F2}%, " +
                    "Avg Processing Time: {AvgProcessingTime:F2}ms, " +
                    "Corruption Recoveries: {CorruptionRecoveries}/{CorruptionAttempts}, " +
                    "Protocol Recoveries: {ProtocolRecoveries}/{ProtocolAttempts}, " +
                    "Bytes Processed: {BytesProcessed}, " +
                    "Version Mismatches: {VersionMismatches}",
                    clientId, successRate, avgProcessingTime,
                    snapshot.SuccessfulRecoveries, snapshot.CorruptionRecoveryAttempts,
                    snapshot.SuccessfulProtocolRecoveries, snapshot.ProtocolCompatibilityAttempts,
                    snapshot.TotalBytesDeserialized, snapshot.ProtocolVersionMismatches);

                // 性能警告
                if (successRate < 95.0)
                {
                    _logger.LogWarning("Low success rate for client {ClientId}: {SuccessRate:F2}%",
                        clientId, successRate);
                }

                if (snapshot.CorruptionRecoveryAttempts > 5)
                {
                    _logger.LogWarning("High corruption recovery attempts for client {ClientId}: {Attempts}",
                        clientId, snapshot.CorruptionRecoveryAttempts);
                }
            }

            // 汇总报告
            _logger.LogInformation(
                "Network Performance Summary: " +
                "Active Clients: {ActiveClients}, " +
                "Total Successful Messages: {TotalSuccessfulMessages}, " +
                "Total Processing Errors: {TotalProcessingErrors}, " +
                "Total Corruption Recoveries: {TotalCorruptionRecoveries}",
                activeClients, totalSuccessfulMessages, totalProcessingErrors, totalCorruptionRecoveries);

            // 模拟异步操作
            await Task.Delay(1);
        }

        /// <summary>
        /// 清理过期的客户端指标
        /// </summary>
        private void CleanupExpiredClients(object? state)
        {
            if (_disposed) return;

            var cutoffTime = DateTime.UtcNow.Subtract(_clientTimeout);
            var expiredClients = new List<string>();

            foreach (var kvp in _clientMetrics.ToArray())
            {
                var clientId = kvp.Key;
                var metrics = kvp.Value;

                if (metrics.GetSnapshot().LastUpdateTime < cutoffTime)
                {
                    expiredClients.Add(clientId);
                }
            }

            foreach (var clientId in expiredClients)
            {
                if (_clientMetrics.TryRemove(clientId, out var removedMetrics))
                {
                    removedMetrics.Dispose();
                    _logger.LogDebug("Cleaned up expired metrics for client {ClientId}", clientId);
                }
            }

            if (expiredClients.Count > 0)
            {
                _logger.LogInformation("Cleaned up {Count} expired client metrics", expiredClients.Count);
            }
        }

        /// <summary>
        /// 获取客户端的性能指标快照
        /// </summary>
        public PerformanceMetricsSnapshot? GetClientMetrics(string clientId)
        {
            if (_disposed || string.IsNullOrEmpty(clientId)) return null;

            return _clientMetrics.TryGetValue(clientId, out var metrics) ? metrics.GetSnapshot() : null;
        }

        /// <summary>
        /// 清理已断开客户端的指标
        /// </summary>
        public void CleanupClientMetrics(string clientId)
        {
            if (_disposed || string.IsNullOrEmpty(clientId)) return;

            if (_clientMetrics.TryRemove(clientId, out var metrics))
            {
                metrics.Dispose();
                _logger.LogDebug("Cleaned up performance metrics for client {ClientId}", clientId);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _reportTimer?.Dispose();
            _cleanupTimer?.Dispose();
            _reportSemaphore?.Dispose();

            // 清理所有客户端指标
            foreach (var kvp in _clientMetrics.ToArray())
            {
                if (_clientMetrics.TryRemove(kvp.Key, out var metrics))
                {
                    metrics.Dispose();
                }
            }

            _logger.LogInformation("Network performance monitor disposed");
        }
    }
}
