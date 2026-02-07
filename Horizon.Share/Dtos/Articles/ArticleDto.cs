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
    public class CreateArticleDto
    {
        /// <summary>
        /// 文体类型Id
        /// </summary>

        public int CategoryId { get; set; }

        /// <summary>
        /// 文章名
        /// </summary>

        public string? Name { get; set; }

        /// <summary>
        /// 阅读付费模式
        /// </summary>
        public ReadingChargeMode Mode { get; set; }
        /// <summary>
        /// 时间区间收费过期时间,单位:天
        /// </summary>       
        public int? ExpireIn { get; set; }
        /// <summary>
        /// 作者Id
        /// </summary>
        public Guid? AuthorId { get; set; }


        /// <summary>
        /// 文章封面
        /// </summary>
        public string? Cover { get; set; }


        /// <summary>
        /// 简介
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 朝代
        /// </summary>
        public string? Dynasty { get; set; }
        /// <summary>
        /// 版权类型
        /// </summary>
        public CopyrightType CopyrightType { get; set; }
        /// <summary>
        /// 创建人通行证
        /// </summary>
        public string Passport { get; set; }
    }


    /// <summary>
    /// 修改文章Dto
    /// </summary>
    public class UpdateArticleDto
    {
        public string Passport { get; set; }
        public Guid Id { get; set; }
        /// <summary>
        /// 文体类型Id
        /// </summary>

        public int CategoryId { get; set; }

        /// <summary>
        /// 文章名
        /// </summary>

        public string? Name { get; set; }

        /// <summary>
        /// 阅读付费模式
        /// </summary>
        public ReadingChargeMode? Mode { get; set; }
        /// <summary>
        /// 时间区间收费过期时间,单位:天
        /// </summary>       
        public int? ExpireIn { get; set; }
        /// <summary>
        /// 文章封面
        /// </summary>
        public string? Cover { get; set; }


        /// <summary>
        /// 简介
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 朝代
        /// </summary>
        public string? Dynasty { get; set; }


        /// <summary>
        /// 完成时间
        /// </summary>
        public DateTime? CompleteDate { get; set; }


        /// <summary>
        /// 状态
        /// </summary>
        public ArticleStatus? Status { get; set; }
        /// <summary>
        /// 审核状态
        /// </summary>
        public AuditStatus? AuditStatus { get; set; }

        /// <summary>
        /// 版权类型
        /// </summary>
        public CopyrightType? CopyrightType { get; set; }
        public bool? IsValid { get; set; }
    }
    /// <summary>
    /// 文章Dto
    /// </summary>
    public class ArticleDto
    {
        public Guid Id { get; set; }
        /// <summary>
        /// 文体类型Id
        /// </summary>

        public int CategoryId { get; set; }
        public string Category { get; set; }

        /// <summary>
        /// 文章名
        /// </summary>

        public string? Name { get; set; }

        /// <summary>
        /// 阅读次数
        /// </summary>

        public long Views { get; set; }

        /// <summary>
        /// 阅读付费模式
        /// </summary>
        public ReadingChargeMode Mode { get; set; }
        /// <summary>
        /// 时间区间收费过期时间,单位:天
        /// </summary>       
        public int? ExpireIn { get; set; }
        public string ModeString => Mode.GetDescription();

        /// <summary>
        /// 作者Id
        /// </summary>
        public Guid AuthorId { get; set; }
        /// <summary>
        /// 作者
        /// </summary>
        public string Author { get; set; }
        /// <summary>
        /// 作者头像
        /// </summary>
        public string Avatar { get; set; }


        /// <summary>
        /// 文章封面
        /// </summary>
        public string? Cover { get; set; }


        /// <summary>
        /// 简介
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 朝代
        /// </summary>
        public string? Dynasty { get; set; }


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
        public string ArticleStatusString => Status.GetDescription();
        /// <summary>
        /// 审核状态
        /// </summary>
        public AuditStatus AuditStatus { get; set; }
        public string AuditStatusString => AuditStatus.GetDescription();

        /// <summary>
        /// 版权类型
        /// </summary>
        public CopyrightType CopyrightType { get; set; }
        public string CopyrightTypeString => CopyrightType.GetDescription();
        /// <summary>
        /// 支持数
        /// </summary>
        public int SupportCount { get; set; }
        /// <summary>
        /// 反对数
        /// </summary>
        public int UnSupportCount { get; set; }
        public bool IsValid { get; set; }
    }
    /// <summary>
    /// 查询
    /// </summary>
    public class ArticleQueryDto : PageQuery
    {
        /// <summary>
        /// 文体类型Id
        /// </summary>
        public int? CategoryId { get; set; }

        /// <summary>
        /// 文章名
        /// </summary>
        public string? Name { get; set; }
        /// <summary>
        /// 阅读付费模式
        /// </summary>
        public ReadingChargeMode? Mode { get; set; }
        /// <summary>
        /// 作者
        /// </summary>
        public Guid? AuthorId { get; set; }
        /// <summary>
        /// 朝代
        /// </summary>
        public string? Dynasty { get; set; }

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
        /// <summary>
        /// 审核状态
        /// </summary>
        public AuditStatus? AuditStatus { get; set; }

        /// <summary>
        /// 版权类型
        /// </summary>
        public CopyrightType? CopyrightType { get; set; }
        public bool? IsValid { get; set; }
    }
}
