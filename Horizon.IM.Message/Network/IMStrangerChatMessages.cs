using Horizon.IM.Message.Enums;
using MemoryPack;
using Orleans;
using System;
using System.Collections.Generic;

namespace Horizon.IM.Message.Network
{
    #region 陌生人聊天消息

    /// <summary>
    /// 陌生人聊天请求
    /// 发起陌生人聊天前需要验证：
    /// 1. 发送方必须已实名认证（IdentityVerificationStatus != Unverified）
    /// 2. 发送方不能是失信人员、诈骗嫌疑人或犯罪分子（UserRiskLevel == Normal）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMStrangerChatRequest : IMMessageUnion, IIMNetworkMessage
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
        /// 目标用户ID（陌生人）
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong TargetUserId { get; set; }

        /// <summary>
        /// 发送者实名认证状态
        /// 未实名认证的用户不能向陌生人发起聊天请求
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public IdentityVerificationStatus SenderVerificationStatus { get; set; }

        /// <summary>
        /// 发送者风险等级
        /// 被研判为失信人员、诈骗嫌疑人或犯罪分子的不允许发起陌生人聊天请求
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public UserRiskLevel SenderRiskLevel { get; set; }

        /// <summary>
        /// 打招呼消息（首次陌生人聊天的问候语）
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public string GreetingMessage { get; set; } = "";

        /// <summary>
        /// 请求时间戳
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public long Timestamp { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public new IMMessageType Type { get; set; } = IMMessageType.StrangerChatRequest;

        [MemoryPackOrder(9)]
        [Id(9)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Chat;
    }

    /// <summary>
    /// 陌生人聊天响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMStrangerChatResponse : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 是否允许聊天
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool IsAllowed { get; set; }

        /// <summary>
        /// 拒绝原因
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public StrangerChatDeniedReason DeniedReason { get; set; } = StrangerChatDeniedReason.None;

        /// <summary>
        /// 拒绝描述信息
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string DeniedMessage { get; set; } = "";

        /// <summary>
        /// 发送者用户ID
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong SenderId { get; set; }

        /// <summary>
        /// 目标用户ID
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public ulong TargetUserId { get; set; }

        /// <summary>
        /// 会话ID（允许聊天时由服务器分配）
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public string SessionId { get; set; } = "";

        /// <summary>
        /// 响应时间戳
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public long Timestamp { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public new IMMessageType Type { get; set; } = IMMessageType.StrangerChatResponse;

        [MemoryPackOrder(8)]
        [Id(8)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Chat;
    }

    /// <summary>
    /// 陌生人聊天消息发送
    /// 只有通过验证并获得会话ID的用户才能发送陌生人消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMStrangerChatSendMessage : IMMessageUnion, IIMNetworkMessage
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
        /// 接收者用户ID（陌生人）
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong ReceiverId { get; set; }

        /// <summary>
        /// 陌生人会话ID（由StrangerChatResponse分配）
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string SessionId { get; set; } = "";

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
        /// 客户端消息ID
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public string ClientMessageId { get; set; } = "";

        /// <summary>
        /// 发送时间戳
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public long Timestamp { get; set; }

        /// <summary>
        /// 附件URL列表
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public List<string> Attachments { get; set; } = new();

        [MemoryPackOrder(10)]
        [Id(10)]
        public new IMMessageType Type { get; set; } = IMMessageType.StrangerChatSend;

        [MemoryPackOrder(11)]
        [Id(11)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Chat;
    }

    /// <summary>
    /// 陌生人聊天消息通知（服务器推送给接收方）
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMStrangerChatNotifyMessage : IMMessageUnion, IIMNetworkMessage
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
        /// 陌生人会话ID
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public string SessionId { get; set; } = "";

        /// <summary>
        /// 消息内容
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public string Content { get; set; } = "";

        /// <summary>
        /// 消息内容类型
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public IMContentType ContentType { get; set; } = IMContentType.Text;

        /// <summary>
        /// 服务器时间戳
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public long Timestamp { get; set; }

        /// <summary>
        /// 附件URL列表
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public List<string> Attachments { get; set; } = new();

        /// <summary>
        /// 标记这是陌生人消息（用于客户端UI展示区分）
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public bool IsStrangerMessage { get; set; } = true;

        [MemoryPackOrder(11)]
        [Id(11)]
        public new IMMessageType Type { get; set; } = IMMessageType.StrangerChatNotify;

        [MemoryPackOrder(12)]
        [Id(12)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Chat;
    }

    #endregion
}
