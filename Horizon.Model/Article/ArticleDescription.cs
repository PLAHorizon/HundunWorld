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
    /// 文章注释/注解
    /// </summary>
    [Table("Article_Descritpion"), TableDescription(Name = "Article_Read", Order = "Article_007", Description = "文章章节注释")]
    [EntityStorage("Article")]
    [Comment("文章注释/注解")]
    public class ArticleDescription : BaseNoneAggregateRootModel<Guid>, ISoftDeleted, ISupport, IPassport
    {
        /// <summary>
        /// 通行证
        /// </summary>
        [Required]
        [StringLength(32), Column(TypeName = "varchar(32)", Order = 2), TableDescription(TypeName = "varchar(32)", Name = "Passport", Order = "2", Description = "通行证")]
        [Comment("通行证")]
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
        /// 文章注释
        /// </summary>
        [StringLength(2048), Column(TypeName = "varchar(2048)", Order = 5), TableDescription(TypeName = "varchar(2048)", Name = "Description", Order = "5", Description = "文章注释")]
        [Comment("文章注释")]
        public string Description { get; set; }


        /// <summary>
        /// 是否共享此注释
        /// </summary>
        [Column(TypeName = "bool", Order = 6), TableDescription(TypeName = "bool", Name = "IsShare", Order = "6", Description = "是否共享此注释")]
        [Comment("是否共享此注释")]
        public bool IsShare { get; set; }

        /// <summary>
        /// 支持数
        /// </summary>
        [Column(TypeName = "int", Order = 7), TableDescription(TypeName = "int", Name = "SupportCount", Order = "7", Description = "支持数")]
        [Comment("支持数")]
        public int SupportCount { get; set; }
        /// <summary>
        /// 反对数
        /// </summary>
        [Column(TypeName = "int", Order = 8), TableDescription(TypeName = "int", Name = "UnSupportCount", Order = "8", Description = "支持数")]
        [Comment("反对数")]
        public int UnSupportCount { get; set; }



        /// <summary>
        /// 注释标记起始位置
        /// </summary>
        [Column(TypeName = "int", Order = 9), TableDescription(TypeName = "int", Name = "StartPoint", Order = "9", Description = "注释标记起始位置")]
        [Comment("注释标记起始位置")]
        public int? StartPoint { get; set; }

        /// <summary>
        /// 注释结束位置
        /// </summary>
        [Column(TypeName = "int", Order = 10), TableDescription(TypeName = "int", Name = "EndPoint", Order = "10", Description = "注释结束位置")]
        [Comment("注释结束位置")]
        public int? EndPoint { get; set; }
        /// <summary>
        /// 是否已删除，true : 已删除，false : 未删除
        /// </summary>
        [Comment("是否已删除，true : 已删除，false : 未删除")]
        public bool IsDeleted { get; set; }
        /// <summary>
        /// 注释类型
        /// </summary>
        [Column(TypeName = "smallint", Order = 11), TableDescription(TypeName = "smallint", Name = "Kind", Order = "11", Description = "注释类型")]
        [Comment("注释类型")]
        public ArticleDescriptionKind Kind { get; set; }


        /// <summary>
        /// 注释内容类型
        /// </summary>
        [Column(TypeName = "smallint", Order = 12), TableDescription(TypeName = "smallint", Name = "ContentKind", Order = "12", Description = "注释内容类型")]
        [Comment("注释内容类型")]
        public ArticleContextKind ContentKind { get; set; }

    }
}
