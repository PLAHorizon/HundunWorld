using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 聊天频道设置实体
    /// </summary>
    [Table("Game_HunduShijie_ChatChannelSetting"), TableDescription(Name = "Game_HunduShijie_ChatChannelSetting", Order = "HunduShijie_016", Description = "聊天频道设置")]
    [Comment("聊天频道设置表")]
    [EntityStorage("Game")]
    public class ChatChannelSettingEntity : BaseGameModel<int>
    {
        /// <summary>
        /// 设置ID
        /// </summary>
        [Key]
        [Column("id", TypeName = "int", Order = 1), TableDescription(TypeName = "int", Name = "id", Order = "1", Description = "设置ID")]
        [Comment("设置ID")]
        public new int Id { get; set; }
        
        /// <summary>
        /// 频道类型
        /// </summary>
        [Column("channel", TypeName = "int", Order = 2), TableDescription(TypeName = "int", Name = "channel", Order = "2", Description = "频道类型")]
        [Comment("频道类型 0-私聊 1-队伍 2-帮会 3-世界 4-跨服 5-附近 6-系统 7-喇叭")]
        public int Channel { get; set; }
        
        /// <summary>
        /// 频道名称
        /// </summary>
        [Column("channel_name", TypeName = "nvarchar(20)", Order = 3), TableDescription(TypeName = "nvarchar(20)", Name = "channel_name", Order = "3", Description = "频道名称")]
        [Comment("频道名称")]
        public string ChannelName { get; set; }
        
        /// <summary>
        /// 最小等级要求
        /// </summary>
        [Column("min_level", TypeName = "int", Order = 4), TableDescription(TypeName = "int", Name = "min_level", Order = "4", Description = "最小等级要求")]
        [Comment("最小等级要求")]
        public int MinLevel { get; set; }
        
        /// <summary>
        /// 最小活跃等级要求
        /// </summary>
        [Column("min_activity_level", TypeName = "int", Order = 5), TableDescription(TypeName = "int", Name = "min_activity_level", Order = "5", Description = "最小活跃等级要求")]
        [Comment("最小活跃等级要求")]
        public int MinActivityLevel { get; set; }
        
        /// <summary>
        /// 发言间隔
        /// </summary>
        [Column("cooldown", TypeName = "int", Order = 6), TableDescription(TypeName = "int", Name = "cooldown", Order = "6", Description = "发言间隔")]
        [Comment("发言间隔（秒）")]
        public int Cooldown { get; set; }
        
        /// <summary>
        /// 消耗道具ID
        /// </summary>
        [Column("consume_item_id", TypeName = "int", Order = 7), TableDescription(TypeName = "int", Name = "consume_item_id", Order = "7", Description = "消耗道具ID")]
        [Comment("消耗道具ID")]
        public int? ConsumeItemId { get; set; }
        
        /// <summary>
        /// 消耗道具数量
        /// </summary>
        [Column("consume_item_count", TypeName = "int", Order = 8), TableDescription(TypeName = "int", Name = "consume_item_count", Order = "8", Description = "消耗道具数量")]
        [Comment("消耗道具数量")]
        public int ConsumeItemCount { get; set; }
        
        /// <summary>
        /// 消息最大长度
        /// </summary>
        [Column("max_length", TypeName = "int", Order = 9), TableDescription(TypeName = "int", Name = "max_length", Order = "9", Description = "消息最大长度")]
        [Comment("消息最大长度")]
        public int MaxLength { get; set; }
        
        /// <summary>
        /// 是否支持富文本
        /// </summary>
        [Column("allow_rich_text", TypeName = "bit", Order = 10), TableDescription(TypeName = "bit", Name = "allow_rich_text", Order = "10", Description = "是否支持富文本")]
        [Comment("是否支持富文本")]
        public bool AllowRichText { get; set; }
        
        /// <summary>
        /// 是否支持语音
        /// </summary>
        [Column("allow_voice", TypeName = "bit", Order = 11), TableDescription(TypeName = "bit", Name = "allow_voice", Order = "11", Description = "是否支持语音")]
        [Comment("是否支持语音")]
        public bool AllowVoice { get; set; }
        
        /// <summary>
        /// 是否支持物品链接
        /// </summary>
        [Column("allow_item_link", TypeName = "bit", Order = 12), TableDescription(TypeName = "bit", Name = "allow_item_link", Order = "12", Description = "是否支持物品链接")]
        [Comment("是否支持物品链接")]
        public bool AllowItemLink { get; set; }
        
        /// <summary>
        /// 是否支持位置分享
        /// </summary>
        [Column("allow_location", TypeName = "bit", Order = 13), TableDescription(TypeName = "bit", Name = "allow_location", Order = "13", Description = "是否支持位置分享")]
        [Comment("是否支持位置分享")]
        public bool AllowLocation { get; set; }
        
        /// <summary>
        /// 消息保存天数
        /// </summary>
        [Column("save_days", TypeName = "int", Order = 14), TableDescription(TypeName = "int", Name = "save_days", Order = "14", Description = "消息保存天数")]
        [Comment("消息保存天数")]
        public int SaveDays { get; set; }
        
        /// <summary>
        /// 是否启用
        /// </summary>
        [Column("is_enabled", TypeName = "bit", Order = 15), TableDescription(TypeName = "bit", Name = "is_enabled", Order = "15", Description = "是否启用")]
        [Comment("是否启用")]
        public bool IsEnabled { get; set; }
        
        /// <summary>
        /// 创建时间
        /// </summary>
        [Column("create_time", TypeName = "datetime", Order = 16), TableDescription(TypeName = "datetime", Name = "create_time", Order = "16", Description = "创建时间")]
        [Comment("创建时间")]
        public DateTime CreateTime { get; set; }
        
        /// <summary>
        /// 更新时间
        /// </summary>
        [Column("update_time", TypeName = "datetime", Order = 17), TableDescription(TypeName = "datetime", Name = "update_time", Order = "17", Description = "更新时间")]
        [Comment("更新时间")]
        public DateTime UpdateTime { get; set; }
    }
}
