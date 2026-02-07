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
    /// 标签
    /// </summary>
    [Table("Basic_Sys_Labe"), DataContract]
    [EntityStorage("Basic")]
    public class Labe : BaseIdentityModel<long>
    {
        public Labe()
        {
            Id = 0;
        }
        /// <summary>
        /// 标签
        /// </summary>
        [Comment("标签")]
        public string Name { get; set; }

        public virtual ICollection<MemberLabe> MemberLabes { get; set; }
    }
}
