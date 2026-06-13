using Horizon.IM.Message.Enums;
using MemoryPack;
using Orleans;
using System;
using System.Collections.Generic;

namespace Horizon.IM.Message.Network
{
    #region 私聊/熟人聊天消息

    /// <summary>
    /// 私聊消息发送（熟人聊天）
    /// 已经是好友关系的用户之间发送消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMPrivateChatSendMessage : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 发送者用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong SenderId { get; set; }

        /// <summary>
        /// 发送者昵称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string SenderName { get; set; } = "";

        /// <summary>
        /// 发送者头像URL
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string SenderAvatar { get; set; } = "";

        /// <summary>
        /// 接收者用户ID
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong ReceiverId { get; set; }

        /// <summary>
        /// 消息内容
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string Content { get; set; } = "";

        /// <summary>
        /// 消息内容类型
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public IMContentType ContentType { get; set; } = IMContentType.Text;

        /// <summary>
        /// 客户端消息ID（用于去重和回执匹配）
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public string ClientMessageId { get; set; } = "";

        /// <summary>
        /// 发送时间戳（毫秒）
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public long Timestamp { get; set; }

        /// <summary>
        /// 附件URL列表（图片、文件等的URL）
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public List<string> Attachments { get; set; } = new();

        /// <summary>
        /// 扩展数据（用于自定义消息类型）
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public Dictionary<string, string> ExtData { get; set; } = new();

        [MemoryPackOrder(10)]
        [Id(10)]
        public new IMMessageType Type { get; set; } = IMMessageType.PrivateChatSend;

        [MemoryPackOrder(11)]
        [Id(11)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Chat;
    }

    /// <summary>
    /// 私聊消息通知（服务器推送给接收方）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMPrivateChatNotifyMessage : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 服务器分配的消息ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string ServerMessageId { get; set; } = "";

        /// <summary>
        /// 发送者用户ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong SenderId { get; set; }

        /// <summary>
        /// 发送者昵称
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string SenderName { get; set; } = "";

        /// <summary>
        /// 发送者头像URL
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string SenderAvatar { get; set; } = "";

        /// <summary>
        /// 接收者用户ID
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public ulong ReceiverId { get; set; }

        /// <summary>
        /// 消息内容
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public string Content { get; set; } = "";

        /// <summary>
        /// 消息内容类型
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public IMContentType ContentType { get; set; } = IMContentType.Text;

        /// <summary>
        /// 服务器时间戳（毫秒）
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public long Timestamp { get; set; }

        /// <summary>
        /// 附件URL列表
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public List<string> Attachments { get; set; } = new();

        /// <summary>
        /// 扩展数据
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public Dictionary<string, string> ExtData { get; set; } = new();

        [MemoryPackOrder(10)]
        [Id(10)]
        public new IMMessageType Type { get; set; } = IMMessageType.PrivateChatNotify;

        [MemoryPackOrder(11)]
        [Id(11)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Chat;
    }

    /// <summary>
    /// 消息回执（送达/已读确认）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMChatAckMessage : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 被确认的消息ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string AckedMessageId { get; set; } = "";

        /// <summary>
        /// 消息状态
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public IMMessageStatus Status { get; set; }

        /// <summary>
        /// 确认时间戳
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long Timestamp { get; set; }

        /// <summary>
        /// 确认者用户ID
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong UserId { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMMessageType Type { get; set; } = IMMessageType.ChatAck;

        [MemoryPackOrder(5)]
        [Id(5)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Chat;
    }

    /// <summary>
    /// 消息撤回
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMChatRecallMessage : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 撤回的消息ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string RecalledMessageId { get; set; } = "";

        /// <summary>
        /// 撤回者用户ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 会话对方ID（私聊时为对方用户ID，群聊时为群组ID）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong PeerId { get; set; }

        /// <summary>
        /// 聊天关系类型
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public IMChatRelationType ChatRelationType { get; set; }

        /// <summary>
        /// 撤回时间戳
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long Timestamp { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public new IMMessageType Type { get; set; } = IMMessageType.ChatRecall;

        [MemoryPackOrder(6)]
        [Id(6)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Chat;
    }

    /// <summary>
    /// 已读回执
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMChatReadReceiptMessage : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 读取者用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 会话对方ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong PeerId { get; set; }

        /// <summary>
        /// 最后已读消息ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string LastReadMessageId { get; set; } = "";

        /// <summary>
        /// 已读时间戳
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long Timestamp { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMMessageType Type { get; set; } = IMMessageType.ChatReadReceipt;

        [MemoryPackOrder(5)]
        [Id(5)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Chat;
    }

    /// <summary>
    /// 正在输入指示
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMTypingIndicatorMessage : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 会话对方ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong PeerId { get; set; }

        /// <summary>
        /// 是否正在输入
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public bool IsTyping { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMMessageType Type { get; set; } = IMMessageType.TypingIndicator;

        [MemoryPackOrder(4)]
        [Id(4)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Chat;
    }

    #endregion
}
