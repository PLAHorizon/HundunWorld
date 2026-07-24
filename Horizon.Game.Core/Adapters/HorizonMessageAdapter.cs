using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using K4os.Compression.LZ4;
using MemoryPack;
using System;
using System.Collections.Generic;
using TouchSocket.Core;

namespace Horizon.Game.Core
{
    /// <summary>
    /// 混沌世界 MMORPG 网络消息适配器（服务端）。
    /// 实现 CustomFixedHeaderDataHandlingAdapter&lt;HorizonMessageInfo&gt;，
    /// 正确处理 8 字节固定协议头（4字节载荷长度 + 1字节消息类型 + 1字节压缩标志 + 2字节校验和）。
    /// </summary>
    public class HorizonMessageAdapter : CustomFixedHeaderDataHandlingAdapter<HorizonMessageInfo>
    {
        private readonly AdapterStatistics _statistics = new();
        private readonly object _statsLock = new();

        /// <summary>
        /// 协议固定头长度：8 字节。
        /// </summary>
        public override int HeaderLength => 8;

        /// <summary>
        /// 获取统计信息快照。
        /// </summary>
        public AdapterStatistics Statistics => _statistics;

        /// <summary>
        /// 创建新的 HorizonMessageInfo 实例供适配器框架使用。
        /// </summary>
        protected override HorizonMessageInfo GetInstance() => new HorizonMessageInfo();

