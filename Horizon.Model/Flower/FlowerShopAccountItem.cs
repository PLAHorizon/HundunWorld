using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_ShopAccountItem")]
    [EntityStorage("Flower")]
    public class FlowerShopAccountItem : BaseIdentityAggregateRootModel<long>
    {
        [Comment("店铺ID")]
        public long ShopId { get; set; }

        [Comment("账户类型0=收入1=支出")]
        public int AccountType { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("金额")]
        public decimal Amount { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("变动后余额")]
        public decimal BalanceAfter { get; set; }

        [StringLength(256), Column(TypeName = "varchar(256)")]
        [Comment("描述")]
        public string Description { get; set; }

        [Comment("关联订单/提现ID")]
        public long RelatedId { get; set; }

        [Comment("创建时间")]
        public DateTime CreatedAt { get; set; }
    }
}
