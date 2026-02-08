using Horizon.Core.Options;
using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Core.Security;
using Horizon.Game.Gateway.Configuration;
using Horizon.Game.Gateway.Network;
using Horizon.Game.Gateway.Services;
using Horizon.Game.Gateway.Monitoring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver.Core.Configuration;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Serialization;
using System;
using System.Reflection;
using System.Threading.Tasks;
using TouchSocket.Core;
using TouchSocket.Sockets;
using GatewayOptions = Orleans.Configuration.GatewayOptions;

namespace Horizon.Game.Gateway
{
    /// <summary>
    /// 混沌世界游戏网关主程序
    /// 负责处理客户端连接、消息路由、负载均衡等核心功能
    /// </summary>
    public class Program
    {
        private static ILogger<Program>? _logger;

        /// <summary>
        /// 程序入口点
        /// </summary>
        /// <param name="args">命令行参数</param>
        public static async Task Main(string[] args)
        {
            try
            {
                Console.WriteLine("=== 混沌世界游戏网关启动 ===");
                Console.WriteLine($"启动时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                // 创建并启动主机
                var host = CreateHostBuilder(args).Build();

                // 获取日志记录器
                _logger = host.Services.GetRequiredService<ILogger<Program>>();
                _logger.LogInformation("混沌世界游戏网关正在启动...");

                // 启动网关服务
                await host.RunAsync();
                while (true)
                {
                    Task.Delay(10);
                    Console.ReadLine();
                }
            }
            catch (Exception ex)
            {
                if (_logger != null)
                {
                    _logger.LogCritical(ex, "网关启动失败");
                }
                else
                {
                    Console.WriteLine($"网关启动失败: {ex.Message}");
                    Console.WriteLine($"堆栈跟踪: {ex.StackTrace}");
                }

                Environment.Exit(1);
            }
        }

        /// <summary>
        /// 创建主机构建器
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <returns>主机构建器</returns>
        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, config) =>
                {
                    // 配置文件设置
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                    config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json",
                                     optional: true, reloadOnChange: true);
                    config.AddEnvironmentVariables();
                    config.AddCommandLine(args);
                })
                .ConfigureLogging((context, logging) =>
                {
                    // 日志配置
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.AddJsonConsole(options =>
                    {
                        options.IncludeScopes = true;
                        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
                        options.UseUtcTimestamp = true;
                        options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions
                        {
                            Indented = false
                        };
                    });
                    logging.AddConfiguration(context.Configuration.GetSection("Logging"));

                })
                .ConfigureServices((context, services) =>
                {
                    // 注册配置
                    services.Configure<Configuration.GatewayOptions>(context.Configuration.GetSection("Gateway"));
                    services.Configure<NetworkOptions>(context.Configuration.GetSection("Network"));
                    services.Configure<OrleansOptions>(context.Configuration.GetSection("Orleans"));
                    services.Configure<OrleansClusteringDbOptions>(context.Configuration.GetSection("ClusteringSiloOptions"));

                    // 注册安全和认证服务
                    services.AddSingleton<AuthenticationValidator>();
                    services.AddSingleton<SecurityManager>();
                    
                    // 注册CorrelationId管理器（分布式追踪）
                    services.AddSingleton<Monitoring.CorrelationIdManager>();
                    
                    // 注册专门的消息处理器
                    services.AddScoped<AuthenticationHandler>();
                    services.AddScoped<CharacterManagementHandler>();
                    
                    // 注册核心服务
                    services.AddSingleton<IGatewayService, GatewayService>();
                    services.AddSingleton<IConnectionManager, ConnectionManager>();
                    services.AddSingleton<ILoadBalancer, LoadBalancer>();
                    services.AddSingleton<ISessionManager, SessionManager>();
                    services.AddSingleton<TouchSocket.Core.ILog>(_ => ConsoleLogger.Default);
                   
                    // 注册新服务
                    services.AddSingleton<IMessageSubscriptionService, MessageSubscriptionService>();
                    services.AddSingleton<RedisClusterStorageFactory>();
                    services.AddSingleton<RedisClusterStorage>(provider =>
                    {
                        var factory = provider.GetRequiredService<RedisClusterStorageFactory>();
                        return factory.CreateStorage();
                    });
                    services.AddSingleton<IClusterCoordinationService, ClusterCoordinationService>();
                    services.AddSingleton<HorizonMessageAdapter>();
                    services.AddAllMessageHandlers(Assembly.GetAssembly(typeof(MessageHandlerBase)));
                    // 注册网络服务
                    services.AddSingleton<ITcpService, TcpService>();
                    services.AddSingleton<GameNetworkServer>();

                    // 注册后台服务
                    services.AddHostedService<GatewayHostedService>();

                    // 健康检查
                    services.AddHealthChecks()
                        .AddCheck<GatewayHealthCheck>("gateway")
                        .AddCheck<NetworkHealthCheck>("network");

                    // OpenTelemetry监控（APM + Prometheus指标导出）
                    var prometheusPort = context.Configuration.GetValue<int>("Monitoring:PrometheusPort", 9465);
                    services.AddGatewayOpenTelemetry(prometheusPort: prometheusPort);
                })
                .UseOrleansClient((context, client) =>
                {
                    // 从应用程序配置中获取Orleans集群数据库配置
                    var networkSettings = new OrleansClusteringDbOptions();
                    context.Configuration.GetSection("ClusteringSiloOptions").Bind(networkSettings);

                    var settings = new OrleansOptions();
                    context.Configuration.GetSection("Orleans").Bind(settings);

                    // 调试：输出配置信息
                    Console.WriteLine($"[DEBUG] 配置加载结果:");
                    Console.WriteLine($"  - OrleansSiloHost: {networkSettings?.OrleansSiloHost ?? "NULL"}");
                    Console.WriteLine($"  - SqlServer ConnectionString: {networkSettings?.SqlServer?.ConnectionString?[..50] ?? "NULL"}...");
                    Console.WriteLine($"  - SqlServer Invariant: {networkSettings?.SqlServer?.Invariant ?? "NULL"}");
                    Console.WriteLine($"  - ClusterId: {settings?.ClusterId ?? "NULL"}");
                    Console.WriteLine($"  - ServiceId: {settings?.ServiceId ?? "NULL"}");

                    if (networkSettings?.SqlServer?.ConnectionString == null)
                    {
                        throw new InvalidOperationException("Orleans 集群数据库配置加载失败：SqlServer ConnectionString 为空");
                    }

                    //集群
                    client.UseAdoNetClustering(options =>
                    {
                        options.ConnectionString = networkSettings.SqlServer.ConnectionString;
                        options.Invariant = networkSettings.SqlServer.Invariant;
                    }).Configure<ClusterOptions>(options =>
                    {
                        options.ClusterId = settings.ClusterId;
                        options.ServiceId = settings.ServiceId;
                    })
                    // ******* 关键修复：客户端超时配置以解决30分钟超时问题 *******
                        .Configure<ClientMessagingOptions>(options =>
                        {
                            options.ResponseTimeout = TimeSpan.FromSeconds(30);  // 客户端响应超时30秒
                            options.ResponseTimeoutWithDebugger = TimeSpan.FromMinutes(5);  // 调试时超时
                        }).Configure<GatewayOptions>(options =>
                        {
                            options.PreferredGatewayIndex = 0;  // 首选网关索引
                            options.GatewayListRefreshPeriod = TimeSpan.FromMinutes(10);  // 网关列表刷新周期增加到10分钟
                        })
                        .Configure<ConnectionOptions>(options =>
                        {
                            options.OpenConnectionTimeout = TimeSpan.FromSeconds(10);  // 连接超时
                        })
                        .ConfigureServices(service =>
                        {
                            service.AddSerializer(serializerBuilder =>
                            {
                                serializerBuilder.AddNewtonsoftJsonSerializer(
                                    isSupported: type => type.Namespace.StartsWith("Horizon.Share"));
                            });
                        });

                }).ConfigureLogging(logging => logging.AddConsole())
                .UseConsoleLifetime();
    }
}

public static class ServiceCollectionExtensions
{
    
        
        /// <summary>
        /// 扩展方法：自动注册所有实现IMessageHandler接口的类型
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="assemblies">要扫描的程序集，如果为空则扫描当前程序集</param>
        public static void AddAllMessageHandlers(this IServiceCollection services, params Assembly[] assemblies)
        {
            if (assemblies == null || assemblies.Length == 0)
            {
                // 扫描所有已加载的程序集
                assemblies = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                    .ToArray();
            }

            var handlerTypes = assemblies
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => type.IsClass && !type.IsAbstract && typeof(IMessageHandler).IsAssignableFrom(type))
                .ToList();

            foreach (var handlerType in handlerTypes)
            {
                // 注册为IMessageHandler接口
                services.AddScoped(typeof(IMessageHandler), handlerType);
                // 注册为具体类型
                services.AddScoped(handlerType);

            }

            // 输出调试信息
            Console.WriteLine($"[DEBUG] 自动注册了 {handlerTypes.Count} 个消息处理器:");
            foreach (var handlerType in handlerTypes)
            {
                Console.WriteLine($"  - {handlerType.Name}");
            }
        }
    
}
