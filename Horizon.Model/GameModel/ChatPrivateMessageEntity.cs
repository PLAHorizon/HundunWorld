using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 私聊消息记录实体
    /// </summary>
    [Table("Game_HunduShijie_ChatPrivateMessage"), TableDescription(Name = "Game_HunduShijie_ChatPrivateMessage", Order = "HunduShijie_015", Description = "私聊消息记录")]
    [Comment("私聊消息记录表")]
    [EntityStorage("Game")]
    public class ChatPrivateMessageEntity : BaseGameModel<long>
    {
        /// <summary>
        /// 记录ID
        /// </summary>
        [Key]
        [Column("id", TypeName = "bigint", Order = 1), TableDescription(TypeName = "bigint", Name = "id", Order = "1", Description = "记录ID")]
        [Comment("记录ID")]
        public new long Id { get; set; }
        
        /// <summary>
        /// 消息ID
        /// </summary>
        [Column("message_id", TypeName = "bigint", Order = 2), TableDescription(TypeName = "bigint", Name = "message_id", Order = "2", Description = "消息ID")]
        [Comment("消息ID")]
        public long MessageId { get; set; }
        
        /// <summary>
        /// 发送者ID
        /// </summary>
        [Column("sender_id", TypeName = "bigint", Order = 3), TableDescription(TypeName = "bigint", Name = "sender_id", Order = "3", Description = "发送者ID")]
        [Comment("发送者角色ID")]
        public long SenderId { get; set; }
        
        /// <summary>
        /// 接收者ID
        /// </summary>
        [Column("receiver_id", TypeName = "bigint", Order = 4), TableDescription(TypeName = "bigint", Name = "receiver_id", Order = "4", Description = "接收者ID")]
        [Comment("接收者角色ID")]
        public long ReceiverId { get; set; }
        
        /// <summary>
        /// 会话ID
        /// </summary>
        [Column("session_id", TypeName = "varchar(100)", Order = 5), TableDescription(TypeName = "varchar(100)", Name = "session_id", Order = "5", Description = "会话ID")]
        [Comment("会话ID（双方ID组合）")]
        public string SessionId { get; set; }
        
        /// <summary>
        /// 是否已读
        /// </summary>
        [Column("is_read", TypeName = "bit", Order = 6), TableDescription(TypeName = "bit", Name = "is_read", Order = "6", Description = "是否已读")]
        [Comment("是否已读")]
        public bool IsRead { get; set; }
        
        /// <summary>
        /// 读取时间
        /// </summary>
        [Column("read_time", TypeName = "datetime", Order = 7), TableDescription(TypeName = "datetime", Name = "read_time", Order = "7", Description = "读取时间")]
        [Comment("读取时间")]
        public DateTime? ReadTime { get; set; }
        
        /// <summary>
        /// 是否删除（发送方）
        /// </summary>
        [Column("is_deleted_sender", TypeName = "bit", Order = 8), TableDescription(TypeName = "bit", Name = "is_deleted_sender", Order = "8", Description = "是否删除（发送方）")]
        [Comment("是否删除（发送方）")]
        public bool IsDeletedSender { get; set; }
        
        /// <summary>
        /// 是否删除（接收方）
        /// </summary>
        [Column("is_deleted_receiver", TypeName = "bit", Order = 9), TableDescription(TypeName = "bit", Name = "is_deleted_receiver", Order = "9", Description = "是否删除（接收方）")]
        [Comment("是否删除（接收方）")]
        public bool IsDeletedReceiver { get; set; }
        
        /// <summary>
        /// 创建时间
        /// </summary>
        [Column("create_time", TypeName = "datetime", Order = 10), TableDescription(TypeName = "datetime", Name = "create_time", Order = "10", Description = "创建时间")]
        [Comment("创建时间")]
        public DateTime CreateTime { get; set; }
    }
}
