using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.Serialization;
using System.Text;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model
{
    /// <summary>
    /// 应用
    /// </summary>
    [Table("Basic_Sys_Applications"), DataContract]
    [EntityStorage("Basic")]
    public class Apps : BaseIdentityModel<long>
    {
        public Apps()
        {
            Id = default;
        }
        /// <summary>
        /// 应用类型
        /// </summary>
        [Comment("应用类型")]
        public AppType AppType { get; set; }
        /// <summary>
        /// 应用名称
        /// </summary>
        [Comment("应用名称")]
        public string Name { get; set; }
        /// <summary>
        /// 应用简述
        /// </summary>
        [Comment("应用简述")]
        public string Description { get; set; }
        /// <summary>
        /// 应用负责人
        /// </summary>
        [Comment("应用负责人")]
        public string Contacts { get; set; }
        /// <summary>
        /// 团队
        /// </summary>
        [Comment("团队")]
        public string Team { get; set; }
        /// <summary>
        /// 应用网站首页地址
        /// </summary>
        [Comment("应用网站首页地址")]
        public string Home { get; set; }
        /// <summary>
        /// 应用Logo
        /// </summary>
        [Comment("应用Logo")]
        public string Logo { get; set; }
        /// <summary>
        /// 应用上线时间
        /// </summary>
        [Comment("应用上线时间")]
        public DateTime? Date { get; set; }
        /// <summary>
        /// 应用下线时间
        /// </summary>
        [Comment("应用下线时间")]
        public DateTime? OverDate { get; set; }
    }
}
