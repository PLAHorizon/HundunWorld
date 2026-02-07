using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model
{

    /// <summary>
    /// 数据分区数据库
    /// </summary>
    [Table("Basic_Sys_DDBs"), DataContract]
    [EntityStorage("Basic")]
    public class DistrictDatabase : BaseNoneModel<Guid>
    {
        /// <summary>
        /// 游戏分区数据库
        /// </summary>
        public DistrictDatabase()
        {
            Id = Guid.NewGuid();
            MODITIME = DateTime.Now;
        }
        /// <summary>
        /// 数据库类型
        /// </summary>
        [Column(TypeName = "int", Order = 4), TableDescription(TypeName = "int", Name = "DDbType", Order = "4", Description = "数据库类型")]
        [Comment("数据库类型")]
        public DataContextType DDbType { get; set; }
        /// <summary>
        /// 数据库IP
        /// </summary>
        [StringLength(39), Column(TypeName = "varchar", Order = 5), TableDescription(TypeName = "varchar(39)", Name = "IP", Order = "5", Description = "数据库IP")]
        [Comment("数据库IP")]
        public string IP { get; set; }
        /// <summary>
        /// 数据库端口
        /// </summary>
        [Column(TypeName = "int", Order = 6), TableDescription(TypeName = "int", Name = "Port", Order = "6", Description = "数据库端口")]
        [Comment("数据库端口")]
        public int Port { get; set; }
        /// <summary>
        /// 数据账号
        /// </summary>
        [StringLength(32), Column(TypeName = "varchar", Order = 7), TableDescription(TypeName = "varchar(32)", Name = "Account", Order = "7", Description = "数据账号")]
        [Comment("数据账号")]
        public string Account { get; set; }
        /// <summary>
        /// 数据密码
        /// </summary>
        [StringLength(256), Column(TypeName = "varchar", Order = 8), TableDescription(TypeName = "varchar(256)", Name = "Password", Order = "8", Description = "数据密码")]
        [Comment("数据密码")]
        public string Password { get; set; }

        /// <summary>
        ///数据应用类型
        /// </summary>
        [Comment("数据应用类型")]
        public AppType AppType { get; set; }
        /// <summary>
        /// 应用Id
        /// </summary>
        [Comment("应用Id")]
        public long APPId { get; set; }
        /// <summary>
        /// 区域Id
        /// </summary>
        [Comment("区域Id")]
        public long AreaId { get; set; }
        /// <summary>
        /// 服务Id
        /// </summary>
        [Comment("服务Id")]
        public long ServerId { get; set; }
        /// <summary>
        /// 备注
        /// </summary>
        [StringLength(256), Column(TypeName = "varchar", Order = 10), TableDescription(TypeName = "varchar(256)", Name = "COMMENTS", Order = "10", Description = "备注")]
        [Comment("备注")]
        public string COMMENTS { get; set; }
        /// <summary>
        /// 时间戳,分库建立时间
        /// </summary>
        [Column(TypeName = "datetime", Order = 11), TableDescription(TypeName = "datetime", Name = "MODITIME", Order = "11", Description = "时间戳")]
        [Comment("时间戳,分库建立时间")]
        public DateTime MODITIME { get; set; }
    }
}
