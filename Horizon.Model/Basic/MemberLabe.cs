using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model
{
    /// <summary>
    /// 会员标签
    /// </summary>
    [Table("Basic_Sys_MemberLabe"), DataContract]
    [EntityStorage("Basic")]
    public class MemberLabe : BaseNoneModel<Guid>
    {
        public MemberLabe()
        {
            Id = Guid.NewGuid();
        }
        /// <summary>
        /// 通行证
        /// </summary>
        [Comment("通行证")]
        public string PassportId { get; set; }
        /// <summary>
        /// 标签Id
        /// </summary>
        [Comment("标签Id")]
        public long LabeId { get; set; }
        [ForeignKey("PassportId"), DataMember]
        public virtual Passport PassportInfo { get; set; }

        [ForeignKey("LabeId"), DataMember]
        public virtual Labe LabeInfo { get; set; }
    }
}
