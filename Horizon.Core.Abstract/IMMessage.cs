using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 及时聊天消息
    /// </summary>
    public class IMMessage
    {
        /// <summary>
        /// 消息Id
        /// </summary>
        public string Id { get; set; }
        /// <summary>
        /// 发送聊天消息这Id
        /// </summary>
        public long PassportId { get; set; }
        /// <summary>
        /// 聊天目标Id
        /// </summary>
        public long TargetId { get; set; }
        /// <summary>
        /// 聊天消息类型
        /// </summary>
        public IMMessageType Type { get; set; }
        /// <summary>
        /// 聊天对象类型
        /// </summary>
        public IMTargetType TargetType { get; set; }

        /// <summary>
        /// 消息内容
        /// </summary>
        public string Content { get; set; }

    }

    /// <summary>
    /// 聊天消息类型
    /// </summary>
    public enum IMMessageType
    {
        Text = 0,
        Image = 1,
        Audio = 2,
        Video = 3,
        Transaction = 4,
    }
    /// <summary>
    /// 聊天目标类型
    /// </summary>
    public enum IMTargetType
    {
        Person = 0,
        Group = 1,
    }
}
