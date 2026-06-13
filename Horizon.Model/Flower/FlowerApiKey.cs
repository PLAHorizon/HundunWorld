using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_ApiKey")]
    [EntityStorage("Flower")]
    public class FlowerApiKey : BaseIdentityAggregateRootModel<long>, ISoftDeleted
    {
        [StringLength(128), Column(TypeName = "varchar(128)")]
        [Comment("API Key")]
        public string ApiKey { get; set; }

        [StringLength(64)]
        [Comment("密钥名称")]
        public string Name { get; set; }

        [Comment("所属用户PassportId")]
        public long OwnerPassportId { get; set; }

        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("套餐类型")]
        public string Plan { get; set; }

        [Comment("是否启用")]
        public bool IsEnabled { get; set; }

        [Comment("总调用次数")]
        public long TotalCallCount { get; set; }

        [Comment("最后调用时间")]
        public DateTime? LastCallTime { get; set; }

        [Comment("过期时间")]
        public DateTime? ExpiresAt { get; set; }

        [Comment("是否已删除")]
        public bool IsDeleted { get; set; }
    }
}
