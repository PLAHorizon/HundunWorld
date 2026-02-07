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
    /// 文章注释点赞表
    /// </summary>
    [Table("Supports_ArticleDescriptions")]
    [EntityStorage("Supports")]
    public class ArticleDescriptionSupport : BaseNoneModel<Guid>, IPassportSupport
    {
        public ArticleDescriptionSupport()
        {
            Id = Guid.NewGuid();
        }
        /// <summary>
        /// 用户
        /// </summary>
        [Comment("用户")]
        public string Passport { get; set; }
        /// <summary>
        /// 注释Id
        /// </summary>
        [Comment("注释Id")]
        public Guid SupportId { get; set; }
        /// <summary>
        /// 是否是赞，true:赞，false：反对
        /// </summary>
        [Comment("是否是赞，true:赞，false：反对")]
        public bool IsSupport { get; set; }
    }
}
