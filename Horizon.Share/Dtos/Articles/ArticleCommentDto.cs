using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;
using Horizon.Core.Abstract.Helper;
using Horizon.Share.Commones;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Share.Dtos.Articles
{
    /// <summary>
    /// 新建文章Dto
    /// </summary>
    public class CreateArticleCommentDto
    {
        public Guid ArticleId { get; set; }


        /// <summary>
        /// 章节Id
        /// </summary>
        public Guid? ChapterId { get; set; }


        /// <summary>
        /// 评论类型
        /// </summary>
        public ArticleCommetKind CommentKind { get; set; }


        /// <summary>
        /// 用户Id
        /// </summary>
        public string Passport { get; set; }
        /// <summary>
        /// 用户
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// 头像
        /// </summary>
        public string? Avatar { get; set; }


        /// <summary>
        /// 引用Id
        /// </summary>
        public Guid? QuoteId { get; set; }


        /// <summary>
        /// 媒体地址(图片、视频、音频等)
        /// </summary>
        public string? MediaAddress { get; set; }
        /// <summary>
        /// 评论内容
        /// </summary>
        public string Content { get; set; }
    }


    /// <summary>
    /// 修改文章Dto
    /// </summary>
    public class UpdateArticleCommentDto
    {

    }
    /// <summary>
    /// 文章Dto
    /// </summary>
    public class ArticleCommentDto
    {
        public Guid Id { get; set; }
        public Guid ArticleId { get; set; }


        /// <summary>
        /// 章节Id
        /// </summary>
        public Guid? ChapterId { get; set; }


        /// <summary>
        /// 评论类型
        /// </summary>
        public ArticleCommetKind CommentKind { get; set; }
        public string CommentKindString => CommentKind.GetDescription();
        /// <summary>
        /// 审核状态
        /// </summary>
        public AuditStatus AuditStatus { get; set; }


        /// <summary>
        /// 用户Id
        /// </summary>
        public string Passport { get; set; }
        /// <summary>
        /// 用户
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// 头像
        /// </summary>
        public string? Avatar { get; set; }
        /// <summary>
        /// 楼层
        /// </summary>
        public int Floor { get; set; }

        /// <summary>
        /// 层内楼层
        /// </summary>
        public int FloorLevel { get; set; }


        /// <summary>
        /// 引用Id
        /// </summary>
        public Guid? QuoteId { get; set; }


        /// <summary>
        /// 媒体地址(图片、视频、音频等)
        /// </summary>
        public string? MediaAddress { get; set; }

        /// <summary>
        /// 评论内容
        /// </summary>
        public string Content { get; set; }
        /// <summary>
        /// 支持数
        /// </summary>
        public int SupportCount { get; set; }
        /// <summary>
        /// 反对数
        /// </summary>
        public int UnSupportCount { get; set; }
    }
    /// <summary>
    /// 查询
    /// </summary>
    public class ArticleCommentQueryDto : PageQuery
    {
        /// <summary>
        /// 文章章节Id
        /// </summary>
        public Guid? ChapterId { get; set; }
        /// <summary>
        /// 文章Id
        /// </summary>
        public Guid? ArticleId { get; set; }
    }


}
