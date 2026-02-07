using Horizon.Model;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model
{
    [Table("Basics_Sys_SysManager")]
    [EntityStorage("Basic")]
    public class SysManager : BaseNoneModel<Guid>
    {
        public SysManager()
        {
            Id = Guid.NewGuid();
        }
        /// <summary>
        /// 用户名
        /// </summary>
        [Comment("用户名")]
        [Display(Name = "用户名")]
        public string Name { get; set; }
        /// <summary>
        /// 密码
        /// </summary>
        [Display(Name = "密码"), StringLength(256, MinimumLength = 6, ErrorMessage = "密码长度在6到32个字符之间")]
        [Comment("密码")]
        public string Password { get; set; }
        /// <summary>
        /// 角色
        /// </summary>
        [Comment("角色")]
        public long RoleId { get; set; }


    }
}
