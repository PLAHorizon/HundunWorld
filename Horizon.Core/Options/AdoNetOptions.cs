using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core.Options
{
    /// <summary>
    /// Orleans 数据库连接选项
    /// </summary>
    public class AdoNetOptions
    {
        /// <summary>
        /// 数据库连结字符串
        /// </summary>
        public string ConnectionString { get; set; }

        /// <summary>
        /// 数据库连接提供程序(驱动程序集名称)
        /// </summary>
        public string Invariant { get; set; }
    }
}
