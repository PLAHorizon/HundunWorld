using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Hosting;
using Horizon.Orleans.Silo.Services;
using Orleans.Runtime.Messaging;

namespace Horizon.Orleans.Silo.Extensions
{
    /// <summary>
    /// Silo启动优化扩展
    /// </summary>
    public static class StartupOptimizationExtensions
    {
        /// <summary>
        /// 应用启动优化配置
        /// </summary>
        public static ISiloBuilder ApplyStartupOptimizations(this ISiloBuilder siloBuilder)
        {
            // 优化端点配置
            siloBuilder.Configure<EndpointOptions>(options =>
            {
                // 端点配置由系统自动处理
            });

            // 优化成员表选项
            siloBuilder.Configure<ClusterMembershipOptions>(options =>
            {
                options.NumMissedProbesLimit = 3;
                options.ProbeTimeout = TimeSpan.FromSeconds(5);
                options.NumProbedSilos = 3;
                options.NumVotesForDeathDeclaration = 2;
            });

            // 优化消息配置
            siloBuilder.Configure<MessagingOptions>(options =>
            {
                options.ResponseTimeout = TimeSpan.FromSeconds(30);
                options.DropExpiredMessages = true;
            });

            // 优化Silo消息配置
            siloBuilder.Configure<SiloMessagingOptions>(options =>
            {
                options.ResponseTimeout = TimeSpan.FromSeconds(30);
                options.MaxForwardCount = 2;
            });

            // 优化连接选项
            siloBuilder.Configure<ConnectionOptions>(options =>
            {
                
                options.OpenConnectionTimeout = TimeSpan.FromSeconds(5);
                options.ProtocolVersion = NetworkProtocolVersion.Version1;
            });

            return siloBuilder;
        }

        /// <summary>
        /// 配置延迟加载的服务
        /// </summary>
        public static IServiceCollection AddLazyServices(this IServiceCollection services)
        {
            // 将一些非关键服务配置为延迟加载
            services.AddSingleton<Lazy<StartupReportService>>(provider =>
            {
                return new Lazy<StartupReportService>(() => 
                {
                    var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StartupReportService>>();
                    var configuration = provider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
                    var environment = provider.GetRequiredService<IHostEnvironment>();
                    return new StartupReportService(logger, configuration, environment);
                });
            });

            return services;
        }
    }
}
