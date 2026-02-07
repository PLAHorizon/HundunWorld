using System;

namespace Horizon.Game.Message.Enums
{
    /// <summary>
    /// 消息标志位枚举
    /// </summary>
    [Flags]
    public enum MessageFlags : uint
    {
        /// <summary>
        /// 无特殊标志
        /// </summary>
        None = 0,

        /// <summary>
        /// 消息需要加密
        /// </summary>
        Encrypted = 1 << 0,

        /// <summary>
        /// 消息需要压缩
        /// </summary>
        Compressed = 1 << 1,

        /// <summary>
        /// 高优先级消息
        /// </summary>
        HighPriority = 1 << 2,

        /// <summary>
        /// 系统消息
        /// </summary>
        SystemMessage = 1 << 3,

        /// <summary>
        /// 广播消息
        /// </summary>
        Broadcast = 1 << 4,

        /// <summary>
        /// 需要确认的消息
        /// </summary>
        RequiresAck = 1 << 5,

        /// <summary>
        /// 重试消息
        /// </summary>
        Retry = 1 << 6,

        /// <summary>
        /// 紧急消息
        /// </summary>
        Urgent = 1 << 7
    }
}