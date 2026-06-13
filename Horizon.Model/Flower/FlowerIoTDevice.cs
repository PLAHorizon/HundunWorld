using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    [Table("Flower_IoTDevice")]
    [EntityStorage("Flower")]
    public class FlowerIoTDevice : BaseIdentityAggregateRootModel<long>
    {
        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("设备唯一标识")]
        public string DeviceCode { get; set; }

        [StringLength(128)]
        [Comment("设备名称")]
        public string DeviceName { get; set; }

        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("设备类型(Sensor/Gateway/Controller)")]
        public string DeviceType { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("所属温室ID")]
        public string GreenhouseId { get; set; }

        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("所属分组ID")]
        public string GroupId { get; set; }

        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("通信协议(MQTT/Modbus/HTTP)")]
        public string Protocol { get; set; }

        [StringLength(128), Column(TypeName = "varchar(128)")]
        [Comment("MQTT Topic")]
        public string MqttTopic { get; set; } = "";

        [StringLength(128), Column(TypeName = "varchar(128)")]
        [Comment("接入API Key")]
        public string ApiKey { get; set; } = "";

        [StringLength(16), Column(TypeName = "varchar(16)")]
        [Comment("在线状态(Online/Offline)")]
        public string OnlineStatus { get; set; } = "Offline";

        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("固件版本")]
        public string FirmwareVersion { get; set; } = "";

        [Comment("最后心跳时间")]
        public DateTime? LastHeartbeatTime { get; set; }

        [Comment("是否启用")]
        public bool IsEnabled { get; set; } = true;

        [StringLength(16), Column(TypeName = "varchar(16)")]
        [Comment("绑定状态(Unbound/Bound/Disabled)")]
        public string BindingStatus { get; set; } = "Unbound";

        [Comment("绑定时间")]
        public DateTime? BoundAt { get; set; }

        [Comment("是否软删除")]
        public bool IsDeleted { get; set; }

        public string Location { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string Model { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public double? BatteryLevel { get; set; }
        public double? SignalStrength { get; set; }
        public string SensorCapabilities { get; set; } = "";
        public DateTime? InstallDate { get; set; }
        public string Remark { get; set; } = "";

        [Column(TypeName = "nvarchar(max)")]
        [Comment("设备孪生期望属性(JSON)")]
        public string TwinDesiredProperties { get; set; } = "{}";

        [Column(TypeName = "nvarchar(max)")]
        [Comment("设备孪生报告属性(JSON)")]
        public string TwinReportedProperties { get; set; } = "{}";
    }
}
