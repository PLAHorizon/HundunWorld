// filepath: d:\Long\Flax\AIProjects\0608\Horizon.Game.Core\Adapters\PerformanceMetrics.cs
using System;
using System.Threading;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 性能指标统计类，用于收集消息处理的性能数据
    /// </summary>
    public class PerformanceMetrics
    {
        // 接收统计
        private long _totalReceivedMessages;
        private long _totalReceivedBytes;
        private long _totalReceiveProcessingTime;

        // 发送统计
        private long _totalSentMessages;
        private long _totalSentBytes;
        private long _totalSentCompressedBytes;
        private long _totalSendProcessingTime;

        // 错误统计
        private long _totalErrors;

        /// <summary>
        /// 获取总接收消息数
        /// </summary>
        public long TotalReceivedMessages => _totalReceivedMessages;

        /// <summary>
        /// 获取总接收字节数
        /// </summary>
        public long TotalReceivedBytes => _totalReceivedBytes;

        /// <summary>
        /// 获取平均接收处理时间（毫秒）
        /// </summary>
        public double AverageReceiveProcessingTime =>
            _totalReceivedMessages > 0 ? (double)_totalReceiveProcessingTime / _totalReceivedMessages : 0;

        /// <summary>
        /// 获取总发送消息数
        /// </summary>
        public long TotalSentMessages => _totalSentMessages;

        /// <summary>
        /// 获取总发送字节数
        /// </summary>
        public long TotalSentBytes => _totalSentBytes;

        /// <summary>
        /// 获取总发送压缩后字节数
        /// </summary>
        public long TotalSentCompressedBytes => _totalSentCompressedBytes;

        /// <summary>
        /// 获取平均发送处理时间（毫秒）
        /// </summary>
        public double AverageSendProcessingTime =>
            _totalSentMessages > 0 ? (double)_totalSendProcessingTime / _totalSentMessages : 0;

        /// <summary>
        /// 获取压缩率
        /// </summary>
        public double CompressionRatio =>
            _totalSentBytes > 0 ? (double)_totalSentCompressedBytes / _totalSentBytes : 1.0;

        /// <summary>
        /// 获取错误总数
        /// </summary>
        public long TotalErrors => _totalErrors;

        /// <summary>
        /// 记录接收统计
        /// </summary>
        public void RecordReceive(int bytesReceived, double processingTimeMs)
        {
            Interlocked.Increment(ref _totalReceivedMessages);
            Interlocked.Add(ref _totalReceivedBytes, bytesReceived);
            Interlocked.Add(ref _totalReceiveProcessingTime, (long)processingTimeMs);
        }

        /// <summary>
        /// 记录发送统计
        /// </summary>
        public void RecordSend(int originalBytes, int compressedBytes, double processingTimeMs)
        {
            Interlocked.Increment(ref _totalSentMessages);
            Interlocked.Add(ref _totalSentBytes, originalBytes);
            Interlocked.Add(ref _totalSentCompressedBytes, compressedBytes);
            Interlocked.Add(ref _totalSendProcessingTime, (long)processingTimeMs);
        }

        /// <summary>
        /// 记录错误
        /// </summary>
        public void RecordError()
        {
            Interlocked.Increment(ref _totalErrors);
        }

        /// <summary>
        /// 获取性能指标摘要
        /// </summary>
        public string GetSummary()
        {
            return $"性能指标:\n" +
                $"  接收: {_totalReceivedMessages} 消息, {FormatBytes(_totalReceivedBytes)}, " +
                $"平均 {AverageReceiveProcessingTime:F2}ms/消息\n" +
                $"  发送: {_totalSentMessages} 消息, {FormatBytes(_totalSentBytes)} -> {FormatBytes(_totalSentCompressedBytes)} " +
                $"({CompressionRatio:P2}), 平均 {AverageSendProcessingTime:F2}ms/消息\n" +
                $"  错误: {_totalErrors}";
        }

        /// <summary>
        /// 格式化字节大小为人类可读形式
        /// </summary>
        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
