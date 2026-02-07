using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    /// 文体
    /// </summary>
    [Table("Article_"), TableDescription(Name = "Article_", Order = "Article_002", Description = "文章章节")]
    [Comment("文体表")]
    [EntityStorage("Article")]
    public class Article : BaseNoneAggregateRootModel<Guid>, ISoftDeleted
    {


        /// <summary>
        /// 文体类型Id
        /// </summary>
        [Column(TypeName = "int", Order = 2), TableDescription(TypeName = "int", Name = "CategoryId", Order = "2", Description = "文体类型Id")]
        [Comment("文体类型Id")]
        public int CategoryId { get; set; }
        /// <summary>
        /// 分类
        /// </summary>
        [Comment("分类")]
        public string Category { get; set; }
        /// <summary>
        /// 文章名
        /// </summary>
        [StringLength(128), Column(TypeName = "varchar(128)", Order = 3), TableDescription(TypeName = "varchar(128)", Name = "Name", Order = "3", Description = "文章名")]
        [Comment("文章名")]
        public string? Name { get; set; }

        /// <summary>
        /// 阅读次数
        /// </summary>
        [Column(TypeName = "bigint", Order = 4), TableDescription(TypeName = "bigint", Name = "Views", Order = "4", Description = "阅读次数")]
        [Comment("阅读次数")]
        public long Views { get; set; }

        /// <summary>
        /// 阅读付费模式
        /// </summary>
        [Column(TypeName = "smallint", Order = 5), TableDescription(TypeName = "smallint", Name = "Mode", Order = "5", Description = "阅读付费模式")]
        [Comment("阅读付费模式")]
        public ReadingChargeMode Mode { get; set; }

        /// <summary>
        /// 时间区间收费过期时间,单位:天
        /// </summary>
        [Comment("阅读付费模式过期时间,单位:天")]
        public int? ExpireIn { get; set; }

        /// <summary>
        /// 作者Id
        /// </summary>
        [Column(TypeName = "uuid", Order = 6), TableDescription(TypeName = "uuid", Name = "AuthorId", Order = "6", Description = "作者Id")]
        [Comment("作者Id")]
        public Guid AuthorId { get; set; }
        /// <summary>
        /// 作者
        /// </summary>
        [Comment("作者")]
        public string Author { get; set; }
        /// <summary>
        /// 作者头像
        /// </summary>
        [Comment("作者头像")]
        public string Avatar { get; set; }


        /// <summary>
        /// 文章封面
        /// </summary>
        [StringLength(256), Column(TypeName = "varchar(256)", Order = 7), TableDescription(TypeName = "varchar(256)", Name = "Cover", Order = "7", Description = "文章封面")]
        [Comment("文章封面")]
        public string? Cover { get; set; }


        /// <summary>
        /// 简介
        /// </summary>
        [StringLength(512), Column(TypeName = "varchar(512)", Order = 8), TableDescription(TypeName = "varchar(512)", Name = "Description", Order = "8", Description = "简介")]
        [Comment("简介")]
        public string? Description { get; set; }

        /// <summary>
        /// 朝代
        /// </summary>
        [StringLength(32), Column(TypeName = "varchar(32)", Order = 9), TableDescription(TypeName = "varchar(32)", Name = "Dynasty", Order = "9", Description = "朝代")]
        [Comment("朝代")]
        public string? Dynasty { get; set; }


        /// <summary>
        /// 开篇时间
        /// </summary>
        [Column(TypeName = "date", Order = 10), TableDescription(TypeName = "date", Name = "StartDate", Order = "10", Description = "开篇时间")]
        [Comment("开篇时间")]
        public DateTime? StartDate { get; set; }


        /// <summary>
        /// 完成时间
        /// </summary>
        [Column(TypeName = "date", Order = 11), TableDescription(TypeName = "date", Name = "CompleteDate", Order = "11", Description = "完成时间")]
        [Comment("完成时间")]
        public DateTime? CompleteDate { get; set; }


        /// <summary>
        /// 状态
        /// </summary>
        [Column(TypeName = "smallint", Order = 12), TableDescription(TypeName = "smallint", Name = "Status", Order = "12", Description = "状态")]
        [Comment("状态")]
        public ArticleStatus? Status { get; set; }
        /// <summary>
        /// 审核状态
        /// </summary>
        [Column(TypeName = "smallint", Order = 20), TableDescription(TypeName = "smallint", Name = "CommentKind", Order = "20", Description = "评论类型")]
        [Comment("审核状态")]
        public AuditStatus AuditStatus { get; set; }

        /// <summary>
        /// 版权类型
        /// </summary>
        [Comment("状态")]
        public CopyrightType CopyrightType { get; set; }

        public virtual ICollection<ArticleChapters> ArticleChapters { get; set; }
        /// <summary>
        /// 是否已删除，true : 已删除，false : 未删除
        /// </summary>
        [Comment("是否已删除，true : 已删除，false : 未删除")]
        public bool IsDeleted { get; set; }
    }
}
