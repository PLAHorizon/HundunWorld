using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_Brand")]
    [EntityStorage("Flower")]
    public class FlowerBrand : BaseIdentityAggregateRootModel<long>, ISoftDeleted
    {
        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("品牌名称")]
        public string Name { get; set; }

        [StringLength(256), Column(TypeName = "varchar(256)")]
        [Comment("品牌Logo")]
        public string Logo { get; set; }

        [StringLength(512), Column(TypeName = "varchar(512)")]
        [Comment("品牌描述")]
        public string Description { get; set; }

        [Comment("排序")]
        public long DisplaySequence { get; set; }

        [Comment("是否推荐")]
        public bool IsRecommend { get; set; }

        [Comment("审核状态")]
        public int AuditStatus { get; set; }

        public bool IsDeleted { get; set; }
    }
}
