using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_ReturnShipment")]
    [EntityStorage("Flower")]
    public class FlowerReturnShipment : BaseIdentityAggregateRootModel<long>
    {
        [Comment("退款单ID")]
        public long RefundId { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("退货物流公司")]
        public string ExpressCompanyName { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("退货运单号")]
        public string ShipOrderNumber { get; set; }

        [StringLength(512), Column(TypeName = "varchar(512)")]
        [Comment("退货地址JSON")]
        public string ReturnAddress { get; set; }

        [Comment("退货发货时间")]
        public DateTime? ShippedAt { get; set; }

        [Comment("商户确认收货时间")]
        public DateTime? ReceivedAt { get; set; }

        [Comment("退货物流状态: 0=待退货, 1=已发货, 2=已收货")]
        public int Status { get; set; }
    }
}
