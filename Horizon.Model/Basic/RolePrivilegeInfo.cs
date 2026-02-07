using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model
{
    /// <summary>
    /// 角色权限
    /// </summary>
    [Table("Basics_Sys_RolePrivilege")]
    [EntityStorage("Basic")]
    public partial class RolePrivilegeInfo : BaseIdentityModel<long>
    {
        public RolePrivilegeInfo()
        {
            Id = 0;
        }
        /// <summary>
        /// 权限值
        /// </summary>
        [Comment("权限值")]
        public long Privilege { get; set; }
        /// <summary>
        /// 角色Id
        /// </summary>
        [Required]
        [Comment("角色Id")]
        public long RoleId { get; set; }
        [ForeignKey("RoleId")]
        public virtual RoleInfo RoleInfo { get; set; }
    }
}
