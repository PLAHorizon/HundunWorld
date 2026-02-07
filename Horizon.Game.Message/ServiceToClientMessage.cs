using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Game.Message
{
    /// <summary>
    /// 服务器到客户端的消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ServiceToClientMessage : MessageUnion, INetworkMessage
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
        public MessageUnion Data { get; set; }
        /// <summary>
        /// 消息时间戳
        /// </summary>
        [Id(2)]
        [MemoryPackOrder(2)]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        /// <summary>
        /// 消息类型
        /// </summary>
        [Id(3)]
        [MemoryPackOrder(3)]
        public MessageType Type { get; set; }
        /// <summary>
        /// 服务类型
        /// </summary>
        [Id(4)]
        [MemoryPackOrder(4)]
        public ServiceType ServiceType { get; set; }
    }
}
