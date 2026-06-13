using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_DeviceGroup")]
    [EntityStorage("Flower")]
    public class FlowerDeviceGroup : BaseIdentityAggregateRootModel<long>
    {
        [StringLength(128)]
        [Comment("分组名称")]
        public string GroupName { get; set; }

        [StringLength(256)]
        [Comment("分组描述")]
        public string Description { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("温室ID")]
        public string GreenhouseId { get; set; }

        [Comment("是否软删除")]
        public bool IsDeleted { get; set; }
    }
}
