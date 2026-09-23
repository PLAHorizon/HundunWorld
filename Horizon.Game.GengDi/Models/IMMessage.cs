using System;
using Horizon.Game.GengDi.Enums;

namespace Horizon.Game.GengDi.Models
{
    public class IMMessage
    {
        [LiteDB.BsonId]
        public string Id { get; set; }
        public string SenderId { get; set; }
        public string ReceiverId { get; set; } // 可以是用户ID或群组ID
        public bool IsGroupConversation { get; set; }
        public string Content { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsRead { get; set; }
        public MessageType Type { get; set; }
        /// <summary>消息是否已被撤回（撤回后以居中灰字提示渲染，不再显示原内容）</summary>
        public bool IsRecalled { get; set; }
    }
}