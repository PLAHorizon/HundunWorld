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
    /// 游戏信息实体
    /// </summary>
    [Table("Game_HunduShijie_Game"), TableDescription(Name = "Game_HunduShijie_Game", Order = "HunduShijie_001", Description = "游戏信息")]
    [Comment("游戏信息表")]
    [EntityStorage("Game")]
    public class GameEntity : BaseIdentityModel<int>
    {
        /// <summary>
        /// 游戏名称
        /// </summary>
        [Comment("游戏名称")]
        [Column("game_name", TypeName = "varchar(255)")]
        public string GameName { get; set; } = string.Empty;

        /// <summary>
        /// 游戏描述
        /// </summary>
        [Comment("游戏描述")]
        [Column("game_description", TypeName = "text")]
        public string GameDescription { get; set; } = string.Empty;

        /// <summary>
        /// 游戏版本
        /// </summary>
        [Comment("游戏版本")]
        [Column("game_version", TypeName = "varchar(50)")]
        public string GameVersion { get; set; } = string.Empty;

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

        /// <summary>
        /// 开发商
        /// </summary>
        [Comment("开发商")]
        [Column("developer", TypeName = "varchar(255)")]
        public string Developer { get; set; } = string.Empty;

        /// <summary>
        /// 发行商
        /// </summary>
        [Comment("发行商")]
        [Column("publisher", TypeName = "varchar(255)")]
        public string Publisher { get; set; } = string.Empty;

        /// <summary>
        /// 游戏类型
        /// </summary>
        [Comment("游戏类型")]
        [Column("genre", TypeName = "varchar(255)")]
        public string Genre { get; set; } = string.Empty;

        /// <summary>
        /// 平台
        /// </summary>
        [Comment("平台")]
        [Column("platform", TypeName = "varchar(255)")]
        public string Platform { get; set; } = string.Empty;

        /// <summary>
        /// 封面图片URL
        /// </summary>
        [Comment("封面图片URL")]
        [Column("cover_image_url", TypeName = "varchar(500)")]
        public string CoverImageUrl { get; set; } = string.Empty;

        /// <summary>
        /// 封面URL
        /// </summary>
        [Comment("封面URL")]
        [Column("cover_url", TypeName = "varchar(500)")]
        public string CoverUrl { get; set; } = string.Empty;

        /// <summary>
        /// 预告片URL
        /// </summary>
        [Comment("预告片URL")]
        [Column("trailer_url", TypeName = "varchar(500)")]
        public string TrailerUrl { get; set; } = string.Empty;

        /// <summary>
        /// 官方网站URL
        /// </summary>
        [Comment("官方网站URL")]
        [Column("website_url", TypeName = "varchar(500)")]
        public string WebsiteUrl { get; set; } = string.Empty;

        /// <summary>
        /// 标签 (JSON 字符串)
        /// </summary>
        [Comment("标签 (JSON 字符串)")]
        [Column("tags", TypeName = "text")]
        public string[] Tags { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 支持语言 (JSON 字符串)
        /// </summary>
        [Comment("支持语言 (JSON 字符串)")]
        [Column("languages", TypeName = "text")]
        public string[] Languages { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 游戏特色 (JSON 字符串)
        /// </summary>
        [Comment("游戏特色 (JSON 字符串)")]
        [Column("features", TypeName = "text")]
        public string[] Features { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 系统要求 (JSON 字符串)
        /// </summary>
        [Comment("系统要求 (JSON 字符串)")]
        [Column("system_requirements", TypeName = "text")]
        public string[] SystemRequirements { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 游戏截图URL列表 (JSON 字符串)
        /// </summary>
        [Comment("游戏截图URL列表 (JSON 字符串)")]
        [Column("screenshots", TypeName = "text")]
        public string[] Screenshots { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 游戏视频 (JSON 字符串)
        /// </summary>
        [Comment("游戏视频 (JSON 字符串)")]
        [Column("videos", TypeName = "text")]
        public string[] Videos { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 游戏视频URL列表 (JSON 字符串)
        /// </summary>
        [Comment("游戏视频URL列表 (JSON 字符串)")]
        [Column("videos_url", TypeName = "text")]
        public string[] VideosUrl { get; set; } = Array.Empty<string>();

        /// <summary>
        /// DLC列表 (JSON 字符串)
        /// </summary>
        [Comment("DLC列表 (JSON 字符串)")]
        [Column("dlcs", TypeName = "text")]
        public string[] DLCs { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 成就列表 (JSON 字符串)
        /// </summary>
        [Comment("成就列表 (JSON 字符串)")]
        [Column("achievements", TypeName = "text")]
        public string[] Achievements { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 模组列表 (JSON 字符串)
        /// </summary>
        [Comment("模组列表 (JSON 字符串)")]
        [Column("mods", TypeName = "text")]
        public string[] Mods { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 社区链接 (JSON 字符串)
        /// </summary>
        [Comment("社区链接 (JSON 字符串)")]
        [Column("community_links", TypeName = "text")]
        public string[] CommunityLinks { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 游戏模式 (JSON 字符串)
        /// </summary>
        [Comment("游戏模式 (JSON 字符串)")]
        [Column("game_modes", TypeName = "text")]
        public string[] GameModes { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 游戏设置 (JSON 字符串)
        /// </summary>
        [Comment("游戏设置 (JSON 字符串)")]
        [Column("game_settings", TypeName = "text")]
        public string[] GameSettings { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 游戏来源 (JSON 字符串)
        /// </summary>
        [Comment("游戏来源 (JSON 字符串)")]
        [Column("game_sources", TypeName = "text")]
        public string[] GameSources { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 游戏资产 (JSON 字符串)
        /// </summary>
        [Comment("游戏资产 (JSON 字符串)")]
        [Column("game_assets", TypeName = "text")]
        public string[] GameAssets { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 游戏资产URL (JSON 字符串)
        /// </summary>
        [Comment("游戏资产URL (JSON 字符串)")]
        [Column("game_assets_url", TypeName = "text")]
        public string[] GameAssetsUrl { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 游戏资产URL列表 (JSON 字符串)
        /// </summary>
        [Comment("游戏资产URL列表 (JSON 字符串)")]
        [Column("game_assets_urls", TypeName = "text")]
        public string[] GameAssetsUrls { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 游戏资产URL的URL (JSON 字符串)
        /// </summary>
        [Comment("游戏资产URL的URL (JSON 字符串)")]
        [Column("game_assets_urls_url", TypeName = "text")]
        public string[] GameAssetsUrlsUrl { get; set; } = Array.Empty<string>();
    }
}
