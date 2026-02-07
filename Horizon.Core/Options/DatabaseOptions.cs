using Horizon.Core.Abstract;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core.Options
{
    /// <summary>
    /// 业务数据库选项
    /// </summary>
    public class DatabaseOptions
    {
        /// <summary>
        /// 基础数据库
        /// </summary>
        public DatabaseInfo Basic { get; set; }
        /// <summary>
        /// 游戏数据库
        /// </summary>
        public DatabaseInfo Game { get; set; }
        /// <summary>
        /// 文章类数据库
        /// </summary>
        public DatabaseInfo Article { get; set; }
        /// <summary>
        /// 支持点赞数据库
        /// </summary>
        public DatabaseInfo Support { get; set; }
        /// <summary>
        /// 星光数据库
        /// </summary>
        public DatabaseInfo Xingguang { get; set; }
    }

    /// <summary>
    /// 数据库信息
    /// </summary>
    public record DatabaseInfo
    {
        /// <summary>
        /// 数据库类型
        /// </summary>
        public DataContextType Type { get; set; }
        /// <summary>
        /// 数据库连接字符串
        /// </summary>
        public string ConnectionString { get; set; }
    }

}
