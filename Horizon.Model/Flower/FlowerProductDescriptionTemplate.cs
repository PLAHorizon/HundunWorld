using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_ProductDescriptionTemplate")]
    [EntityStorage("Flower")]
    public class FlowerProductDescriptionTemplate : BaseIdentityAggregateRootModel<long>, ISoftDeleted
    {
        [Comment("店铺ID")]
        public long ShopId { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("模板名称")]
        public string TemplateName { get; set; }

        [Comment("顶部内容")]
        public string TopContent { get; set; }

        [Comment("底部内容")]
        public string BottomContent { get; set; }

        public bool IsDeleted { get; set; }
    }
}
