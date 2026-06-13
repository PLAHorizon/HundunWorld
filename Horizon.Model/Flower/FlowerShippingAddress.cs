using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_ShippingAddress")]
    [EntityStorage("Flower")]
    public class FlowerShippingAddress : BaseIdentityAggregateRootModel<long>
    {
        [Comment("用户ID")]
        public Guid UserId { get; set; }

        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("收货人姓名")]
        public string ShipTo { get; set; } = "";

        [StringLength(20), Column(TypeName = "varchar(20)")]
        [Comment("联系电话")]
        public string Phone { get; set; } = "";

        [Comment("省ID")]
        public int? ProvinceId { get; set; }

        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("省名称")]
        public string ProvinceName { get; set; } = "";

        [Comment("市ID")]
        public int? CityId { get; set; }

        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("市名称")]
        public string CityName { get; set; } = "";

        [Comment("区/县ID")]
        public int? DistrictId { get; set; }

        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("区/县名称")]
        public string DistrictName { get; set; } = "";

        [StringLength(256), Column(TypeName = "varchar(256)")]
        [Comment("详细地址")]
        public string Address { get; set; } = "";

        [Comment("是否默认地址")]
        public bool IsDefault { get; set; }

        [Comment("纬度")]
        public double? Latitude { get; set; }

        [Comment("经度")]
        public double? Longitude { get; set; }
    }
}
