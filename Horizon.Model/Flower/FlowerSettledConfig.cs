using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_SettledConfig")]
    [EntityStorage("Flower")]
    public class FlowerSettledConfig : BaseIdentityAggregateRootModel<long>
    {
        [Comment("商家类型0=企业1=个体2=均可")]
        public int BusinessType { get; set; }

        [Comment("结算账户类型0=银行1=微信2=均支持")]
        public int SettlementAccountType { get; set; }

        [Comment("试用天数")]
        public int TrialDays { get; set; }

        [Comment("地址城市是否必填")]
        public bool IsCity { get; set; }

        [Comment("人数是否必填")]
        public bool IsPeopleNumber { get; set; }

        [Comment("详细地址是否必填")]
        public bool IsAddress { get; set; }

        [Comment("营业执照号是否必填")]
        public bool IsBusinessLicenseCode { get; set; }

        [Comment("经营范围是否必填")]
        public bool IsBusinessScope { get; set; }

        [Comment("营业执照是否必填")]
        public bool IsBusinessLicense { get; set; }
    }
}
