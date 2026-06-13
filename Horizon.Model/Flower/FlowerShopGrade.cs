using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_ShopGrade")]
    [EntityStorage("Flower")]
    public class FlowerShopGrade : BaseIdentityAggregateRootModel<long>, ISoftDeleted
    {
        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("等级名称")]
        public string Name { get; set; }

        [Comment("最大商品数")]
        public int ProductLimit { get; set; }

        [Comment("最大图片空间MB")]
        public int ImageLimit { get; set; }

        [Comment("最大模板数")]
        public int TemplateLimit { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("收费标准")]
        public decimal ChargeStandard { get; set; }

        [StringLength(256), Column(TypeName = "varchar(256)")]
        [Comment("备注")]
        public string Remark { get; set; }

        public bool IsDeleted { get; set; }
    }
}
