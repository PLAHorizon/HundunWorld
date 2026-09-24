using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 聊天举报记录实体（游戏侧；独立于 IM 的 ChatComplaint，物理隔离）。
    /// </summary>
    [Table("Game_HunduShijie_ChatReport"), TableDescription(Name = "Game_HunduShijie_ChatReport", Order = "HunduShijie_016", Description = "聊天举报记录")]
    [Comment("聊天举报记录表")]
    [EntityStorage("Game")]
    public class ChatReportEntity : BaseGameModel<long>
    {
        /// <summary>记录ID</summary>
        [Key]
        [Column("id", TypeName = "bigint", Order = 1), TableDescription(TypeName = "bigint", Name = "id", Order = "1", Description = "记录ID")]
        [Comment("记录ID")]
        public new long Id { get; set; }

        /// <summary>被举报的消息ID</summary>
        [Column("message_id", TypeName = "bigint", Order = 2), TableDescription(TypeName = "bigint", Name = "message_id", Order = "2", Description = "被举报的消息ID")]
        [Comment("被举报的消息ID")]
        public long MessageId { get; set; }

        /// <summary>消息所在频道</summary>
        [Column("channel", TypeName = "int", Order = 3), TableDescription(TypeName = "int", Name = "channel", Order = "3", Description = "消息所在频道")]
        [Comment("消息所在频道")]
        public int Channel { get; set; }

        /// <summary>举报者角色ID</summary>
        [Column("reporter_id", TypeName = "bigint", Order = 4), TableDescription(TypeName = "bigint", Name = "reporter_id", Order = "4", Description = "举报者角色ID")]
        [Comment("举报者角色ID")]
        public long ReporterId { get; set; }

        /// <summary>被举报者角色ID</summary>
        [Column("target_id", TypeName = "bigint", Order = 5), TableDescription(TypeName = "bigint", Name = "target_id", Order = "5", Description = "被举报者角色ID")]
        [Comment("被举报者角色ID")]
        public long TargetId { get; set; }

        /// <summary>举报原因</summary>
        [Column("reason", TypeName = "nvarchar(200)", Order = 6), TableDescription(TypeName = "nvarchar(200)", Name = "reason", Order = "6", Description = "举报原因")]
        [Comment("举报原因")]
        public string Reason { get; set; }

        /// <summary>处理状态</summary>
        [Column("status", TypeName = "int", Order = 7), TableDescription(TypeName = "int", Name = "status", Order = "7", Description = "处理状态")]
        [Comment("处理状态 0-待处理 1-已处理 2-已驳回")]
        public int Status { get; set; }

        /// <summary>举报时间</summary>
        [Column("create_time", TypeName = "datetime", Order = 8), TableDescription(TypeName = "datetime", Name = "create_time", Order = "8", Description = "举报时间")]
        [Comment("举报时间")]
        public DateTime CreateTime { get; set; }
    }
}
