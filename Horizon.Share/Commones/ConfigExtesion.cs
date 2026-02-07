using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Share.Commones
{
    /// <summary>
    /// 配置扩展
    /// </summary>
    public class ConfigExtesion
    {
        /// <summary>
        /// 配置文件的名称
        /// </summary>
        public static string ConfigFileName = "appsettings.json";
        /// <summary>
        /// 配置信息
        /// </summary>
        public static IConfigurationRoot Config => new ConfigurationBuilder()
      .SetBasePath(Directory.GetCurrentDirectory())
      .AddJsonFile(ConfigFileName)
      .Build();
    }
}
