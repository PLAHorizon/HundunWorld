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
    }
}