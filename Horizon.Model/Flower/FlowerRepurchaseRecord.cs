using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_RepurchaseRecord")]
    [EntityStorage("Flower")]
    public class FlowerRepurchaseRecord : BaseIdentityModel<long>
    {
        [Comment("买家ID")]
        public Guid BuyerId { get; set; }

        [Comment("原订单ID")]
        public long OriginalOrderId { get; set; }

        [Comment("新订单ID")]
        public long? NewOrderId { get; set; }

        [Comment("复购时间")]
        public DateTime RepurchaseTime { get; set; }
    }
}
