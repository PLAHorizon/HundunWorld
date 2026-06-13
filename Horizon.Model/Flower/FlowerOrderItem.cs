using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_OrderItem")]
    [EntityStorage("Flower")]
    public class FlowerOrderItem : BaseIdentityModel<long>
    {
        [Comment("订单ID")]
        public long OrderId { get; set; }

        [Comment("商品ID")]
        public long ProductId { get; set; }

        [Comment("品种ID")]
        public int SpeciesId { get; set; }

        [StringLength(128), Column(TypeName = "varchar(128)")]
        [Comment("商品名称")]
        public string ProductName { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("单价")]
        public decimal Price { get; set; }

        [Comment("数量")]
        public int Quantity { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("小计")]
        public decimal Subtotal { get; set; }
    }
}
