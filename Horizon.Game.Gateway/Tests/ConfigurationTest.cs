using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;
using Horizon.Core.Options;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// 配置测试程序
    /// </summary>
    public class ConfigurationTest
    {
        public static async Task<bool> TestOrleansClusteringDbOptions()
        {
            try
            {
                // 创建配置
                var configuration = new ConfigurationBuilder()
                    .AddJsonFile("appsettings.json", optional: false)
                    .Build();

                // 创建服务集合
                var services = new ServiceCollection();
                services.Configure<OrleansClusteringDbOptions>(configuration.GetSection("ClusteringSiloOptions"));

                // 构建服务提供者
                var serviceProvider = services.BuildServiceProvider();

                // 获取配置选项
                var options = serviceProvider.GetRequiredService<IOptions<OrleansClusteringDbOptions>>();
                var clusteringOptions = options.Value;

                // 验证配置
                if (clusteringOptions?.SqlServer?.ConnectionString != null)
                {
                    Console.WriteLine("✅ OrleansClusteringDbOptions 配置加载成功");
                    Console.WriteLine($"  - OrleansSiloHost: {clusteringOptions.OrleansSiloHost}");
                    Console.WriteLine($"  - SqlServer ConnectionString: {clusteringOptions.SqlServer.ConnectionString[..50]}...");
                    Console.WriteLine($"  - SqlServer Invariant: {clusteringOptions.SqlServer.Invariant}");
                    return true;
                }
                else
                {
                    Console.WriteLine("❌ OrleansClusteringDbOptions 配置加载失败：SqlServer 配置为空");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 配置测试失败：{ex.Message}");
                return false;
            }
        }

        public static async Task<bool> TestConfigurationBinding()
        {
            try
            {
                // 创建配置
                var configuration = new ConfigurationBuilder()
                    .AddJsonFile("appsettings.json", optional: false)
                    .Build();

                // 手动绑定配置（模拟 Program.cs 中的逻辑）
                var networkSettings = new OrleansClusteringDbOptions();
                configuration.GetSection("ClusteringSiloOptions").Bind(networkSettings);

                // 验证绑定结果
                if (networkSettings?.SqlServer?.ConnectionString != null)
                {
                    Console.WriteLine("✅ 手动配置绑定成功");
                    Console.WriteLine($"  - OrleansSiloHost: {networkSettings.OrleansSiloHost}");
                    Console.WriteLine($"  - SqlServer ConnectionString: {networkSettings.SqlServer.ConnectionString[..50]}...");
                    Console.WriteLine($"  - SqlServer Invariant: {networkSettings.SqlServer.Invariant}");
                    return true;
                }
                else
                {
                    Console.WriteLine("❌ 手动配置绑定失败：SqlServer 配置为空");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 配置绑定测试失败：{ex.Message}");
                return false;
            }
        }
    }
}
