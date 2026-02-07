using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 消息状态
    /// </summary>
    public class SimpleMessageState<T>
    {
        /// <summary>
        /// 消息Id
        /// </summary>
        public string Id { get; set; }
        /// <summary>
        /// 消息类型
        /// </summary>
        public CoreMessageType Type { get; set; }
        /// <summary>
        /// 消息状态
        /// </summary>
        public MessageState State { get; set; }
        /// <summary>
        /// 消息
        /// </summary>
        public T Message { get; set; }
    }

    /// <summary>
    /// 消息状态
    /// </summary>
    public enum MessageState
    {
        /// <summary>
        /// 已消费
        /// </summary>
        [Description("已消费")]
        Consume = 1,
        /// <summary>
        /// 未消费
        /// </summary>
        [Description("未消费")]
        Unconsume = 0,
        /// <summary>
        /// 未送达
        /// </summary>
        [Description("未送达")]
        NotReached = -1,
        /// <summary>
        /// 未发出
        /// </summary>
        [Description("未发出")]
        Unsent = -2,
    }
}
