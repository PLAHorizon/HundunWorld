using Horizon.IM.Message.Enums;
using MemoryPack;
using Orleans;
using System;

namespace Horizon.IM.Message.Network
{
    #region IM系统消息

    /// <summary>
    /// IM心跳消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMHeartbeatMessage : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 客户端时间戳
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long ClientTimestamp { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public new IMMessageType Type { get; set; } = IMMessageType.Heartbeat;

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Gateway;
    }

    /// <summary>
    /// IM心跳响应消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMHeartbeatResponse : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 服务器时间戳
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public long ServerTimestamp { get; set; }

        /// <summary>
        /// 待处理的离线消息数量
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int PendingMessageCount { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public new IMMessageType Type { get; set; } = IMMessageType.HeartbeatResponse;

        [MemoryPackOrder(3)]
        [Id(3)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Gateway;
    }

    /// <summary>
    /// IM错误消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMErrorMessage : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 错误码
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public IMErrorCode ErrorCode { get; set; }

        /// <summary>
        /// 错误消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 关联的原始消息ID（如果有）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string RelatedMessageId { get; set; } = "";

        /// <summary>
        /// 错误详情
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Details { get; set; } = "";

        /// <summary>
        /// 时间戳
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long Timestamp { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public new IMMessageType Type { get; set; } = IMMessageType.Error;

        [MemoryPackOrder(6)]
        [Id(6)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Gateway;
    }

    /// <summary>
    /// IM系统通知消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMSystemNotificationMessage : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 通知标题
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string Title { get; set; } = "";

        /// <summary>
        /// 通知内容
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Content { get; set; } = "";

        /// <summary>
        /// 目标用户ID（0表示全体用户）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong TargetUserId { get; set; }

        /// <summary>
        /// 通知时间戳
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long Timestamp { get; set; }

        /// <summary>
        /// 通知优先级
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public byte Priority { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public new IMMessageType Type { get; set; } = IMMessageType.SystemNotification;

        [MemoryPackOrder(6)]
        [Id(6)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Notification;
    }

    #endregion
}
