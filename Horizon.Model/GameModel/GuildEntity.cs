using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 帮会实体
    /// </summary>
    [Table("Game_HunduShijie_Guild"), TableDescription(Name = "Game_HunduShijie_Guild", Order = "HunduShijie_009", Description = "帮会信息")]
    [Comment("帮会信息表")]
    [EntityStorage("Game")]
    public class GuildEntity : BaseGameModel<long>
    {
        /// <summary>
        /// 帮会ID
        /// </summary>
        [Key]
        [Column("guild_id", TypeName = "bigint", Order = 1), TableDescription(TypeName = "bigint", Name = "guild_id", Order = "1", Description = "帮会ID")]
        [Comment("帮会ID")]
        public new long Id { get; set; }
        
        /// <summary>
        /// 帮会名称
        /// </summary>
        [Column("guild_name", TypeName = "nvarchar(50)", Order = 2), TableDescription(TypeName = "nvarchar(50)", Name = "guild_name", Order = "2", Description = "帮会名称")]
        [Comment("帮会名称")]
        public string GuildName { get; set; }

        /// <summary>
        /// 帮会等级
        /// </summary>
        [Column("guild_level", Order = 3), TableDescription(Name = "guild_level", Order = "3", Description = "帮会等级")]
        [Comment("帮会等级")]
        public int GuildLevel { get; set; }

        /// <summary>
        /// 帮会经验
        /// </summary>
        [Column("guild_experience", Order = 4), TableDescription(Name = "guild_experience", Order = "4", Description = "帮会经验")]
        [Comment("帮会经验")]
        public long GuildExperience { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        [Column("create_time", TypeName = "datetime", Order = 5), TableDescription(TypeName = "datetime", Name = "create_time", Order = "5", Description = "创建时间")]
        [Comment("创建时间")]
        public DateTime CreateTime { get; set; }

        /// <summary>
        /// 帮主ID
        /// </summary>
        [Column("leader_id", TypeName = "bigint", Order = 6), TableDescription(TypeName = "bigint", Name = "leader_id", Order = "6", Description = "帮主ID")]
        [Comment("帮主ID")]
        public long LeaderId { get; set; }

        /// <summary>
        /// 帮会公告
        /// </summary>
        [Column("announcement", TypeName = "nvarchar(200)", Order = 7), TableDescription(TypeName = "nvarchar(200)", Name = "announcement", Order = "7", Description = "帮会公告")]
        [Comment("帮会公告")]
        public string Announcement { get; set; }

        /// <summary>
        /// 帮会状态
        /// </summary>
        [Column("guild_status", Order = 8), TableDescription(Name = "guild_status", Order = "8", Description = "帮会状态")]
        [Comment("帮会状态")]
        public int GuildStatus { get; set; }
    }
}