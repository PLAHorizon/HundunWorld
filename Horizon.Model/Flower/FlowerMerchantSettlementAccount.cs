using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_MerchantSettlementAccount")]
    [EntityStorage("Flower")]
    public class FlowerMerchantSettlementAccount : BaseIdentityAggregateRootModel<long>
    {
        [Comment("商户ID")]
        public long MerchantId { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("银行名称")]
        public string BankName { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("银行账号")]
        public string AccountNo { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("账户名")]
        public string AccountName { get; set; }

        [Comment("是否默认")]
        public bool IsDefault { get; set; }
    }
}
