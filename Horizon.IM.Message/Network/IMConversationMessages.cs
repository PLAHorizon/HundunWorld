using Horizon.IM.Message.Enums;
using MemoryPack;
using Orleans;
using System;
using System.Collections.Generic;

namespace Horizon.IM.Message.Network
{
    #region 会话管理消息

    /// <summary>
    /// 会话列表请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMConversationListRequest : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 分页偏移量
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int Offset { get; set; }

        /// <summary>
        /// 每页数量
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Limit { get; set; } = 20;

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMMessageType Type { get; set; } = IMMessageType.ConversationListRequest;

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Chat;
    }

    /// <summary>
    /// 会话列表响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMConversationListResponse : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 会话列表
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public List<IMConversationInfo> Conversations { get; set; } = new();

        /// <summary>
        /// 总数
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int TotalCount { get; set; }

        /// <summary>
        /// 是否还有更多
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public bool HasMore { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMMessageType Type { get; set; } = IMMessageType.ConversationListResponse;

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Chat;
    }

    /// <summary>
    /// 删除会话消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMConversationDeleteMessage : IMMessageUnion, IIMNetworkMessage
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

        [MemoryPackOrder(2)]
        [Id(2)]
        public new IMMessageType Type { get; set; } = IMMessageType.ConversationDelete;

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Chat;
    }

    /// <summary>
    /// 置顶会话消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMConversationPinMessage : IMMessageUnion, IIMNetworkMessage
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
        /// 是否置顶
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public bool IsPinned { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMMessageType Type { get; set; } = IMMessageType.ConversationPin;

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Chat;
    }

    /// <summary>
    /// 会话免打扰消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMConversationMuteMessage : IMMessageUnion, IIMNetworkMessage
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
        /// 是否免打扰
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public bool IsMuted { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMMessageType Type { get; set; } = IMMessageType.ConversationMute;

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Chat;
    }

    /// <summary>
    /// 会话信息数据模型
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMConversationInfo : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 会话ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string ConversationId { get; set; } = "";

        /// <summary>
        /// 聊天关系类型
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public IMChatRelationType ChatRelationType { get; set; }

        /// <summary>
        /// 对方用户ID（私聊时）/ 群组ID（群聊时）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong PeerId { get; set; }

        /// <summary>
        /// 会话显示名称
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string DisplayName { get; set; } = "";

        /// <summary>
        /// 会话头像URL
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string Avatar { get; set; } = "";

        /// <summary>
        /// 最后一条消息内容摘要
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public string LastMessageSummary { get; set; } = "";

        /// <summary>
        /// 最后一条消息时间戳
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public long LastMessageTime { get; set; }

        /// <summary>
        /// 未读消息数
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public int UnreadCount { get; set; }

        /// <summary>
        /// 是否置顶
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public bool IsPinned { get; set; }

        /// <summary>
        /// 是否免打扰
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public bool IsMuted { get; set; }

        [MemoryPackOrder(10)]
        [Id(10)]
        public new IMMessageType Type { get; set; } = IMMessageType.ConversationListResponse;

        [MemoryPackOrder(11)]
        [Id(11)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Chat;
    }

    #endregion
}
