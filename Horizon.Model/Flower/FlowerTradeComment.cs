using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_TradeComment")]
    [EntityStorage("Flower")]
    public class FlowerTradeComment : BaseIdentityAggregateRootModel<long>
    {
        [Comment("订单ID")]
        public long OrderId { get; set; }

        [Comment("用户ID")]
        public Guid UserId { get; set; }

        [Comment("店铺ID")]
        public long ShopId { get; set; }

        [Comment("描述相符1-5")]
        public int DescriptionScore { get; set; }

        [Comment("服务态度1-5")]
        public int ServiceScore { get; set; }

        [Comment("物流速度1-5")]
        public int LogisticsScore { get; set; }

        [Comment("评价内容")]
        public string Content { get; set; }

        [Comment("是否匿名")]
        public bool IsAnonymous { get; set; }
    }
}
