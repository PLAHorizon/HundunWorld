using Horizon.Core.Options;
using Horizon.Core.Security;
using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Core.Security;
using Horizon.Game.Gateway.Configuration;
using Horizon.Game.Gateway.Network;
using Horizon.Game.Gateway.Services;
using Horizon.Game.Gateway.Monitoring;
using Horizon.Core.Monitoring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver.Core.Configuration;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Serialization;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TouchSocket.Core;
using TouchSocket.Sockets;
using GatewayOptions = Orleans.Configuration.GatewayOptions;
using Horizon.Orleans.Grains.World;
using Horizon.Orleans.Interface.World;
using Horizon.Strategy.Storage.Redis;
using StackExchange.Redis;

[assembly: Orleans.ApplicationPart("Horizon.Orleans.Grains")]
[assembly: Orleans.ApplicationPart("Horizon.Orleans.Interface")]

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
            // 确保控制台能正确输出简体中文
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // 强制加载所有 grain 实现程序集，确保 Orleans 10 运行时在初始化 IClusterClient 之前
            // 能扫描到 [assembly: ApplicationPartAttribute] 标记并发现所有 grain 实现。
            // .NET 默认按需加载程序集，若不强制加载，GetGrain<T> 会报 "Could not find an implementation"。
            _ = typeof(Horizon.Orleans.Grains.World.ZoneShardGrain).Assembly;
            _ = typeof(Horizon.Orleans.Grains.CharacterGrain).Assembly;
            _ = typeof(Horizon.Orleans.Grains.PassportGrain).Assembly;
            _ = typeof(Horizon.Orleans.Grains.IMUserGrain).Assembly;
            _ = typeof(Horizon.Orleans.Grains.GameServerGrain).Assembly;

            try
            {
                // 创建并启动主机
                var host = CreateHostBuilder(args).Build();

                // 获取日志记录器
                _logger = host.Services.GetRequiredService<ILogger<Program>>();
                _logger.LogInformation("混沌世界游戏网关正在启动...");

                // 启动网关服务
                await host.RunAsync();
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

        private static void ApplyGatewayConfiguration(IConfiguration configuration, Configuration.GatewayOptions options)
        {
            options.ClusterId = ResolveGatewayClusterId(configuration, options.ClusterId);
            options.RedisConnectionString = ResolveGatewayRedisConnectionString(configuration, options.RedisConnectionString);
        }

        private static string ResolveGatewayClusterId(IConfiguration configuration, string currentValue)
        {
            var configuredGatewayClusterId = configuration["Gateway:ClusterId"];
            if (!string.IsNullOrWhiteSpace(configuredGatewayClusterId))
            {
                return configuredGatewayClusterId;
            }

            var configuredOrleansClusterId = configuration["Orleans:ClusterId"];
            if (!string.IsNullOrWhiteSpace(configuredOrleansClusterId))
            {
                return configuredOrleansClusterId;
            }

            var configuredClusterOptionsClusterId = configuration["ClusterOptions:ClusterId"];
            if (!string.IsNullOrWhiteSpace(configuredClusterOptionsClusterId))
            {
                return configuredClusterOptionsClusterId;
            }

            return currentValue;
        }

        private static string ResolveGatewayRedisConnectionString(IConfiguration configuration, string currentValue)
        {
            var configuredGatewayConnectionString = configuration["Gateway:RedisConnectionString"];
            if (!string.IsNullOrWhiteSpace(configuredGatewayConnectionString))
            {
                return configuredGatewayConnectionString;
            }

            var primaryRedisMaster = configuration.GetSection("DataBase:RedisMasters").GetChildren().FirstOrDefault();
            if (primaryRedisMaster == null)
            {
                return currentValue;
            }

            var host = primaryRedisMaster["Host"];
            var port = primaryRedisMaster["Port"];
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(port))
            {
                return currentValue;
            }

            var password = primaryRedisMaster["Password"];
            return string.IsNullOrWhiteSpace(password)
                ? $"{host}:{port}"
                : $"{host}:{port},password={password}";
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
                    // 设置基础路径为执行文件所在目录，解决工作目录不一致导致的配置文件找不到问题
                    config.SetBasePath(AppContext.BaseDirectory);
                    
                    // 配置文件设置
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                    config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json",
                                     optional: true, reloadOnChange: true);
                    config.AddEnvironmentVariables();
                    config.AddCommandLine(args);
                })
                .ConfigureLogging((context, logging) =>
                {
                    // 日志配置：
                    // - 控制台：简单格式（只显示格式化后的 Message，不暴露 State/{OriginalFormat} 占位符），
                    //           通过 appsettings 的 "Logging:Console:LogLevel" 过滤为只显示主要信息
                    // - 文件：全量日志落盘（Debug+），按日切割 + 大小滚动，便于事后排查
                    // - Seq：可选的日志聚合（开发环境）
                    logging.ClearProviders();
                    logging.AddConfiguration(context.Configuration.GetSection("Logging"));

                    // 控制台使用 Simple 格式器，避免 JsonConsole 暴露 {OriginalFormat} 原始模板与 State 字典
                    logging.AddSimpleConsole(options =>
                    {
                        options.SingleLine = true;
                        options.IncludeScopes = false;
                        options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
                        options.UseUtcTimestamp = false;
                    });

                    // 文件日志：全量落盘，控制台只显示主要信息时文件仍保留完整日志
                    logging.AddFile(context.Configuration, "HundunWorld-Gateway");

                    // 开发环境启用Seq日志聚合（Phase 2.2）
                    logging.AddSeqIfEnabled(context.Configuration, "HundunWorld.Gateway");
                })
                .ConfigureServices((context, services) =>
                {
                    // 注册配置
                    services.AddOptions<Configuration.GatewayOptions>()
                        .Bind(context.Configuration.GetSection("Gateway"));
                    services.PostConfigure<Configuration.GatewayOptions>(options => ApplyGatewayConfiguration(context.Configuration, options));
                    services.Configure<NetworkOptions>(context.Configuration.GetSection("Network"));
                    services.Configure<OrleansOptions>(context.Configuration.GetSection("Orleans"));
                    services.Configure<OrleansClusteringDbOptions>(context.Configuration.GetSection("ClusteringSiloOptions"));

                    // 注册安全和认证服务
                    services.AddSingleton<AuthenticationValidator>();
                    services.AddSingleton<SecurityManager>();
                    
                    // 注册用户鉴权令牌提供器
                    services.AddSingleton<UserAuthTokenProvider>(provider =>
                    {
                        var config = provider.GetRequiredService<IConfiguration>();
                        var logger = provider.GetRequiredService<ILogger<UserAuthTokenProvider>>();
                        var secretKey = config["Security:AuthTokenSecret"];
                        if (string.IsNullOrWhiteSpace(secretKey))
                        {
                            logger.LogError("未配置 Security:AuthTokenSecret，生产环境必须配置自定义密钥！当前使用临时密钥，仅限开发环境使用。");
                            secretKey = $"HundunWorld-Dev-Only-{Environment.MachineName}";
                        }
                        return new UserAuthTokenProvider(secretKey, logger);
                    });
                    
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

                    // 注册Core服务
                    services.AddSingleton<Horizon.Game.Core.Interfaces.IArenaService, Horizon.Game.Core.Services.ArenaService>();
                    services.AddSingleton<Horizon.Game.Core.Interfaces.ICrossServerService, Horizon.Game.Core.Services.CrossServerService>();
                   
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

                    // 注册角色指纹服务（基于 Redis，防止同一角色同时在线）
                    services.AddSingleton<Horizon.Game.Core.Interfaces.ICharacterFingerprintService>(provider =>
                    {
                        var gatewayOpts = provider.GetRequiredService<IOptionsMonitor<Configuration.GatewayOptions>>();
                        var fpLogger = provider.GetRequiredService<ILogger<Services.CharacterFingerprintService>>();
                        var connectionString = gatewayOpts.CurrentValue.RedisConnectionString ?? "localhost:6379";
                        var gatewayId = gatewayOpts.CurrentValue.GatewayId ?? "Unknown";
                        return new Services.CharacterFingerprintService(connectionString, fpLogger, gatewayId);
                    });

                    services.AddAllMessageHandlers(
                        Assembly.GetAssembly(typeof(MessageHandlerBase)),
                        Assembly.GetExecutingAssembly());

                    // ===== 角色在线状态 Redis 存储（双轨制架构） =====
                    // 注册 RedisConnection 单例，供 RedisCharacterPresenceStore / 后续心跳监控服务复用。
                    // 连接字符串优先从 Redis:ConnectionString 读取，回退到 Gateway:RedisConnectionString。
                    services.AddSingleton<RedisConnection>(provider =>
                    {
                        var config = provider.GetRequiredService<IConfiguration>();
                        var connStr = config.GetSection("Redis:ConnectionString").Value
                                      ?? config.GetSection("Gateway:RedisConnectionString").Value
                                      ?? "127.0.0.1:9379,password=DB65F7F9C,abortConnect=false,syncTimeout=5000,asyncTimeout=10000";
                        return new RedisConnection(connStr);
                    });
                    services.AddSingleton<Horizon.Game.Core.Sim.Server.ICharacterPresenceStore>(provider =>
                    {
                        var redisConnection = provider.GetRequiredService<RedisConnection>();
                        var logger = provider.GetService<ILogger<RedisCharacterPresenceStore>>();
                        return new RedisCharacterPresenceStore(redisConnection, logger);
                    });

                    // 注册网络服务
                    services.AddSingleton<ITcpService, TcpService>();
                    services.AddSingleton<PlayerDespawnScheduler>();
                    services.AddSingleton<GameNetworkServer>();

                    // P6 运行时连线：ZoneShard fanout → GatewaySyncDispatcher → IGameConnection。
                    services.AddSingleton<Services.GatewayZoneShardFanoutSource>();
                    services.AddSingleton<Horizon.Orleans.Interface.World.IZoneShardFanoutObserver>(sp => sp.GetRequiredService<Services.GatewayZoneShardFanoutSource>());
                    services.AddSingleton<Horizon.Game.Core.Sim.Server.IZoneShardFanoutSource>(sp => sp.GetRequiredService<Services.GatewayZoneShardFanoutSource>());
                    services.AddSingleton<Horizon.Game.Core.Sim.Server.ISessionRegistry>(sp => new Services.ConnectionManagerSessionRegistry(
                        sp.GetRequiredService<Services.IConnectionManager>()));
                    services.AddSingleton<Horizon.Game.Core.Sim.Server.IClientPacketSink, Services.GameConnectionPacketSink>();
                    services.AddSingleton(sp => new Horizon.Game.Core.Sim.Server.GatewaySyncDispatcher(
                        sp.GetRequiredService<Horizon.Game.Core.Sim.Server.IZoneShardFanoutSource>(),
                        sp.GetRequiredService<Horizon.Game.Core.Sim.Server.ISessionRegistry>(),
                        sp.GetRequiredService<Horizon.Game.Core.Sim.Server.IClientPacketSink>(),
                        logger: sp.GetRequiredService<ILogger<Horizon.Game.Core.Sim.Server.GatewaySyncDispatcher>>(),
                        enabled: sp.GetRequiredService<IOptions<Configuration.GatewayOptions>>().Value.UseSyncPacketDispatch));

                    // Task C.5.6：注册场景对象持久化存储到 DI（Singleton，复用 Game 库连接字符串）。
                    services.AddSingleton<Horizon.Game.Core.Persistence.ISceneObjectPersistenceStore>(sp =>
                    {
                        var config = sp.GetRequiredService<IConfiguration>();
                        var logger = sp.GetService<ILogger<Horizon.Game.Core.Persistence.SqlServerSceneObjectPersistenceStore>>();
                        // 优先使用 DatabaseOptions:Game，回退到 ClusteringSiloOptions:SqlServer（同一 SqlServer 实例）。
                        var connStr = config.GetSection("DatabaseOptions:Game")["ConnectionString"]
                                      ?? config.GetSection("ClusteringSiloOptions:SqlServer")["ConnectionString"]
                                      ?? "Data Source=.;Initial Catalog=Game;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;";
                        return new Horizon.Game.Core.Persistence.SqlServerSceneObjectPersistenceStore(connStr, logger);
                    });

                    // 注册后台服务
                    services.AddHostedService<GatewayHostedService>();
                    services.AddHostedService<GatewayRegistryHostedService>();
                    services.AddHostedService<Services.SyncDispatcherHostedService>();

                    // 角色在线状态监控后台服务（双轨制架构：每 10 秒扫描过期 Redis presence，兜底清理离线角色）
                    services.AddHostedService<Services.CharacterPresenceMonitorHostedService>();

                    // 花卉市场数据采集后台服务
                    services.AddHostedService<Services.FlowerDataCollectionService>();
                    services.AddHostedService<Services.KifaMarketDataFetcher>();
                    services.AddHostedService<Services.FlowerWeatherFetcher>();

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

                    // ===== Redis 集群配置（主方案） =====
                    var redisConnectionStr = context.Configuration.GetSection("Redis:ConnectionString").Value
                        ?? "127.0.0.1:9379,password=DB65F7F9C,abortConnect=false,syncTimeout=5000,asyncTimeout=10000";
                    var redisConfigOptions = StackExchange.Redis.ConfigurationOptions.Parse(redisConnectionStr);

                    //集群
                    client.UseRedisClustering(options =>
                    {
                        options.ConfigurationOptions = redisConfigOptions;
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
                            options.GatewayListRefreshPeriod = TimeSpan.FromSeconds(15);  // 更快刷新网关列表，避免卡在陈旧网关地址上
                        })
                        .Configure<ConnectionOptions>(options =>
                        {
                            options.OpenConnectionTimeout = TimeSpan.FromSeconds(10);  // 连接超时
                        })
                        .ConfigureServices(service =>
                        {
                            service.AddSingleton<IClientConnectionRetryFilter, OrleansStartupConnectionRetryFilter>();
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
        }
    
}
