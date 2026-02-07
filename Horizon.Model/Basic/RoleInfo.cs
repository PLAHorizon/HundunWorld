using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model
{
    /// <summary>
    /// 角色
    /// </summary>
    [Table("Basics_Sys_Role")]
    [EntityStorage("Basic")]
    public class RoleInfo : BaseIdentityModel<long>
    {
        public RoleInfo()
        {
            Id = 0;
            this.RolePrivilegeInfo = new HashSet<RolePrivilegeInfo>();
        }
        /// <summary>
        /// 系统用户Id
        /// </summary>
        [Comment("系统用户Id")]
        public long AdminId { get; set; }//等于0则指的是系统的角色
        /// <summary>
        /// 角色名称
        /// </summary>
        [Comment("角色名称")]
        public string RoleName { get; set; }
        /// <summary>
        /// 简述
        /// </summary>
        [Comment("简述")]
        public string Description { get; set; }

        public virtual ICollection<RolePrivilegeInfo> RolePrivilegeInfo { get; set; }
    }
}
