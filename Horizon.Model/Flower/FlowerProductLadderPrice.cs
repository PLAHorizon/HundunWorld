using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_ProductLadderPrice")]
    [EntityStorage("Flower")]
    public class FlowerProductLadderPrice : BaseIdentityAggregateRootModel<long>, ISoftDeleted
    {
        [Comment("商品ID")]
        public long ProductId { get; set; }

        [Comment("最小批量")]
        public int MinBatch { get; set; }

        [Comment("最大批量")]
        public int MaxBatch { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("价格")]
        public decimal Price { get; set; }

        public bool IsDeleted { get; set; }
    }
}
