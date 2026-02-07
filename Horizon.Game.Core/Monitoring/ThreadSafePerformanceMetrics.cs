using System;
using System.Threading;

namespace Horizon.Game.Core.Monitoring
{
    /// <summary>
    /// 线程安全的性能指标数据结构
    /// </summary>
    public class ThreadSafePerformanceMetrics : IDisposable
    {
        private long _successfulMessages;
        private long _processingErrors;
        private long _totalProcessingTimeMs;
        private long _corruptionRecoveryAttempts;
        private long _successfulRecoveries;
        private long _totalBytesSkipped;
        private long _successfulDeserializations;
        private long _totalBytesDeserialized;
        private long _protocolVersionMismatches;
        private long _protocolCompatibilityAttempts;
        private long _successfulProtocolRecoveries;
        private DateTime _lastUpdateTime;
        private readonly object _updateTimeLock = new();

        public ThreadSafePerformanceMetrics()
        {
            _lastUpdateTime = DateTime.UtcNow;
        }

        public void RecordSuccessfulProcessing(long processingTimeMs)
        {
            Interlocked.Increment(ref _successfulMessages);
            Interlocked.Add(ref _totalProcessingTimeMs, processingTimeMs);
            UpdateLastUpdateTime();
        }

        public void RecordProcessingError()
        {
            Interlocked.Increment(ref _processingErrors);
            UpdateLastUpdateTime();
        }

        public void RecordCorruptionRecovery(int bytesSkipped, bool successful)
        {
            Interlocked.Increment(ref _corruptionRecoveryAttempts);

            if (successful)
            {
                Interlocked.Increment(ref _successfulRecoveries);
                Interlocked.Add(ref _totalBytesSkipped, bytesSkipped);
            }

            UpdateLastUpdateTime();
        }

        public void RecordSuccessfulDeserialization(int messageLength)
        {
            Interlocked.Increment(ref _successfulDeserializations);
            Interlocked.Add(ref _totalBytesDeserialized, messageLength);
            UpdateLastUpdateTime();
        }

        public void RecordProtocolVersionMismatch()
        {
            Interlocked.Increment(ref _protocolVersionMismatches);
            UpdateLastUpdateTime();
        }

        public void RecordProtocolCompatibilityRecovery(bool successful)
        {
            Interlocked.Increment(ref _protocolCompatibilityAttempts);

            if (successful)
            {
                Interlocked.Increment(ref _successfulProtocolRecoveries);
            }

            UpdateLastUpdateTime();
        }

        private void UpdateLastUpdateTime()
        {
            lock (_updateTimeLock)
            {
                _lastUpdateTime = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// 获取当前指标的线程安全快照
        /// </summary>
        public PerformanceMetricsSnapshot GetSnapshot()
        {
            lock (_updateTimeLock)
            {
                return new PerformanceMetricsSnapshot
                {
                    SuccessfulMessages = Interlocked.Read(ref _successfulMessages),
                    ProcessingErrors = Interlocked.Read(ref _processingErrors),
                    TotalProcessingTimeMs = Interlocked.Read(ref _totalProcessingTimeMs),
                    CorruptionRecoveryAttempts = Interlocked.Read(ref _corruptionRecoveryAttempts),
                    SuccessfulRecoveries = Interlocked.Read(ref _successfulRecoveries),
                    TotalBytesSkipped = Interlocked.Read(ref _totalBytesSkipped),
                    SuccessfulDeserializations = Interlocked.Read(ref _successfulDeserializations),
                    TotalBytesDeserialized = Interlocked.Read(ref _totalBytesDeserialized),
                    ProtocolVersionMismatches = Interlocked.Read(ref _protocolVersionMismatches),
                    ProtocolCompatibilityAttempts = Interlocked.Read(ref _protocolCompatibilityAttempts),
                    SuccessfulProtocolRecoveries = Interlocked.Read(ref _successfulProtocolRecoveries),
                    LastUpdateTime = _lastUpdateTime
                };
            }
        }

        public void Dispose()
        {
            // 清理资源（如果有的话）
        }
    }

    /// <summary>
    /// 性能指标快照 - 不可变数据结构
    /// </summary>
    public class PerformanceMetricsSnapshot
    {
        public long SuccessfulMessages { get; init; }
        public long ProcessingErrors { get; init; }
        public long TotalProcessingTimeMs { get; init; }
        public long CorruptionRecoveryAttempts { get; init; }
        public long SuccessfulRecoveries { get; init; }
        public long TotalBytesSkipped { get; init; }
        public long SuccessfulDeserializations { get; init; }
        public long TotalBytesDeserialized { get; init; }
        public long ProtocolVersionMismatches { get; init; }
        public long ProtocolCompatibilityAttempts { get; init; }
        public long SuccessfulProtocolRecoveries { get; init; }
        public DateTime LastUpdateTime { get; init; }

        /// <summary>
        /// 计算成功率
        /// </summary>
        public double SuccessRate
        {
            get
            {
                var totalMessages = SuccessfulMessages + ProcessingErrors;
                return totalMessages > 0 ? (double)SuccessfulMessages / totalMessages * 100 : 100.0;
            }
        }

        /// <summary>
        /// 计算平均处理时间
        /// </summary>
        public double AverageProcessingTimeMs
        {
            get
            {
                return SuccessfulMessages > 0 ? (double)TotalProcessingTimeMs / SuccessfulMessages : 0.0;
            }
        }

        /// <summary>
        /// 计算损坏恢复成功率
        /// </summary>
        public double CorruptionRecoverySuccessRate
        {
            get
            {
                return CorruptionRecoveryAttempts > 0 ? (double)SuccessfulRecoveries / CorruptionRecoveryAttempts * 100 : 100.0;
            }
        }

        /// <summary>
        /// 计算协议兼容性恢复成功率
        /// </summary>
        public double ProtocolRecoverySuccessRate
        {
            get
            {
                return ProtocolCompatibilityAttempts > 0 ? (double)SuccessfulProtocolRecoveries / ProtocolCompatibilityAttempts * 100 : 100.0;
            }
        }
    }
}
