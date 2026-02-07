using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 游戏分区实体
    /// </summary>
    [Table("Game_HunduShijie_Zone"), TableDescription(Name = "Game_HunduShijie_Zone", Order = "HunduShijie_002", Description = "游戏分区信息")]
    [Comment("游戏分区信息表")]
    [EntityStorage("Game")]
    public class ZoneEntity : BaseIdentityModel<int>
    {
        /// <summary>
        /// 游戏Id
        /// </summary>
        [Comment("游戏Id")]
        [Column("game_id")]
        public int GameId { get; set; } 
        /// <summary>
        /// 分区名称
        /// </summary>
        [Comment("分区名称")]
        [Column("zone_name", TypeName = "varchar(255)")]
        public string ZoneName { get; set; } = string.Empty;

        /// <summary>
        /// 分区描述
        /// </summary>
        [Comment("分区描述")]
        [Column("description", TypeName = "varchar(500)")]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 创建时间
        /// </summary>
        [Comment("创建时间")]
        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 更新时间
        /// </summary>
        [Comment("更新时间")]
        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
