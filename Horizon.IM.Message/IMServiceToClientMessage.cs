using Horizon.IM.Message.Enums;
using Horizon.IM.Message.Network;
using MemoryPack;
using Orleans;
using System;

namespace Horizon.IM.Message
{
    /// <summary>
    /// IM服务器到客户端的消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMServiceToClientMessage : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 消息ID，客户端请求时的消息ID，用于匹配请求和响应
        /// </summary>
        [Id(0)]
        [MemoryPackOrder(0)]
        public ulong S2CMessageId { get; set; }

        /// <summary>
        /// 发送给客户端的数据
        /// </summary>
        [Id(1)]
        [MemoryPackOrder(1)]
        public IMMessageUnion? Data { get; set; }

        /// <summary>
        /// 消息时间戳
        /// </summary>
        [Id(2)]
        [MemoryPackOrder(2)]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// IM消息类型
        /// </summary>
        [Id(3)]
        [MemoryPackOrder(3)]
        public new IMMessageType Type { get; set; }

        /// <summary>
        /// IM服务类型
        /// </summary>
        [Id(4)]
        [MemoryPackOrder(4)]
        public new IMServiceType ServiceType { get; set; }
    }
}
