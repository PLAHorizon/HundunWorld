using System;
using System.Buffers;
using System.Diagnostics;
using System.Threading;
using Horizon.Game.Message;
using Microsoft.Extensions.Logging;
using Horizon.Game.Core.ProtocolDetection;

namespace Horizon.Game.Core.Performance
{
    /// <summary>
    /// 高性能TCP消息处理器 - 使用Span&lt;T&gt;和Memory&lt;T&gt;减少内存分配
    /// </summary>
    public class HighPerformanceMessageProcessor
    {
        private static readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
        private const int MaxStackallocSize = 1024; // 1KB以下使用stackalloc

        private readonly ProtocolDeserializerFactory _protocolFactory;
        private readonly ILogger<HighPerformanceMessageProcessor> _logger;
        private readonly MessageProcessingStats _stats;

        public HighPerformanceMessageProcessor(
            ProtocolDeserializerFactory protocolFactory,
            ILogger<HighPerformanceMessageProcessor> logger)
        {
            _protocolFactory = protocolFactory;
            _logger = logger;
            _stats = new MessageProcessingStats();
        }

        /// <summary>
        /// 获取处理统计信息
        /// </summary>
        public MessageProcessingStats Stats => _stats;        /// <summary>
                                                              /// 尝试反序列化消息 - 集成协议检测和高性能处理
                                                              /// </summary>
        public MessageProcessingResult TryDeserializeMessage(ReadOnlySpan<byte> data, string? clientId = null)
        {
            var stopwatch = Stopwatch.StartNew();
            var usedMemoryPool = false;

            try
            {
                // 首先进行快速协议检测
                var protocolResult = DetectProtocolFast(data); if (protocolResult == FastProtocolDetectionResult.Invalid)
                {
                    stopwatch.Stop();
                    return MessageProcessingResult.CreateFailure(
                        "Invalid message format detected",
                        stopwatch.Elapsed,
                        data.Length,
                        clientId);
                }
                if (protocolResult == FastProtocolDetectionResult.Incomplete)
                {
                    stopwatch.Stop();
                    return MessageProcessingResult.CreateFailure(
                        "Incomplete message data",
                        stopwatch.Elapsed,
                        data.Length,
                        clientId);
                }

                // 使用协议工厂进行反序列化
                var deserializationResult = _protocolFactory.TryDeserialize(data);

                stopwatch.Stop();

                // 记录统计信息
                _stats.RecordMessage(data.Length, stopwatch.ElapsedMilliseconds, usedMemoryPool);

                if (deserializationResult.Success && deserializationResult.Packet != null)
                {
                    _logger.LogDebug("客户端 {ClientId} 消息反序列化成功，使用协议: {Protocol}",
                        clientId, deserializationResult.ProtocolVersion);
                    return MessageProcessingResult.CreateSuccess(
                        deserializationResult.Packet,
                        deserializationResult.ProtocolVersion ?? "Unknown",
                        stopwatch.Elapsed,
                        data.Length,
                        clientId,
                        usedMemoryPool);
                }
                else
                {
                    _logger.LogWarning("客户端 {ClientId} 消息反序列化失败: {Error}",
                        clientId, deserializationResult.ErrorMessage);
                    return MessageProcessingResult.CreateFailure(
                        deserializationResult.ErrorMessage ?? "反序列化错误",
                        stopwatch.Elapsed,
                        data.Length,
                        clientId);
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "处理客户端 {ClientId} 消息时发生异常", clientId);
                return MessageProcessingResult.CreateFailure(
                    $"处理异常: {ex.Message}",
                    stopwatch.Elapsed,
                    data.Length,
                    clientId);
            }
        }

        /// <summary>
        /// 高性能协议检测 - 使用模式匹配避免多次数据访问
        /// </summary>
        public static FastProtocolDetectionResult DetectProtocolFast(ReadOnlySpan<byte> data)
        {
            if (data.Length < 4)
                return FastProtocolDetectionResult.Unknown;

            var messageLength = BitConverter.ToInt32(data.Slice(0, 4));

            // 快速长度验证
            if (messageLength <= 0 || messageLength > 50 * 1024 * 1024)
                return FastProtocolDetectionResult.Invalid;

            // 模式匹配检测
            return data.Length switch
            {
                var len when len == 4 + messageLength => FastProtocolDetectionResult.LegacyV2, // 长度匹配，无标志位
                var len when len == 5 + messageLength - 1 => // 当前版本：长度包含标志位
                    CheckCurrentProtocolFlags(data[4]) ? FastProtocolDetectionResult.Current : FastProtocolDetectionResult.LegacyV2,
                var len when len >= 4 + messageLength => FastProtocolDetectionResult.LegacyV1, // 可能有额外数据
                _ => FastProtocolDetectionResult.Incomplete
            };
        }

        /// <summary>
        /// 检查标志位是否符合当前协议格式
        /// </summary>
        private static bool CheckCurrentProtocolFlags(byte flags)
        {
            // 当前协议只使用低3位：压缩标志、加密标志、保留位
            return (flags & 0xF8) == 0;
        }

        /// <summary>
        /// 高性能消息边界搜索 - 使用SIMD优化的搜索
        /// </summary>
        public static int FindNextMessageBoundary(ReadOnlySpan<byte> buffer, int startOffset = 0)
        {
            if (startOffset >= buffer.Length - 4)
                return -1;

            var searchSpan = buffer.Slice(startOffset);

            // 对于较小的缓冲区，使用简单循环
            if (searchSpan.Length < 64)
            {
                return FindBoundaryLinear(searchSpan) + startOffset;
            }

            // 对于较大缓冲区，可以使用向量化搜索（.NET 6+）
            return FindBoundaryVectorized(searchSpan) + startOffset;
        }

