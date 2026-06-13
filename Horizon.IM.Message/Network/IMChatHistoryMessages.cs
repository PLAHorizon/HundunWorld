using Horizon.IM.Message.Enums;
using MemoryPack;
using Orleans;
using System;
using System.Collections.Generic;

namespace Horizon.IM.Message.Network
{
    #region 聊天记录消息

    /// <summary>
    /// 聊天记录查询请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMChatHistoryQueryRequest : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 会话ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string ConversationId { get; set; } = "";

        /// <summary>
        /// 聊天关系类型
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public IMChatRelationType ChatRelationType { get; set; }

        /// <summary>
        /// 对方用户ID（私聊/陌生人聊天）或群组ID（群聊）
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong PeerId { get; set; }

        /// <summary>
        /// 起始时间戳（毫秒）
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long StartTime { get; set; }

        /// <summary>
        /// 结束时间戳（毫秒）
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long EndTime { get; set; }

        /// <summary>
        /// 查询数量
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public int Count { get; set; } = 20;

        /// <summary>
        /// 锚点消息ID（用于翻页加载更多历史消息）
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public string AnchorMessageId { get; set; } = "";

        [MemoryPackOrder(8)]
        [Id(8)]
        public new IMMessageType Type { get; set; } = IMMessageType.ChatHistoryQuery;

        [MemoryPackOrder(9)]
        [Id(9)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Chat;
    }

    /// <summary>
    /// 聊天记录查询响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMChatHistoryQueryResponse : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 会话ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string ConversationId { get; set; } = "";

        /// <summary>
        /// 私聊消息列表
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public List<IMPrivateChatNotifyMessage> PrivateMessages { get; set; } = new();

        /// <summary>
        /// 群聊消息列表
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public List<IMGroupChatNotifyMessage> GroupMessages { get; set; } = new();

        /// <summary>
        /// 陌生人聊天消息列表
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public List<IMStrangerChatNotifyMessage> StrangerMessages { get; set; } = new();

        /// <summary>
        /// 聊天关系类型（标识返回的是哪种消息列表）
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public IMChatRelationType ChatRelationType { get; set; }

        /// <summary>
        /// 是否还有更早的消息
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public bool HasMore { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public new IMMessageType Type { get; set; } = IMMessageType.ChatHistoryQueryResponse;

        [MemoryPackOrder(7)]
        [Id(7)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Chat;
    }

    /// <summary>
    /// 清空聊天记录
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMChatHistoryClearMessage : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 会话ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string ConversationId { get; set; } = "";

        /// <summary>
        /// 聊天关系类型
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public IMChatRelationType ChatRelationType { get; set; }

        /// <summary>
        /// 对方用户ID或群组ID
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong PeerId { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMMessageType Type { get; set; } = IMMessageType.ChatHistoryClear;

        [MemoryPackOrder(5)]
        [Id(5)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Chat;
    }

    #endregion
}
