using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model
{
    [Table("Basics_Sys_OpenIds")]
    [EntityStorage("Basic")]
    public partial class OpenIds : BaseIdentityModel<long>
    {
        public OpenIds()
        {
            Id = 0;
        }
        /// <summary>
        /// OpenId
        /// </summary>
        [Comment("OpenId")]
        public string OpenId { get; set; }
        /// <summary>
        /// UnionId
        /// </summary>
        [Comment("UnionId")]
        public string UnionId { get; set; }
        /// <summary>
        /// 订阅时间
        /// </summary>
        [Comment("订阅时间")]
        public System.DateTime? SubscribeTime { get; set; }
        /// <summary>
        /// 是否订阅
        /// </summary>
        [Comment("是否订阅")]
        public bool IsSubscribe { get; set; }
    }
}
