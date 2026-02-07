using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Model.Basic
{
    /// <summary>
    /// 组织机构
    /// </summary>
    [Table("Basic_Sys_Organization")]
    [EntityStorage("Basic")]
    public class Organization : BaseModel<long>
    {
        /// <summary>
        /// 父级
        /// </summary>
        [Comment("父级")]
        public long ParentId { get; set; }

        /// <summary>
        /// 分类Id
        /// </summary>
        [Comment("分类Id")]
        public long CategoryId { get; set; }

        /// <summary>
        /// 名称
        /// </summary>
        [Comment("名称")]
        public string Name { get; set; }
        /// <summary>
        /// 简介
        /// </summary>
        [Comment("简介")]
        public string Description { get; set; }
        /// <summary>
        /// 组织机构类型
        /// </summary>
        [Comment("组织机构类型")]
        public OrganizationType OrganizationType { get; set; }

        [ForeignKey("ParentId")]
        public virtual Organization Parent { get; set; }

        [ForeignKey("CategoryId")]
        public virtual OrganizationCategory Category { get; set; }
    }
}