        /// <summary>
        /// 生成网络消息包。
        /// </summary>
        public HorizonMessagePacket CreateHorizonMessage<T>(T message) where T : MessageUnion, INetworkMessage
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message), "网络消息不能为空");
            }

            var header = new MessageHeader
            {
                MessageType = ((INetworkMessage)message).Type,
                ServiceType = ((INetworkMessage)message).ServiceType,
                IsResponse = false,
                Timestamp = DateTime.UtcNow.Ticks,
                GameId = 1,
                ZoneId = 1,
                ServerId = 1,
                CharacterId = ExtractCharacterId(message),
            };

            var messagePacket = new HorizonMessagePacket
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
        /// 序列化并打包消息为线路帧。
        /// </summary>
        public byte[] PackMessage<T>(T message, MessageType messageType, bool compress = true) where T : MessageUnion, INetworkMessage
        {
            return PackMessage(message, messageType, responseToMessageId: null, compress: compress);
        }

        /// <summary>
        /// 序列化并打包消息为线路帧（支持请求-响应关联）。
        /// </summary>
        public byte[] PackMessage<T>(T message, MessageType messageType, string responseToMessageId, bool compress = true) where T : MessageUnion, INetworkMessage
        {
            try
            {
                var data = CreateHorizonMessage(message);
                if (!string.IsNullOrEmpty(responseToMessageId))
                {
                    data.Header.IsResponse = true;
                    data.Header.ResponseToMessageId = responseToMessageId;
                }

                return PackPacket(data, compress);
            }
            catch (Exception ex)
            {
                UpdateErrorStats();
                throw new InvalidOperationException($"消息打包失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 序列化并打包完整消息包为线路帧，保留调用方已经设置好的头部字段。
        /// </summary>
        public byte[] PackPacket(HorizonMessagePacket packet, bool compress = true)
        {
            try
            {
                if (packet == null)
                {
                    throw new ArgumentNullException(nameof(packet), "消息包不能为空");
                }

                if (packet.Header == null)
                {
                    throw new ArgumentException("消息包头不能为空", nameof(packet));
                }

                if (packet.Body != null && (packet.RawData == null || packet.RawData.Length == 0))
                {
                    packet.RawData = MemoryPackSerializer.Serialize(packet.Body);
                }

                if (packet.RawData != null && packet.RawData.Length > 0)
                {
                    packet.Header.SequenceId = CRC32.Compute(packet.RawData);
                }

                var messageData = MemoryPackSerializer.Serialize(packet);
                return WrapPacket(messageData, packet.Header.MessageType, compress);
            }
            catch (Exception ex)
            {
                UpdateErrorStats();
                throw new InvalidOperationException($"消息打包失败: {ex.Message}", ex);
            }
        }

        private byte[] WrapPacket(byte[] messageData, MessageType messageType, bool compress)
        {
            byte[] finalData;
            bool isCompressed = false;

            if (compress && messageData.Length > 256)
            {
                finalData = LZ4Pickler.Pickle(messageData);
                isCompressed = finalData.Length < messageData.Length;
                if (!isCompressed)
                {
                    finalData = messageData;
                }
            }
            else
            {
                finalData = messageData;
            }

            var packet = new byte[HeaderLength + finalData.Length];
            var span = packet.AsSpan();

            BitConverter.TryWriteBytes(span.Slice(0, 4), finalData.Length);
            span[4] = (byte)messageType;
            span[5] = (byte)(isCompressed ? 1 : 0);
            var checksum = CalculateChecksum(finalData);
            BitConverter.TryWriteBytes(span.Slice(6, 2), checksum);
            finalData.AsSpan().CopyTo(span.Slice(HeaderLength));

            return packet;
        }

        /// <summary>
        /// 解包并反序列化消息（用于旧有回退路径，正常流量通过适配器回调处理）。
        /// </summary>
        public HorizonMessagePacket UnpackMessage(byte[] data)
        {
            try
            {
                if (data.Length < HeaderLength)
                    throw new InvalidOperationException("数据长度不足消息头长度");

                var span = data.AsSpan();
                var messageLength = BitConverter.ToInt32(span.Slice(0, 4));

                if (data.Length < HeaderLength + messageLength)
                    throw new InvalidOperationException("数据长度不足");

                var isCompressed = span[5] != 0;
                var expectedChecksum = BitConverter.ToUInt16(span.Slice(6, 2));
                var messageBody = span.Slice(HeaderLength, messageLength);

                var actualChecksum = CalculateChecksum(messageBody);
                if (actualChecksum != expectedChecksum)
                    throw new InvalidOperationException("消息校验和验证失败");

                byte[] finalData = isCompressed ? LZ4Pickler.Unpickle(messageBody) : messageBody.ToArray();

                var packet = MemoryPackSerializer.Deserialize<HorizonMessagePacket>(finalData);
                if (packet == null)
                    throw new InvalidOperationException("消息反序列化失败");

                packet.RawData = data;
                return packet;
            }
            catch (Exception ex)
            {
                UpdateErrorStats();
                throw new InvalidOperationException($"消息解包失败: {ex.Message}", ex);
            }
        }

        private static ushort CalculateChecksum(ReadOnlySpan<byte> data) =>
            HorizonProtocol.CalculateChecksum(data);

        /// <summary>
        /// 从消息体提取角色ID并填充到消息头，供服务端按角色路由和订阅管理使用。
        /// Phase 5 优化：优先检查 ICharacterIdCarrier 接口（避免反射），回退到反射查找
        /// CharacterId（ulong/long）和 LocalCharacterId（ulong）两种常见字段名。
        /// </summary>
        private static ulong ExtractCharacterId<T>(T message) where T : MessageUnion, INetworkMessage
        {
            // Phase 5: 优先检查接口（零反射，高频路径性能优化）
            if (message is ICharacterIdCarrier carrier)
            {
                return carrier.CarrierCharacterId;
            }

            // 回退到反射（兼容未实现 ICharacterIdCarrier 的旧消息类型）
            try
            {
                var type = message.GetType();

                // 优先查找 CharacterId 属性（最常见的字段名）
                var characterIdProp = type.GetProperty("CharacterId");
                if (characterIdProp != null && characterIdProp.CanRead)
                {
                    var value = characterIdProp.GetValue(message);
                    if (value is ulong ul)
                        return ul;
                    if (value is long l && l >= 0)
                        return (ulong)l;
                }

                // 其次查找 LocalCharacterId（如 HandshakePacket / ReconnectResumePacket）
                var localCharacterIdProp = type.GetProperty("LocalCharacterId");
                if (localCharacterIdProp != null && localCharacterIdProp.CanRead)
                {
                    var value = localCharacterIdProp.GetValue(message);
                    if (value is ulong ul)
                        return ul;
                    if (value is long l && l >= 0)
                        return (ulong)l;
                }
            }
            catch
            {
                // 提取失败不应阻塞消息发送，静默忽略
            }

            return 0;
        }

        private void UpdateErrorStats()
        {
            lock (_statsLock)
            {
                _statistics.ErrorCount++;
            }
        }
    }

    /// <summary>
    /// 适配器统计信息
    /// </summary>
    public class AdapterStatistics
    {
        /// <summary>处理的消息总数</summary>
        public long TotalMessagesProcessed { get; set; }

        /// <summary>处理的字节总数</summary>
        public long TotalBytesProcessed { get; set; }

        /// <summary>错误总数</summary>
        public long ErrorCount { get; set; }

        /// <summary>各类型消息统计</summary>
        public Dictionary<MessageType, long> MessageTypeStats { get; set; } = new();

        /// <summary>错误率</summary>
        public double ErrorRate => TotalMessagesProcessed > 0 ? (double)ErrorCount / TotalMessagesProcessed : 0;

        /// <summary>平均消息大小（字节）</summary>
        public double AverageMessageSize => TotalMessagesProcessed > 0 ? (double)TotalBytesProcessed / TotalMessagesProcessed : 0;
    }
}
