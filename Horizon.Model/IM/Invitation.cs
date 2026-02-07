using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;

namespace Horizon.Model
{
    /// <summary>
    /// 邀请/挑战 
    /// </summary>
    [Table("IM_Invitation")]
    public class Invitation : BaseIdentityModel<long>
    {
        private long _id;
        [Key]
        [Required]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity), Column(Order = 1)]

        public new long Id
        {
            get { return _id; }
            set { _id = value; base.Id = value; }
        }
        /// <summary>
        /// 邀请名称
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        ///简介
        /// </summary>
        public string Description { get; set; }
        /// <summary>
        /// 发起时间
        /// </summary>
        public DateTime CreateDate { get; set; }
        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime StartDate { get; set; }
        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime EndDate { get; set; }


        /// <summary>
        /// 可持续时间，单位:秒
        /// </summary>
        public long TimeLenght { get; set; }
        /// <summary>
        /// 邀请类型
        /// </summary>
        public InvitationType InvitationType { get; set; }
        /// <summary>
        /// 邀请奖励Id
        /// </summary>
        public long RewardId { get; set; }
    }
}
