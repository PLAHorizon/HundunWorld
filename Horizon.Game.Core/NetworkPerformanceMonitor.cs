using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.Core
{
    /// <summary>
    /// 网络性能监控类，用于监控TCP包处理性能和错误恢复效率
    /// </summary>
    public class NetworkPerformanceMonitor
    {
        private readonly ILogger<NetworkPerformanceMonitor> _logger;
        private readonly ConcurrentDictionary<string, PerformanceMetrics> _clientMetrics = new();
        private readonly Timer _reportTimer;

        public NetworkPerformanceMonitor(ILogger<NetworkPerformanceMonitor> logger)
        {
            _logger = logger;
            _reportTimer = new Timer(ReportMetrics, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        /// <summary>
        /// 记录消息处理开始
        /// </summary>
        public IDisposable TrackMessageProcessing(string clientId, string messageType)
        {
            var metrics = _clientMetrics.GetOrAdd(clientId, _ => new PerformanceMetrics());
            return new MessageProcessingTracker(metrics, messageType);
        }

        /// <summary>
        /// 记录TCP包损坏恢复事件
        /// </summary>
        public void RecordCorruptionRecovery(string clientId, int bytesSkipped, bool successful)
        {
            var metrics = _clientMetrics.GetOrAdd(clientId, _ => new PerformanceMetrics());
            Interlocked.Increment(ref metrics.CorruptionRecoveryAttempts);

            if (successful)
            {
                Interlocked.Increment(ref metrics.SuccessfulRecoveries);
                Interlocked.Add(ref metrics.TotalBytesSkipped, bytesSkipped);
            }

            _logger.LogWarning("TCP corruption recovery for client {ClientId}: {Result}, bytes skipped: {BytesSkipped}",
                clientId, successful ? "SUCCESS" : "FAILED", bytesSkipped);
        }

        /// <summary>
        /// 记录消息处理错误
        /// </summary>
        public void RecordProcessingError(string clientId, string errorType, Exception exception)
        {
            var metrics = _clientMetrics.GetOrAdd(clientId, _ => new PerformanceMetrics());
            Interlocked.Increment(ref metrics.ProcessingErrors);

            _logger.LogError(exception, "Message processing error for client {ClientId}, type: {ErrorType}",
                clientId, errorType);
        }

        /// <summary>
        /// 记录消息处理成功
        /// </summary>
        public void RecordSuccessfulProcessing(string clientId, string messageType, long processingTimeMs)
        {
            var metrics = _clientMetrics.GetOrAdd(clientId, _ => new PerformanceMetrics());
            Interlocked.Increment(ref metrics.SuccessfulMessages);
            Interlocked.Add(ref metrics.TotalProcessingTimeMs, processingTimeMs);

            // 只有当处理时间超过阈值时才记录警告
            if (processingTimeMs > 100) // 100ms阈值
            {
                _logger.LogWarning("Slow message processing for client {ClientId}: {MessageType} took {ProcessingTime}ms",
                    clientId, messageType, processingTimeMs);
            }
        }

        /// <summary>
        /// 记录反序列化成功
        /// </summary>
        public void RecordSuccessfulDeserialization(string clientId, int messageLength)
        {
            var metrics = _clientMetrics.GetOrAdd(clientId, _ => new PerformanceMetrics());
            Interlocked.Increment(ref metrics.SuccessfulDeserializations);
            Interlocked.Add(ref metrics.TotalBytesDeserialized, messageLength);

            _logger.LogTrace("Successful deserialization for client {ClientId}: {MessageLength} bytes",
                clientId, messageLength);
        }

        /// <summary>
        /// 记录协议版本不匹配事件
        /// </summary>
        public void RecordProtocolVersionMismatch(string clientId, int expectedVersion, int actualVersion)
        {
            var metrics = _clientMetrics.GetOrAdd(clientId, _ => new PerformanceMetrics());
            Interlocked.Increment(ref metrics.ProtocolVersionMismatches);

            _logger.LogWarning("Protocol version mismatch for client {ClientId}: expected {Expected}, got {Actual}",
                clientId, expectedVersion, actualVersion);
        }

        /// <summary>
        /// 记录协议兼容性恢复事件
        /// </summary>
        public void RecordProtocolCompatibilityRecovery(string clientId, string protocolVersion, bool successful)
        {
            var metrics = _clientMetrics.GetOrAdd(clientId, _ => new PerformanceMetrics());
            Interlocked.Increment(ref metrics.ProtocolCompatibilityAttempts);

            if (successful)
            {
                Interlocked.Increment(ref metrics.SuccessfulProtocolRecoveries);
            }

            _logger.LogInformation("Protocol compatibility recovery for client {ClientId}: {Protocol} -> {Result}",
                clientId, protocolVersion, successful ? "SUCCESS" : "FAILED");
        }

        /// <summary>
        /// 定期报告性能指标
        /// </summary>
        private void ReportMetrics(object state)
        {
            foreach (var kvp in _clientMetrics)
            {
                var clientId = kvp.Key;
                var metrics = kvp.Value;

                var totalMessages = metrics.SuccessfulMessages + metrics.ProcessingErrors;
                if (totalMessages == 0) continue;

                var successRate = (double)metrics.SuccessfulMessages / totalMessages * 100;
                var avgProcessingTime = metrics.SuccessfulMessages > 0
                    ? (double)metrics.TotalProcessingTimeMs / metrics.SuccessfulMessages
                    : 0; _logger.LogInformation(
                    "Network Performance Report for {ClientId}: " +
                    "Success Rate: {SuccessRate:F2}%, " +
                    "Avg Processing Time: {AvgProcessingTime:F2}ms, " +
                    "Corruption Recoveries: {CorruptionRecoveries}/{CorruptionAttempts}, " +
                    "Protocol Recoveries: {ProtocolRecoveries}/{ProtocolAttempts}, " +
                    "Bytes Processed: {BytesProcessed}, " +
                    "Version Mismatches: {VersionMismatches}",
                    clientId, successRate, avgProcessingTime,
                    metrics.SuccessfulRecoveries, metrics.CorruptionRecoveryAttempts,
                    metrics.SuccessfulProtocolRecoveries, metrics.ProtocolCompatibilityAttempts,
                    metrics.TotalBytesDeserialized, metrics.ProtocolVersionMismatches);

                // 如果成功率太低或恢复次数太多，发出警告
                if (successRate < 95.0)
                {
                    _logger.LogWarning("Low success rate for client {ClientId}: {SuccessRate:F2}%",
                        clientId, successRate);
                }

                if (metrics.CorruptionRecoveryAttempts > 5)
                {
                    _logger.LogWarning("High corruption recovery attempts for client {ClientId}: {Attempts}",
                        clientId, metrics.CorruptionRecoveryAttempts);
                }
            }
        }

        /// <summary>
        /// 获取客户端的性能指标
        /// </summary>
        public PerformanceMetrics GetClientMetrics(string clientId)
        {
            return _clientMetrics.GetOrAdd(clientId, _ => new PerformanceMetrics());
        }

        /// <summary>
        /// 清理已断开客户端的指标
        /// </summary>
        public void CleanupClientMetrics(string clientId)
        {
            _clientMetrics.TryRemove(clientId, out _);
            _logger.LogDebug("Cleaned up performance metrics for client {ClientId}", clientId);
        }

        public void Dispose()
        {
            _reportTimer?.Dispose();
        }
    }    /// <summary>
         /// 性能指标数据结构
         /// </summary>
    public class PerformanceMetrics
    {
        public long SuccessfulMessages;
        public long ProcessingErrors;
        public long TotalProcessingTimeMs;
        public long CorruptionRecoveryAttempts;
        public long SuccessfulRecoveries;
        public long TotalBytesSkipped;
        public long SuccessfulDeserializations;
        public long TotalBytesDeserialized;
        public long ProtocolVersionMismatches;
        public long ProtocolCompatibilityAttempts;
        public long SuccessfulProtocolRecoveries;
    }

    /// <summary>
    /// 消息处理时间跟踪器
    /// </summary>
    internal class MessageProcessingTracker : IDisposable
    {
        private readonly PerformanceMetrics _metrics;
        private readonly string _messageType;
        private readonly Stopwatch _stopwatch;

        public MessageProcessingTracker(PerformanceMetrics metrics, string messageType)
        {
            _metrics = metrics;
            _messageType = messageType;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            // 这里可以根据需要调用相应的性能记录方法
            // 但由于静态上下文限制，这里只记录时间
        }
    }
}
