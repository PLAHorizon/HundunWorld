using Horizon.Core;
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
            // [修复] 补齐角色管理 Grain：IUserCharacterManagerGrain 的实现类在同一程序集内，
            // 显式引用可确保它在 IClusterClient 初始化前完成加载与类型解析准备，
            // 避免 GetGrain<IUserCharacterManagerGrain> 报
            // "Could not find an implementation for interface ..."。
            _ = typeof(Horizon.Orleans.Grains.UserCharacterManagerGrain).Assembly;

            try
            {
                // 创建并启动主机
                var host = CreateHostBuilder(args).Build();

                // 获取日志记录器
                _logger = host.Services.GetRequiredService<ILogger<Program>>();
                _logger.LogInformation("混沌世界游戏网关正在启动...");

                // 阶段 5.4.1：接线 HorizonMessageInfo 协议解析日志（替代原 Console.WriteLine 直接打印，
                // 解析失败按 Error 级进入统一日志管线/文件落盘/告警）。
                Horizon.Game.Message.Network.HorizonMessageInfo.Log = host.Services
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("HorizonMessageInfo");

                // [诊断] Orleans Client 的 Grain 接口解析自检。
                // GetGrain<T> 只创建本地代理、不发任何网络调用，因此：
                //   通过 → Client 端解析表（ApplicationPart / ClusterManifest）已包含该 Grain；
                //   失败 → 解析表缺失，业务调用必然报
                //          "Could not find an implementation for interface ..."。
                // 放在 host.RunAsync() 之后异步执行，不阻塞启动。
                _ = Task.Run(async () =>
                {
                    // ⚠ 时序说明：Client 的「接口 → Grain 类」映射由 Silo 通过 ClusterManifest 下发，
                    //   需要等 Silo 就绪，启动后数十秒内 GetGrain 报
                    //   "Could not find an implementation" 属正常现象
                    //   （项目内 FlowerWeatherFetcher / DounanMarketDataFetcher 也是等待约 60 秒才成功）。
                    //   因此这里轮询探测到成功或超时为止，避免把「启动早期」误报成「解析表缺失」。
                    var deadline = DateTime.UtcNow.AddSeconds(150);
                    Exception lastError = null;
                    int attempt = 0;

                    while (DateTime.UtcNow < deadline)
                    {
                        attempt++;
                        try
                        {
                            var clusterClient = host.Services.GetRequiredService<global::Orleans.IClusterClient>();
                            _ = clusterClient.GetGrain<global::Horizon.Orleans.Interface.IUserCharacterManagerGrain>(0);
                            _logger?.LogInformation(
                                $"[自检] ✅ Grain 解析通过（第 {attempt} 次探测）：IUserCharacterManagerGrain 已注册到 Client 解析表");
                            lastError = null;
                            break;
                        }
                        catch (Exception ex)
                        {
                            lastError = ex;
                            await Task.Delay(TimeSpan.FromSeconds(5));
                        }
                    }

                    if (lastError == null) return;

                    _logger?.LogError(lastError,
                        $"[自检] ❌ 轮询 150 秒（{attempt} 次探测）仍未通过：IUserCharacterManagerGrain 未注册（Client 解析表缺失）");

                    // 失败时补充 ClusterManifest 状态，便于判断是「manifest 没到位」还是「manifest 里没有该 Grain」。
                    // 用类型名反射查找，避免 Orleans 版本间命名空间变动（Orleans.Runtime vs Orleans）导致编译失败。
                    try
                    {
                        Type mpType = null;
                        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            try
                            {
                                mpType = asm.GetType("Orleans.Runtime.IClusterManifestProvider", false);
                                if (mpType != null) break;
                            }
                            catch { /* 个别程序集可能抛异常，忽略 */ }
                        }

                        if (mpType == null)
                        {
                            _logger?.LogWarning("[自检] 未能定位 IClusterManifestProvider 类型");
                        }
                        else
                        {
                            var mp = host.Services.GetService(mpType);
                            if (mp == null)
                            {
                                _logger?.LogWarning("[自检] 未注册 IClusterManifestProvider（Client 拿不到 Grain 类型清单）");
                            }
                            else
                            {
                                var mt = mp.GetType();
                                var isAvail = mt.GetProperty("IsAvailable")?.GetValue(mp);
                                var current = mt.GetProperty("Current")?.GetValue(mp);
                                _logger?.LogWarning(
                                    $"[自检] ClusterManifest: IsAvailable={isAvail}, " +
                                    $"Current={(current == null ? "null" : current.GetType().FullName)}");

                                if (current != null)
                                {
                                    foreach (var pname in new[] { "AllGrainManifests", "GrainManifests", "Grains" })
                                    {
                                        var prop = current.GetType().GetProperty(pname);
                                        if (prop?.GetValue(current) is System.Collections.IEnumerable seq)
                                        {
                                            int n = 0;
                                            foreach (var _ in seq) n++;
                                            _logger?.LogWarning($"[自检] ClusterManifest.{pname} 条目数={n}");
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "[自检] ClusterManifest 检查异常");
                    }
                });

                // [诊断] 类型可见性：区分「程序集/类型加载失败」与「Orleans 序列化器清单缺失」。
                //   Type.GetType 成功 ⇒ 类型在进程内可见，问题在 Orleans 的类型清单/别名注册；
                //   Type.GetType 返回 null / 抛异常 ⇒ 程序集或类型根本加载不到。
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(30));

                    foreach (var typeName in new[]
                    {
                        "Horizon.Orleans.Interface.IFlowerMarketGrain,Horizon.Orleans.Interface",
                        "Horizon.Orleans.Interface.IFlowerSpeciesGrain,Horizon.Orleans.Interface",
                        "Horizon.Orleans.Interface.IUserCharacterManagerGrain,Horizon.Orleans.Interface",
                        "Horizon.Orleans.Interface.World.IZoneShardGrain,Horizon.Orleans.Interface",
                    })
                    {
                        try
                        {
                            var t = Type.GetType(typeName, throwOnError: false);
                            _logger?.LogInformation(
                                $"[自检] Type.GetType(\"{typeName}\") = " +
                                $"{(t == null ? "null ❌（类型不可见）" : "OK ✅ " + t.FullName)}");
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, $"[自检] Type.GetType(\"{typeName}\") 抛异常");
                        }
                    }

                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            var n = asm.GetName().Name;
                            if (n == "Horizon.Orleans.Interface" || n == "Horizon.Orleans.Grains")
                                _logger?.LogInformation($"[自检] 已加载程序集: {asm.FullName} @ {asm.Location}");
                        }
                        catch { }
                    }
                });

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

                    // 加载 .env 文件到环境变量（不覆盖系统变量），并解析 ${VAR} 占位符，
                    // 兑现 .env.example 的 "${VAR} 由此注入" 约定。用 in-memory 叠加替换后的值（优先级最高）。
                    EnvironmentPlaceholderResolver.LoadDotEnvFile(AppContext.BaseDirectory);
                    var resolved = EnvironmentPlaceholderResolver.BuildResolvedOverrides(config.Build());
                    config.AddInMemoryCollection(resolved);
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
                    // 阶段 6.3 P2：自动回滚门配置嵌套于 Gateway:DsRollback 节（可选——未配置用类内默认值）。
                    services.PostConfigure<Configuration.GatewayOptions>(options => ApplyGatewayConfiguration(context.Configuration, options));
                    services.Configure<NetworkOptions>(context.Configuration.GetSection("Network"));
                    services.Configure<OrleansOptions>(context.Configuration.GetSection("Orleans"));
                    services.Configure<OrleansClusteringDbOptions>(context.Configuration.GetSection("ClusteringSiloOptions"));
                    services.Configure<SyncReconciliationOptions>(context.Configuration.GetSection("SyncReconciliation"));
                    // [连接精简治理 spec 4.4.4] 注册连接治理配置（非法值由 ConnectionGovernanceOptionsValidator 兜底回退默认）。
                    services.AddOptions<Configuration.ConnectionGovernanceOptions>()
                        .Bind(context.Configuration.GetSection("ConnectionGovernance"));

                    // 注册安全和认证服务
                    services.AddSingleton<AuthenticationValidator>();
                    services.AddSingleton<SecurityManager>();
                    
                    // 注册用户鉴权令牌提供器
                    services.AddSingleton<UserAuthTokenProvider>(provider =>
                    {
                        var config = provider.GetRequiredService<IConfiguration>();
                        var logger = provider.GetRequiredService<ILogger<UserAuthTokenProvider>>();
                        var secretKey = config["Security:AuthTokenSecret"];
                        if (string.IsNullOrWhiteSpace(secretKey) || secretKey.StartsWith("${"))
                        {
                            logger.LogError("未配置 Security:AuthTokenSecret（或环境变量 GATEWAY_AUTH_TOKEN_SECRET 未注入），生产环境必须配置自定义密钥！当前使用临时密钥，仅限开发环境使用。");
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
                    services.AddSingleton<Horizon.Game.Core.Interfaces.ISessionBindingValidator, SessionBindingValidator>();
                    services.AddSingleton<PresenceRefreshService>();
                    services.AddSingleton<Horizon.Game.Core.World.IShardRouter>(sp =>
                    {
                        var opts = sp.GetRequiredService<IOptionsMonitor<Horizon.Game.Gateway.Configuration.GatewayOptions>>();
                        return new Horizon.Game.Core.World.ZoneBasedShardRouter(opts.CurrentValue.ShardCount);
                    });
                    // 阶段 4.3：跨 Shard 边界双重订阅的空间分片解析器（2D X/Z 网格划分；
                    // ChunksPerShardX/ChunksPerShardZ/ShardsPerRowX 任一为 0 → 仅主 Shard，行为不变）。
                    services.AddSingleton<Horizon.Game.Core.World.IShardRegionResolver>(sp =>
                    {
                        var opts = sp.GetRequiredService<IOptionsMonitor<Horizon.Game.Gateway.Configuration.GatewayOptions>>();
                        var router = sp.GetRequiredService<Horizon.Game.Core.World.IShardRouter>();
                        return new Horizon.Game.Core.World.ShardRegionResolver(
                            router,
                            opts.CurrentValue.ChunksPerShardX,
                            opts.CurrentValue.ChunksPerShardZ,
                            opts.CurrentValue.ShardsPerRowX);
                    });
                    // 共享连接态（SyncPacketHandler 与 PlayerDespawnScheduler 共享：边界订阅跟踪供 despawn 精确清理）。
                    services.AddSingleton<Horizon.Game.Core.Handlers.SyncSessionState>();

                    // 阶段 0.6：协议版本匹配策略（RequireStrictVersionMatch 可配置）。
                    // false（默认，宽松）→ ProtocolVersionPolicy：版本 < MinimumSupported 拒绝，否则告警放行（开发/过渡期）；
                    // true（生产）→ StrictProtocolVersionPolicy：任何 != Current 都拒绝。
                    services.AddSingleton<Horizon.Game.Core.Interfaces.IProtocolVersionPolicy>(sp =>
                    {
                        var opts = sp.GetRequiredService<IOptionsMonitor<Configuration.GatewayOptions>>();
                        return opts.CurrentValue.RequireStrictVersionMatch
                            ? Horizon.Game.Core.Handlers.StrictProtocolVersionPolicy.Instance
                            : Horizon.Game.Core.Handlers.ProtocolVersionPolicy.Instance;
                    });

                    // 阶段 2：UE5 DS 灰度通道（UseDS 开关，默认 false）。
                    // false → 不注册数据面实现，SyncPacketHandler 以 null 兜底，完全走旧 Orleans 路径（零行为变化）；
                    // true → 注册 DSRegistry + DsSyncDataPlane，Handshake/Input/Reconnect 按 shard 转发到
                    // 已注册的 UE5 DS，无可用 DS / 转发失败回退 Orleans（灰度期双轨，任一时点可回切）。
                    services.AddSingleton<Services.DSRegistry>();
                    // 阶段 6.3 P2：自动回滚门协调器（override 状态机；数据面与判定宿主共享）。
                    // 注入持久化存储：override 跨网关重启保持（构造时恢复上次回切状态）；
                    // 键 cluster:{clusterId}:ds_rollback_override:{gatewayId}（TTL 自清理）。
                    services.AddSingleton<Services.IRollbackStateStore>(sp =>
                    {
                        var opts = sp.GetRequiredService<IOptionsMonitor<Configuration.GatewayOptions>>();
                        var logger = sp.GetRequiredService<ILogger<Services.RedisRollbackStateStore>>();
                        return new Services.RedisRollbackStateStore(
                            opts.CurrentValue.RedisConnectionString, opts.CurrentValue.ClusterId,
                            opts.CurrentValue.GatewayId, logger);
                    });
                    services.AddSingleton<Services.IRollbackCoordinator>(sp =>
                    {
                        var opts = sp.GetRequiredService<IOptionsMonitor<Configuration.GatewayOptions>>();
                        var logger = sp.GetRequiredService<ILogger<Services.RollbackCoordinator>>();
                        var store = sp.GetRequiredService<Services.IRollbackStateStore>();
                        return new Services.RollbackCoordinator(opts, stateStore: store, logger: logger);
                    });
                    // Sprint 2：Gateway→DS 上行 TCP 连接池（按 (Host, Port) 复用长连接，消除每包新建 TCP 的三次握手 + TIME_WAIT 开销）。
                    // 恒注册为单例；是否真正启用由 GatewayOptions.DsRouteUsePersistentConnection 开关逐调用热切换（默认 false = 旧路径，行为不变）。
                    // 容器关闭时自动 Dispose（清空所有空闲连接与后台空闲回收 Timer）。
                    services.AddSingleton<Services.DsTcpConnectionPool>();
                    services.AddSingleton<Horizon.Game.Core.Interfaces.IDSyncDataPlane>(sp =>
                    {
                        var opts = sp.GetRequiredService<IOptionsMonitor<Configuration.GatewayOptions>>();
                        var logger = sp.GetRequiredService<ILogger<Services.DsSyncDataPlane>>();
                        var registry = sp.GetRequiredService<Services.DSRegistry>();
                        var coordinator = sp.GetRequiredService<Services.IRollbackCoordinator>();
                        // 可选依赖：连接池未注册（理论上不会发生，已在上方 AddSingleton）或开关关闭时，DsSyncDataPlane 自动回退到每包新建 TCP 的旧路径。
                        var pool = sp.GetService<Services.DsTcpConnectionPool>();
                        // 传入 IOptionsMonitor：IsEnabled 每次路由实时读取 UseDS，
                        // 支持不重启网关的热切换（appsettings 热重载后立即生效）；
                        // 自动回滚门 override 生效期间强制回切（IsEnabled=false）。
                        return new Services.DsSyncDataPlane(registry, logger, opts, rollbackCoordinator: coordinator, pool: pool);
                    });
                    // ===== MMORPG 开发计划 M2.1/M2.3：DS 上行通道（控制面 + 快照回传）=====
                    // 在此之前 DSRegistry.RegisterOrUpdate/Heartbeat 全仓无调用方（缺口 G3），
                    // 且 DS 快照没有回到分发管线的入口（缺口 G4）。以下三件把这两条链路接通：
                    //   1) DsDownlinkIngress —— DS 上行帧 → FanoutEvent（复用既有分发管线治理）
                    //   2) DsControlPlane    —— 注册/心跳/摘除语义（纯逻辑，已被单测覆盖）
                    //   3) DsUplinkServer    —— TouchSocket 监听（IO 薄壳；UseDS=false 或 DsUplinkPort=0 时不启动）
                    services.AddSingleton<Horizon.Game.Core.Sim.Server.DsDownlinkIngress>(sp =>
                    {
                        var fanout = sp.GetRequiredService<Services.GatewayZoneShardFanoutSource>();
                        var logger = sp.GetRequiredService<ILogger<Horizon.Game.Core.Sim.Server.DsDownlinkIngress>>();
                        return new Horizon.Game.Core.Sim.Server.DsDownlinkIngress(
                            fanout,
                            reason => logger.LogDebug("DsDownlinkIngress：条目丢弃。Reason={Reason}", reason));
                    });
                    // MMORPG 开发计划 M4.3/M4.4 收尾：DS 跨层玩法事件 → 真实业务 Grain。
                    // 在此之前 IInventoryGrain / 玩家维度 IQuestGrain 全仓无生产调用方；
                    // 本注册是它们的首个真实调用方（掉落→背包、击杀→任务进度）。
                    // 用有界队列 + 后台泵，避免在 TouchSocket 接收线程上直接 await Grain RPC。
                    services.AddSingleton<Horizon.Game.Core.Sim.Server.OrleansGameplayEventSink>(sp =>
                    {
                        var clusterClient = sp.GetRequiredService<IClusterClient>();
                        var logger = sp.GetRequiredService<ILogger<Horizon.Game.Core.Sim.Server.OrleansGameplayEventSink>>();
                        var inventory = new Horizon.Game.Core.Sim.Server.OrleansInventoryWriter(clusterClient);
                        var quests = new Horizon.Game.Core.Sim.Server.OrleansQuestProgressWriter(clusterClient);
                        return new Horizon.Game.Core.Sim.Server.OrleansGameplayEventSink(
                            inventory, quests,
                            message => logger.LogDebug("{Message}", message));
                    });
                    services.AddSingleton<Horizon.Game.Core.Sim.Server.IDsGameplayEventSink>(sp =>
                        sp.GetRequiredService<Horizon.Game.Core.Sim.Server.OrleansGameplayEventSink>());

                    services.AddSingleton<Services.DsControlPlane>(sp =>
                    {
                        var opts = sp.GetRequiredService<IOptionsMonitor<Configuration.GatewayOptions>>();
                        var registry = sp.GetRequiredService<Services.DSRegistry>();
                        var downlink = sp.GetRequiredService<Horizon.Game.Core.Sim.Server.DsDownlinkIngress>();
                        var logger = sp.GetRequiredService<ILogger<Services.DsControlPlane>>();
                        var gameplaySink = sp.GetRequiredService<Horizon.Game.Core.Sim.Server.IDsGameplayEventSink>();
                        // 逐调用读取 UseDS：支持不重启网关热切换（与 DsSyncDataPlane 同源语义）。
                        return new Services.DsControlPlane(
                            registry, downlink,
                            isEnabled: () => opts.CurrentValue.UseDS,
                            expectedToken: opts.CurrentValue.DsControlToken,
                            logger: logger,
                            gameplaySink: gameplaySink);
                    });

                    // 阶段 2.5+：DSRegistry 持久化快照存储（Redis 单 blob）。恒注册、惰性初始化——
                    // 仅由维护服务在 UseDS=true 时调用（false 时零副作用）。消除网关重启/新实例
                    // 冷启动后「等 DS 下一轮心跳」的恢复空窗（见 DSRegistryMaintenanceHostedService）。
                    services.AddSingleton<Services.IDSnapshotStore>(sp =>
                    {
                        var opts = sp.GetRequiredService<IOptionsMonitor<Configuration.GatewayOptions>>();
                        var logger = sp.GetRequiredService<ILogger<Services.DSRegistrySnapshotStore>>();
                        return new Services.DSRegistrySnapshotStore(
                            logger, opts.CurrentValue.RedisConnectionString, opts.CurrentValue.ClusterId);
                    });
                    // 阶段 6.3 P2：跨进程修正计数汇聚存储（Redis；Silo 写 / 网关读）。
                    // 恒注册、惰性初始化（仅采样器启用修正率信号时读取）。键 cluster:{clusterId}:sync:corrections:total
                    // 与 Silo 侧 CorrectionPublisherHostedService 写侧一致。
                    services.AddSingleton<Horizon.Game.Core.Observability.ICorrectionCounterStore>(sp =>
                    {
                        var opts = sp.GetRequiredService<IOptionsMonitor<Configuration.GatewayOptions>>();
                        var logger = sp.GetRequiredService<ILogger<Horizon.Strategy.Storage.Redis.RedisCorrectionCounterStore>>();
                        return new Horizon.Strategy.Storage.Redis.RedisCorrectionCounterStore(
                            opts.CurrentValue.RedisConnectionString, opts.CurrentValue.ClusterId, logger: logger);
                    });
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

                    // 游戏内聊天出站推送（ChatHandler 依赖）：经连接管理器把聊天通知写到目标角色连接。
                    services.AddSingleton<Horizon.Game.Core.Handlers.IChatPushService, Horizon.Game.Gateway.Services.ChatPushService>();

                    // 注册角色指纹服务（基于 Redis，防止同一角色同时在线）
                    services.AddSingleton<Horizon.Game.Core.Interfaces.ICharacterFingerprintService>(provider =>
                    {
                        var gatewayOpts = provider.GetRequiredService<IOptionsMonitor<Configuration.GatewayOptions>>();
                        var fpLogger = provider.GetRequiredService<ILogger<Services.CharacterFingerprintService>>();
                        var connMgr = provider.GetRequiredService<IConnectionManager>();
                        var connectionString = gatewayOpts.CurrentValue.RedisConnectionString ?? "localhost:6379";
                        var gatewayId = gatewayOpts.CurrentValue.GatewayId ?? "Unknown";
                        return new Services.CharacterFingerprintService(connectionString, fpLogger, gatewayId, connMgr);
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
                        // 阶段 0.1：配置必需——删除 127.0.0.1 本地默认回退，缺失抛清晰错误。
                        var connStr = Horizon.Game.Message.Configuration.ConnectionStrings.Require(
                            config.GetSection("Redis:ConnectionString").Value
                                ?? config.GetSection("Gateway:RedisConnectionString").Value,
                            "Redis:ConnectionString", "角色在线状态 Redis 存储", "GATEWAY_REDIS_CONNECTION_STRING");
                        return new RedisConnection(connStr);
                    });
                    services.AddSingleton<Horizon.Game.Core.Sim.Server.ICharacterPresenceStore>(provider =>
                    {
                        var redisConnection = provider.GetRequiredService<RedisConnection>();
                        var logger = provider.GetService<ILogger<RedisCharacterPresenceStore>>();
                        return new RedisCharacterPresenceStore(redisConnection, logger);
                    });

                    // ===== 角色位置 Redis 永久存储（双轨制架构） =====
                    // 使用独立 Redis Key 永久存储角色最后位置（无 TTL），服务器重启激活 Grain 时从此处恢复。
                    // Key: character:position:{characterId} → Hash { x, y, z, yaw, updatedAt }
                    services.AddSingleton<Horizon.Game.Core.Sim.Server.ICharacterPositionStore>(provider =>
                    {
                        var redisConnection = provider.GetRequiredService<RedisConnection>();
                        var logger = provider.GetService<ILogger<RedisCharacterPositionStore>>();
                        return new RedisCharacterPositionStore(redisConnection, logger);
                    });

                    // 注册网络服务
                    services.AddSingleton<ITcpService, TcpService>();
                    // 构造参数 dsDataPlane 为可空+默认 null：UseDS=false 时容器内没有 IDSyncDataPlane，
                    // DI 会使用默认值（行为与改动前完全一致）；启用时自动注入，使 Despawn/租约判定
                    // 能感知"该会话是否由 UE5 DS 承载权威"，从而跳过基于 ZoneShardGrain 的幽灵映射判定。
                    services.AddSingleton<PlayerDespawnScheduler>();
                    // 阶段 3：GameNetworkServer 拆分——连接生命周期与数据接收流水线独立注册。
                    services.AddSingleton<Network.ConnectionLifecycleManager>();
                    services.AddSingleton<Network.ClientPacketPipeline>();
                    services.AddSingleton<GameNetworkServer>();

                    // P6 运行时连线：ZoneShard fanout → GatewaySyncDispatcher → IGameConnection。
                    // Sprint 4：Channel 容量从配置读取（默认 65536），代替原固定 8192，支撑更高并发。
                    services.AddSingleton<Services.GatewayZoneShardFanoutSource>(sp =>
                    {
                        var opts = sp.GetRequiredService<IOptionsMonitor<Configuration.GatewayOptions>>();
                        var logger = sp.GetRequiredService<ILogger<Services.GatewayZoneShardFanoutSource>>();
                        return new Services.GatewayZoneShardFanoutSource(opts.CurrentValue.DsDownlinkChannelCapacity, logger);
                    });
                    services.AddSingleton<Horizon.Orleans.Interface.World.IZoneShardFanoutObserver>(sp => sp.GetRequiredService<Services.GatewayZoneShardFanoutSource>());
                    services.AddSingleton<Horizon.Game.Core.Sim.Server.IZoneShardFanoutSource>(sp => sp.GetRequiredService<Services.GatewayZoneShardFanoutSource>());
                    services.AddSingleton<Horizon.Game.Core.Sim.Server.ISessionRegistry>(sp => new Services.ConnectionManagerSessionRegistry(
                        sp.GetRequiredService<Services.IConnectionManager>()));
                    services.AddSingleton<Horizon.Game.Core.Sim.Server.IClientPacketSink, Services.GameConnectionPacketSink>();
                    services.AddSingleton(sp =>
                    {
                        var dispatcher = new Horizon.Game.Core.Sim.Server.GatewaySyncDispatcher(
                            sp.GetRequiredService<Horizon.Game.Core.Sim.Server.IZoneShardFanoutSource>(),
                            sp.GetRequiredService<Horizon.Game.Core.Sim.Server.ISessionRegistry>(),
                            sp.GetRequiredService<Horizon.Game.Core.Sim.Server.IClientPacketSink>(),
                            logger: sp.GetRequiredService<ILogger<Horizon.Game.Core.Sim.Server.GatewaySyncDispatcher>>(),
                            enabled: sp.GetRequiredService<IOptions<Configuration.GatewayOptions>>().Value.UseSyncPacketDispatch);

                        // 会话离线残留自愈（2026-08）：同一会话持续 ~45s 无法投递（远超 15s Despawn
                        // 宽限与正常断线重连窗口）说明 Despawn 未清理其 shard 侧 AOI 订阅
                        // （映射缺失被跳过 / 网关重启丢失边界分片记录等），grain 会继续向死会话
                        // 广播并被 dispatcher 持续丢弃。此处触发立即 Despawn：
                        // UnregisterEntity + RemoveSession（主 shard + 边界 shard）+ GoOffline，
                        // 闭环止血。正常断线/重连路径不受影响（45s 内要么恢复投递、要么常规
                        // Despawn 已清理订阅使丢包停止）。
                        dispatcher.StaleOfflineSessionDetected += sessionId =>
                        {
                            try
                            {
                                var scheduler = sp.GetRequiredService<PlayerDespawnScheduler>();
                                _ = scheduler.DespawnImmediatelyAsync(sessionId);
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"[StaleOfflineSession] 清理离线残留会话失败: sessionId={sessionId}, {ex.Message}");
                            }
                        };
                        return dispatcher;
                    });

                    // Task C.5.6：注册场景对象持久化存储到 DI（Singleton，复用 Game 库连接字符串）。
                    services.AddSingleton<Horizon.Game.Core.Persistence.ISceneObjectPersistenceStore>(sp =>
                    {
                        var config = sp.GetRequiredService<IConfiguration>();
                        var logger = sp.GetService<ILogger<Horizon.Game.Core.Persistence.SqlServerSceneObjectPersistenceStore>>();
                        // 阶段 0.1：配置必需——删除 Data Source=. 本地默认回退，缺失抛清晰错误。
                        var connStr = Horizon.Game.Message.Configuration.ConnectionStrings.Require(
                            config.GetSection("DatabaseOptions:Game")["ConnectionString"]
                                ?? config.GetSection("ClusteringSiloOptions:SqlServer")["ConnectionString"],
                            "DatabaseOptions:Game:ConnectionString", "场景对象持久化 SQL Server", "DB_GAME_CONNECTION_STRING");
                        return new Horizon.Game.Core.Persistence.SqlServerSceneObjectPersistenceStore(connStr, logger);
                    });

                    // 注册后台服务
                    services.AddHostedService<GatewayHostedService>();
                    services.AddHostedService<GatewayRegistryHostedService>();
                    services.AddHostedService<Services.SyncDispatcherHostedService>();

                    // 阶段 2.5：DSRegistry 路由表自愈维护（UseDS=false 时内部直接返回，零副作用）
                    services.AddHostedService<Services.DSRegistryMaintenanceHostedService>();

                    // MMORPG 开发计划 M2.1/M2.2：DS 上行监听（注册/心跳/摘除 + 权威快照回传）。
                    // UseDS=false 或 DsUplinkPort<=0 时 StartAsync 直接返回，不占用端口、零行为变化。
                    // 同时以具体类型注册：既作为 HostedService 启动，又供
                    // IDsSessionNotifier（会话结束通知）复用**同一条** DS 上行连接反向下发。
                    services.AddSingleton<Services.DsUplinkServer>();
                    services.AddHostedService(sp => sp.GetRequiredService<Services.DsUplinkServer>());
                    services.AddSingleton<Horizon.Game.Core.Sim.Server.IDsSessionNotifier>(
                        sp => sp.GetRequiredService<Services.DsUplinkServer>());

                    // 阶段 6.3 P2：自动回滚门（采样→连续窗口判定→回切 override→自愈恢复；UseDS=false 时空采样零副作用）
                    // 采样器注入跨进程修正计数存储：启用 DsRollback:CorrectionsPerWindowThreshold>0 时修正率
                    // 成为第三个坏窗口信号（回退率 + 延迟 + 修正率）；未启用（默认 0）时存储注入无副作用。
                    services.AddSingleton<Services.IRollbackWindowSampler>(sp =>
                    {
                        var logger = sp.GetRequiredService<ILogger<Services.RollbackWindowSampler>>();
                        var store = sp.GetService<Horizon.Game.Core.Observability.ICorrectionCounterStore>();
                        return new Services.RollbackWindowSampler(logger, store);
                    });
                    services.AddHostedService<Services.RollbackGateHostedService>();

                    // 阶段 6.3 P2⑨：灰度对比面板（每窗口汇总 route.fallback 按 reason + corrections 按 reason
                    // 为 Prometheus 对比指标集——attempts{result}/fallback{reason}/corrections{reason} gauge）。
                    // 复用 ICorrectionCounterStore（按 reason 哈希，Silo 写/网关读）；恒注册：UseDS=false 时
                    // 仅修正信号有值（attempts/fallback 恒 0），零副作用。窗口与自动回滚门对齐（DsRollback:WindowSeconds）。
                    services.AddHostedService<Services.GrayComparisonHostedService>();

                    // 角色在线状态监控后台服务（双轨制架构：每 10 秒扫描过期 Redis presence，兜底清理离线角色）
                    services.AddHostedService<Services.CharacterPresenceMonitorHostedService>();

                    // 花卉市场数据采集后台服务
                    services.AddHostedService<Services.FlowerDataCollectionService>();
                    // 昆明斗南花卉市场真实行情抓取（平台自有花卉数据的权威行情源，替代已移除的朝阳市场抓取服务）
                    services.AddHostedService<Services.DounanMarketDataFetcher>();
                    services.AddHostedService<Services.FlowerWeatherFetcher>();

                    // 健康检查
                    services.AddHealthChecks()
                        .AddCheck<GatewayHealthCheck>("gateway")
                        .AddCheck<NetworkHealthCheck>("network");

                    // OpenTelemetry监控（APM + Prometheus指标导出）
                    var prometheusPort = context.Configuration.GetValue<int>("Monitoring:PrometheusPort", 9465);
                    // 阶段 6.3 P2⑩：OTLP collector 端点（Monitoring:Otlp:Endpoint，未配置则不推送，仅 Prometheus）。
                    var otlpEndpoint = context.Configuration["Monitoring:Otlp:Endpoint"];
                    services.AddGatewayOpenTelemetry(prometheusPort: prometheusPort, otlpEndpoint: otlpEndpoint);
                })
                .UseOrleansClient((context, client) =>
                {
                    // 从应用程序配置中获取Orleans集群数据库配置
                    var networkSettings = new OrleansClusteringDbOptions();
                    context.Configuration.GetSection("ClusteringSiloOptions").Bind(networkSettings);

                    var settings = new OrleansOptions();
                    context.Configuration.GetSection("Orleans").Bind(settings);

                    // ===== Redis 集群配置（主方案） =====
                    // 阶段 0.1：配置必需——删除 127.0.0.1 本地默认回退，缺失抛清晰错误。
                    var redisConnectionStr = Horizon.Game.Message.Configuration.ConnectionStrings.Require(
                        context.Configuration.GetSection("Redis:ConnectionString").Value,
                        "Redis:ConnectionString", "Orleans 客户端 Redis 集群", "GATEWAY_REDIS_CONNECTION_STRING");
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
                                // [修复] 显式登记程序集到 Client 端序列化器清单。
                                // 与 Silo 侧 ConfigureOrleansServices 的写法对齐：Silo 一直有 AddAssembly，
                                // 网关此前只有 AddNewtonsoftJsonSerializer —— 而 Client 模式下仅靠
                                // ApplicationPart 扫描不足以装入 Horizon.Orleans.Interface 的
                                // Invokable_*/CompoundTypeAlias 清单，导致反序列化 Silo 响应时报
                                //   TypeLoadException: Unable to parse or load type
                                //   "Horizon.Orleans.Interface.IUserCharacterManagerGrain,Horizon.Orleans.Interface"
                                // 以及 IFlowerMarketGrain / IFlowerSpeciesGrain / IFlowerDataPoolGrain /
                                // IZoneShardGrain 等同一程序集内的接口。
                                serializerBuilder.AddAssembly(
                                    typeof(global::Horizon.Orleans.Interface.IUserCharacterManagerGrain).Assembly);
                                serializerBuilder.AddAssembly(
                                    typeof(global::Horizon.Game.Message.Network.HorizonMessagePacket).Assembly);

                                serializerBuilder.AddNewtonsoftJsonSerializer(
                                    isSupported: type => type.Namespace != null && type.Namespace.StartsWith("Horizon.Share"));
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
