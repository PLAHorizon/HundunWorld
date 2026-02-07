using System;
using Horizon.Game.Message;
using Horizon.Game.Message.Network;
using MemoryPack;

namespace Horizon.Game.Core.ProtocolDetection.Deserializers
{
    /// <summary>
    /// Legacy V2 协议反序列化器
    /// </summary>
    public class LegacyV2ProtocolDeserializer : IProtocolDeserializer
    {
        public string ProtocolVersion => "Legacy_v2.0";
        public int Priority => 2;

        public bool CanHandle(ReadOnlySpan<byte> data)
        {
            if (data.Length < 4) return false;

            var messageLength = BitConverter.ToInt32(data);

            // 检查长度是否合理
            if (messageLength <= 0 || messageLength > 10 * 1024 * 1024) return false;

            // 检查是否有完整的消息（4字节长度 + 消息数据，无标志位）
            return data.Length >= 4 + messageLength;
        }

        public HorizonMessagePacket? TryDeserialize(ReadOnlySpan<byte> data, byte[]? encryptionKey)
        {
            try
            {
                var messageLength = BitConverter.ToInt32(data);
                if (data.Length < 4 + messageLength) return null;

                var messageData = data.Slice(4, messageLength);

                // Legacy V2 协议没有标志位，直接反序列化
                return MemoryPackSerializer.Deserialize<HorizonMessagePacket>(messageData);
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Legacy V1 协议反序列化器
    /// </summary>
    public class LegacyV1ProtocolDeserializer : IProtocolDeserializer
    {
        public string ProtocolVersion => "Legacy_v1.0";
        public int Priority => 3;

        public bool CanHandle(ReadOnlySpan<byte> data)
        {
            if (data.Length < 4) return false;

            var messageLength = BitConverter.ToInt32(data);

            // Legacy V1 通常处理较小的消息
            if (messageLength <= 0 || messageLength > 1024) return false;

            return data.Length >= 4 + messageLength;
        }

        public HorizonMessagePacket? TryDeserialize(ReadOnlySpan<byte> data, byte[]? encryptionKey)
        {
            try
            {
                var messageLength = BitConverter.ToInt32(data);
                if (data.Length < 4 + messageLength) return null;

                var messageData = data.Slice(4, messageLength);

                // Legacy V1 协议的特殊处理逻辑
                return MemoryPackSerializer.Deserialize<HorizonMessagePacket>(messageData);
            }
            catch
            {
                return null;
            }
        }

       
    }
}
