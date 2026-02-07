using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 聊天黑名单实体
    /// </summary>
    [Table("Game_HunduShijie_ChatBlacklist"), TableDescription(Name = "Game_HunduShijie_ChatBlacklist", Order = "HunduShijie_017", Description = "聊天黑名单")]
    [Comment("聊天黑名单表")]
    [EntityStorage("Game")]
    public class ChatBlacklistEntity : BaseGameModel<long>
    {
        /// <summary>
        /// 记录ID
        /// </summary>
        [Key]
        [Column("id", TypeName = "bigint", Order = 1), TableDescription(TypeName = "bigint", Name = "id", Order = "1", Description = "记录ID")]
        [Comment("记录ID")]
        public new long Id { get; set; }
        
        /// <summary>
        /// 角色ID
        /// </summary>
        [Column("character_id", TypeName = "bigint", Order = 2), TableDescription(TypeName = "bigint", Name = "character_id", Order = "2", Description = "角色ID")]
        [Comment("角色ID")]
        public long CharacterId { get; set; }
        
        /// <summary>
        /// 屏蔽角色ID
        /// </summary>
        [Column("blocked_character_id", TypeName = "bigint", Order = 3), TableDescription(TypeName = "bigint", Name = "blocked_character_id", Order = "3", Description = "屏蔽角色ID")]
        [Comment("被屏蔽的角色ID")]
        public long BlockedCharacterId { get; set; }
        
        /// <summary>
        /// 屏蔽角色名
        /// </summary>
        [Column("blocked_character_name", TypeName = "nvarchar(20)", Order = 4), TableDescription(TypeName = "nvarchar(20)", Name = "blocked_character_name", Order = "4", Description = "屏蔽角色名")]
        [Comment("被屏蔽的角色名")]
        public string BlockedCharacterName { get; set; }
        
        /// <summary>
        /// 屏蔽原因
        /// </summary>
        [Column("block_reason", TypeName = "nvarchar(200)", Order = 5), TableDescription(TypeName = "nvarchar(200)", Name = "block_reason", Order = "5", Description = "屏蔽原因")]
        [Comment("屏蔽原因")]
        public string BlockReason { get; set; }
        
        /// <summary>
        /// 屏蔽时间
        /// </summary>
        [Column("block_time", TypeName = "datetime", Order = 6), TableDescription(TypeName = "datetime", Name = "block_time", Order = "6", Description = "屏蔽时间")]
        [Comment("屏蔽时间")]
        public DateTime BlockTime { get; set; }
        
        /// <summary>
        /// 是否永久屏蔽
        /// </summary>
        [Column("is_permanent", TypeName = "bit", Order = 7), TableDescription(TypeName = "bit", Name = "is_permanent", Order = "7", Description = "是否永久屏蔽")]
        [Comment("是否永久屏蔽")]
        public bool IsPermanent { get; set; }
        
        /// <summary>
        /// 解除屏蔽时间
        /// </summary>
        [Column("unblock_time", TypeName = "datetime", Order = 8), TableDescription(TypeName = "datetime", Name = "unblock_time", Order = "8", Description = "解除屏蔽时间")]
        [Comment("解除屏蔽时间")]
        public DateTime? UnblockTime { get; set; }
        
        /// <summary>
        /// 是否有效
        /// </summary>
        [Column("is_active", TypeName = "bit", Order = 9), TableDescription(TypeName = "bit", Name = "is_active", Order = "9", Description = "是否有效")]
        [Comment("是否有效")]
        public bool IsActive { get; set; }
    }
}
