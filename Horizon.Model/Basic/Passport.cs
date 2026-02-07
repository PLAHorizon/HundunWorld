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
        /// 登录密码
        /// </summary>
        [Comment("登录密码")]
        public string Password { get; set; }

        public virtual ICollection<MemberLabe> MemberLabes { get; set; }
    }
}
