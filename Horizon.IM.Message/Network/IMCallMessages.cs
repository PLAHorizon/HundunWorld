using Horizon.IM.Message.Enums;
using MemoryPack;
using Orleans;
using System;

namespace Horizon.IM.Message.Network
{
    #region 通话信令消息

    /// <summary>
    /// 通话信令消息（语音/视频通话的状态流转事件）。
    /// 信令走现有 IM 网关长连接，服务端只负责转发与忙线判定，不落库。
    /// 媒体流（音频/视频）由客户端之间通过 UDP 直连传输，不经过服务端。
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMCallSignalMessage : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 通话会话ID（由主叫生成，整通通话内所有信令共享该ID）
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string CallId { get; set; } = "";

        /// <summary>
        /// 信令发送者用户ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong SenderId { get; set; }

        /// <summary>
        /// 信令接收者用户ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong ReceiverId { get; set; }

        /// <summary>
        /// 信令类型（发起/接听/拒绝/取消/忙线/挂断/媒体就绪/保活/媒体状态/超时）
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public IMCallSignalType SignalType { get; set; }

        /// <summary>
        /// 通话类型（语音/视频）
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public IMCallType CallType { get; set; }

        /// <summary>
        /// 发送者昵称（用于来电展示）
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public string SenderName { get; set; } = "";

        /// <summary>
        /// 发送者头像URL（用于来电展示）
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public string SenderAvatar { get; set; } = "";

        /// <summary>
        /// 媒体UDP端点（仅 MediaReady 信令携带，格式：IP:Port）
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public string MediaEndpoint { get; set; } = "";

        /// <summary>
        /// 是否处于静音状态（MediaState 信令携带）
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public bool IsMuted { get; set; }

        /// <summary>
        /// 摄像头是否关闭（MediaState 信令携带，仅视频通话有效）
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public bool IsCameraOff { get; set; }

        /// <summary>
        /// 结束原因（Reject/Cancel/Hangup/Timeout 等终结性信令携带）
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public IMCallEndReason EndReason { get; set; }

        /// <summary>
        /// 附加说明（如忙线提示语、设备异常描述）
        /// </summary>
        [MemoryPackOrder(11)]
        [Id(11)]
        public string Remark { get; set; } = "";

        /// <summary>
        /// 发送时间戳（毫秒）
        /// </summary>
        [MemoryPackOrder(12)]
        [Id(12)]
        public long Timestamp { get; set; }

        [MemoryPackOrder(13)]
        [Id(13)]
        public new IMMessageType Type { get; set; } = IMMessageType.CallSignal;

        [MemoryPackOrder(14)]
        [Id(14)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Call;
    }

    /// <summary>
    /// 通话信令应答消息（服务端对信令发送者的确认）。
    /// Accepted=false 时通过 EndReason/Remark 说明失败原因（如对方忙线）。
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class IMCallSignalAckMessage : IMMessageUnion, IIMNetworkMessage
    {
        /// <summary>
        /// 通话会话ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string CallId { get; set; } = "";

        /// <summary>
        /// 被确认的信令类型
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public IMCallSignalType SignalType { get; set; }

        /// <summary>
        /// 服务端是否成功受理并转发该信令
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public bool Accepted { get; set; }

        /// <summary>
        /// 失败原因（Accepted=false 时有效）
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public IMCallEndReason EndReason { get; set; }

        /// <summary>
        /// 附加说明
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 服务端时间戳（毫秒）
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long Timestamp { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public new IMMessageType Type { get; set; } = IMMessageType.CallSignalAck;

        [MemoryPackOrder(7)]
        [Id(7)]
        public new IMServiceType ServiceType { get; set; } = IMServiceType.Call;
    }

    #endregion
}
