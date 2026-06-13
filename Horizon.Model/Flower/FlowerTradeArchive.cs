using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_TradeArchive")]
    [EntityStorage("Flower")]
    public class FlowerTradeArchive : BaseIdentityModel<long>
    {
        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("归档类型")]
        public string ArchiveType { get; set; }

        [Comment("关联ID")]
        public long? RelatedId { get; set; }

        [Column(TypeName = "varbinary(max)")]
        [Comment("归档数据")]
        public byte[] ArchiveData { get; set; }

        [Comment("归档时间")]
        public DateTime ArchivedAt { get; set; }
    }
}
