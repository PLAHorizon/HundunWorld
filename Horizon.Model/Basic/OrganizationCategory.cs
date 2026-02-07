using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Model.Basic
{
    ///<summary>
    /// 组织机构类型
    /// </summary>
    [Table("Basic_Sys_OrganizationCategory")]
    [EntityStorage("Basic")]
    public class OrganizationCategory : BaseModel<long>
    {
        /// <summary>
        /// 父级
        /// </summary>
        [Comment("父级")]
        public long ParentId { get; set; }

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

        [ForeignKey("ParentId")]
        public virtual OrganizationCategory Parent { get; set; }

        public virtual ICollection<Organization> Organizations { get; set; }
    }
}
