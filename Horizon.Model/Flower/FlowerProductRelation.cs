using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_ProductRelation")]
    [EntityStorage("Flower")]
    public class FlowerProductRelation : BaseIdentityAggregateRootModel<long>
    {
        [Comment("商品ID")]
        public long ProductId { get; set; }

        [Comment("关联商品ID")]
        public long RelatedProductId { get; set; }

        [Comment("排序")]
        public int DisplaySequence { get; set; }
    }
}
