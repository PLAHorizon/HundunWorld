using System;
using System.Collections.Generic;
using Horizon.Game.Message;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Core.Performance
{
    /// <summary>
    /// 消息处理结果 - 封装消息反序列化和处理的结果
    /// </summary>
    public class MessageProcessingResult
    {
        /// <summary>
        /// 是否成功处理消息
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 反序列化后的消息包
        /// </summary>
        public HorizonMessagePacket? Packet { get; set; }

        /// <summary>
        /// 错误信息（如果处理失败）
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 异常详情（如果有的话）
        /// </summary>
        public Exception? Exception { get; set; }

        /// <summary>
        /// 处理耗时（毫秒）
        /// </summary>
        public double ProcessingTimeMs { get; set; }

        /// <summary>
        /// 使用的内存（字节）
        /// </summary>
        public long MemoryUsed { get; set; }

        /// <summary>
        /// 消息优先级
        /// </summary>
        public MessagePriority Priority { get; set; } = MessagePriority.Normal;

        /// <summary>
        /// 处理的字节数
        /// </summary>
        public int BytesProcessed { get; set; }

        /// <summary>
        /// 检测到的协议类型
        /// </summary>
        public string? ProtocolType { get; set; }

        /// <summary>
        /// 是否使用了内存池
        /// </summary>
        public bool UsedMemoryPool { get; set; }

        /// <summary>
        /// 额外的调试信息
        /// </summary>
        public Dictionary<string, object>? DebugInfo { get; set; }
        /// <summary>
        /// 创建成功结果
        /// </summary>
        public static MessageProcessingResult CreateSuccess(HorizonMessagePacket packet, string protocolVersion, TimeSpan elapsed, int bytesProcessed, string? clientId = null, bool usedMemoryPool = false)
        {
            return new MessageProcessingResult
            {
                Success = true,
                Packet = packet,
                ProcessingTimeMs = elapsed.TotalMilliseconds,
                BytesProcessed = bytesProcessed,
                ProtocolType = protocolVersion,
                UsedMemoryPool = usedMemoryPool,
                DebugInfo = new Dictionary<string, object>
                {
                    ["ClientId"] = clientId ?? "Unknown",
                    ["Timestamp"] = DateTime.UtcNow
                }
            };
        }        /// <summary>
                 /// 创建失败结果
                 /// </summary>
        public static MessageProcessingResult CreateFailure(string errorMessage, TimeSpan elapsed, int bytesProcessed, string? clientId = null, Exception? exception = null)
        {
            return new MessageProcessingResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                Exception = exception,
                ProcessingTimeMs = elapsed.TotalMilliseconds,
                BytesProcessed = bytesProcessed,
                DebugInfo = new Dictionary<string, object>
                {
                    ["ClientId"] = clientId ?? "Unknown",
                    ["Timestamp"] = DateTime.UtcNow,
                    ["Error"] = errorMessage
                }
            };
        }
    }

    /// <summary>
    /// 消息优先级枚举
    /// </summary>
    public enum MessagePriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }
}
