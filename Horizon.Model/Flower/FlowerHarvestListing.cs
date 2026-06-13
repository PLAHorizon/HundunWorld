using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_HarvestListing")]
    [EntityStorage("Flower")]
    public class FlowerHarvestListing : BaseIdentityAggregateRootModel<long>
    {
        [Comment("关联采收记录ID")]
        public long YieldRecordId { get; set; }

        [Comment("关联商品ID")]
        public long? ProductId { get; set; }

        [Comment("关联批次ID")]
        public long BatchId { get; set; }

        [Comment("商户ID")]
        public long MerchantId { get; set; }

        [Comment("品种ID")]
        public int SpeciesId { get; set; }

        [StringLength(128)]
        [Comment("品种名称")]
        public string SpeciesName { get; set; }

        [StringLength(8), Column(TypeName = "varchar(8)")]
        [Comment("等级(A/B/C)")]
        public string Grade { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Comment("采收数量")]
        public decimal Quantity { get; set; }

        [StringLength(16), Column(TypeName = "varchar(16)")]
        [Comment("数量单位(Stems/Kg)")]
        public string Unit { get; set; }

        [Comment("状态: 0=草稿, 1=已上架, 2=已下架")]
        public int Status { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("AI建议价格")]
        public decimal SuggestedPrice { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("实际上架价格")]
        public decimal ActualPrice { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("来源温室ID")]
        public string GreenhouseId { get; set; }

        [Comment("采收日期")]
        public DateTime HarvestDate { get; set; }

        [Comment("上架确认时间")]
        public DateTime? ListedDate { get; set; }

        [Comment("是否软删除")]
        public bool IsDeleted { get; set; }
    }
}
