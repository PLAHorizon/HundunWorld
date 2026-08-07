using System;

using Horizon.IM.Message.Enums;

namespace Horizon.Game.GengDi.Core.Services.Call
{
    /// <summary>
    /// 客户端本地通话状态（覆盖发起、接听、拒绝、取消、忙线、超时、挂断、异常断开全流程）。
    /// </summary>
    public enum CallState
    {
        /// <summary>空闲（无通话）</summary>
        Idle,

        /// <summary>主叫振铃中（等待对方接听/拒绝，超时将自动取消）</summary>
        OutgoingRinging,

        /// <summary>被叫振铃中（等待本端接听/拒绝，超时将自动拒绝）</summary>
        IncomingRinging,

        /// <summary>连接中（双方已同意通话，正在交换媒体端点并建立媒体流）</summary>
        Connecting,

        /// <summary>通话中（媒体流已建立）</summary>
        InCall,

        /// <summary>结束中（已发送终结信令，等待资源释放）</summary>
        Ending
    }

    /// <summary>
    /// 通话会话快照（供 UI 绑定与事件传递使用）。
    /// </summary>
    public sealed class CallSessionSnapshot
    {
        public CallState State { get; init; } = CallState.Idle;

        /// <summary>通话会话ID</summary>
        public string CallId { get; init; } = string.Empty;

        /// <summary>对端用户ID（PassportId 字符串）</summary>
        public string PeerId { get; init; } = string.Empty;

        /// <summary>对端显示名称（用于来电/通话界面展示）</summary>
        public string PeerDisplayName { get; init; } = string.Empty;

        /// <summary>对端头像</summary>
        public string PeerAvatar { get; init; } = string.Empty;

        /// <summary>通话类型</summary>
        public IMCallType CallType { get; init; }

        /// <summary>本端是否为发起方</summary>
        public bool IsOutgoing { get; init; }

        /// <summary>通话已接通时长（未接通为 TimeSpan.Zero）</summary>
        public TimeSpan Elapsed { get; init; }

        /// <summary>本端是否静音</summary>
        public bool IsMuted { get; init; }

        /// <summary>本端摄像头是否关闭（仅视频通话）</summary>
        public bool IsCameraOff { get; init; }

        /// <summary>对端是否静音（由 MediaState 信令同步）</summary>
        public bool IsRemoteMuted { get; init; }

        /// <summary>对端摄像头是否关闭（由 MediaState 信令同步）</summary>
        public bool IsRemoteCameraOff { get; init; }

        /// <summary>状态提示语（如"正在呼叫…"、"对方忙线"）</summary>
        public string StatusText { get; init; } = string.Empty;

        /// <summary>结束原因（仅 State=Ending/Idle 时有意义）</summary>
        public IMCallEndReason EndReason { get; init; }
    }

    /// <summary>
    /// 通话状态变更事件参数。
    /// </summary>
    public sealed class CallStateChangedEventArgs : EventArgs
    {
        public CallStateChangedEventArgs(CallSessionSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public CallSessionSnapshot Snapshot { get; }
    }

    /// <summary>
    /// 通话提示事件参数（设备异常、网络异常等需要 Toast 展示的信息）。
    /// </summary>
    public sealed class CallNoticeEventArgs : EventArgs
    {
        public CallNoticeEventArgs(string message, bool isError)
        {
            Message = message;
            IsError = isError;
        }

        public string Message { get; }

        public bool IsError { get; }
    }

    /// <summary>
    /// 视频帧事件参数（JPEG 编码后的帧数据）。
    /// </summary>
    public sealed class CallVideoFrameEventArgs : EventArgs
    {
        public CallVideoFrameEventArgs(byte[] jpegData)
        {
            JpegData = jpegData;
        }

        public byte[] JpegData { get; }
    }
}
