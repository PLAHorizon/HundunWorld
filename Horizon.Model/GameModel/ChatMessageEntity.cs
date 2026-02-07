using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 聊天消息实体
    /// </summary>
    [Table("Game_HunduShijie_ChatMessage"), TableDescription(Name = "Game_HunduShijie_ChatMessage", Order = "HunduShijie_014", Description = "聊天消息信息")]
    [Comment("聊天消息表")]
    [EntityStorage("Game")]
    public class ChatMessageEntity : BaseGameModel<long>
    {
        /// <summary>
        /// 消息ID
        /// </summary>
        [Key]
        [Column("message_id", TypeName = "bigint", Order = 1), TableDescription(TypeName = "bigint", Name = "message_id", Order = "1", Description = "消息ID")]
        [Comment("消息ID")]
        public new long Id { get; set; }
        
        /// <summary>
        /// 发送者ID
        /// </summary>
        [Column("sender_id", TypeName = "bigint", Order = 2), TableDescription(TypeName = "bigint", Name = "sender_id", Order = "2", Description = "发送者ID")]
        [Comment("发送者角色ID")]
        public long SenderId { get; set; }
        
        /// <summary>
        /// 发送者名称
        /// </summary>
        [Required]
        [Column("sender_name", TypeName = "nvarchar(20)", Order = 3), TableDescription(TypeName = "nvarchar(20)", Name = "sender_name", Order = "3", Description = "发送者名称")]
        [Comment("发送者角色名")]
        public string SenderName { get; set; }
        
        /// <summary>
        /// 发送者等级
        /// </summary>
        [Column("sender_level", TypeName = "int", Order = 4), TableDescription(TypeName = "int", Name = "sender_level", Order = "4", Description = "发送者等级")]
        [Comment("发送者等级")]
        public int SenderLevel { get; set; }
        
        /// <summary>
        /// 发送者活跃等级
        /// </summary>
        [Column("sender_activity_level", TypeName = "int", Order = 5), TableDescription(TypeName = "int", Name = "sender_activity_level", Order = "5", Description = "发送者活跃等级")]
        [Comment("发送者活跃等级")]
        public int SenderActivityLevel { get; set; }
        
        /// <summary>
        /// 聊天频道
        /// </summary>
        [Column("channel", TypeName = "int", Order = 6), TableDescription(TypeName = "int", Name = "channel", Order = "6", Description = "聊天频道")]
        [Comment("聊天频道 0-私聊 1-队伍 2-帮会 3-世界 4-跨服 5-附近 6-系统 7-喇叭")]
        public int Channel { get; set; }
        
        /// <summary>
        /// 接收者ID
        /// </summary>
        [Column("receiver_id", TypeName = "bigint", Order = 7), TableDescription(TypeName = "bigint", Name = "receiver_id", Order = "7", Description = "接收者ID")]
        [Comment("接收者ID（私聊时使用）")]
        public long? ReceiverId { get; set; }
        
        /// <summary>
        /// 接收者名称
        /// </summary>
        [Column("receiver_name", TypeName = "nvarchar(20)", Order = 8), TableDescription(TypeName = "nvarchar(20)", Name = "receiver_name", Order = "8", Description = "接收者名称")]
        [Comment("接收者名称（私聊时使用）")]
        public string ReceiverName { get; set; }
        
        /// <summary>
        /// 消息内容
        /// </summary>
        [Required]
        [Column("content", TypeName = "nvarchar(500)", Order = 9), TableDescription(TypeName = "nvarchar(500)", Name = "content", Order = "9", Description = "消息内容")]
        [Comment("消息内容")]
        public string Content { get; set; }
        
        /// <summary>
        /// 消息类型
        /// </summary>
        [Column("content_type", TypeName = "int", Order = 10), TableDescription(TypeName = "int", Name = "content_type", Order = "10", Description = "消息类型")]
        [Comment("消息类型 0-文本 1-表情 2-物品链接 3-位置信息 4-语音 5-图片 6-红包")]
        public int ContentType { get; set; }
        
        /// <summary>
        /// 扩展数据
        /// </summary>
        [Column("ext_data", TypeName = "nvarchar(2000)", Order = 11), TableDescription(TypeName = "nvarchar(2000)", Name = "ext_data", Order = "11", Description = "扩展数据")]
        [Comment("扩展数据（JSON格式，包含物品信息、位置坐标等）")]
        public string ExtData { get; set; }
        
        /// <summary>
        /// 语音时长
        /// </summary>
        [Column("voice_duration", TypeName = "int", Order = 12), TableDescription(TypeName = "int", Name = "voice_duration", Order = "12", Description = "语音时长")]
        [Comment("语音时长（秒）")]
        public int? VoiceDuration { get; set; }
        
        /// <summary>
        /// 语音URL
        /// </summary>
        [Column("voice_url", TypeName = "varchar(500)", Order = 13), TableDescription(TypeName = "varchar(500)", Name = "voice_url", Order = "13", Description = "语音URL")]
        [Comment("语音文件URL")]
        public string VoiceUrl { get; set; }
        
        /// <summary>
        /// 是否已读
        /// </summary>
        [Column("is_read", TypeName = "bit", Order = 14), TableDescription(TypeName = "bit", Name = "is_read", Order = "14", Description = "是否已读")]
        [Comment("是否已读（私聊使用）")]
        public bool IsRead { get; set; }
        
        /// <summary>
        /// 发送时间
        /// </summary>
        [Column("send_time", TypeName = "datetime", Order = 15), TableDescription(TypeName = "datetime", Name = "send_time", Order = "15", Description = "发送时间")]
        [Comment("发送时间")]
        public DateTime SendTime { get; set; }
        
        /// <summary>
        /// 消息状态
        /// </summary>
        [Column("status", TypeName = "int", Order = 16), TableDescription(TypeName = "int", Name = "status", Order = "16", Description = "消息状态")]
        [Comment("消息状态 0-正常 1-已撤回 2-已屏蔽 3-已删除")]
        public int Status { get; set; }
        
        /// <summary>
        /// 服务器ID
        /// </summary>
        [Column("server_id", TypeName = "int", Order = 17), TableDescription(TypeName = "int", Name = "server_id", Order = "17", Description = "服务器ID")]
        [Comment("服务器ID")]
        public int ServerId { get; set; }
        
        /// <summary>
        /// 队伍ID
        /// </summary>
        [Column("team_id", TypeName = "bigint", Order = 18), TableDescription(TypeName = "bigint", Name = "team_id", Order = "18", Description = "队伍ID")]
        [Comment("队伍ID（队伍频道使用）")]
        public long? TeamId { get; set; }
        
        /// <summary>
        /// 帮会ID
        /// </summary>
        [Column("guild_id", TypeName = "bigint", Order = 19), TableDescription(TypeName = "bigint", Name = "guild_id", Order = "19", Description = "帮会ID")]
        [Comment("帮会ID（帮会频道使用）")]
        public long? GuildId { get; set; }
        
        /// <summary>
        /// 消耗道具ID
        /// </summary>
        [Column("consume_item_id", TypeName = "int", Order = 20), TableDescription(TypeName = "int", Name = "consume_item_id", Order = "20", Description = "消耗道具ID")]
        [Comment("消耗道具ID（喇叭频道使用）")]
        public int? ConsumeItemId { get; set; }
        
        /// <summary>
        /// 是否含有敏感词
        /// </summary>
        [Column("has_sensitive", TypeName = "bit", Order = 21), TableDescription(TypeName = "bit", Name = "has_sensitive", Order = "21", Description = "是否含有敏感词")]
        [Comment("是否含有敏感词")]
        public bool HasSensitive { get; set; }
        
        /// <summary>
        /// 过滤后内容
        /// </summary>
        [Column("filtered_content", TypeName = "nvarchar(500)", Order = 22), TableDescription(TypeName = "nvarchar(500)", Name = "filtered_content", Order = "22", Description = "过滤后内容")]
        [Comment("过滤后内容")]
        public string FilteredContent { get; set; }
    }
}