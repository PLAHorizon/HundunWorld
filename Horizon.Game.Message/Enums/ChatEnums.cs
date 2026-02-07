using System;
using System.ComponentModel;

namespace Horizon.Game.Message.Enums
{
    /// <summary>
    /// 聊天消息类型
    /// </summary>
    public enum ChatMessageType
    {
        Normal,      // 普通消息
        System,      // 系统消息
        Private,     // 私聊消息
        Guild,       // 帮派消息
        Team,        // 队伍消息
        World,       // 世界消息
        Announcement // 公告消息
    }

    /// <summary>
    /// 附件类型枚举
    /// </summary>
    public enum AttachmentType
    {
        /// <summary>
        /// 无附件
        /// </summary>
        [Description("无附件")]
        None,
        Experience,
        /// <summary>
        /// 图片附件
        /// </summary>
        [Description("图片附件")]
        Image,

        /// <summary>
        /// 音频附件
        /// </summary>
        [Description("音频附件")]
        Audio,

        /// <summary>
        /// 视频附件
        /// </summary>
        [Description("视频附件")]
        Video,

        /// <summary>
        /// 文件附件
        /// </summary>
        [Description("文件附件")]
        File,

        /// <summary>
        /// 物品附件
        /// </summary>
        [Description("物品附件")]
        Item,

        /// <summary>
        /// 货币附件
        /// </summary>
        [Description("货币附件")]
        Currency,

        /// <summary>
        /// 位置附件
        /// </summary>
        [Description("位置附件")]
        Location,

        /// <summary>
        /// 联系人附件
        /// </summary>
        [Description("联系人附件")]
        Contact,

        /// <summary>
        /// 自定义附件
        /// </summary>
        [Description("自定义附件")]
        Custom
    }
}