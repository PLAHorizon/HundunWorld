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
    /// 文体类型
    /// </summary>
    [Table("Article_Category"), TableDescription(Name = "Article_Category", Order = "Article_001", Description = "文体类型表")]
    [EntityStorage("Article")]
    [Comment("文体类型")]
    public class ArticleCategory : BaseIdentityModel<int>, ISoftDeleted, IPassport
    {
        /// <summary>
        /// 通行证
        /// </summary>
        [Comment("通行证")]
        public string Passport { get; set; }
        /// <summary>
        /// 父级Id
        /// </summary>
        [Column(TypeName = "int", Order = 2), TableDescription(TypeName = "int", Name = "ParentId", Order = "2", Description = "父级Id")]
        [Comment("父级Id")]
        public int ParentId { get; set; }

        /// <summary>
        /// 文体类型名
        /// </summary>
        [StringLength(64), Column(TypeName = "varchar(64)", Order = 3), TableDescription(TypeName = "varchar(64)", Name = "Name", Order = "3", Description = "文体类型名")]
        [Comment("文体类型名")]
        public string? Name { get; set; }
        /// <summary>
        /// 是否已删除，true : 已删除，false : 未删除
        /// </summary>
        [Comment("是否已删除，true : 已删除，false : 未删除")]
        public bool IsDeleted { get; set; }
        [ForeignKey("ParentId")]
        public virtual ArticleCategory Parent { get; set; }


        /// <summary>
        /// 获取当前区域到最上级的名称串
        /// </summary>
        /// <param name="split">名称之前的分隔符，默认为空格</param>
        /// <returns></returns>
        public string GetNamePath(string split = " ")
        {
            if (Parent != null)
                return string.Format("{0}{1}{2}", Parent.GetNamePath(), split, Name);
            return Name;
        }

        /// <summary>
        /// 获取当前区域到最上级的id串
        /// </summary>
        /// <param name="split">名称之前的分隔符，默认为逗号</param>
        /// <returns></returns>
        public string GetIdPath(string split = ",")
        {
            if (Parent != null)
                return string.Format("{0}{1}{2}", Parent.GetIdPath(), split, Id);
            return Id.ToString();
        }
    }
}
