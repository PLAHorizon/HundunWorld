using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_InventoryChangeLog")]
    [EntityStorage("Flower")]
    public class FlowerInventoryChangeLog : BaseIdentityModel<long>
    {
        [Comment("商品ID")]
        public long ProductId { get; set; }

        [Comment("变更前数量")]
        public int BeforeQuantity { get; set; }

        [Comment("变更后数量")]
        public int AfterQuantity { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("变更原因")]
        public string ChangeReason { get; set; }

        [Comment("关联订单ID")]
        public long? OrderId { get; set; }

        [Comment("变更时间")]
        public DateTime ChangedAt { get; set; }
    }
}
