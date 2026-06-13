using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_FreightTemplate")]
    [EntityStorage("Flower")]
    public class FlowerFreightTemplate : BaseIdentityAggregateRootModel<long>, ISoftDeleted
    {
        [Comment("商户ID")]
        public long MerchantId { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("模板名称")]
        public string Name { get; set; }

        [Comment("计价方式: 0=按件数, 1=按重量, 2=按体积")]
        public int ValuationMethod { get; set; }

        [Comment("是否包邮")]
        public bool IsFree { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("首件/首重/首体积")]
        public decimal FirstUnit { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("首费")]
        public decimal FirstPrice { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("续件/续重/续体积")]
        public decimal ContinueUnit { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("续费")]
        public decimal ContinuePrice { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("包邮条件金额")]
        public decimal? FreeConditionAmount { get; set; }

        [StringLength(512), Column(TypeName = "varchar(512)")]
        [Comment("地区规则JSON")]
        public string AreaRules { get; set; }

        public bool IsDeleted { get; set; }
    }
}
