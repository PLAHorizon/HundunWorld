using AutoMapper;
using Horizon.Core;
using Horizon.Core.Abstract;
using Horizon.Core.Options;
using Horizon.Entities;
using Horizon.Strategy.Storage.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Core;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using System.Threading;
using Horizon.Game.Message;
using Horizon.Orleans.Interface;
using Horizon.Orleans.Silo.Configuration;
using Horizon.Orleans.Silo.Extensions;
using Horizon.Orleans.Silo.Diagnostics;
using Horizon.Orleans.Silo.Services;
using Horizon.Orleans.Silo.Filters;
using ClientConnectionOptions = Horizon.Orleans.Silo.Services.ClientConnectionOptions;
using Horizon.Orleans.Silo.Tasks;
using Horizon.Game.Message.Network;
using Horizon.IM.Message.Network;
using Horizon.Orleans.Silo.Monitoring;
using Horizon.Core.Monitoring;
using Horizon.Orleans.Grains;
using Horizon.Orleans.Grains.Payment;
using Horizon.IoT.MQTT;
using StackExchange.Redis;

namespace Horizon.Orleans.Silo
{
    class Program
    {
        private static ILogger<Program>? _logger;
        private static IConfiguration? _config;
        private static DatabaseOptions? _database;
        private static HorizonTimeoutConfiguration? _timeoutConfig;

        public static async Task<int> Main(string[] args)
        {
            try
            {                // Configure logging first
                using var loggerFactory = LoggerFactory.Create(builder =>
                    builder.AddConsole().SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information));
                _logger = loggerFactory.CreateLogger<Program>();

                _logger.LogInformation("混沌世界Orleans Silo正在启动...");                // Load configuration
                _config = await SiloStartupExtension.GetConfiguration();
                if (_config == null)
                {
                    _logger.LogError("加载配置失败");
                    return 1;
                }

                // Load timeout configuration
                _timeoutConfig = HorizonTimeoutConfigurationExtensions.CreateFromConfiguration(_config);
                _logger.LogInformation("Horizon Orleans超时配置已加载");

                Log.LogConfig();

                // Start the silo
                var host = await StartSilo();
                if (host == null)
                {
                    if (IsEfDesignTimeInvocation())
                    {
                        _logger.LogInformation("检测到 EF 设计时启动探测，已跳过 Orleans Silo 运行时启动。");
                        return 0;
                    }

                    _logger.LogError("Silo主机启动失败");
                    return 1;
                }

                _logger.LogInformation("混沌世界Orleans Silo启动成功");
                _logger.LogInformation("按Ctrl+C可优雅地关闭服务器...");

                // Wait for cancellation
                var cancellationToken = new CancellationTokenSource();
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    cancellationToken.Cancel();
                    _logger.LogInformation("正在请求关闭服务器...");
                };

                await Task.Delay(-1, cancellationToken.Token);

                _logger.LogInformation("正在停止Orleans Silo...");
                await host.StopAsync();
                _logger.LogInformation("Orleans Silo已成功停止");

