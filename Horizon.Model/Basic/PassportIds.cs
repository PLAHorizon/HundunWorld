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

    /// <summary>
    /// 通行证
    /// </summary>
    [Table("Basic_Sys_PassportIds")]
    [EntityStorage("Basic")]
    public class PassportIds : BaseNoneModel<string>
    {
        public PassportIds()
        {
            Id = "0";
        }
        public DateTime CreatingTime { get; set; } = DateTime.Now;
        /// <summary>
        /// 应用时间
        /// </summary>
        [Comment("应用时间")]
        public DateTime? ApplyTime { get; set; }

    }
}
