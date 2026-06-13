using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_LogisticsTrack")]
    [EntityStorage("Flower")]
    public class FlowerLogisticsTrack : BaseIdentityModel<long>
    {
        [Comment("订单ID")]
        public long OrderId { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("物流公司")]
        public string ExpressCompanyName { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("运单号")]
        public string ShipOrderNumber { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        [Comment("物流轨迹数据JSON")]
        public string TrackData { get; set; }

        [Comment("最后查询时间")]
        public DateTime? LastQueriedAt { get; set; }

        [Comment("物流状态: 0=无轨迹, 1=已揽收, 2=运输中, 3=派送中, 4=已签收, 5=异常")]
        public int LogisticsStatus { get; set; }

        [Comment("是否退货物流")]
        public bool IsReturn { get; set; }

        [Comment("关联退款单ID(退货物流时)")]
        public long? RefundId { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("始发城市")]
        public string OriginCity { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("目的城市")]
        public string DestinationCity { get; set; }

        [StringLength(128)]
        [Comment("当前位置描述")]
        public string CurrentLocation { get; set; }
    }
}
