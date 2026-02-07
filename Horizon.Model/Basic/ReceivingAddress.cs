using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.Serialization;
using System.Text;

namespace Horizon.Model
{
    /// <summary>
    /// 用户收货地址
    /// </summary>
    [Table("Basic_Sys_UserReceipt"), DataContract]
    [EntityStorage("Basic")]
    public class ReceivingAddress : BaseNoneModel<Guid>
    {
        /// <summary>
        /// 用户通行证
        /// </summary>
        [Comment("通行证")]
        public string PassportId { get; set; }
        /// <summary>
        /// 是否是默认项
        /// </summary>
        [Comment("是否是默认项")]
        public bool IsDefault { get; set; }
        /// <summary>
        /// 是否是选中项
        /// </summary>
        [Comment("是否是选中项")]
        public bool IsSelected { get; set; }
        /// <summary>
        /// 行政区域Id
        /// </summary>
        [Comment("行政区域Id")]
        public int RegionId { get; set; }
        /// <summary>
        /// 行政区域外的详细地址
        /// </summary>
        [Comment("行政区域外的详细地址")]
        public string Address { get; set; }
    }
}
