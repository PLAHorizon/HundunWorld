using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_PaymentStatusChangeLog")]
    [EntityStorage("Flower")]
    public class FlowerPaymentStatusChangeLog : BaseIdentityModel<long>
    {
        [Comment("交易ID")]
        public long TransactionId { get; set; }

        [Comment("变更前状态")]
        public int BeforeStatus { get; set; }

        [Comment("变更后状态")]
        public int AfterStatus { get; set; }

        [StringLength(128), Column(TypeName = "varchar(128)")]
        [Comment("通知ID")]
        public string NotifyId { get; set; } = "";

        [Column(TypeName = "nvarchar(max)")]
        [Comment("渠道响应")]
        public string ChannelResponse { get; set; }

        [Comment("变更时间")]
        public DateTime ChangedAt { get; set; }
    }
}