                return 0;
            }
            catch (OperationCanceledException)
            {
                _logger?.LogInformation("Orleans Silo关闭完成");
                return 0;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Orleans Silo运行过程中发生致命错误");
                return 1;
            }
        }
        private static async Task<IHost?> StartSilo()
        {
            try
            {
                var startTime = DateTime.Now;
                _logger?.LogInformation("正在配置Orleans Silo...");

                var oco = _config?.GetSection("ClusteringSiloOptions").Get<OrleansClusteringDbOptions>();
                var sql = oco?.SqlServer;
                _database = _config?.GetSection("DatabaseOptions").Get<DatabaseOptions>();
                
                // 调试：检查数据库配置是否加载成功
                if (_database == null)
                {
                    _logger?.LogWarning("从配置文件加载DatabaseOptions失败，请检查appsettings.json结构。");
                }
                else
                {
                    _logger?.LogInformation("DatabaseOptions加载成功");
                    LogDatabaseConfiguration(_database);
                }

                // 并行获取可用端口，减少启动时间
                var portTasks = new[]
                {
                    Task.Run(() => SiloStartupExtension.GetAvailablePort(11111, 11119)),
                    Task.Run(() => SiloStartupExtension.GetAvailablePort(30000, 30009)),
                    Task.Run(() => SiloStartupExtension.GetAvailablePort(8880, 8889))
                };
                
                var ports = await Task.WhenAll(portTasks);
                var siloPort = ports[0];
                var gatewayPort = ports[1];
                var healthCheckPort = ports[2];

                _logger?.LogInformation("端口配置完成 - Silo端口: {SiloPort}, 网关端口: {GatewayPort}, 健康检查端口: {HealthCheckPort}",
                    siloPort, gatewayPort, healthCheckPort);
                    
                var builder = Host.CreateDefaultBuilder()
                    .UseOrleans(siloBuilder =>
                    {
                        ConfigureOrleansCluster(siloBuilder, sql, oco, siloPort, gatewayPort);
                        ConfigureOrleansStorage(siloBuilder, sql);
                        ConfigureOrleansServices(siloBuilder, healthCheckPort);
                        
                        // 应用启动优化
                        siloBuilder.ApplyStartupOptimizations();

                        // 应用Horizon超时配置
                        if (_timeoutConfig != null)
                        {
                            siloBuilder.ApplyHorizonTimeoutConfiguration(_timeoutConfig);
                            siloBuilder.ValidateAndLogTimeoutConfiguration(_timeoutConfig, _logger!);
                            
                            var advertisedIP = oco != null && !string.IsNullOrEmpty(oco.OrleansSiloHost) &&
                                           oco.OrleansSiloHost != "localhost"
                                ? IPAddress.Parse(oco.OrleansSiloHost)
                                : IPAddress.Loopback;

                            siloBuilder.ApplyOptimizedEndpointConfiguration(_timeoutConfig, siloPort, gatewayPort, advertisedIP);
                            siloBuilder.ApplyPerformanceOptimizations(_timeoutConfig);
                        }
                    })
                    .ConfigureServices((context, services) =>
                    {
                        // 延迟初始化非关键服务
                        var startupConfig = context.Configuration.GetSection("StartupOptimization");
                        var enableOptimization = startupConfig.GetValue<bool>("EnableParallelInitialization", true);
                        
                        if (enableOptimization)
                        {
                            // 关键服务立即注册
                            ConfigureApplicationServices(services);

                            // 非关键服务延迟注册
                            services.AddHostedService<DelayedServiceInitializer>();
                        }
                        else
                        {
                            ConfigureApplicationServices(services);
                        }

                        // 注册诊断和测试服务
                        if (_timeoutConfig != null)
                        {
                            services.AddSingleton(_timeoutConfig);
                            services.AddSingleton<HorizonGatewayDiagnostic>();
                        }

                        // 注册任务状态监控服务（必须在其他服务之前注册）
                        services.AddSingleton<ITaskStatusMonitor, TaskStatusMonitor>();
                        services.AddHostedService<TaskStatusReporterService>();

                        // 注册客户端连接跟踪服务
                        services.AddSingleton<IClientConnectionTracker, ClientConnectionTracker>();
                        services.AddHostedService<ClientConnectionMonitorService>();
                        
                        // 注册生命周期日志记录器
                        services.AddHostedService<SiloLifecycleLogger>();
                        
                        // 根据环境配置客户端连接日志选项
                        var environment = context.HostingEnvironment;
                        services.Configure<ClientConnectionOptions>(options =>
                        {
                            options.EnableDetailedLogging = environment.IsDevelopment();
                            options.LogConnectionDetails = environment.IsDevelopment();
                            options.LogInterval = TimeSpan.FromMinutes(environment.IsDevelopment() ? 1 : 5);
                        });
                        
                        // 注册启动报告服务
                        services.AddHostedService<StartupReportService>();

                        // 注册花卉用户数据同步服务
                        services.AddSingleton<FlowerUserDataSyncService>();
                        services.AddHostedService<FlowerUserSyncStartupService>();

                        // 注册OpenTelemetry监控（APM + Prometheus指标导出）
                        var prometheusPort = context.Configuration.GetValue<int>("Monitoring:PrometheusPort", 9464);
                        services.AddHorizonOpenTelemetry(prometheusPort: prometheusPort);

                        services.Configure<MqttBrokerOptions>(context.Configuration.GetSection(MqttBrokerOptions.SectionName));
                        services.AddSingleton<MqttConnectionValidator>();
                        services.AddSingleton<MqttTopicAuthorizer>();
                        services.AddHostedService<MqttBrokerService>();
                        services.AddHostedService<MqttBridgeHostedService>();
                        services.AddSingleton<IMqttClientProvider, MqttClientProvider>();

                        services.AddHostedService<PaymentCompensationService>();

                        // 注册订单超时定时任务激活服务
                        services.AddHostedService<OrderTimeoutStartupService>();
                    })
                    .ConfigureLogging((context, logging) =>
                    {
                        logging.ClearProviders();
                        logging.AddConfiguration(context.Configuration.GetSection("Logging"));
                        
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

                        // 减少框架底层的冗余日志
                        logging.AddFilter("Orleans", Microsoft.Extensions.Logging.LogLevel.Warning);
                        logging.AddFilter("Runtime", Microsoft.Extensions.Logging.LogLevel.Warning);
                        logging.AddFilter("Microsoft", Microsoft.Extensions.Logging.LogLevel.Warning);
                        logging.AddFilter("Microsoft.Hosting.Lifetime", Microsoft.Extensions.Logging.LogLevel.Information);

                        // 开发环境启用Seq日志聚合（Phase 2.2）
                        logging.AddSeqIfEnabled(context.Configuration, "HundunWorld.Silo");
                    });

                var siloHost = builder.Build();

                // Set the mapper instance for the application
                var mapper = siloHost.Services.GetService<IMapper>();
                if (mapper != null)
                {
                    MapperInstance.Current = mapper;
                }
                
                _logger?.LogInformation("正在启动Orleans Silo...");
                var siloStartTime = DateTime.Now;
                await siloHost.StartAsync();
                
                var siloStartDuration = DateTime.Now - siloStartTime;
                _logger?.LogInformation("Orleans Silo启动成功，耗时 {Duration}ms", siloStartDuration.TotalMilliseconds);

                // 将诊断移到后台执行，不阻塞启动
                _ = Task.Run(async () => await RunPostStartupDiagnosticsAsync(siloHost));

                var totalDuration = DateTime.Now - startTime;
                _logger?.LogInformation("Silo总启动时间: {Duration}ms", totalDuration.TotalMilliseconds);

                return siloHost;
            }
            catch (HostAbortedException) when (IsEfDesignTimeInvocation())
            {
                _logger?.LogInformation("检测到 EF 设计时主机构建中止，忽略该异常并交由 EF 工具继续处理。");
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Orleans Silo启动失败");
                return null;
            }
        }

        private static bool IsEfDesignTimeInvocation()
        {
            return DesignTimeContextChecker.IsDesignTime()
                || (AppDomain.CurrentDomain.FriendlyName?.Contains("ef", StringComparison.OrdinalIgnoreCase) ?? false)
                || Environment.GetCommandLineArgs().Any(arg => arg.Contains("ef", StringComparison.OrdinalIgnoreCase));
        }

        private static void ConfigureOrleansCluster(ISiloBuilder siloBuilder, DbInfo? sql, OrleansClusteringDbOptions? oco, int siloPort, int gatewayPort)
        {
            // ===== Redis 集群配置（主方案） =====
            // 从配置读取 Redis 连接字符串，默认连接本地 Redis
            var redisConnectionStr = _config?.GetSection("Redis:ConnectionString").Value
                ?? "127.0.0.1:9379,password=DB65F7F9C,abortConnect=false,syncTimeout=5000,asyncTimeout=10000";
            var redisConfigOptions = StackExchange.Redis.ConfigurationOptions.Parse(redisConnectionStr);
            _logger?.LogInformation("使用 Redis 集群存储: {Endpoint}", redisConfigOptions.ToString());
            siloBuilder.UseRedisClustering(options =>
            {
                options.ConfigurationOptions = redisConfigOptions;
            });

            // ===== 原 SQL Server 集群配置（备份方案，Redis 故障时取消注释上方 Redis 配置块并启用下方代码） =====
            // if (sql != null)
            // {
            //     siloBuilder.UseAdoNetClustering(options =>
            //     {
            //         options.ConnectionString = sql.ConnectionString;
            //         options.Invariant = sql.Invariant;
            //     });
            // }
            // else
            // {
            //     _logger?.LogWarning("未找到SQL配置，使用本地集群");
            //     siloBuilder.UseLocalhostClustering();
            // }

            // Configure cluster options
            siloBuilder.Configure<ClusterOptions>(options =>
            {
                var clusterOptions = _config?.GetSection("ClusterOptions").Get<ClusterOptions>();
                options.ClusterId = clusterOptions?.ClusterId ?? "dev";
                options.ServiceId = clusterOptions?.ServiceId ?? "BaseService";
            });

            // 配置Grain接口版本管理策略（支持滚动升级）
            siloBuilder.Configure<GrainVersioningOptions>(options =>
            {
                options.DefaultCompatibilityStrategy = "BackwardCompatible";
                options.DefaultVersionSelectorStrategy = "AllCompatibleVersions";
            });

            // 注意：超时配置现在通过HorizonTimeoutConfigurationExtensions处理
            _logger?.LogInformation("Orleans集群配置完成 - Silo端口: {SiloPort}, 网关端口: {GatewayPort}", siloPort, gatewayPort);
        }
        private static void ConfigureOrleansStorage(ISiloBuilder siloBuilder, DbInfo? sql)
        {
            // 配置Orleans Memory Stream Provider（事件驱动架构）
            siloBuilder.AddMemoryStreams(OrleansConst.CommonMessageStreamProvider);

            // ===== Redis 存储配置（主方案） =====
            // 从配置读取 Redis 连接字符串（与 Clustering 共用同一 Redis 实例）
            var redisConnectionStr = _config?.GetSection("Redis:ConnectionString").Value
                ?? "127.0.0.1:9379,password=DB65F7F9C,abortConnect=false,syncTimeout=5000,asyncTimeout=10000";
            var redisConfigOptions = StackExchange.Redis.ConfigurationOptions.Parse(redisConnectionStr);
            _logger?.LogInformation("使用 Redis 存储（Reminders + GrainStorage）: {Endpoint}", redisConfigOptions.ToString());

            // Configure reminders (Redis)
            siloBuilder.UseRedisReminderService(options =>
            {
                options.ConfigurationOptions = redisConfigOptions;
            });

            // Configure grain storage (Redis)
            siloBuilder.AddRedisGrainStorage(OrleansConst.PubSubStore, options =>
            {
                options.ConfigurationOptions = redisConfigOptions;
            });
            siloBuilder.AddRedisGrainStorageAsDefault(options =>
            {
                options.ConfigurationOptions = redisConfigOptions;
            });
            siloBuilder.AddRedisGrainStorage(OrleansConst.GameStore, options =>
            {
                options.ConfigurationOptions = redisConfigOptions;
                // 保留自定义序列化器（与原 SQL Server 配置一致，确保 grain 状态序列化兼容）
                options.GrainStorageSerializer = new CustomGrainStorageSerializer();
            });
            siloBuilder.AddRedisGrainStorage(OrleansConst.PassportStore, options =>
            {
                options.ConfigurationOptions = redisConfigOptions;
                options.GrainStorageSerializer = new CustomGrainStorageSerializer();
            });
            // P4-a：世界状态持久化（WorldChunkCellGrain / WorldDiffLogGrain）。
            // 切换到 Redis 后通过 [PersistentState("chunk"/"difflog", OrleansConst.WorldSqlStore)] 使用。
            siloBuilder.AddRedisGrainStorage(OrleansConst.WorldSqlStore, options =>
            {
                options.ConfigurationOptions = redisConfigOptions;
                options.GrainStorageSerializer = new CustomGrainStorageSerializer();
            });
            siloBuilder.AddRedisGrainStorage(OrleansConst.FlowerStore, options =>
            {
                options.ConfigurationOptions = redisConfigOptions;
                options.GrainStorageSerializer = new CustomGrainStorageSerializer();
            });
            siloBuilder.AddRedisGrainStorage(OrleansConst.AIStore, options =>
            {
                options.ConfigurationOptions = redisConfigOptions;
                options.GrainStorageSerializer = new CustomGrainStorageSerializer();
            });

            // ===== 原 SQL Server 存储配置（备份方案，Redis 故障时取消注释上方 Redis 配置块并启用下方代码） =====
            // if (sql != null)
            // {
            //     siloBuilder.UseAdoNetReminderService(options =>
            //     {
            //         options.ConnectionString = sql.ConnectionString;
            //         options.Invariant = sql.Invariant;
            //     });
            //     siloBuilder.AddAdoNetGrainStorage(OrleansConst.PubSubStore, options =>
            //     {
            //         options.ConnectionString = sql.ConnectionString;
            //         options.Invariant = sql.Invariant;
            //     });
            //     siloBuilder.AddAdoNetGrainStorageAsDefault(options =>
            //     {
            //         options.ConnectionString = sql.ConnectionString;
            //         options.Invariant = sql.Invariant;
            //     });
            //     siloBuilder.AddAdoNetGrainStorage(OrleansConst.GameStore, options =>
            //     {
            //         options.ConnectionString = sql.ConnectionString;
            //         options.Invariant = sql.Invariant;
            //         options.GrainStorageSerializer = new CustomGrainStorageSerializer();
            //     });
            //     siloBuilder.AddAdoNetGrainStorage(OrleansConst.PassportStore, options =>
            //     {
            //         options.ConnectionString = sql.ConnectionString;
            //         options.Invariant = sql.Invariant;
            //         options.GrainStorageSerializer = new CustomGrainStorageSerializer();
            //     });
            //     siloBuilder.AddAdoNetGrainStorage(OrleansConst.WorldSqlStore, options =>
            //     {
            //         options.ConnectionString = sql.ConnectionString;
            //         options.Invariant = sql.Invariant;
            //         options.GrainStorageSerializer = new CustomGrainStorageSerializer();
            //     });
            //     siloBuilder.AddAdoNetGrainStorage(OrleansConst.FlowerStore, options =>
            //     {
            //         options.ConnectionString = sql.ConnectionString;
            //         options.Invariant = sql.Invariant;
            //         options.GrainStorageSerializer = new CustomGrainStorageSerializer();
            //     });
            //     siloBuilder.AddAdoNetGrainStorage(OrleansConst.AIStore, options =>
            //     {
            //         options.ConnectionString = sql.ConnectionString;
            //         options.Invariant = sql.Invariant;
            //         options.GrainStorageSerializer = new CustomGrainStorageSerializer();
            //     });
            // }
        }

        private static void ConfigureOrleansServices(ISiloBuilder siloBuilder, int healthCheckPort)
        {
            // Configure serialization
            siloBuilder.ConfigureServices(services =>
            {
                services.AddSerializer(serializerBuilder =>
                {
                    serializerBuilder.AddAssembly(typeof(HorizonMessagePacket).Assembly);
                    serializerBuilder.AddAssembly(typeof(IMGroupChatNotifyMessage).Assembly);
                    serializerBuilder.AddAssembly(typeof(Horizon.Share.VMs.ResultVM<>).Assembly);
                    serializerBuilder.AddNewtonsoftJsonSerializer(
                        isSupported: type => type.Namespace != null && type.Namespace.StartsWith("Horizon.Share"));
                });

                // Configure health checks
                services.AddHealthChecks();
                services.AddSingleton<IHealthCheckPublisher, LoggingHealthCheckPublisher>()
                    .Configure<HealthCheckPublisherOptions>(options =>
                    {
                        options.Period = TimeSpan.FromSeconds(60); // 增加健康检查间隔
                        options.Delay = TimeSpan.FromSeconds(30);  // 延迟首次健康检查
                    });

                // Configure console lifetime
                services.Configure<ConsoleLifetimeOptions>(options =>
                {
                    options.SuppressStatusMessages = true;
                });

                services.Configure<HealthCheckHostedServiceOptions>(options =>
                {
                    options.Port = healthCheckPort;
                    options.PathString = "/health";
                });
                
                // 注册启动任务
                services.AddSingleton<StartupDiagnosticsTask>();
                services.AddSingleton<ClientConnectionStartupTask>();
                
                // 注册生命周期日志记录器
                services.AddSingleton<SiloLifecycleLogger>();
            });

            // Configure retries
            siloBuilder.AddIncomingGrainCallFilter<RetryFilter>();
            
            // 添加CorrelationId过滤器（分布式追踪）
            siloBuilder.AddIncomingGrainCallFilter<CorrelationIdFilter>();
            
            // 添加统一异常处理过滤器（架构改进）
            siloBuilder.AddIncomingGrainCallFilter<GrainExceptionFilter>();
            
            // 添加请求参数验证过滤器（架构改进）
            siloBuilder.AddIncomingGrainCallFilter<GrainCallValidationFilter>();
            
            // 添加客户端连接跟踪过滤器
            siloBuilder.AddIncomingGrainCallFilter<ClientConnectionTrackingFilter>();
            
            // 添加启动任务 - 使用优先级
            siloBuilder.AddStartupTask(async (provider, token) =>
            {
                var diagnosticsTask = provider.GetService<StartupDiagnosticsTask>();
                if (diagnosticsTask != null)
                {
                    await diagnosticsTask.Execute(token);
                }
            }, 1000); // 低优先级
            
            siloBuilder.AddStartupTask(async (provider, token) =>
            {
                var connectionTask = provider.GetService<ClientConnectionStartupTask>();
                if (connectionTask != null)
                {
                    await connectionTask.Execute(token);
                }
            }, 2000); // 更低优先级
            
            // 注释掉 Dashboard 配置，因为我们不再使用 OrleansDashboard
            /*
            // Configure dashboard
            siloBuilder.UseDashboard(options =>
            {
                var dashboard = _config?.GetSection("DashboardOptions").Get<OrleansDashboard.DashboardOptions>();
                options.Host = dashboard?.Host ?? "*";
                options.Port = dashboard?.Port ?? 8080;
                options.Username = dashboard?.Username ?? "admin";
                options.Password = dashboard?.Password ?? "admin";
            });
            */
        }

        private static void ConfigureApplicationServices(IServiceCollection services)
        {
            // Configure Entity Framework contexts
            ConfigureDbContexts(services);

            // Configure repositories and services
            services.AddDataServiceProvider();

            // Configure Redis
            services.AddRedisServiceProvider();

            // ===== 角色在线状态 Redis 存储（双轨制架构） =====
            // 注册 RedisConnection 单例，供 RedisCharacterPresenceStore / 后续心跳监控服务复用。
            // 连接字符串优先从 Redis:ConnectionString 读取，回退到 DataBase:RedisMasters[0]。
            services.AddSingleton<RedisConnection>(provider =>
            {
                var connStr = _config?.GetSection("Redis:ConnectionString").Value;
                if (string.IsNullOrWhiteSpace(connStr))
                {
                    // 回退：从 DataBase:RedisMasters[0] 拼接连接字符串
                    var redisMaster = _config?.GetSection("DataBase:RedisMasters").GetChildren().FirstOrDefault();
                    var host = redisMaster?["Host"] ?? "127.0.0.1";
                    var port = redisMaster?["Port"] ?? "9379";
                    var password = redisMaster?["Password"] ?? "DB65F7F9C";
                    // StackExchange.Redis 标准格式：端点在前，password 作为 key-value 参数
                    connStr = $"{host}:{port},password={password},abortConnect=false,syncTimeout=5000,asyncTimeout=10000";
                }
                return new RedisConnection(connStr);
            });
            services.AddSingleton<Horizon.Game.Core.Sim.Server.ICharacterPresenceStore>(provider =>
            {
                var redisConnection = provider.GetRequiredService<RedisConnection>();
                var logger = provider.GetService<ILogger<RedisCharacterPresenceStore>>();
                return new RedisCharacterPresenceStore(redisConnection, logger);
            });

            // 注册角色指纹服务（Silo 端，复用 RedisConnection 单例）。
            // 修复 BUG：CharacterGrain.GoOfflineAsync 需要清理 character:fingerprint:{id} key（TTL 5min），
            // 否则角色离线后 Redis 中仍残留 fingerprint key，外部观察"角色在线信息未及时更新"。
            // Silo 端的 ZoneShardGrain.TryGoOfflineAsync 兜底路径不经过网关的 PlayerDespawnScheduler，
            // 因此 GoOfflineAsync 本身必须能清理 fingerprint。
            services.AddSingleton<Horizon.Game.Core.Interfaces.ICharacterFingerprintService>(provider =>
            {
                var redisConnection = provider.GetRequiredService<RedisConnection>();
                var logger = provider.GetService<ILogger<Horizon.Strategy.Storage.Redis.RedisCharacterFingerprintStore>>();
                return new Horizon.Strategy.Storage.Redis.RedisCharacterFingerprintStore(redisConnection, logger);
            });

            // Configure event publisher (Orleans Stream事件驱动架构)
            services.AddSingleton<IGameEventPublisher, GameEventPublisher>();

            // Configure options
            services.ConfigureOptions();

            // Configure AutoMapper
            services.AddMappingProfiles();

            // Configure payment settings
            services.AddSingleton(sp =>
            {
                var config = sp.GetRequiredService<IConfiguration>();
                var section = config.GetSection("AlipaySettings");
                return new AlipaySettings
                {
                    AppId = Environment.GetEnvironmentVariable("ALIPAY_APP_ID") ?? section["AppId"] ?? "",
                    PrivateKey = Environment.GetEnvironmentVariable("ALIPAY_PRIVATE_KEY") ?? section["PrivateKey"] ?? "",
                    AlipayPublicKey = Environment.GetEnvironmentVariable("ALIPAY_PUBLIC_KEY") ?? section["AlipayPublicKey"] ?? "",
                    NotifyUrl = Environment.GetEnvironmentVariable("ALIPAY_NOTIFY_URL") ?? section["NotifyUrl"] ?? "",
                    ReturnUrl = Environment.GetEnvironmentVariable("ALIPAY_RETURN_URL") ?? section["ReturnUrl"] ?? "",
                    IsSandbox = section.GetValue<bool>("IsSandbox", true)
                };
            });
            services.AddSingleton(sp =>
            {
                var config = sp.GetRequiredService<IConfiguration>();
                var section = config.GetSection("WechatPaySettings");
                return new WechatPaySettings
                {
                    MerchantId = Environment.GetEnvironmentVariable("WECHAT_MERCHANT_ID") ?? section["MerchantId"] ?? "",
                    AppId = Environment.GetEnvironmentVariable("WECHAT_APP_ID") ?? section["AppId"] ?? "",
                    MerchantV3Secret = Environment.GetEnvironmentVariable("WECHAT_V3_SECRET") ?? section["MerchantV3Secret"] ?? "",
                    CertSerialNumber = Environment.GetEnvironmentVariable("WECHAT_CERT_SERIAL") ?? section["CertSerialNumber"] ?? "",
                    PrivateKey = Environment.GetEnvironmentVariable("WECHAT_PRIVATE_KEY") ?? section["PrivateKey"] ?? "",
                    NotifyUrl = Environment.GetEnvironmentVariable("WECHAT_NOTIFY_URL") ?? section["NotifyUrl"] ?? "",
                    IsSandbox = section.GetValue<bool>("IsSandbox", true)
                };
            });
            services.AddSingleton<WechatPaymentChannel>(sp =>
            {
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                var settings = sp.GetRequiredService<WechatPaySettings>();
                return new WechatPaymentChannel(
                    loggerFactory.CreateLogger<WechatPaymentChannel>(),
                    settings.MerchantId,
                    settings.MerchantV3Secret,
                    settings.CertSerialNumber,
                    settings.PrivateKey,
                    settings.NotifyUrl,
                    settings.IsSandbox,
                    settings.AppId);
            });
            services.AddSingleton<AlipayChannel>(sp =>
            {
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                var settings = sp.GetRequiredService<AlipaySettings>();
                return new AlipayChannel(
                    loggerFactory.CreateLogger<AlipayChannel>(),
                    settings.AppId,
                    settings.PrivateKey,
                    settings.AlipayPublicKey,
                    settings.NotifyUrl,
                    settings.ReturnUrl,
                    settings.IsSandbox);
            });
            services.AddScoped<FlowerPaymentCallbackService>();
            services.AddHttpClient();
            services.AddSingleton<KdniaoApiClient>();
        }

        private static void ConfigureDbContexts(IServiceCollection services)
        {
            if (_database?.Basic == null || _database?.Game == null || _database?.Article == null || _database?.Support == null || _database?.Xingguang == null || _database?.Flower == null)
            {
                _logger?.LogError("未找到数据库配置或配置不完整，请检查appsettings.json文件。");
                _logger?.LogError("Basic: {Basic}, Game: {Game}, Article: {Article}, Support: {Support}, Xingguang: {Xingguang}, Flower: {Flower}", 
                    _database?.Basic, _database?.Game, _database?.Article, _database?.Support, _database?.Xingguang, _database?.Flower);
                throw new InvalidOperationException("数据库配置缺失或不完整，请检查appsettings.json文件。");
            }

            services.AddDbContextPool<BasicEntityContext>((provider, options) =>
            {
                options.SetDbContext(_database.Basic);
            });

            services.AddDbContextPool<GameEntityContext>((provider, options) =>
            {
                options.SetDbContext(_database.Game);
            });

            services.AddDbContextPool<ArticleEntityContext>((provider, options) =>
            {
                options.SetDbContext(_database.Article);
            });

            services.AddDbContextPool<SupportsEntityContext>((provider, options) =>
            {
                options.SetDbContext(_database.Support);
            });
            
            services.AddDbContextPool<XingguangEntityContext>((provider, options) =>
            {
                options.SetDbContext(_database.Xingguang);
            });

            services.AddDbContextPool<FlowerEntityContext>((provider, options) =>
            {
                options.SetDbContext(_database.Flower);
            });
        }

        /// <summary>
        /// 记录数据库配置信息用于调试
        /// </summary>
        private static void LogDatabaseConfiguration(DatabaseOptions database)
        {
            if (database == null)
            {
                _logger?.LogWarning("DatabaseOptions为空");
                return;
            }

            _logger?.LogInformation("数据库配置信息:");
            LogDatabaseInfo("Basic", database.Basic);
            LogDatabaseInfo("Game", database.Game);
            LogDatabaseInfo("Article", database.Article);
            LogDatabaseInfo("Support", database.Support);
            LogDatabaseInfo("Xingguang", database.Xingguang);
            LogDatabaseInfo("Flower", database.Flower);
        }

        private static void LogDatabaseInfo(string name, DatabaseInfo? info)
        {
            if (info == null)
            {
                _logger?.LogWarning("{Name}: 为空", name);
                return;
            }

            _logger?.LogInformation("{Name}: 类型={Type}, 连接字符串={ConnectionString}", 
                name, info.Type, string.IsNullOrEmpty(info.ConnectionString) ? "空/未配置" : "***");
        }

        /// <summary>
        /// 启动后运行诊断和测试
        /// </summary>
        private static async Task RunPostStartupDiagnosticsAsync(IHost siloHost)
        {
            try
            {
                _logger?.LogInformation("🔍 开始运行启动后诊断...");

                // 获取诊断服务
                var diagnostic = siloHost.Services.GetService<HorizonGatewayDiagnostic>();
                if (diagnostic != null)
                {
                    _logger?.LogInformation("正在运行网关连接诊断...");

                    // 添加超时控制，避免无限等待
                    var diagnosticTask = diagnostic.RunCompleteDiagnosticAsync();
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(15));

                    var completedTask = await Task.WhenAny(diagnosticTask, timeoutTask);

                    if (completedTask == diagnosticTask)
                    {
                        var diagnosticResult = await diagnosticTask;
                        if (diagnosticResult.IsHealthy)
                        {
                            _logger?.LogInformation("✅ 网关诊断通过 - 系统健康");
                        }
                        else
                        {
                            _logger?.LogInformation("⚠️ 网关诊断发现问题 - 这是正常的，网关服务尚未启动");
                        }
                    }
                    else
                    {
                        _logger?.LogWarning("⏰ 网关诊断超时 (15秒) - 继续启动");
                    }
                }

                _logger?.LogInformation("🏁 启动后诊断完成");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "启动后诊断过程中发生错误");
                // 不抛出异常，避免影响Silo启动
            }
        }


    }

    internal class PaymentCompensationService : IHostedService, IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PaymentCompensationService> _logger;
        private Timer _timer;

        public PaymentCompensationService(
            IServiceProvider serviceProvider,
            ILogger<PaymentCompensationService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _timer = new Timer(DoCompensationQuery, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            _logger.LogInformation("支付补偿查询服务已启动，间隔5分钟");
            return Task.CompletedTask;
        }

        private void DoCompensationQuery(object state)
        {
            _ = ExecuteCompensationAsync();
        }

        private async Task ExecuteCompensationAsync()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var callbackService = scope.ServiceProvider.GetService<FlowerPaymentCallbackService>();
                if (callbackService != null)
                {
                    await callbackService.CompensatePendingTransactionsAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "支付补偿查询执行失败");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _timer?.Change(Timeout.Infinite, 0);
            _logger.LogInformation("支付补偿查询服务已停止");
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
