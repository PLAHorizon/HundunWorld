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
using Horizon.Orleans.Silo.Monitoring;
using Horizon.Core.Monitoring;
using Horizon.Orleans.Grains;

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

                _logger.LogInformation("Starting Horizon Orleans Silo...");                // Load configuration
                _config = await SiloStartupExtension.GetConfiguration();
                if (_config == null)
                {
                    _logger.LogError("Failed to load configuration");
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
                    _logger.LogError("Silo host failed to start");
                    return 1;
                }

                _logger.LogInformation("Horizon Orleans Silo started successfully");
                _logger.LogInformation("Press Ctrl+C to gracefully shutdown...");

                // Wait for cancellation
                var cancellationToken = new CancellationTokenSource();
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    cancellationToken.Cancel();
                    _logger.LogInformation("Shutdown requested...");
                };

                await Task.Delay(-1, cancellationToken.Token);

                _logger.LogInformation("Stopping Orleans Silo...");
                await host.StopAsync();
                _logger.LogInformation("Orleans Silo stopped successfully");

                return 0;
            }
            catch (OperationCanceledException)
            {
                _logger?.LogInformation("Orleans Silo shutdown completed");
                return 0;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fatal error occurred during Orleans Silo execution");
                return 1;
            }
        }
        private static async Task<IHost?> StartSilo()
        {
            try
            {
                var startTime = DateTime.Now;
                _logger?.LogInformation("Configuring Orleans Silo...");

                var oco = _config?.GetSection("ClusteringSiloOptions").Get<OrleansClusteringDbOptions>();
                var sql = oco?.SqlServer;
                _database = _config?.GetSection("DatabaseOptions").Get<DatabaseOptions>();
                
                // 调试：检查数据库配置是否加载成功
                if (_database == null)
                {
                    _logger?.LogWarning("Failed to load DatabaseOptions from configuration. Check appsettings.json structure.");
                }
                else
                {
                    _logger?.LogInformation("DatabaseOptions loaded successfully");
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

                _logger?.LogInformation("Configured ports - Silo: {SiloPort}, Gateway: {GatewayPort}, HealthCheck: {HealthCheckPort}",
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

                        // 注册OpenTelemetry监控（APM + Prometheus指标导出）
                        var prometheusPort = context.Configuration.GetValue<int>("Monitoring:PrometheusPort", 9464);
                        services.AddHorizonOpenTelemetry(prometheusPort: prometheusPort);
                    })
                    .ConfigureLogging((context, logging) =>
                    {
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
                        logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);

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
                
                _logger?.LogInformation("Starting Orleans Silo...");
                var siloStartTime = DateTime.Now;
                await siloHost.StartAsync();
                
                var siloStartDuration = DateTime.Now - siloStartTime;
                _logger?.LogInformation("Orleans Silo started successfully in {Duration}ms", siloStartDuration.TotalMilliseconds);

                // 将诊断移到后台执行，不阻塞启动
                _ = Task.Run(async () => await RunPostStartupDiagnosticsAsync(siloHost));

                var totalDuration = DateTime.Now - startTime;
                _logger?.LogInformation("Total Silo startup time: {Duration}ms", totalDuration.TotalMilliseconds);

                return siloHost;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to start Orleans Silo");
                return null;
            }
        }
        private static void ConfigureOrleansCluster(ISiloBuilder siloBuilder, DbInfo? sql, OrleansClusteringDbOptions? oco, int siloPort, int gatewayPort)
        {
            // Configure clustering
            if (sql != null)
            {
                siloBuilder.UseAdoNetClustering(options =>
                {
                    options.ConnectionString = sql.ConnectionString;
                    options.Invariant = sql.Invariant;
                });
            }
            else
            {
                _logger?.LogWarning("No SQL configuration found, using localhost clustering");
                siloBuilder.UseLocalhostClustering();
            }

            // Configure cluster options
            siloBuilder.Configure<ClusterOptions>(options =>
            {
                var clusterOptions = _config?.GetSection("ClusterOptions").Get<ClusterOptions>();
                options.ClusterId = clusterOptions?.ClusterId ?? "dev";
                options.ServiceId = clusterOptions?.ServiceId ?? "HorizonService";
            });

            // 注意：超时配置现在通过HorizonTimeoutConfigurationExtensions处理
            _logger?.LogInformation("Orleans集群配置完成 - Silo端口: {SiloPort}, 网关端口: {GatewayPort}", siloPort, gatewayPort);
        }
        private static void ConfigureOrleansStorage(ISiloBuilder siloBuilder, DbInfo? sql)
        {
            // 配置Orleans Memory Stream Provider（事件驱动架构）
            siloBuilder.AddMemoryStreams(OrleansConst.CommonMessageStreamProvider);

            if (sql != null)
            {
                // Configure reminders
                siloBuilder.UseAdoNetReminderService(options =>
                {
                    options.ConnectionString = sql.ConnectionString;
                    options.Invariant = sql.Invariant;
                });

                // Configure grain storage
                siloBuilder.AddAdoNetGrainStorage(OrleansConst.PubSubStore, options =>
                {
                    options.ConnectionString = sql.ConnectionString;
                    options.Invariant = sql.Invariant;
                });
                siloBuilder.AddAdoNetGrainStorageAsDefault(options =>
                {
                    options.ConnectionString = sql.ConnectionString;
                    options.Invariant = sql.Invariant;
                });
                siloBuilder.AddAdoNetGrainStorage(OrleansConst.GameStore, options =>
                {
                    options.ConnectionString = sql.ConnectionString;
                    options.Invariant = sql.Invariant;
                    // 添加显式参数映射

                    options.GrainStorageSerializer = new CustomGrainStorageSerializer();
                });
                siloBuilder.AddAdoNetGrainStorage(OrleansConst.PassportStore, options =>
                {
                    options.ConnectionString = sql.ConnectionString;
                    options.Invariant = sql.Invariant;
                    // 添加显式参数映射

                    options.GrainStorageSerializer = new CustomGrainStorageSerializer();
                });
               
            }
        }

        private static void ConfigureOrleansServices(ISiloBuilder siloBuilder, int healthCheckPort)
        {
            // Configure serialization
            siloBuilder.ConfigureServices(services =>
            {
                services.AddSerializer(serializerBuilder =>
                {
                    serializerBuilder.AddAssembly(typeof(HorizonMessagePacket).Assembly);
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

            // Configure event publisher (Orleans Stream事件驱动架构)
            services.AddSingleton<IGameEventPublisher, GameEventPublisher>();

            // Configure options
            services.ConfigureOptions();

            // Configure AutoMapper
            services.AddMappingProfiles();
        }

        private static void ConfigureDbContexts(IServiceCollection services)
        {
            if (_database?.Basic == null || _database?.Game == null || _database?.Article == null || _database?.Support == null || _database?.Xingguang == null)
            {
                _logger?.LogError("Database configuration not found or incomplete. Please check your appsettings.json file.");
                _logger?.LogError("Basic: {Basic}, Game: {Game}, Article: {Article}, Support: {Support}, Xingguang: {Xingguang}", 
                    _database?.Basic, _database?.Game, _database?.Article, _database?.Support, _database?.Xingguang);
                throw new InvalidOperationException("Database configuration is missing or incomplete. Please check your appsettings.json file.");
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
        }

        /// <summary>
        /// 记录数据库配置信息用于调试
        /// </summary>
        private static void LogDatabaseConfiguration(DatabaseOptions database)
        {
            if (database == null)
            {
                _logger?.LogWarning("DatabaseOptions is null");
                return;
            }

            _logger?.LogInformation("Database configuration:");
            LogDatabaseInfo("Basic", database.Basic);
            LogDatabaseInfo("Game", database.Game);
            LogDatabaseInfo("Article", database.Article);
            LogDatabaseInfo("Support", database.Support);
            LogDatabaseInfo("Xingguang", database.Xingguang);
        }

        private static void LogDatabaseInfo(string name, DatabaseInfo? info)
        {
            if (info == null)
            {
                _logger?.LogWarning("{Name}: null", name);
                return;
            }

            _logger?.LogInformation("{Name}: Type={Type}, ConnectionString={ConnectionString}", 
                name, info.Type, string.IsNullOrEmpty(info.ConnectionString) ? "null/empty" : "***");
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
}
