using System;

using Horizon.IM.Message.Enums;

namespace Horizon.Game.GengDi.Core.Services.Call
{
    /// <summary>
    /// 通话状态机（纯逻辑、无 IO、可单元测试）。
    /// 覆盖状态流转：
    /// Idle → OutgoingRinging（发起）→ Connecting（对方接听）→ InCall（媒体建立）→ Idle（挂断/异常）
    /// Idle → IncomingRinging（来电）→ Connecting（本端接听）→ InCall → Idle
    /// 以及振铃阶段的 拒绝/取消/忙线/超时 直接回到 Idle。
    /// 该类不做线程同步，调用方需保证串行访问（CallService 内部加锁）。
    /// </summary>
    public sealed class CallStateMachine
    {
        /// <summary>当前状态。</summary>
        public CallState State { get; private set; } = CallState.Idle;

        /// <summary>当前通话会话ID（Idle 时为空）。</summary>
        public string CallId { get; private set; } = string.Empty;

        /// <summary>本端是否为发起方。</summary>
        public bool IsOutgoing { get; private set; }

        /// <summary>
        /// 主叫发起通话：Idle → OutgoingRinging。
        /// </summary>
        public bool TryStartOutgoing(string callId)
        {
            if (State != CallState.Idle || string.IsNullOrEmpty(callId))
            {
                return false;
            }

            State = CallState.OutgoingRinging;
            CallId = callId;
            IsOutgoing = true;
            return true;
        }

        /// <summary>
        /// 被叫收到来电：Idle → IncomingRinging。
        /// </summary>
        public bool TryReceiveOffer(string callId)
        {
            if (State != CallState.Idle || string.IsNullOrEmpty(callId))
            {
                return false;
            }

            State = CallState.IncomingRinging;
            CallId = callId;
            IsOutgoing = false;
            return true;
        }

        /// <summary>
        /// 被叫接听：IncomingRinging → Connecting。
        /// </summary>
        public bool TryAccept()
        {
            if (State != CallState.IncomingRinging)
            {
                return false;
            }

            State = CallState.Connecting;
            return true;
        }

        /// <summary>
        /// 主叫收到对方接听：OutgoingRinging → Connecting。
        /// </summary>
        public bool TryRemoteAccept()
        {
            if (State != CallState.OutgoingRinging)
            {
                return false;
            }

            State = CallState.Connecting;
            return true;
        }

        /// <summary>
        /// 媒体通道建立完成：Connecting → InCall。
        /// </summary>
        public bool TryEnterInCall()
        {
            if (State != CallState.Connecting)
            {
                return false;
            }

            State = CallState.InCall;
            return true;
        }

        /// <summary>
        /// 判断指定信令在当前状态下是否应当受理（用于过滤乱序/过期信令）。
        /// </summary>
        public bool ShouldHandleSignal(IMCallSignalType signalType)
        {
            return (State, signalType) switch
            {
                (CallState.OutgoingRinging, IMCallSignalType.Accept) => true,
                (CallState.OutgoingRinging, IMCallSignalType.Reject) => true,
                (CallState.OutgoingRinging, IMCallSignalType.Busy) => true,
                (CallState.OutgoingRinging, IMCallSignalType.Timeout) => true,
                (CallState.OutgoingRinging, IMCallSignalType.Cancel) => true,

                (CallState.IncomingRinging, IMCallSignalType.Cancel) => true,
                (CallState.IncomingRinging, IMCallSignalType.Timeout) => true,
                (CallState.IncomingRinging, IMCallSignalType.Reject) => true,

                (CallState.Connecting, IMCallSignalType.MediaReady) => true,
                (CallState.Connecting, IMCallSignalType.Hangup) => true,
                (CallState.Connecting, IMCallSignalType.MediaState) => true,

                (CallState.InCall, IMCallSignalType.Hangup) => true,
                (CallState.InCall, IMCallSignalType.MediaState) => true,
                (CallState.InCall, IMCallSignalType.KeepAlive) => true,

                // 终结性信令在结束中阶段也允许重复到达（幂等处理）
                (CallState.Ending, IMCallSignalType.Hangup) => true,
                (CallState.Ending, IMCallSignalType.Cancel) => true,
                (CallState.Ending, IMCallSignalType.Reject) => true,

                _ => false
            };
        }

        /// <summary>
        /// 进入结束中状态（任何非 Idle 状态均可进入）。
        /// </summary>
        public bool TryBeginEnding()
        {
            if (State == CallState.Idle || State == CallState.Ending)
            {
                return false;
            }

            State = CallState.Ending;
            return true;
        }

        /// <summary>
        /// 复位到空闲状态，开始新的通话会话。
        /// </summary>
        public void Reset()
        {
            State = CallState.Idle;
            CallId = string.Empty;
            IsOutgoing = false;
        }
    }
}
