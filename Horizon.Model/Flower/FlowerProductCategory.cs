using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_ProductCategory")]
    [EntityStorage("Flower")]
    public class FlowerProductCategory : BaseIdentityAggregateRootModel<long>, ISoftDeleted
    {
        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("分类名称")]
        public string Name { get; set; }

        [Comment("分类深度1/2/3")]
        public int Depth { get; set; }

        [StringLength(128), Column(TypeName = "varchar(128)")]
        [Comment("分类路径")]
        public string Path { get; set; }

        [Comment("父分类ID")]
        public long ParentCategoryId { get; set; }

        [Comment("排序")]
        public long DisplaySequence { get; set; }

        [StringLength(256), Column(TypeName = "varchar(256)")]
        [Comment("图标")]
        public string Icon { get; set; }

        [StringLength(256), Column(TypeName = "varchar(256)")]
        [Comment("图片")]
        public string Image { get; set; }

        public bool IsDeleted { get; set; }
    }
}
