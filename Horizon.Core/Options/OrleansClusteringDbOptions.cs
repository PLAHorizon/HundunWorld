using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core.Options
{
    /// <summary>
    /// Orleans 集群数据库选项
    /// </summary>
    public class OrleansClusteringDbOptions
    {
        public DbInfo Npgsql { get; set; }
        public DbInfo SqlServer { get; set; }
        public DbInfo Mysql { get; set; }
        public DbInfo Oracle { get; set; }
        /// <summary>
        /// silo 通信地址 IPv4
        /// </summary>
        public string OrleansSiloHost { get; set; }
    }

    public class DbInfo
    {
        /// <summary>
        /// 连接字符串
        /// </summary>
        public string ConnectionString { get; set; }
        /// <summary>
        /// 数据库驱动
        /// </summary>
        public string Invariant { get; set; }
    }
}
