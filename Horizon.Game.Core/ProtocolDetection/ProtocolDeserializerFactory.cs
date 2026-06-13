using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Horizon.Game.Message;
using Horizon.Game.Message.Network;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.Core.ProtocolDetection
{
    /// <summary>
    /// 协议检测结果
    /// </summary>
    public class ProtocolDetectionResult
    {
        public bool Success { get; set; }
        public HorizonMessagePacket? Packet { get; set; }
        public string? ProtocolVersion { get; set; }
        public string? ErrorMessage { get; set; }
        public TimeSpan ProcessingTime { get; set; }
        public int BytesProcessed { get; set; }
    }

    /// <summary>
    /// 协议反序列化器工厂 - 管理多个协议版本的反序列化器
    /// </summary>
    public class ProtocolDeserializerFactory
    {
        private readonly IProtocolDeserializer[] _deserializers;
        private readonly ILogger<ProtocolDeserializerFactory> _logger;
        private readonly Dictionary<string, int> _protocolUsageStats = new();
        private readonly object _statsLock = new();

        public ProtocolDeserializerFactory(IEnumerable<IProtocolDeserializer> deserializers, ILogger<ProtocolDeserializerFactory> logger)
        {
            _deserializers = deserializers.OrderBy(d => d.Priority).ToArray();
            _logger = logger;
        }

        /// <summary>
        /// 尝试反序列化数据，按优先级顺序尝试各个协议版本
        /// </summary>
        public ProtocolDetectionResult TryDeserialize(ReadOnlySpan<byte> data, byte[]? encryptionKey = null)
        {
            var stopwatch = Stopwatch.StartNew();

            foreach (var deserializer in _deserializers)
            {
                try
                {
                    if (deserializer.CanHandle(data))
                    {
                        var packet = deserializer.TryDeserialize(data, encryptionKey);
                        if (packet != null)
                        {
                            stopwatch.Stop();
                            RecordProtocolUsage(deserializer.ProtocolVersion);

                            _logger.LogDebug("使用协议 {Protocol} 反序列化成功，耗时 {Ms}ms",
                                deserializer.ProtocolVersion, stopwatch.Elapsed.TotalMilliseconds);

                            return new ProtocolDetectionResult
                            {
                                Success = true,
                                Packet = packet,
                                ProtocolVersion = deserializer.ProtocolVersion,
                                ProcessingTime = stopwatch.Elapsed,
                                BytesProcessed = data.Length
                            };
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "协议反序列化器 {Protocol} 处理数据失败", deserializer.ProtocolVersion);
                    // 继续尝试下一个反序列化器
                }
            }

            stopwatch.Stop();
            return new ProtocolDetectionResult
            {
                Success = false,
                ErrorMessage = "No compatible protocol deserializer found",
                ProcessingTime = stopwatch.Elapsed,
                BytesProcessed = data.Length
            };
        }

        /// <summary>
        /// 记录协议使用统计
        /// </summary>
        private void RecordProtocolUsage(string protocolVersion)
        {
            lock (_statsLock)
            {
                _protocolUsageStats[protocolVersion] = _protocolUsageStats.GetValueOrDefault(protocolVersion, 0) + 1;
            }
        }

        /// <summary>
        /// 获取协议使用统计信息
        /// </summary>
        public Dictionary<string, int> GetProtocolUsageStats()
        {
            lock (_statsLock)
            {
                return new Dictionary<string, int>(_protocolUsageStats);
            }
        }

        /// <summary>
        /// 清理统计信息
        /// </summary>
        public void ClearStats()
        {
            lock (_statsLock)
            {
                _protocolUsageStats.Clear();
            }
        }
    }
}
