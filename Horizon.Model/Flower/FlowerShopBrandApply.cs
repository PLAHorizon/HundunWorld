using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_ShopBrandApply")]
    [EntityStorage("Flower")]
    public class FlowerShopBrandApply : BaseIdentityAggregateRootModel<long>
    {
        [Comment("店铺ID")]
        public long ShopId { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("品牌名称")]
        public string BrandName { get; set; }

        [StringLength(512), Column(TypeName = "varchar(512)")]
        [Comment("证明材料")]
        public string ProofMaterial { get; set; }

        [Comment("审核状态")]
        public int AuditStatus { get; set; }

        [StringLength(256), Column(TypeName = "varchar(256)")]
        [Comment("审核备注")]
        public string AuditRemark { get; set; }
    }
}
