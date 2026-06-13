using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    /// <summary>
    /// 花卉聊天历史
    /// </summary>
    [Table("Flower_ChatHistory")]
    [EntityStorage("Flower")]
    public class FlowerChatHistory : BaseIdentityAggregateRootModel<long>
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [Comment("用户ID")]
        public Guid UserId { get; set; }

        /// <summary>
        /// 会话ID
        /// </summary>
        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("会话ID")]
        public string ConversationId { get; set; }

        /// <summary>
        /// 角色
        /// </summary>
        [StringLength(16), Column(TypeName = "varchar(16)")]
        [Comment("角色")]
        public string Role { get; set; }

        /// <summary>
        /// 内容
        /// </summary>
        [Column(TypeName = "nvarchar(max)")]
        [Comment("内容")]
        public string Content { get; set; }

        /// <summary>
        /// 模型版本
        /// </summary>
        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("模型版本")]
        public string ModelVersion { get; set; }
    }
}
