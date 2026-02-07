using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Horizon.Core.Abstract;

namespace Horizon.Model
{
    /// <summary>
    /// 系统钱包
    /// </summary>
    [Table("IM_SysMoneyPackage")]
    public class SysMoneyPackage : BaseNoneModel<Guid>
    {
        private Guid _id;
        [Key]
        [Required]
        [DatabaseGenerated(DatabaseGeneratedOption.None), Column(Order = 1)]

        public new Guid Id
        {
            get { return _id; }
            set { _id = value; base.Id = value; }
        }
        /// <summary>
        /// 支出会员Id
        /// </summary>
        public string OutPassportId { get; set; }
        /// <summary>
        /// 获得会员Id
        /// </summary>
        public string IntPassportId { get; set; }
        /// <summary>
        /// 礼物Id
        /// </summary>
        public long IMGiftId { get; set; }
        /// <summary>
        /// 礼物数量
        /// </summary>
        public long GiftCount { get; set; }
        /// <summary>
        /// 系统存留金额
        /// </summary>
        public decimal Amount { get; set; }
    }
}
