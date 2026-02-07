using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Horizon.Game.Message.Enums;

namespace Horizon.Model
{
    /// <summary>
    /// 奖励
    /// </summary>
    [Table("IM_Reward")]
    public class Reward : BaseIdentityModel<long>
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
        /// 奖励名称
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 奖励类型
        /// </summary>
        public RewardType RewardType { get; set; }
        /// <summary>
        /// 奖励简述
        /// </summary>
        public string Description { get; set; }
        /// <summary>
        /// 奖金
        /// </summary>
        public decimal Money { get; set; }
        /// <summary>
        /// 实物图片地址
        /// </summary>
        public string EntityPath { get; set; }
        /// <summary>
        /// 第三方简介
        /// </summary>
        public string ThirdPartyDescription { get; set; }
        /// <summary>
        /// 第三方图或视频地址
        /// </summary>
        public string ThirdPartyPath { get; set; }
    }
}
