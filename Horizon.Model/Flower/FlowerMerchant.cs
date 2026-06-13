using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_Merchant")]
    [EntityStorage("Flower")]
    public class FlowerMerchant : BaseIdentityAggregateRootModel<long>, ISoftDeleted
    {
        [Comment("用户ID")]
        public Guid UserId { get; set; }

        [Comment("商户类型")]
        public int MerchantType { get; set; }

        [StringLength(128), Column(TypeName = "varchar(128)")]
        [Comment("店铺名称")]
        public string ShopName { get; set; }

        [StringLength(512), Column(TypeName = "varchar(512)")]
        [Comment("店铺描述")]
        public string ShopDescription { get; set; }

        [StringLength(20), Column(TypeName = "varchar(20)")]
        [Comment("联系电话")]
        public string ContactPhone { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("营业执照")]
        public string BusinessLicense { get; set; }

        [Comment("是否认证")]
        public bool IsVerified { get; set; }

        [Comment("认证时间")]
        public DateTime? VerifiedAt { get; set; }

        [Comment("店铺等级ID")]
        public long? GradeId { get; set; }

        [Comment("审核状态: 0=不可用, 1=待审核, 2=审核通过, 3=审核拒绝, 4=已开启, 5=已冻结, 6=已过期")]
        public int AuditStatus { get; set; }

        [Comment("入驻步骤: 0=协议, 1=公司信息, 2=银行账户, 3=店铺信息, 4=完成")]
        public int Stage { get; set; }

        [Comment("到期时间")]
        public DateTime? EndDate { get; set; }

        [StringLength(128), Column(TypeName = "varchar(128)")]
        [Comment("公司名称")]
        public string CompanyName { get; set; }

        [Comment("公司地区ID")]
        public int? CompanyRegionId { get; set; }

        [StringLength(256), Column(TypeName = "varchar(256)")]
        [Comment("公司地址")]
        public string CompanyAddress { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("营业执照号")]
        public string BusinessLicenceNumber { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("银行开户名")]
        public string BankAccountName { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("银行账号")]
        public string BankAccountNumber { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("开户银行")]
        public string BankName { get; set; }

        [Comment("开户行地区ID")]
        public int? BankRegionId { get; set; }

        [StringLength(256), Column(TypeName = "varchar(256)")]
        [Comment("拒绝原因")]
        public string RefuseReason { get; set; }

        [StringLength(512), Column(TypeName = "varchar(512)")]
        [Comment("经营类目JSON")]
        public string BusinessCategory { get; set; }

        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("身份证号")]
        public string IDCard { get; set; }

        [StringLength(256), Column(TypeName = "varchar(256)")]
        [Comment("身份证正面照")]
        public string IDCardUrl { get; set; }

        [StringLength(256), Column(TypeName = "varchar(256)")]
        [Comment("身份证反面照")]
        public string IDCardUrl2 { get; set; }

        [Comment("是否已删除，true : 已删除，false : 未删除")]
        public bool IsDeleted { get; set; }
    }
}
