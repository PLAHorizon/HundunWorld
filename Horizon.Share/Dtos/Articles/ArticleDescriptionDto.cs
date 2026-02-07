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
    /// 新建文章注释Dto
    /// </summary>
    public class CreateArticleDescriptionDto
    {
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
        /// 文章Id
        /// </summary>
        public Guid ArticleId { get; set; }

        /// <summary>
        /// 章节Id
        /// </summary>
        public Guid ChapterId { get; set; }


        /// <summary>
        /// 文章注释
        /// </summary>
        public string Description { get; set; }


        /// <summary>
        /// 是否共享此注释
        /// </summary>
        public bool IsShare { get; set; }
        /// <summary>
        /// 注释标记起始位置
        /// </summary>
        public int? StartPoint { get; set; }

        /// <summary>
        /// 注释结束位置
        /// </summary>
        public int? EndPoint { get; set; }

        /// <summary>
        /// 注释类型
        /// </summary>
        public ArticleDescriptionKind Kind { get; set; }


        /// <summary>
        /// 注释内容类型
        /// </summary>
        public ArticleContextKind ContentKind { get; set; }
    }


    /// <summary>
    /// 修改文章注释Dto
    /// </summary>
    public class UpdateArticleDescriptionDto
    {
        public string Passport { get; set; }
        public Guid Id { get; set; }


        /// <summary>
        /// 文章注释
        /// </summary>
        public string Description { get; set; }
        /// <summary>
        /// 是否共享此注释
        /// </summary>
        public bool IsShare { get; set; }
        /// <summary>
        /// 注释标记起始位置
        /// </summary>
        public int? StartPoint { get; set; }

        /// <summary>
        /// 注释结束位置
        /// </summary>
        public int? EndPoint { get; set; }
        /// <summary>
        /// 注释类型
        /// </summary>
        public ArticleDescriptionKind Kind { get; set; }


        /// <summary>
        /// 注释内容类型
        /// </summary>
        public ArticleContextKind ContentKind { get; set; }
    }
    /// <summary>
    /// 文章注释Dto
    /// </summary>
    public class ArticleDescriptionDto
    {
        public Guid Id { get; set; }
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
        /// 文章Id
        /// </summary>
        public Guid ArticleId { get; set; }

        /// <summary>
        /// 章节Id
        /// </summary>
        public Guid ChapterId { get; set; }


        /// <summary>
        /// 文章注释
        /// </summary>
        public string Description { get; set; }


        /// <summary>
        /// 是否共享此注释
        /// </summary>
        public bool IsShare { get; set; }

        /// <summary>
        /// 支持数
        /// </summary>
        public int SupportCount { get; set; }
        /// <summary>
        /// 反对数
        /// </summary>
        public int UnSupportCount { get; set; }



        /// <summary>
        /// 注释标记起始位置
        /// </summary>
        public int? StartPoint { get; set; }

        /// <summary>
        /// 注释结束位置
        /// </summary>
        public int? EndPoint { get; set; }
        /// <summary>
        /// 是否有效
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// 注释类型
        /// </summary>
        public ArticleDescriptionKind Kind { get; set; }
        public string KindString => Kind.GetDescription();


        /// <summary>
        /// 注释内容类型
        /// </summary>
        public ArticleContextKind ContentKind { get; set; }
        public string ContentKindString => ContentKind.GetDescription();
    }
    /// <summary>
    /// 查询注释
    /// </summary>
    public class ArticleDescriptionQueryDto : PageQuery
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
