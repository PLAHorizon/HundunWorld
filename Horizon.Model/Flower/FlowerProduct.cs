using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_Product")]
    [EntityStorage("Flower")]
    public class FlowerProduct : BaseIdentityAggregateRootModel<long>, ISoftDeleted
    {
        [Comment("商户ID")]
        public long MerchantId { get; set; }

        [Comment("品种ID")]
        public int SpeciesId { get; set; }

        [StringLength(128), Column(TypeName = "varchar(128)")]
        [Comment("商品名称")]
        public string ProductName { get; set; }

        [StringLength(512), Column(TypeName = "varchar(512)")]
        [Comment("商品描述")]
        public string Description { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("价格")]
        public decimal Price { get; set; }

        [Comment("库存")]
        public int Stock { get; set; }

        [StringLength(16), Column(TypeName = "varchar(16)")]
        [Comment("单位")]
        public string Unit { get; set; }

        [StringLength(1024), Column(TypeName = "varchar(1024)")]
        [Comment("图片")]
        public string Images { get; set; }

        [Comment("是否上架")]
        public bool IsActive { get; set; }

        [Comment("版本号")]
        public int Version { get; set; }

        [Comment("商品分类ID")]
        public long? CategoryId { get; set; }

        [Comment("商品类型ID")]
        public long? TypeId { get; set; }

        [Comment("品牌ID")]
        public long? BrandId { get; set; }

        [Comment("审核状态: 0=待审核, 1=审核通过, 2=审核拒绝")]
        public int AuditStatus { get; set; }

        [Comment("运费模板ID")]
        public long? FreightTemplateId { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("重量kg")]
        public decimal? Weight { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("体积m3")]
        public decimal? Volume { get; set; }

        [Comment("最大购买数")]
        public int MaxBuyCount { get; set; }

        [Comment("是否开启阶梯价")]
        public bool IsOpenLadder { get; set; }

        [Comment("商品类型: 0=实物, 1=虚拟")]
        public int ProductType { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("市场价")]
        public decimal? MarketPrice { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("最低销售价")]
        public decimal MinSalePrice { get; set; }

        [Comment("浏览量")]
        public long VisitCount { get; set; }

        [Comment("销量")]
        public long SaleCount { get; set; }

        [Comment("是否预售")]
        public bool IsPresale { get; set; }

        [Comment("预售发货日期")]
        public DateTime? PresaleDeliveryDate { get; set; }

        [Comment("关联种植批次ID")]
        public long? RelatedBatchId { get; set; }

        [Comment("是否已删除，true : 已删除，false : 未删除")]
        public bool IsDeleted { get; set; }
    }
}
