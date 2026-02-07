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
    /// 会员礼物
    /// </summary>
    [Table("IM_MemberGift")]
    public class MemberGift : BaseNoneModel<Guid>
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
        /// 礼物Id
        /// </summary>
        public long IMGiftId { get; set; }
        /// <summary>
        /// 数量
        /// </summary>
        public long Count { get; set; }
        /// <summary>
        /// 获得时间
        /// </summary>
        public DateTime GetDate { get; set; }
        /// <summary>
        /// 最后一次使用时间
        /// </summary>
        public DateTime? LastApplyDate { get; set; }
    }
}
