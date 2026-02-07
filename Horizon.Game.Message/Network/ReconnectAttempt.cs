using Horizon.Game.Message.Enums;
using System;

namespace Horizon.Game.Message.Network
{
    /// <summary>
    /// 重连尝试信息
    /// </summary>
    public class ReconnectAttempt
    {
        /// <summary>
        /// 尝试次数
        /// </summary>
        public int AttemptNumber { get; set; }
        
        /// <summary>
        /// 最大尝试次数
        /// </summary>
        public int MaxAttempts { get; set; }
        
        /// <summary>
        /// 当前使用的重连策略
        /// </summary>
        public ReconnectStrategy Strategy { get; set; }
        
        /// <summary>
        /// 重连原因
        /// </summary>
        public ReconnectReason Reason { get; set; }
        
        /// <summary>
        /// 尝试时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }
    }
}