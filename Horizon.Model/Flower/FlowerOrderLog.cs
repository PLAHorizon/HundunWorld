using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_OrderLog")]
    [EntityStorage("Flower")]
    public class FlowerOrderLog : BaseIdentityModel<long>
    {
        [Comment("订单ID")]
        public long OrderId { get; set; }

        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("操作类型")]
        public string ActionType { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        [Comment("操作前快照")]
        public string BeforeSnapshot { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        [Comment("操作后快照")]
        public string AfterSnapshot { get; set; }

        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("操作人通行证")]
        public string OperatorPassport { get; set; }

        [Comment("操作时间")]
        public DateTime OperatedAt { get; set; }
    }
}
