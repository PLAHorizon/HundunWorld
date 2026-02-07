using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using K4os.Compression.LZ4;
using MemoryPack;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using TouchSocket.Core;
using TouchSocket.Sockets;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 混沌世界MMORPG网络消息适配器（客户端版本）
    /// 专为武侠游戏的消息传输优化
    /// </summary>
    public class HorizonMessageAdapter : FixedHeaderPackageAdapter
    {
        private readonly AdapterStatistics _statistics = new();
        private readonly object _statsLock = new();

        /// <summary>
        /// 消息头大小（字节）
        /// 4字节长度 + 1字节类型 + 1字节压缩标志 + 2字节校验和 = 8字节
        /// </summary>
        public int HeaderLength { get; } = 8;

        public override bool CanSendRequestInfo { get; } = true;

   
   

        /// <summary>
        /// 重写解析请求信息的方法
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        protected  HorizonMessageInfo ParseBody(byte[] target, int offset, int length)
        {
            try
            {
                // 创建一个新的数组来包含完整的消息数据（包括头部）
                var fullData = new byte[HeaderLength + length];
                Array.Copy(target, offset, fullData, 0, length);
                
                var packet = UnpackMessage(fullData);
                return new HorizonMessageInfo
                {
                    Packet = packet
                };
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[HorizonMessageAdapter] 解析请求时发生错误: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 生成网络消息包
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="message"></param>
        /// <returns></returns>
        public HorizonMessagePacket CreateHorizonMessage<T>(T message) where T : MessageUnion, INetworkMessage
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message), "网络消息不能为空");
            }

            // 创建默认的消息头
            var header = new MessageHeader
            {
                MessageType = ((INetworkMessage)message).Type,
                IsResponse = false, // 默认不是响应消息
                Timestamp = DateTime.UtcNow.Ticks,
                GameId = 1,    // 设置默认GameId
                ZoneId = 1,    // 设置默认ZoneId
                ServerId = 1,  // 设置默认ServerId
            };

            HorizonMessagePacket messagePacket = new HorizonMessagePacket
            {
                Header = header,
                ServiceType = ((INetworkMessage)message).ServiceType,
                Body = message,
                RawData = MemoryPackSerializer.Serialize(message),
            };

            messagePacket.Header.SequenceId = CRC32.Compute(messagePacket.RawData);
            return messagePacket;
        }
        
        /// <summary>
        /// 序列化并打包消息
        /// </summary>
        public byte[] PackMessage<T>(T message, MessageType messageType, bool compress = true) where T : MessageUnion,INetworkMessage
        {
            try
            {
                var data = CreateHorizonMessage(message);
                // 序列化消息
                var messageData = MemoryPackSerializer.Serialize(data);
                
                // 压缩消息（如果需要且大于阈值）
                byte[] finalData;
                bool isCompressed = false;

                if (compress && messageData.Length > 256) // 大于256字节才压缩
                {
                    finalData = LZ4Pickler.Pickle(messageData);
                    isCompressed = finalData.Length < messageData.Length; // 只有压缩有效果才使用
                    if (!isCompressed)
                        finalData = messageData;
                }
                else
                {
                    finalData = messageData;
                }

                // 构建完整消息包
                var packet = new byte[HeaderLength + finalData.Length];
                var span = packet.AsSpan();

                // 写入消息长度
                BitConverter.TryWriteBytes(span.Slice(0, 4), finalData.Length);

                // 写入消息类型
                span[4] = (byte)messageType;

                // 写入压缩标志
                span[5] = (byte)(isCompressed ? 1 : 0);

                // 计算并写入校验和
                var checksum = CalculateChecksum(finalData);
                BitConverter.TryWriteBytes(span.Slice(6, 2), checksum);

                // 写入消息体
                finalData.AsSpan().CopyTo(span.Slice(HeaderLength));

                return packet;
            }
            catch (Exception ex)
            {
                UpdateErrorStats();
                throw new InvalidOperationException($"消息打包失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 解包并反序列化消息
        /// </summary>
        public HorizonMessagePacket UnpackMessage(byte[] data)
        {
            try
            {
                if (data == null)
                {
                    throw new InvalidOperationException("数据为空");
                }
                
                if (data.Length < HeaderLength)
                {
                    throw new InvalidOperationException($"数据长度不足消息头长度: {data.Length} < {HeaderLength}");
                }

                var span = data.AsSpan();

                // 读取消息长度
                var messageLength = BitConverter.ToInt32(span.Slice(0, 4));
                
                // 输出调试信息
                FlaxEngine.Debug.Log($"[HorizonMessageAdapter] 消息头信息 - 长度: {messageLength}, 数据总长度: {data.Length}");

                // 验证数据长度
                if (data.Length < HeaderLength + messageLength)
                {
                    throw new InvalidOperationException($"数据长度不足: {data.Length} < {HeaderLength + messageLength}");
                }

                // 读取消息类型
                var messageType = (MessageType)span[4];

                // 读取压缩标志
                var isCompressed = span[5] != 0;

                // 读取校验和
                var expectedChecksum = BitConverter.ToUInt16(span.Slice(6, 2));
                
                // 输出调试信息
                FlaxEngine.Debug.Log($"[HorizonMessageAdapter] 消息详情 - 类型: {messageType}, 压缩: {isCompressed}, 校验和: {expectedChecksum}");

                // 读取消息体
                var messageBody = span.Slice(HeaderLength, messageLength);

                // 验证校验和
                var actualChecksum = CalculateChecksum(messageBody);
                if (actualChecksum != expectedChecksum)
                {
                    throw new InvalidOperationException($"消息校验和验证失败: 期望 {expectedChecksum}, 实际 {actualChecksum}");
                }

                // 解压缩消息（如果需要）
                byte[] finalData = isCompressed ? LZ4Pickler.Unpickle(messageBody) : messageBody.ToArray();
                
                // 输出调试信息
                FlaxEngine.Debug.Log($"[HorizonMessageAdapter] 消息体处理 - 压缩前长度: {messageBody.Length}, 压缩后长度: {finalData.Length}");

                // 反序列化消息包
                var packet = MemoryPackSerializer.Deserialize<HorizonMessagePacket>(finalData);
                if (packet == null)
                {
                    throw new InvalidOperationException("消息反序列化失败");
                }
                
                // 输出调试信息
                FlaxEngine.Debug.Log($"[HorizonMessageAdapter] 消息反序列化成功 - 类型: {packet.Header?.MessageType}, 服务: {packet.ServiceType}");

                packet.RawData = data;
                UpdateProcessStats(messageType, data.Length);
                return packet;
            }
            catch (Exception ex)
            {
                UpdateErrorStats();
                FlaxEngine.Debug.LogError($"[HorizonMessageAdapter] 消息解包失败: {ex.Message}");
                FlaxEngine.Debug.LogError($"[HorizonMessageAdapter] 堆栈跟踪: {ex.StackTrace}");
                throw new InvalidOperationException($"消息解包失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 计算校验和
        /// </summary>
        private static ushort CalculateChecksum(ReadOnlySpan<byte> data)
        {
            uint checksum = 0;
            foreach (var b in data)
            {
                checksum += b;
            }
            return (ushort)(checksum & 0xFFFF);
        }

        /// <summary>
        /// 更新处理统计信息
        /// </summary>
        private void UpdateProcessStats(MessageType messageType, int length)
        {
            lock (_statsLock)
            {
                _statistics.TotalMessagesProcessed++;
                _statistics.TotalBytesProcessed += length;

                if (!_statistics.MessageTypeStats.ContainsKey(messageType))
                    _statistics.MessageTypeStats[messageType] = 0;

                _statistics.MessageTypeStats[messageType]++;
            }
        }

        /// <summary>
        /// 更新错误统计信息
        /// </summary>
        private void UpdateErrorStats()
        {
            lock (_statsLock)
            {
                _statistics.ErrorCount++;
            }
        }
    }

    /// <summary>
    /// Horizon消息信息类
    /// </summary>
    public class HorizonMessageInfo : TouchSocket.Core.IRequestInfo
    {
        public HorizonMessagePacket Packet { get; set; }

        
    }

    /// <summary>
    /// 适配器统计信息
    /// </summary>
    public class AdapterStatistics
    {
        /// <summary>
        /// 处理的消息总数
        /// </summary>
        public long TotalMessagesProcessed { get; set; }

        /// <summary>
        /// 处理的字节总数
        /// </summary>
        public long TotalBytesProcessed { get; set; }

        /// <summary>
        /// 错误总数
        /// </summary>
        public long ErrorCount { get; set; }

        /// <summary>
        /// 各类型消息统计
        /// </summary>
        public Dictionary<MessageType, long> MessageTypeStats { get; set; } = new();

        /// <summary>
        /// 错误率
        /// </summary>
        public double ErrorRate => TotalMessagesProcessed > 0 ? (double)ErrorCount / TotalMessagesProcessed : 0;

        /// <summary>
        /// 平均消息大小
        /// </summary>
        public double AverageMessageSize => TotalMessagesProcessed > 0 ? (double)TotalBytesProcessed / TotalMessagesProcessed : 0;
    }
}
