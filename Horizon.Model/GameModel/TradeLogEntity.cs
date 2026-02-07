using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Model.Base;
using Horizon.Core.Abstract;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 交易记录实体
    /// </summary>
    [Table("Game_HunduShijie_TradeLog")]
    [EntityStorage("Game")]
    public class TradeLogEntity : BaseGameModel<long>
    {
        /// <summary>
        /// 交易ID
        /// </summary>
        [Key]
        [Column("trade_id")]
        public new long Id { get; set; }
        
        /// <summary>
        /// 卖方角色ID
        /// </summary>
        [Column("seller_id")]
        public long SellerId { get; set; }
        
        /// <summary>
        /// 卖方角色名
        /// </summary>
        [StringLength(20)]
        [Column("seller_name")]
        public string SellerName { get; set; }
        
        /// <summary>
        /// 买方角色ID
        /// </summary>
        [Column("buyer_id")]
        public long BuyerId { get; set; }
        
        /// <summary>
        /// 买方角色名
        /// </summary>
        [StringLength(20)]
        [Column("buyer_name")]
        public string BuyerName { get; set; }
        
        /// <summary>
        /// 交易类型 0-面对面交易 1-拍卖行 2-摆摊
        /// </summary>
        [Column("trade_type")]
        public int TradeType { get; set; }
        
        /// <summary>
        /// 交易物品（JSON格式）
        /// </summary>
        [Column("trade_items")]
        public string TradeItems { get; set; }
        
        /// <summary>
        /// 交易货币类型
        /// </summary>
        [Column("currency_type")]
        public int CurrencyType { get; set; }
        
        /// <summary>
        /// 交易金额
        /// </summary>
        [Column("amount")]
        public long Amount { get; set; }
        
        /// <summary>
        /// 交易税费
        /// </summary>
        [Column("tax")]
        public long Tax { get; set; }
        
        /// <summary>
        /// 交易时间
        /// </summary>
        [Column("trade_time")]
        public DateTime TradeTime { get; set; }
        
        /// <summary>
        /// 交易状态 0-成功 1-取消 2-异常
        /// </summary>
        [Column("status")]
        public int Status { get; set; }
    }
}