        /// <summary>
        /// 线性搜索消息边界
        /// </summary>
        private static int FindBoundaryLinear(ReadOnlySpan<byte> span)
        {
            for (int i = 0; i <= span.Length - 4; i++)
            {
                if (TryReadMessageLength(span.Slice(i), out var length, out var headerSize))
                {
                    var totalSize = headerSize + length;
                    if (i + totalSize <= span.Length)
                        return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// 向量化搜索消息边界（可以进一步优化）
        /// </summary>
        private static int FindBoundaryVectorized(ReadOnlySpan<byte> span)
        {
            // 为了简化，这里还是使用线性搜索
            // 在实际生产环境中，可以使用System.Numerics.Vectors进行SIMD优化
            return FindBoundaryLinear(span);
        }

        /// <summary>
        /// 高性能消息长度解析 - 使用Span避免数组复制
        /// </summary>
        public static bool TryReadMessageLength(ReadOnlySpan<byte> data, out int messageLength, out int headerSize)
        {
            messageLength = 0;
            headerSize = 4; // 默认4字节长度前缀

            if (data.Length < 4)
                return false;

            // 使用Span直接读取，避免BitConverter的数组参数要求
            messageLength = BitConverter.ToInt32(data.Slice(0, 4));

            // 验证长度的合理性
            if (messageLength <= 0 || messageLength > 50 * 1024 * 1024) // 50MB限制
                return false;

            // 检查是否有标志位（当前协议版本）
            if (data.Length >= 5)
            {
                var potentialFlags = data[4];
                // 如果标志位看起来合理（只使用了低3位）
                if ((potentialFlags & 0xF8) == 0)
                {
                    headerSize = 5; // 4字节长度 + 1字节标志
                }
            }

            return true;
        }

        /// <summary>
        /// 高性能消息提取 - 使用内存池减少分配
        /// </summary>
        public static PooledMessageData ExtractMessageData(ReadOnlySpan<byte> buffer, int messageLength, int headerSize)
        {
            var totalMessageSize = headerSize + messageLength;
            if (buffer.Length < totalMessageSize)
                throw new ArgumentException("Buffer too small for message");

            // 对于小消息使用stackalloc，大消息使用池
            if (messageLength <= MaxStackallocSize)
            {
                // 小消息：使用栈分配（零拷贝）
                return new PooledMessageData(buffer.Slice(headerSize, messageLength), isPooled: false);
            }
            else
            {
                // 大消息：使用对象池
                var pooledArray = _arrayPool.Rent(messageLength);
                var messageSpan = buffer.Slice(headerSize, messageLength);
                messageSpan.CopyTo(pooledArray);

                return new PooledMessageData(pooledArray.AsSpan(0, messageLength), isPooled: true, pooledArray);
            }
        }
    }    /// <summary>
         /// 快速协议检测结果枚举
         /// </summary>
    public enum FastProtocolDetectionResult
    {
        Unknown,
        Invalid,
        Incomplete,
        Current,
        LegacyV2,
        LegacyV1
    }    /// <summary>
         /// 池化消息数据包装器 - 自动管理内存池资源
         /// </summary>
    public readonly ref struct PooledMessageData
    {
        private readonly byte[]? _pooledArray;
        private readonly bool _isPooled;

        public ReadOnlySpan<byte> Data { get; }

        internal PooledMessageData(ReadOnlySpan<byte> data, bool isPooled, byte[]? pooledArray = null)
        {
            Data = data;
            _isPooled = isPooled;
            _pooledArray = pooledArray;
        }

        public void Dispose()
        {
            if (_isPooled && _pooledArray != null)
            {
                ArrayPool<byte>.Shared.Return(_pooledArray);
            }
        }
    }

    /// <summary>
    /// 高性能消息处理统计
    /// </summary>
    public class MessageProcessingStats
    {
        private long _totalMessages;
        private long _totalBytes;
        private long _processingTimeMs;
        private long _memoryAllocations;

        public long TotalMessages => _totalMessages;
        public long TotalBytes => _totalBytes;
        public long ProcessingTimeMs => _processingTimeMs;
        public long MemoryAllocations => _memoryAllocations;

        public double AverageMessageSize => _totalMessages > 0 ? (double)_totalBytes / _totalMessages : 0;
        public double AverageProcessingTime => _totalMessages > 0 ? (double)_processingTimeMs / _totalMessages : 0;
        public double MemoryEfficiency => _totalBytes > 0 ? (double)_memoryAllocations / _totalBytes : 0;

        internal void RecordMessage(int messageSize, long processingTime, bool wasPooled)
        {
            Interlocked.Increment(ref _totalMessages);
            Interlocked.Add(ref _totalBytes, messageSize);
            Interlocked.Add(ref _processingTimeMs, processingTime);

            if (wasPooled)
                Interlocked.Increment(ref _memoryAllocations);
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _totalMessages, 0);
            Interlocked.Exchange(ref _totalBytes, 0);
            Interlocked.Exchange(ref _processingTimeMs, 0);
            Interlocked.Exchange(ref _memoryAllocations, 0);
        }
    }
}
