using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;

using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Model.Article
{
    /// <summary>
    /// 文章评论
    /// </summary>
    [Table("Article_Comment"), TableDescription(Name = "Article_Comment", Order = "Article_005", Description = "文章评论表")]
    [EntityStorage("Article")]
    [Comment("文章评论")]
    public class ArticleComment : BaseNoneModel<Guid>, ISoftDeleted, ISupport, IPassport
    {

        /// <summary>
        /// 文章Id
        /// </summary>
        [Column(TypeName = "uuid", Order = 2), TableDescription(TypeName = "uuid", Name = "ArticleId", Order = "2", Description = "文章Id")]
        [Comment("文章Id")]
        public Guid ArticleId { get; set; }


        /// <summary>
        /// 章节Id
        /// </summary>
        [Column(TypeName = "uuid", Order = 3), TableDescription(TypeName = "uuid", Name = "ChapterId", Order = "3", Description = "章节Id")]
        [Comment("章节Id")]
        public Guid? ChapterId { get; set; }


        /// <summary>
        /// 评论类型
        /// </summary>
        [Column(TypeName = "smallint", Order = 4), TableDescription(TypeName = "smallint", Name = "CommentKind", Order = "4", Description = "评论类型")]
        [Comment("评论类型")]
        public ArticleCommetKind CommentKind { get; set; }
        /// <summary>
        /// 审核状态
        /// </summary>
        [Column(TypeName = "smallint", Order = 20), TableDescription(TypeName = "smallint", Name = "CommentKind", Order = "20", Description = "评论类型")]
        [Comment("审核状态")]
        public AuditStatus AuditStatus { get; set; }


        /// <summary>
        /// 用户Id
        /// </summary>
        [Column(TypeName = "varchar(32)", Order = 5), TableDescription(TypeName = "varchar(32)", Name = "Passport", Order = "5", Description = "用户Id")]
        [Comment("用户Id")]
        public string Passport { get; set; }
        /// <summary>
        /// 用户
        /// </summary>
        [StringLength(32), Column(TypeName = "varchar(32)", Order = 23), TableDescription(TypeName = "varchar(32)", Name = "Name", Order = "23", Description = "用户")]
        [Comment("用户")]
        public string? Name { get; set; }

        /// <summary>
        /// 头像
        /// </summary>
        [StringLength(256), Column(TypeName = "varchar(256)", Order = 24), TableDescription(TypeName = "varchar(256)", Name = "Avatar", Order = "24", Description = "头像")]
        [Comment("头像")]
        public string? Avatar { get; set; }
        /// <summary>
        /// 楼层
        /// </summary>
        [Column(TypeName = "int", Order = 6), TableDescription(TypeName = "int", Name = "Floor", Order = "6", Description = "楼层")]
        [Comment("楼层")]
        public int Floor { get; set; }

        /// <summary>
        /// 层内楼层
        /// </summary>
        [Column(TypeName = "int", Order = 7), TableDescription(TypeName = "int", Name = "FloorLevel", Order = "7", Description = "层内楼层")]
        [Comment("层内楼层")]
        public int FloorLevel { get; set; }


        /// <summary>
        /// 引用Id
        /// </summary>
        [Column(TypeName = "uuid", Order = 8), TableDescription(TypeName = "uuid", Name = "QuoteId", Order = "8", Description = "引用Id")]
        [Comment("引用Id")]
        public Guid? QuoteId { get; set; }


        /// <summary>
        /// 媒体地址(图片、视频、音频等)
        /// </summary>
        [StringLength(256), Column(TypeName = "varchar(256)", Order = 9), TableDescription(TypeName = "varchar(256)", Name = "MediaAddress", Order = "9", Description = "媒体地址(图片、视频、音频等)")]
        [Comment("媒体地址(图片、视频、音频等)")]
        public string? MediaAddress { get; set; }
        /// <summary>
        /// 评论内容
        /// </summary>
        [StringLength(1024), Column(TypeName = "varchar(1024)", Order = 29), TableDescription(TypeName = "varchar(1024)", Name = "Content", Order = "29", Description = "评论内容")]
        [Comment("评论内容)")]
        public string Content { get; set; }

        /// <summary>
        /// 支持数
        /// </summary>
        [Column(TypeName = "int", Order = 10), TableDescription(TypeName = "int", Name = "SupportCount", Order = "10", Description = "支持数")]
        [Comment("支持数")]
        public int SupportCount { get; set; }
        /// <summary>
        /// 反对数
        /// </summary>
        [Column(TypeName = "int", Order = 11), TableDescription(TypeName = "int", Name = "UnSupportCount", Order = "11", Description = "支持数")]
        [Comment("反对数")]
        public int UnSupportCount { get; set; }

        /// <summary>
        /// 是否已删除，true : 已删除，false : 未删除
        /// </summary>
        [Comment("是否已删除，true : 已删除，false : 未删除")]
        public bool IsDeleted { get; set; }

    }
}
