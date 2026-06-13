using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_BusinessCategory")]
    [EntityStorage("Flower")]
    public class FlowerBusinessCategory : BaseIdentityAggregateRootModel<long>
    {
        [Comment("店铺ID")]
        public long ShopId { get; set; }

        [Comment("类目ID")]
        public long CategoryId { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("佣金率")]
        public decimal CommissionRate { get; set; }

        [Comment("审核状态0=待审核1=已通过2=已拒绝")]
        public int AuditStatus { get; set; }

        [StringLength(256), Column(TypeName = "varchar(256)")]
        [Comment("审核备注")]
        public string AuditRemark { get; set; }
    }
}
