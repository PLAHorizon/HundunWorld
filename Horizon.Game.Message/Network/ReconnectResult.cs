using Horizon.Game.Message.Enums;
using System;

namespace Horizon.Game.Message.Network
{
    /// <summary>
    /// 重连结果信息
    /// </summary>
    public class ReconnectResult
    {
        /// <summary>
        /// 重连是否成功
        /// </summary>
        public bool Success { get; set; }
        
        /// <summary>
        /// 尝试次数
        /// </summary>
        public int AttemptNumber { get; set; }
        
        /// <summary>
        /// 使用的重连策略
        /// </summary>
        public ReconnectStrategy Strategy { get; set; }
        
        /// <summary>
        /// 重连原因
        /// </summary>
        public ReconnectReason Reason { get; set; }
        
        /// <summary>
        /// 重连持续时间（毫秒）
        /// </summary>
        public double Duration { get; set; }
        
        /// <summary>
        /// 重连过程中发生的异常（如果有的话）
        /// </summary>
        public Exception Exception { get; set; }
        
        /// <summary>
        /// 结果时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }
    }
}