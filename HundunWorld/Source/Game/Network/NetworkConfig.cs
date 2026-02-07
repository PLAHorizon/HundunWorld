using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 网络配置
    /// </summary>
    public class NetworkConfig
    {
        /// <summary>
        /// 网关列表
        /// </summary>
        public List<GatewayConfig> GatewayList { get; set; } = new List<GatewayConfig>();

        /// <summary>
        /// 是否自动连接
        /// </summary>
        public bool AutoConnect { get; set; } = true;

        /// <summary>
        /// 重连间隔（毫秒）
        /// </summary>
        public int ReconnectInterval { get; set; } = 5000;
    }

    /// <summary>
    /// 网关配置
    /// </summary>
    public class GatewayConfig
    {
        /// <summary>
        /// IP地址
        /// </summary>
        public string IP { get; set; } = string.Empty;

        /// <summary>
        /// 端口号
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// 区域
        /// </summary>
        public string Region { get; set; } = string.Empty;
    }

    /// <summary>
    /// 网络配置管理器
    /// </summary>
    public static class NetworkConfigManager
    {
        private const string ConfigFileName = "network_config.json";
        private static NetworkConfig _config;

        /// <summary>
        /// 加载网络配置
        /// </summary>
        /// <returns>网络配置</returns>
        public static NetworkConfig LoadConfig()
        {
            if (_config != null)
                return _config;

            try
            {
                // 获取配置文件路径
                string configPath = GetConfigPath();
                
                // 检查配置文件是否存在
                if (!File.Exists(configPath))
                {
                    // 创建默认配置
                    _config = CreateDefaultConfig();
                    SaveConfig(_config, configPath);
                    return _config;
                }

                // 读取配置文件
                var json = File.ReadAllText(configPath);
                _config = JsonSerializer.Deserialize<NetworkConfig>(json);
                return _config;
            }
            catch
            {
                // 如果加载失败，返回默认配置
                _config = CreateDefaultConfig();
                return _config;
            }
        }

        /// <summary>
        /// 保存网络配置
        /// </summary>
        /// <param name="config">网络配置</param>
        public static void SaveConfig(NetworkConfig config)
        {
            string configPath = GetConfigPath();
            SaveConfig(config, configPath);
        }

        /// <summary>
        /// 保存网络配置到指定路径
        /// </summary>
        /// <param name="config">网络配置</param>
        /// <param name="configPath">配置文件路径</param>
        private static void SaveConfig(NetworkConfig config, string configPath)
        {
            try
            {
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);
            }
            catch
            {
                // 忽略保存错误
            }
        }

        /// <summary>
        /// 获取配置文件路径
        /// </summary>
        /// <returns>配置文件路径</returns>
        private static string GetConfigPath()
        {
            // 首先检查Config目录
            string configDir = Path.Combine(Directory.GetCurrentDirectory(), "Config");
            if (Directory.Exists(configDir))
            {
                string configPath = Path.Combine(configDir, ConfigFileName);
                return configPath;
            }
            
            // 如果Config目录不存在，则使用当前目录
            return Path.Combine(Directory.GetCurrentDirectory(), ConfigFileName);
        }

        /// <summary>
        /// 创建默认配置
        /// </summary>
        /// <returns>默认网络配置</returns>
        private static NetworkConfig CreateDefaultConfig()
        {
            return new NetworkConfig
            {
                GatewayList = new List<GatewayConfig>
                {
                    new GatewayConfig { IP = "192.168.1.78", Port = 7789, Region = "华东" },
                    new GatewayConfig { IP = "192.168.2.78", Port = 7789, Region = "华南" },
                    new GatewayConfig { IP = "192.168.3.78", Port = 7789, Region = "华北" }
                },
                AutoConnect = true,
                ReconnectInterval = 5000
            };
        }

        /// <summary>
        /// 将网关配置转换为网关信息
        /// </summary>
        /// <param name="gatewayConfigs">网关配置列表</param>
        /// <returns>网关信息列表</returns>
        public static List<GatewayInfo> ConvertToGatewayInfo(List<GatewayConfig> gatewayConfigs)
        {
            var gatewayInfos = new List<GatewayInfo>();
            
            foreach (var config in gatewayConfigs)
            {
                gatewayInfos.Add(new GatewayInfo
                {
                    IP = config.IP,
                    Port = config.Port,
                    Region = config.Region
                });
            }
            
            return gatewayInfos;
        }
    }
}
