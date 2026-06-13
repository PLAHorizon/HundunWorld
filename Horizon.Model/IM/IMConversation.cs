using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model
{
    /// <summary>
    /// IM 会话快照
    /// </summary>
    [Table("IM_Conversation")]
    [Horizon.Core.Abstract.EntityStorage("IM")]
    public class IMConversation : Horizon.Core.Abstract.BaseNoneModel<Guid>
    {
        private Guid _id;

        [Key]
        [Required]
        [DatabaseGenerated(DatabaseGeneratedOption.None), Column(Order = 1)]
        public new Guid Id
        {
            get { return _id; }
            set { _id = value; base.Id = value; }
        }

        /// <summary>
        /// 会话所属用户通行证Id
        /// </summary>
        public string OwnerPassportId { get; set; } = string.Empty;

        /// <summary>
        /// 会话唯一标识，例如 p_123 / g_456 / s_789
        /// </summary>
        public string ConversationId { get; set; } = string.Empty;

        /// <summary>
        /// 聊天关系类型，取值见 IMChatRelationType
        /// </summary>
        public int ChatType { get; set; }

        /// <summary>
        /// 对端用户Id或群Id
        /// </summary>
        public string TargetId { get; set; } = string.Empty;

        /// <summary>
        /// 展示名称
        /// </summary>
        public string TargetName { get; set; } = string.Empty;

        /// <summary>
        /// 展示头像
        /// </summary>
        public string TargetAvatar { get; set; } = string.Empty;

        /// <summary>
        /// 最后一条消息摘要
        /// </summary>
        public string LastMessage { get; set; } = string.Empty;

        /// <summary>
        /// 最后一条消息时间戳
        /// </summary>
        public long LastMessageTime { get; set; }

        /// <summary>
        /// 未读消息数
        /// </summary>
        public int UnreadCount { get; set; }

        /// <summary>
        /// 是否置顶
        /// </summary>
        public bool IsPinned { get; set; }

        /// <summary>
        /// 是否免打扰
        /// </summary>
        public bool IsMuted { get; set; }
    }
}