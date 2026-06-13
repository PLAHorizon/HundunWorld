using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_ProductComment")]
    [EntityStorage("Flower")]
    public class FlowerProductComment : BaseIdentityAggregateRootModel<long>
    {
        [Comment("商品ID")]
        public long ProductId { get; set; }

        [Comment("订单ID")]
        public long OrderId { get; set; }

        [Comment("用户ID")]
        public Guid UserId { get; set; }

        [Comment("评分1-5")]
        public int Rank { get; set; }

        [StringLength(1024), Column(TypeName = "varchar(1024)")]
        [Comment("评价内容")]
        public string Content { get; set; }

        [StringLength(1024), Column(TypeName = "varchar(1024)")]
        [Comment("评价图片")]
        public string Images { get; set; }

        [StringLength(512), Column(TypeName = "varchar(512)")]
        [Comment("商户回复")]
        public string ReplyContent { get; set; }

        [Comment("回复时间")]
        public DateTime? ReplyTime { get; set; }

        [Comment("是否匿名")]
        public bool IsAnonymous { get; set; }
    }
}
