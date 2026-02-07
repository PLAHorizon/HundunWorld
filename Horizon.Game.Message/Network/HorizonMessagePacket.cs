using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;
using System;
using System.Buffers;
using System.Linq;
using TouchSocket.Core;

namespace Horizon.Game.Message.Network
{
    /// <summary>
    /// 游戏消息包装类
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class HorizonMessagePacket 
    {
        /// <summary>
        /// 服务类型
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ServiceType ServiceType { get; set; }
       
        /// <summary>
        /// 消息头
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public MessageHeader Header { get; set; }

        /// <summary>
        /// 消息体
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageUnion Body { get; set; }

        /// <summary>
        /// 原始数据
        /// </summary>
        [MemoryPackIgnore]
        [Id(3)] public byte[]? RawData { get; set; }

        /// <summary>
        /// 数据长度
        /// </summary>
        [MemoryPackIgnore]
        [Id(4)] public int Length { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        [MemoryPackConstructor]
        public HorizonMessagePacket()
        {
            Header = new MessageHeader();
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="body">消息体</param>
        public HorizonMessagePacket(MessageUnion body)
        {
            Header = new MessageHeader();
            Body = body;
        }
                
    }

    /// <summary>
    /// 包括消息体、长度和关联的数据包。
    /// </summary>
    /// <remarks>此类提供构造、序列化和反序列化地平线消息的功能。
    /// 它支持从缓冲区构建请求信息、将消息转换为字节数组、解析消息头和消息体等操作。
    /// 此类的实例封装了地平线消息的原始数据和元数据。</remarks>
    [MemoryPackable(SerializeLayout.Explicit)]
    public partial class HorizonMessageInfo :   IRequestInfo
    {
        /// <summary>
        /// 消息体长度
        /// </summary>
        [MemoryPackOrder(0)]
        public int BodyLength { get; set; }

        /// <summary>
        /// 消息数据
        /// </summary>
        [MemoryPackOrder(1)]
        public byte[]? Body { get; set; }

        /// <summary>
        /// 消息包对象
        /// </summary>
        [MemoryPackIgnore]
        public HorizonMessagePacket? Packet { get; set; }

        /// <summary>
        /// 最大长度
        /// </summary>
        [MemoryPackIgnore]
        public int MaxLength => 1024 * 1024;

        /// <summary>
        /// 构造函数
        /// </summary>
        [MemoryPackConstructor]
        public HorizonMessageInfo()
        {
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="packet">消息包</param>
       
        public HorizonMessageInfo(HorizonMessagePacket packet)
        {
            Packet = packet;
            if (packet?.RawData != null)
            {
                Body = packet.RawData;
                BodyLength = packet.RawData.Length;
            }
        }

        /// <summary>
        /// 尝试构建请求信息
        /// </summary>
        /// <param name="buffer">数据缓冲区</param>
        /// <param name="length">数据长度</param>
        /// <param name="requestInfo">输出的请求信息</param>
        /// <returns>是否成功构建</returns>
        public bool TryBuild(ReadOnlySequence<byte> buffer, int length, out IRequestInfo requestInfo)
        {
            requestInfo = null;
            
            try
            {
                if (buffer.Length < 4) // 至少需要4字节来读取长度
                {
                    return false;
                }

                var reader = new SequenceReader<byte>(buffer);
                
                // 读取消息长度
                if (!reader.TryReadLittleEndian(out int messageLength))
                {
                    return false;
                }

                // 检查是否有足够的数据
                if (buffer.Length < messageLength + 4)
                {
                    return false;
                }

                // 读取消息体数据
                var bodyData = new byte[messageLength];
                if (!reader.TryCopyTo(bodyData))
                {
                    return false;
                }

                // 反序列化消息包
                var packet = MemoryPackSerializer.Deserialize<HorizonMessagePacket>(bodyData);
                if (packet == null)
                {
                    return false;
                }

                packet.RawData = bodyData;

                // 创建请求信息
                requestInfo = new HorizonMessageInfo(packet)
                {
                    BodyLength = messageLength,
                    Body = bodyData
                };

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 尝试构建请求信息
        /// </summary>
        /// <param name="buffer">数据缓冲区</param>
        /// <param name="requestInfo">输出的请求信息</param>
        /// <returns>是否成功构建</returns>
        public bool TryBuild(ReadOnlySequence<byte> buffer, out IRequestInfo requestInfo)
        {
            return TryBuild(buffer, (int)buffer.Length, out requestInfo);
        }

        /// <summary>
        /// 将消息包序列化为字节数组
        /// </summary>
        /// <returns>序列化后的字节数组</returns>
        public byte[] ToBytes()
        {
            if (Packet == null)
            {
                return Array.Empty<byte>();
            }

            try
            {
                var bodyData = MemoryPackSerializer.Serialize(Packet);
                var lengthBytes = BitConverter.GetBytes(bodyData.Length);
                
                var result = new byte[lengthBytes.Length + bodyData.Length];
                Array.Copy(lengthBytes, 0, result, 0, lengthBytes.Length);
                Array.Copy(bodyData, 0, result, lengthBytes.Length, bodyData.Length);
                
                return result;
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        /// <summary>
        /// 从字节数组创建消息信息
        /// </summary>
        /// <param name="data">字节数组</param>
        /// <returns>消息信息对象</returns>
        public static HorizonMessageInfo? FromBytes(byte[] data)
        {
            if (data == null || data.Length < 4)
            {
                return null;
            }

            try
            {
                var messageLength = BitConverter.ToInt32(data, 0);
                if (data.Length < messageLength + 4)
                {
                    return null;
                }

                var bodyData = new byte[messageLength];
                Array.Copy(data, 4, bodyData, 0, messageLength);

                var packet = MemoryPackSerializer.Deserialize<HorizonMessagePacket>(bodyData);
                if (packet == null)
                {
                    return null;
                }

                packet.RawData = bodyData;

                return new HorizonMessageInfo(packet)
                {
                    BodyLength = messageLength,
                    Body = bodyData
                };
            }
            catch
            {
                return null;
            }
        }

        public bool OnParsingHeader(ReadOnlySpan<byte> header)
        {
            if (header.Length < 4)
            {
                return false;
            }

            try
            {
                // 读取消息体长度
                BodyLength = BitConverter.ToInt32(header);
                return BodyLength > 0 && BodyLength <= MaxLength;
            }
            catch
            {
                return false;
            }
        }

        public bool OnParsingBody(ReadOnlySpan<byte> body)
        {
            if (body.Length != BodyLength)
            {
                return false;
            }

            try
            {
                Body = body.ToArray();

                // 反序列化消息包
                Packet = MemoryPackSerializer.Deserialize<HorizonMessagePacket>(body);
                if (Packet == null)
                {
                    return false;
                }

                Packet.RawData = Body;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Build<TByteBlock>(ref TByteBlock byteBlock) where TByteBlock : IByteBlock
        {
            if (Packet == null)
            {
                return;
            }

            try
            {
                var bodyData = MemoryPackSerializer.Serialize(Packet);
                var lengthBytes = BitConverter.GetBytes(bodyData.Length);

                byteBlock.Write(lengthBytes);
                byteBlock.Write(bodyData);
            }
            catch
            {
                // 构建失败时不写入任何数据
            }
        }
    }
}