using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;

using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Article
{
    /// <summary>
    /// 文章章节
    /// </summary>
    [Table("Article_Chapters"), TableDescription(Name = "Article_Chapters", Order = "Article_004", Description = "文章章节")]
    [EntityStorage("Article")]
    [Comment("文章章节")]
    public class ArticleChapters : BaseNoneModel<Guid>, ISoftDeleted, ISupport
    {

        /// <summary>
        /// 文章Id
        /// </summary>
        [Column(TypeName = "uuid", Order = 2), TableDescription(TypeName = "uuid", Name = "ArticleId", Order = "2", Description = "文章Id")]
        [Comment("文章Id")]
        public Guid ArticleId { get; set; }
        /// <summary>
        /// 章节名
        /// </summary>
        [StringLength(64), Column(TypeName = "varchar(64)", Order = 3), TableDescription(TypeName = "varchar(64)", Name = "Name", Order = "3", Description = "章节名")]
        [Comment("章节名")]
        public string? Name { get; set; }

        /// <summary>
        /// 章节封面
        /// </summary>
        [StringLength(256), Column(TypeName = "varchar(256)", Order = 4), TableDescription(TypeName = "varchar(256)", Name = "Cover", Order = "4", Description = "文章封面")]
        [Comment("章节封面")]
        public string? Cover { get; set; }


        /// <summary>
        /// 简介
        /// </summary>
        [StringLength(512), Column(TypeName = "varchar(512)", Order = 5), TableDescription(TypeName = "varchar(512)", Name = "Description", Order = "5", Description = "简介")]
        [Comment("简介")]
        public string? Description { get; set; }

        /// <summary>
        /// 内容
        /// </summary>
        [Column(TypeName = "varchar", Order = 6), TableDescription(TypeName = "varchar", Name = "Content", Order = "6", Description = "内容")]
        [Comment("内容")]
        public string? Content { get; set; }


        /// <summary>
        /// 阅读数
        /// </summary>
        [Column(TypeName = "int", Order = 7), TableDescription(TypeName = "int", Name = "Views", Order = "7", Description = "阅读数")]
        [Comment("阅读数")]
        public int? Views { get; set; }
        /// <summary>
        /// 序号
        /// </summary>
        [Column(TypeName = "int", Order = 8), TableDescription(TypeName = "int", Name = "Index", Order = "8", Description = "序号")]
        [Comment("序号")]
        public int Index { get; set; }


        /// <summary>
        /// 支持数
        /// </summary>
        [Column(TypeName = "int", Order = 9), TableDescription(TypeName = "int", Name = "SupportCount", Order = "9", Description = "支持数")]
        [Comment("支持数")]
        public int SupportCount { get; set; }
        /// <summary>
        /// 反对数
        /// </summary>
        [Column(TypeName = "int", Order = 10), TableDescription(TypeName = "int", Name = "UnSupportCount", Order = "10", Description = "支持数")]
        [Comment("反对数")]
        public int UnSupportCount { get; set; }

        /// <summary>
        /// 开篇时间
        /// </summary>
        [Column(TypeName = "date", Order = 11), TableDescription(TypeName = "date", Name = "StartDate", Order = "11", Description = "开篇时间")]
        [Comment("开篇时间")]
        public DateTime? StartDate { get; set; }


        /// <summary>
        /// 完成时间
        /// </summary>
        [Column(TypeName = "date", Order = 12), TableDescription(TypeName = "date", Name = "CompleteDate", Order = "12", Description = "完成时间")]
        [Comment("完成时间")]
        public DateTime? CompleteDate { get; set; }

        /// <summary>
        /// 状态
        /// </summary>
        [Column(TypeName = "smallint", Order = 13), TableDescription(TypeName = "smallint", Name = "Status", Order = "13", Description = "状态")]
        [Comment("状态")]
        public ArticleStatus? Status { get; set; }

        /// <summary>
        /// 审核状态
        /// </summary>
        [Column(TypeName = "smallint", Order = 20), TableDescription(TypeName = "smallint", Name = "CommentKind", Order = "20", Description = "评论类型")]
        [Comment("审核状态")]
        public AuditStatus AuditStatus { get; set; }


        /// <summary>
        /// 价格
        /// </summary>
        [Column(TypeName = "decimal(18,2)", Order = 14), TableDescription(TypeName = "decimal(18,2)", Name = "Price", Order = "14", Description = "价格")]
        [Comment("价格")]
        public decimal Price { get; set; } = 0m;

        /// <summary>
        /// 阅读付费模式
        /// </summary>
        [Column(TypeName = "smallint", Order = 25), TableDescription(TypeName = "smallint", Name = "Mode", Order = "25", Description = "阅读付费模式")]
        [Comment("阅读付费模式")]
        public ReadingChargeMode Mode { get; set; }

        /// <summary>
        /// 时间区间收费过期时间,单位:天
        /// </summary>
        [Comment("阅读付费模式过期时间,单位:天")]
        public int? ExpireIn { get; set; }
        /// <summary>
        /// 内容类型
        /// </summary>
        [Column(TypeName = "smallint", Order = 15), TableDescription(TypeName = "smallint", Name = "ContentKind", Order = "15", Description = "内容类型")]
        [Comment("内容类型")]
        public ArticleContextKind ContentKind { get; set; } = 0;
        /// <summary>
        /// 文章主体
        /// </summary>
        [ForeignKey("ArticleId")]
        public virtual Article Article { get; set; }

        /// <summary>
        /// 是否已删除，true : 已删除，false : 未删除
        /// </summary>
        [Comment("是否已删除，true : 已删除，false : 未删除")]
        public bool IsDeleted { get; set; }
        /// <summary>
        /// 字数
        /// </summary>
        [Comment("字数")]
        public int WordCount { get; set; }
        /// <summary>
        /// 是否是最后一章节
        /// </summary>
        [Comment("是否是最后一章节")]
        public bool IsEnd { get; set; } = false;
    }
}
