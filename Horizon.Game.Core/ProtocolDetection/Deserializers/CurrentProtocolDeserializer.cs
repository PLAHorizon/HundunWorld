using System;
using Horizon.Game.Message;
using Horizon.Game.Message.Network;
using MemoryPack;

namespace Horizon.Game.Core.ProtocolDetection.Deserializers
{
    /// <summary>
    /// 当前协议版本反序列化器
    /// </summary>
    public class CurrentProtocolDeserializer : IProtocolDeserializer
    {
        public string ProtocolVersion => "Current_v3.0";
        public int Priority => 1; // 最高优先级

        public bool CanHandle(ReadOnlySpan<byte> data)
        {
            if (data.Length < 5) return false;

            var messageLength = BitConverter.ToInt32(data);

            // 检查长度是否合理
            if (messageLength <= 0 || messageLength > 10 * 1024 * 1024) return false;

            // 检查是否有完整的消息（4字节长度 + 1字节标志 + 消息数据）
            if (data.Length >= 4 + messageLength)
            {
                var flags = data[4];
                // 检查标志位是否合理（只使用低3位）
                return (flags & 0xF8) == 0; // 高5位应该都是0
            }

            return false;
        }

        public HorizonMessagePacket? TryDeserialize(ReadOnlySpan<byte> data, byte[]? encryptionKey)
        {
            try
            {
                var messageLength = BitConverter.ToInt32(data);
                if (data.Length < 4 + messageLength) return null;

                var messageData = data.Slice(4, messageLength);
                var flags = messageData[0];
                var actualMessageData = messageData.Slice(1);

                bool isCompressed = (flags & 0x01) != 0;
                bool isEncrypted = (flags & 0x02) != 0;

                ReadOnlySpan<byte> processedData = actualMessageData;

                // 解密
                if (isEncrypted && encryptionKey != null)
                {
                    processedData = DecryptData(processedData, encryptionKey);
                }

                // 解压缩
                if (isCompressed)
                {
                    processedData = DecompressData(processedData);
                }

                // 反序列化
                return MemoryPackSerializer.Deserialize<HorizonMessagePacket>(processedData);
            }
            catch
            {
                return null;
            }
        }

        private static ReadOnlySpan<byte> DecryptData(ReadOnlySpan<byte> data, byte[] key)
        {
            // 这里应该实现实际的解密逻辑
            // 为了示例，直接返回原数据
            return data;
        }

        private static ReadOnlySpan<byte> DecompressData(ReadOnlySpan<byte> data)
        {
            // 这里应该实现实际的解压缩逻辑
            // 为了示例，直接返回原数据
            return data;
        }
    }
}
