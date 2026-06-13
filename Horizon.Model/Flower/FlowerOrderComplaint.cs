using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_OrderComplaint")]
    [EntityStorage("Flower")]
    public class FlowerOrderComplaint : BaseIdentityAggregateRootModel<long>
    {
        [Comment("订单ID")]
        public long OrderId { get; set; }

        [Comment("用户ID")]
        public Guid UserId { get; set; }

        [Comment("店铺ID")]
        public long ShopId { get; set; }

        [StringLength(256), Column(TypeName = "varchar(256)")]
        [Comment("投诉原因")]
        public string ComplaintReason { get; set; }

        [Comment("投诉内容")]
        public string ComplaintContent { get; set; }

        [Comment("状态0=待处理1=处理中2=已解决3=已关闭")]
        public int Status { get; set; }

        [Comment("回复内容")]
        public string ReplyContent { get; set; }

        [Comment("创建时间")]
        public DateTime CreatedAt { get; set; }

        [Comment("解决时间")]
        public DateTime? ResolvedAt { get; set; }
    }
}
