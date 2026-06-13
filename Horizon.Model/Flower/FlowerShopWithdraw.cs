using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_ShopWithdraw")]
    [EntityStorage("Flower")]
    public class FlowerShopWithdraw : BaseIdentityAggregateRootModel<long>
    {
        [Comment("店铺ID")]
        public long ShopId { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Comment("提现金额")]
        public decimal Amount { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("银行名称")]
        public string BankName { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("银行账号")]
        public string AccountNo { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("账户名")]
        public string AccountName { get; set; }

        [Comment("状态0=待审核1=已通过2=已拒绝3=已打款")]
        public int Status { get; set; }

        [StringLength(256), Column(TypeName = "varchar(256)")]
        [Comment("审核备注")]
        public string AuditRemark { get; set; }

        [Comment("创建时间")]
        public DateTime CreatedAt { get; set; }

        [Comment("审核时间")]
        public DateTime? AuditedAt { get; set; }

        [Comment("打款时间")]
        public DateTime? PaidAt { get; set; }
    }
}
