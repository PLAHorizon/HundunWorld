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

namespace Horizon.Model
{
    /// <summary>
    /// 通行证
    /// </summary>
    [Table("Basic_Sys_Passport")]
    [EntityStorage("Basic")]
    public class Passport : BaseNoneModel<string>
    {
        public Passport()
        {
            Id = "0";
        }

        /// <summary>
        /// 登录密码（哈希值）
        /// </summary>
        [Comment("登录密码哈希值")]
        public string Password { get; set; }

        /// <summary>
        /// 密码盐值
        /// </summary>
        [Comment("密码盐值")]
        public string PasswordSalt { get; set; }

        public virtual ICollection<MemberLabe> MemberLabes { get; set; }
    }
}
