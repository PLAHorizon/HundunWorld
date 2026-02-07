using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using Horizon.Core;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 消息传递模型
    /// </summary>
    [Serializable]
    public class TransactionMessage<T>
    {
        public TransactionMessage()
        {
            Message = new StateMessage();
        }
        /// <summary>
        /// 消息头
        /// </summary>

        public Header Header { get; set; }
        /// <summary>
        /// 消息数据主体
        /// </summary>
        public T Body { get; set; }
        /// <summary>
        /// 返回的标记消息
        /// </summary>
        public StateMessage Message { get; set; }
    }
}
