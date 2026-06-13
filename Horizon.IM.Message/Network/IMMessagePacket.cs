using Horizon.IM.Message.Enums;
using MemoryPack;
using Orleans;
using System;

namespace Horizon.IM.Message.Network
{
    /// <summary>
    /// IM消息包装类
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class IMMessagePacket
    {
        /// <summary>
        /// IM服务类型
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public IMServiceType ServiceType { get; set; }

        /// <summary>
        /// 消息头
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public IMMessageHeader Header { get; set; }

        /// <summary>
        /// 消息体
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public IMMessageUnion? Body { get; set; }

        /// <summary>
        /// 原始数据
        /// </summary>
        [MemoryPackIgnore]
        [Id(3)]
        public byte[]? RawData { get; set; }

        /// <summary>
        /// 数据长度
        /// </summary>
        [MemoryPackIgnore]
        [Id(4)]
        public int Length { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        [MemoryPackConstructor]
        public IMMessagePacket()
        {
            Header = new IMMessageHeader();
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="body">消息体</param>
        public IMMessagePacket(IMMessageUnion body)
        {
            Header = new IMMessageHeader();
            Body = body;
        }
    }
}
