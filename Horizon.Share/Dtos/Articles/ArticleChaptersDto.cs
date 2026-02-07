using Horizon.Core.Abstract.Enums;
using Horizon.Core.Abstract.Helper;
using Horizon.Share.Commones;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Share.Dtos.Articles
{
    /// <summary>
    /// 新建文章章节Dto
    /// </summary>
    public class CreateArticleChaptersDto
    {

        /// <summary>
        /// 文章Id
        /// </summary>
        public Guid ArticleId { get; set; }
        /// <summary>
        /// 章节名
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// 章节封面
        /// </summary>
        public string? Cover { get; set; }


        /// <summary>
        /// 简介
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 内容
        /// </summary>
        public string? Content { get; set; }

        /// <summary>
        /// 状态
        /// </summary>
        public ArticleStatus? Status { get; set; }

        /// <summary>
        /// 价格
        /// </summary>
        public decimal Price { get; set; }
        /// <summary>
        /// 阅读付费模式
        /// </summary>
        public ReadingChargeMode Mode { get; set; }
        /// <summary>
        /// 时间区间收费过期时间,单位:天
        /// </summary>       
        public int? ExpireIn { get; set; }
        /// <summary>
        /// 内容类型
        /// </summary>
        public ArticleContextKind ContentKind { get; set; }
        public bool IsEnd { get; set; }
    }


    /// <summary>
    /// 修改文章章节Dto
    /// </summary>
    public class UpdateArticleChaptersDto
    {
        public string Passport { get; set; }
        public Guid Id { get; set; }
        /// <summary>
        /// 章节名
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// 章节封面
        /// </summary>
        public string? Cover { get; set; }


        /// <summary>
        /// 简介
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 内容
        /// </summary>
        public string? Content { get; set; }

        /// <summary>
        /// 状态
        /// </summary>
        public ArticleStatus? Status { get; set; }

        /// <summary>
        /// 价格
        /// </summary>
        public decimal? Price { get; set; }
        /// <summary>
        /// 阅读付费模式
        /// </summary>
        public ReadingChargeMode? Mode { get; set; }
        /// <summary>
        /// 时间区间收费过期时间,单位:天
        /// </summary>       
        public int? ExpireIn { get; set; }
        /// <summary>
        /// 内容类型
        /// </summary>
        public ArticleContextKind? ContentKind { get; set; }
        public bool? IsValid { get; set; }
        public AuditStatus? AuditStatus { get; set; }
    }
    /// <summary>
    /// 文章章节Dto
    /// </summary>
    public class ArticleChaptersDto
    {
        public Guid Id { get; set; }
        /// <summary>
        /// 文章Id
        /// </summary>
        public Guid ArticleId { get; set; }
        /// <summary>
        /// 章节名
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// 章节封面
        /// </summary>
        public string? Cover { get; set; }


        /// <summary>
        /// 简介
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 内容
        /// </summary>
        public string? Content { get; set; }


        /// <summary>
        /// 阅读数
        /// </summary>
        public int? Views { get; set; }
        /// <summary>
        /// 序号
        /// </summary>
        public int Index { get; set; }


        /// <summary>
        /// 支持数
        /// </summary>
        public int SupportCount { get; set; }
        /// <summary>
        /// 反对数
        /// </summary>
        public int UnSupportCount { get; set; }

        /// <summary>
        /// 开篇时间
        /// </summary>
        public DateTime? StartDate { get; set; }


        /// <summary>
        /// 完成时间
        /// </summary>
        public DateTime? CompleteDate { get; set; }

        /// <summary>
        /// 状态
        /// </summary>
        public ArticleStatus? Status { get; set; }
        public string? StatusString => Status.GetDescription();

        /// <summary>
        /// 审核状态
        /// </summary>
        public AuditStatus AuditStatus { get; set; }
        public string AuditStatusString => AuditStatus.GetDescription();


        /// <summary>
        /// 价格
        /// </summary>
        public decimal Price { get; set; }
        /// <summary>
        /// 阅读付费模式
        /// </summary>
        public ReadingChargeMode Mode { get; set; }

        public string ModeString => Mode.GetDescription();

        /// <summary>
        /// 时间区间收费过期时间,单位:天
        /// </summary>       
        public int? ExpireIn { get; set; }
        /// <summary>
        /// 内容类型
        /// </summary>
        public ArticleContextKind ContentKind { get; set; }
        public string ContentKindString => ContentKind.GetDescription();

    }


    public class ArticleChaptersItemDto
    {
        public Guid Id { get; set; }
        /// <summary>
        /// 文章Id
        /// </summary>
        public Guid ArticleId { get; set; }
        /// <summary>
        /// 章节名
        /// </summary>
        public string? Name { get; set; }
        /// <summary>
        /// 简介
        /// </summary>
        public string? Description { get; set; }
        /// <summary>
        /// 阅读数
        /// </summary>
        public int? Views { get; set; }
        /// <summary>
        /// 序号
        /// </summary>
        public int Index { get; set; }
        /// <summary>
        /// 是否是最后一章节
        /// </summary>
        public bool IsEnd { get; set; }
        /// <summary>
        /// 字数
        /// </summary>
        public int WordCount { get; set; }
        public ReadingChargeMode Mode { get; set; }
        public string ModeString => Mode.GetDescription();
        /// <summary>
        /// 获赞数
        /// </summary>
        public int SupportCount { get; set; }
    }


    /// <summary>
    /// 查询
    /// </summary>
    public class ArticleChaptersQueryDto : PageQuery
    {

    }
}
