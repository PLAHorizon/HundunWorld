using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Supports
{
    /// <summary>
    /// 文章点赞表
    /// </summary>
    [Table("Supports_Articles")]
    [EntityStorage("Supports")]
    public class ArticleSupport : BaseNoneModel<Guid>, IPassportSupport
    {
        public ArticleSupport()
        {
            Id = Guid.NewGuid();
        }
        /// <summary>
        /// 用户
        /// </summary>
        [Comment("用户")]
        public string Passport { get; set; }
        /// <summary>
        /// 章节Id
        /// </summary>
        [Comment("章节Id")]
        public Guid SupportId { get; set; }
        /// <summary>
        /// 是否是赞，true:赞，false：反对
        /// </summary>
        [Comment("是否是赞，true:赞，false：反对")]
        public bool IsSupport { get; set; }
    }
}
