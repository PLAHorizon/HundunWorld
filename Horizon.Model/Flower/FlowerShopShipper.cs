using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_ShopShipper")]
    [EntityStorage("Flower")]
    public class FlowerShopShipper : BaseIdentityAggregateRootModel<long>, ISoftDeleted
    {
        [Comment("店铺ID")]
        public long ShopId { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("发货点名称")]
        public string ShipperTag { get; set; }

        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("发货人姓名")]
        public string ShipperName { get; set; }

        [Comment("地区ID")]
        public int RegionId { get; set; }

        [StringLength(256), Column(TypeName = "varchar(256)")]
        [Comment("详细地址")]
        public string Address { get; set; }

        [StringLength(20), Column(TypeName = "varchar(20)")]
        [Comment("电话")]
        public string TelPhone { get; set; }

        [Comment("是否默认发货点")]
        public bool IsDefaultSendGoods { get; set; }

        [Comment("经度")]
        public float? Longitude { get; set; }

        [Comment("纬度")]
        public float? Latitude { get; set; }

        public bool IsDeleted { get; set; }
    }
}
