using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Article
{
    /// <summary>
    /// 阅读进度
    /// </summary>
    [Table("Article_Read"), TableDescription(Name = "Article_Read", Order = "Article_006", Description = "文章章节阅读")]
    [EntityStorage("Article")]
    [Comment("阅读进度")]
    public class ArticleRead : BaseNoneModel<Guid>, ISoftDeleted, IPassport
    {
        /// <summary>
        /// 通行证
        /// </summary>
        [Required]
        [StringLength(32), Column(TypeName = "varchar(32)", Order = 2), TableDescription(TypeName = "varchar(32)", Name = "Passport", Order = "2", Description = "通行证")]
        [Comment("通行证")]
        public string Passport { get; set; }


        /// <summary>
        /// 文章Id
        /// </summary>
        [Column(TypeName = "uuid", Order = 3), TableDescription(TypeName = "uuid", Name = "ArticleId", Order = "3", Description = "文章Id")]
        [Comment("文章Id")]
        public Guid ArticleId { get; set; }

        /// <summary>
        /// 章节Id
        /// </summary>
        [Column(TypeName = "uuid", Order = 4), TableDescription(TypeName = "uuid", Name = "ChapterId", Order = "4", Description = "章节Id")]
        [Comment("章节Id")]
        public Guid ChapterId { get; set; }

        /// <summary>
        /// 阅读进度
        /// </summary>
        [Column(TypeName = "decimal(18,2)", Order = 5), TableDescription(TypeName = "decimal(18,2)", Name = "Progress", Order = "5", Description = "阅读进度")]
        [Comment("阅读进度")]
        public decimal Progress { get; set; }


        /// <summary>
        /// 章节序号
        /// </summary>
        [Column(TypeName = "int", Order = 6), TableDescription(TypeName = "int", Name = "Index", Order = "6", Description = "章节序号")]
        [Comment("章节序号")]
        public int Index { get; set; }

        /// <summary>
        /// 是否可以继续阅读下一章节
        /// </summary>
        [Column(TypeName = "bool", Order = 7), TableDescription(TypeName = "bool", Name = "IsNext", Order = "7", Description = "是否可以继续阅读下一章节")]
        [Comment("是否可以继续阅读下一章节")]
        public bool IsNext { get; set; }

        /// <summary>
        /// 是否是最后一章节
        /// </summary>
        [Column(TypeName = "bool", Order = 8), TableDescription(TypeName = "bool", Name = "IsEnd", Order = "8", Description = "是否是最后一章节")]
        [Comment("是否是最后一章节")]
        public bool IsEnd { get; set; }

        /// <summary>
        /// 阅读时间
        /// </summary>
        [Column(TypeName = "datetime", Order = 9), TableDescription(TypeName = "datetime", Name = "ReadTime", Order = "9", Description = "阅读时间")]
        [Comment("阅读时间")]
        public DateTime ReadTime { get; set; }

        /// <summary>
        /// 是否已删除，true : 已删除，false : 未删除
        /// </summary>
        [Comment("是否已删除，true : 已删除，false : 未删除")]
        public bool IsDeleted { get; set; }

    }
}
