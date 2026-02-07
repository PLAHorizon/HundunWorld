using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 游戏服务器实体
    /// </summary>
    [Table("Game_HunduShijie_Server"), TableDescription(Name = "Game_HunduShijie_Server", Order = "HunduShijie_003", Description = "游戏服务器信息")]
    [Comment("游戏服务器信息表")]
    [EntityStorage("Game")]
    public class ServerEntity : BaseIdentityModel<int>
    {
        /// <summary>
        /// 分区ID
        /// </summary>
        [Comment("分区ID")]
        [Column("zone_id")]
        public int ZoneId { get; set; }

        /// <summary>
        /// 服务器名称
        /// </summary>
        [Comment("服务器名称")]
        [Column("server_name", TypeName = "varchar(255)")]
        public string ServerName { get; set; } = string.Empty;

        /// <summary>
        /// IP地址
        /// </summary>
        [Comment("IP地址")]
        [Column("ip_address", TypeName = "varchar(255)")]
        public string IpAddress { get; set; } = string.Empty;

        /// <summary>
        /// 端口
        /// </summary>
        [Comment("端口")]
        [Column("port")]
        public int Port { get; set; }

        /// <summary>
        /// 服务器状态 (例如: Online, Offline, Full)
        /// </summary>
        [Comment("服务器状态")]
        [Column("status", TypeName = "varchar(50)")]
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// 最大玩家数
        /// </summary>
        [Comment("最大玩家数")]
        [Column("max_players")]
        public int MaxPlayers { get; set; }

        /// <summary>
        /// 当前玩家数
        /// </summary>
        [Comment("当前玩家数")]
        [Column("current_players")]
        public int CurrentPlayers { get; set; }

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