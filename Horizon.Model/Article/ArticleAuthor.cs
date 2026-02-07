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
    /// 文章作者
    /// </summary>
    [Table("Article_Author"), TableDescription(Name = "Article_Author", Order = "Article_003", Description = "文章章节")]
    [EntityStorage("Article")]
    [Comment("文章作者")]
    public class ArticleAuthor : BaseNoneModel<Guid>, ISoftDeleted, IPassport
    {

        /// <summary>
        /// 通行证
        /// </summary>
        [Required]
        [StringLength(32), Column(TypeName = "varchar(32)", Order = 2), TableDescription(TypeName = "varchar(32)", Name = "Passport", Order = "2", Description = "通行证")]
        [Comment("通行证")]
        public string Passport { get; set; }


        /// <summary>
        /// 作者别名
        /// </summary>
        [StringLength(32), Column(TypeName = "varchar(32)", Order = 3), TableDescription(TypeName = "varchar(32)", Name = "Name", Order = "3", Description = "作者别名")]
        [Comment("作者别名")]
        public string? Name { get; set; }

        /// <summary>
        /// 作者头像
        /// </summary>
        [StringLength(256), Column(TypeName = "varchar(256)", Order = 4), TableDescription(TypeName = "varchar(256)", Name = "Avatar", Order = "4", Description = "作者头像")]
        [Comment("作者头像")]
        public string? Avatar { get; set; }

        /// <summary>
        /// 国家
        /// </summary>
        [StringLength(32), Column(TypeName = "varchar(32)", Order = 5), TableDescription(TypeName = "varchar(32)", Name = "Country", Order = "5", Description = "国家")]
        [Comment("国家")]
        public string? Country { get; set; }


        /// <summary>
        /// 国家代码
        /// </summary>
        [StringLength(8), Column(TypeName = "varchar(8)", Order = 6), TableDescription(TypeName = "varchar(8)", Name = "CountryCode", Order = "6", Description = "国家代码")]
        [Comment("国家代码")]
        public string? CountryCode { get; set; }


        /// <summary>
        /// 区域码
        /// </summary>
        [StringLength(32), Column(TypeName = "varchar(32)", Order = 7), TableDescription(TypeName = "varchar(32)", Name = "AreaCode", Order = "7", Description = "区域码")]
        [Comment("区域码")]
        public string? AreaCode { get; set; }

        /// <summary>
        /// 省
        /// </summary>
        [StringLength(32), Column(TypeName = "varchar(32)", Order = 8), TableDescription(TypeName = "varchar(32)", Name = "Province", Order = "8", Description = "省")]
        [Comment("省")]
        public string? Province { get; set; }

        /// <summary>
        /// 市
        /// </summary>
        [StringLength(32), Column(TypeName = "varchar(32)", Order = 9), TableDescription(TypeName = "varchar(32)", Name = "City", Order = "9", Description = "市")]
        [Comment("市")]
        public string? City { get; set; }


        /// <summary>
        /// 区
        /// </summary>
        [StringLength(32), Column(TypeName = "varchar(32)", Order = 10), TableDescription(TypeName = "varchar(32)", Name = "Area", Order = "10", Description = "区")]
        [Comment("区")]
        public string? Area { get; set; }


        /// <summary>
        /// 性别
        /// </summary>
        [Column(TypeName = "smallint", Order = 11), TableDescription(TypeName = "smallint", Name = "Gender", Order = "11", Description = "性别")]
        [Comment("性别")]
        public Gender Gender { get; set; }

        /// <summary>
        /// 邮箱
        /// </summary>
        [StringLength(256), Column(TypeName = "varchar(256)", Order = 12), TableDescription(TypeName = "varchar(256)", Name = "Email", Order = "12", Description = "邮箱")]
        [Comment("邮箱")]
        public string? Email { get; set; }

        /// <summary>
        /// 手机号
        /// </summary>
        [StringLength(32), Column(TypeName = "varchar(32)", Order = 13), TableDescription(TypeName = "varchar(32)", Name = "Phone", Order = "13", Description = "手机号")]
        [Comment("手机号")]
        public string? Phone { get; set; }
        /// <summary>
        /// 审核状态
        /// </summary>
        [Column(TypeName = "smallint", Order = 20), TableDescription(TypeName = "smallint", Name = "CommentKind", Order = "20", Description = "评论类型")]
        [Comment("审核状态")]
        public AuditStatus AuditStatus { get; set; }
        /// <summary>
        /// 简介
        /// </summary>
        [Column(TypeName = "varchar", Order = 14), TableDescription(TypeName = "varchar", Name = "Description", Order = "14", Description = "简介")]
        [Comment("简介")]
        public string? Description { get; set; }

        /// <summary>
        /// 简介
        /// </summary>
        [StringLength(1024), Column(TypeName = "varchar(1024)", Order = 15), TableDescription(TypeName = "varchar(1024)", Name = "ShortDescription", Order = "15", Description = "简介")]
        [Comment("简介")]
        public string? ShortDescription { get; set; }

        /// <summary>
        /// 是否已删除，true : 已删除，false : 未删除
        /// </summary>
        [Comment("是否已删除，true : 已删除，false : 未删除")]
        public bool IsDeleted { get; set; }

    }
}
