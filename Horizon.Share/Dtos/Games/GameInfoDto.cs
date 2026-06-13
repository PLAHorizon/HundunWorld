using System;
using System.Collections.Generic;

namespace Horizon.Share.Dtos.Games
{
    /// <summary>
    /// 游戏信息DTO
    /// </summary>
    [Serializable]
    public class GameInfoDto
    {
        /// <summary>
        /// 游戏ID
        /// </summary>
        public int id { get; set; }

        /// <summary>
        /// 游戏名称
        /// </summary>
        public string name { get; set; } = string.Empty;

        /// <summary>
        /// 游戏描述
        /// </summary>
        public string description { get; set; } = string.Empty;

        /// <summary>
        /// 游戏版本
        /// </summary>
        public string version { get; set; } = string.Empty;

        /// <summary>
        /// 游戏类型
        /// </summary>
        public string genre { get; set; } = string.Empty;

        /// <summary>
        /// 平台
        /// </summary>
        public string platform { get; set; } = string.Empty;

        /// <summary>
        /// 开发商
        /// </summary>
        public string developer { get; set; } = string.Empty;

        /// <summary>
        /// 发行商
        /// </summary>
        public string publisher { get; set; } = string.Empty;

        /// <summary>
        /// 封面图URL
        /// </summary>
        public string coverUrl { get; set; } = string.Empty;

        /// <summary>
        /// 下载URL
        /// </summary>
        public string downloadUrl { get; set; } = string.Empty;

        /// <summary>
        /// 标签
        /// </summary>
        public string[] tags { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 特性
        /// </summary>
        public string[] features { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 截图
        /// </summary>
        public string[] screenshots { get; set; } = Array.Empty<string>();
    }
}
