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
    /// 钱包
    /// </summary>
    [Table("IM_MoneyPackage")]
    public class MoneyPackage : BaseNoneModel<Guid>
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
        /// 会员Id
        /// </summary>
        public string PassportId { get; set; }
        /// <summary>
        /// 钱包余额
        /// </summary>
        public decimal Balance { get; set; }
    }
}
