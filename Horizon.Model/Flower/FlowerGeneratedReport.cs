using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    /// <summary>
    /// 花卉生成报告
    /// </summary>
    [Table("Flower_GeneratedReport")]
    [EntityStorage("Flower")]
    public class FlowerGeneratedReport : BaseIdentityAggregateRootModel<long>
    {
        /// <summary>
        /// 报告类型
        /// </summary>
        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("报告类型")]
        public string ReportType { get; set; }

        /// <summary>
        /// 报告日期
        /// </summary>
        [Comment("报告日期")]
        public DateTime ReportDate { get; set; }

        /// <summary>
        /// 内容
        /// </summary>
        [Column(TypeName = "nvarchar(max)")]
        [Comment("内容")]
        public string Content { get; set; }

        /// <summary>
        /// 模型版本
        /// </summary>
        [StringLength(32), Column(TypeName = "varchar(32)")]
        [Comment("模型版本")]
        public string ModelVersion { get; set; }
    }
}
