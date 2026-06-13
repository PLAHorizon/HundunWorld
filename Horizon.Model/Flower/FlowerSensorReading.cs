using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    /// <summary>
    /// 传感器读数
    /// </summary>
    [Table("Flower_SensorReading")]
    [EntityStorage("Flower")]
    public class FlowerSensorReading : BaseIdentityModel<long>
    {
        /// <summary>
        /// 设备ID
        /// </summary>
        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("设备ID")]
        public string DeviceId { get; set; }

        /// <summary>
        /// 温室ID
        /// </summary>
        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("温室ID")]
        public string GreenhouseId { get; set; }

        /// <summary>
        /// 温度
        /// </summary>
        [Comment("温度")]
        public double Temperature { get; set; }

        /// <summary>
        /// 湿度
        /// </summary>
        [Comment("湿度")]
        public double Humidity { get; set; }

        /// <summary>
        /// 光照强度
        /// </summary>
        [Comment("光照强度")]
        public double LightIntensity { get; set; }

        /// <summary>
        /// 二氧化碳浓度
        /// </summary>
        [Comment("二氧化碳浓度")]
        public double Co2Level { get; set; }

        /// <summary>
        /// 土壤湿度
        /// </summary>
        [Comment("土壤湿度")]
        public double SoilMoisture { get; set; }

        /// <summary>
        /// 读数时间
        /// </summary>
        [Comment("读数时间")]
        public DateTime ReadingTime { get; set; }

        /// <summary>
        /// 数据质量标识(Normal/Abnormal/Missing)
        /// </summary>
        [StringLength(16), Column(TypeName = "varchar(16)")]
        [Comment("数据质量标识(Normal/Abnormal/Missing)")]
        public string DataQuality { get; set; } = "Normal";

        /// <summary>
        /// 数据来源(Device/Manual)
        /// </summary>
        [StringLength(16), Column(TypeName = "varchar(16)")]
        [Comment("数据来源(Device/Manual)")]
        public string DataSource { get; set; } = "Device";

        /// <summary>
        /// 通行证ID
        /// </summary>
        [StringLength(64), Column(TypeName = "varchar(64)")]
        [Comment("通行证ID")]
        public string Passport { get; set; }

        /// <summary>
        /// 关联批次ID
        /// </summary>
        [Comment("关联批次ID")]
        public long? BatchId { get; set; }
    }
}
