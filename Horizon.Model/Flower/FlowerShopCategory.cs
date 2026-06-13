using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_ShopCategory")]
    [EntityStorage("Flower")]
    public class FlowerShopCategory : BaseIdentityAggregateRootModel<long>
    {
        [Comment("店铺ID")]
        public long ShopId { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("分类名称")]
        public string Name { get; set; }

        [Comment("父分类ID")]
        public long ParentCategoryId { get; set; }

        [Comment("排序")]
        public long DisplaySequence { get; set; }

        [Comment("是否显示")]
        public bool IsShow { get; set; }
    }
}
