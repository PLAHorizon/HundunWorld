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
    /// 聊天中的礼物
    /// </summary>
    [Table("IM_IMGift")]
    public class IMGift : BaseIdentityModel<long>
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
        /// 礼物名称
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 礼物文件存储路径
        /// </summary>
        public string GiftPath { get; set; }
        /// <summary>
        /// 礼物单价
        /// </summary>
        public decimal Price { get; set; }
        /// <summary>
        /// 兑换比例
        /// </summary>
        public decimal ExchangeRatio { get; set; }
        /// <summary>
        /// 购买礼物后的失效时间长度，单位：秒
        /// </summary>
        public long TimeLenght { get; set; }
        /// <summary>
        ///是否可兑现，默认可兑换现金
        /// </summary>
        public bool IsExchange { get; set; } = true;
    }
}
