using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 消息格式
    /// </summary>
    public class MessageFormat
    {
        /// <summary>
        /// 消息类型 4个字节
        /// </summary>
        public const int HeadLength = 4;
        /// <summary>
        /// 消息标识
        /// </summary>
        public const int GUID = 32;
        /// <summary>
        /// 预留空位
        /// </summary>
        public const int Length = 64;
    }

    /// <summary>
    /// 消息类型
    /// </summary>
    public enum CoreMessageType
    {
        /// <summary>
        /// 心跳
        /// </summary>
        [Description("心跳消息")]
        Heart = 0,
        /// <summary>
        /// 即时聊天消息
        /// </summary>
        [Description("即时聊天消息")]
        IM = 1,
        /// <summary>
        /// 数据交换消息
        /// </summary>
        [Description("数据交换消息")]
        Excahnge = 2,
        /// <summary>
        /// 交易消息
        /// </summary>
        [Description("交易消息")]
        Transaction = 3,
    }
}
