using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_ProductSKU")]
    [EntityStorage("Flower")]
    public class FlowerProductSKU : BaseIdentityAggregateRootModel<long>, ISoftDeleted
    {
        [Comment("商品ID")]
        public long ProductId { get; set; }

        [StringLength(128), Column(TypeName = "varchar(128)")]
        [Comment("SKU编码")]
        public string SkuCode { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("颜色")]
        public string Color { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("尺码")]
        public string Size { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("版本")]
        public string Version { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("销售价")]
        public decimal SalePrice { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("成本价")]
        public decimal CostPrice { get; set; }

        [Comment("库存")]
        public long Stock { get; set; }

        [Comment("安全库存")]
        public long? SafeStock { get; set; }

        [StringLength(512), Column(TypeName = "varchar(512)")]
        [Comment("展示图片")]
        public string ShowPic { get; set; }

        public bool IsDeleted { get; set; }
    }
}
