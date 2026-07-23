using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Serialization.Configuration;
using System.Threading;
using Horizon.Orleans.Silo.Configuration;

namespace Horizon.Orleans.Silo.Extensions;

/// <summary>
/// Horizon Orleans超时配置扩展类，用于配置Orleans Silo的各种超时设置
/// 包括消息响应超时、连接超时、网关刷新周期等关键配置项
/// </summary>
public static class HorizonTimeoutConfigurationExtensions
{
    /// <summary>
    /// 为指定的Horizon Orleans Silo应用超时配置
    /// </summary>
    /// <param name="siloBuilder">Orleans Silo构建器</param>
    /// <param name="timeoutConfig">超时配置对象</param>
    /// <returns>配置完成的Silo构建器</returns>
    public static ISiloBuilder ApplyHorizonTimeoutConfiguration(
        this ISiloBuilder siloBuilder,
        HorizonTimeoutConfiguration timeoutConfig)
    {
        // 将超时配置注册到依赖注入容器中
        siloBuilder.ConfigureServices(services =>
        {
            services.AddSingleton(timeoutConfig);
        });

        // 配置Silo消息传递选项
        siloBuilder.Configure<SiloMessagingOptions>(options =>
        {
            options.ResponseTimeout = timeoutConfig.ResponseTimeout;
            options.ResponseTimeoutWithDebugger = timeoutConfig.ResponseTimeoutWithDebugger;
            options.MaxForwardCount = timeoutConfig.MaxForwardCount;
            options.DropExpiredMessages = true; // 自动丢弃过期消息，避免堆积
        });

        // 配置网关选项
        siloBuilder.Configure<GatewayOptions>(options =>
        {
            options.GatewayListRefreshPeriod = timeoutConfig.GatewayListRefreshPeriod;
        });

        // 配置调度选项
        siloBuilder.Configure<SchedulingOptions>(options =>
        {
            options.DelayWarningThreshold = timeoutConfig.DelayWarningThreshold;
        });

        // 配置集群成员选项
        siloBuilder.Configure<ClusterMembershipOptions>(options =>
        {
            options.DefunctSiloExpiration = timeoutConfig.ClusterMembershipTimeout;
            options.DefunctSiloCleanupPeriod = TimeSpan.FromSeconds(60);
            options.IAmAliveTablePublishTimeout = TimeSpan.FromSeconds(60);
        });

        // 配置连接选项
        siloBuilder.Configure<ConnectionOptions>(options =>
        {
            options.OpenConnectionTimeout = timeoutConfig.GatewayConnectionTimeout;
        });

        return siloBuilder;
    }

    /// <summary>
    /// 验证并记录超时配置的详细信息
    /// </summary>
    /// <param name="siloBuilder">Orleans Silo构建器</param>
    /// <param name="timeoutConfig">超时配置对象</param>
    /// <param name="logger">日志记录器</param>
    /// <returns>配置完成的Silo构建器</returns>
    public static ISiloBuilder ValidateAndLogTimeoutConfiguration(
        this ISiloBuilder siloBuilder,
        HorizonTimeoutConfiguration timeoutConfig,
        ILogger logger)
    {
        // 验证配置是否有效
        var warnings = timeoutConfig.ValidateConfiguration();

        if (warnings.Any())
        {
            logger.LogWarning("发现超时配置警告，请检查配置是否合理：");
            foreach (var warning in warnings)
            {
                logger.LogWarning("警告：{Warning}", warning);
            }
        }
        else
        {
            logger.LogInformation("超时配置验证通过，所有配置项都正常");
        }

        // 记录网关超时配置详情
        logger.LogInformation("网关超时配置详情：");
        var gatewayTimeouts = timeoutConfig.GetGatewayTimeouts();
        foreach (var timeout in gatewayTimeouts)
        {
            logger.LogInformation("   配置项 {Name}: {Seconds}秒", timeout.Key, timeout.Value.TotalSeconds);
        }

        // 记录核心超时配置详情
        logger.LogInformation("核心超时配置详情：");
        logger.LogInformation("   响应超时时间: {Seconds}秒", timeoutConfig.ResponseTimeout.TotalSeconds);
        logger.LogInformation("   连接超时时间: {Seconds}秒", timeoutConfig.ConnectionTimeout.TotalSeconds);
        logger.LogInformation("   最大转发次数: {Count}", timeoutConfig.MaxForwardCount);

        return siloBuilder;
    }

    /// <summary>
    /// 应用优化的端点配置，包括Silo端口、网关端口和广播IP地址
    /// </summary>
    /// <param name="siloBuilder">Orleans Silo构建器</param>
    /// <param name="timeoutConfig">超时配置对象</param>
    /// <param name="siloPort">Silo端口</param>
    /// <param name="gatewayPort">网关端口</param>
    /// <param name="advertisedIP">广播IP地址</param>
    /// <returns>配置完成的Silo构建器</returns>
    public static ISiloBuilder ApplyOptimizedEndpointConfiguration(
        this ISiloBuilder siloBuilder,
        HorizonTimeoutConfiguration timeoutConfig,
        int siloPort,
        int gatewayPort,
        System.Net.IPAddress advertisedIP)
    {
        siloBuilder.Configure<EndpointOptions>(options =>
        {
            options.SiloPort = siloPort;
            options.GatewayPort = gatewayPort;
            options.AdvertisedIPAddress = advertisedIP;

            options.SiloListeningEndpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Any, siloPort);
            options.GatewayListeningEndpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Any, gatewayPort);
        });
        
        // 配置连接选项 - 适用于Orleans 9.x的ConnectionOptions
        siloBuilder.Configure<ConnectionOptions>(options =>
        {
            // Orleans 9.x中的ConnectionOptions配置
            options.OpenConnectionTimeout = timeoutConfig.GatewayConnectionTimeout;
        });        return siloBuilder;
    }    /// <summary>
    /// 应用性能优化配置
    /// </summary>
    /// <param name="siloBuilder">Orleans Silo构建器</param>
    /// <param name="timeoutConfig">超时配置对象</param>
    /// <returns>配置完成的Silo构建器</returns>
    public static ISiloBuilder ApplyPerformanceOptimizations(
        this ISiloBuilder siloBuilder,
        HorizonTimeoutConfiguration timeoutConfig)
    {
        // 配置Grain集合选项 - 适用于Orleans 9.x
        siloBuilder.Configure<GrainCollectionOptions>(options =>
        {
            // 确保 CollectionAge 大于 CollectionQuantum
            // CollectionQuantum 默认为 1 分钟，所以 CollectionAge 至少要 2 分钟
            var minCollectionAge = TimeSpan.FromMinutes(2);
            var configuredCollectionAge = timeoutConfig.GrainDeactivationTimeout;
            
            // 使用较大的值作为 CollectionAge
            options.CollectionAge = configuredCollectionAge > minCollectionAge 
                ? configuredCollectionAge 
                : minCollectionAge;
                
            // 设置 CollectionQuantum 为 1 分钟
            options.CollectionQuantum = TimeSpan.FromMinutes(1);
            
            // 设置失活超时
            options.DeactivationTimeout = TimeSpan.FromSeconds(30);

            // 配置Grain类型特定的回收年龄
            // 战斗Grain: 短生命周期，战斗结束后快速回收释放资源
            var combatGrainName = typeof(Horizon.Orleans.Grains.CombatGrain).FullName;
            if (combatGrainName != null)
                options.ClassSpecificCollectionAge[combatGrainName] = TimeSpan.FromMinutes(2);
            // 认证Grain: 中短生命周期，可快速重新激活
            var passportGrainName = typeof(Horizon.Orleans.Grains.PassportGrain).FullName;
            if (passportGrainName != null)
                options.ClassSpecificCollectionAge[passportGrainName] = TimeSpan.FromMinutes(5);
            // 角色Grain: 中等生命周期，玩家在线期间保持活跃
            var characterGrainName = typeof(Horizon.Orleans.Grains.CharacterGrain).FullName;
            if (characterGrainName != null)
                options.ClassSpecificCollectionAge[characterGrainName] = TimeSpan.FromMinutes(10);
            // ZoneShardGrain: 超长生命周期（关键修复）。
            // ZoneShardGrain 持有所有在线实体的内存状态（_simulatedEntities）、AOI 订阅和 fanout 观察者，
            // 一旦被 Grain Collection 停用，所有实体状态永久丢失，其他客户端将看不到任何角色。
            // RegisterTimer 不能阻止 Grain Collection（Orleans 只根据最后一次收到外部消息的时间判断空闲），
            // 因此必须显式配置为超长 CollectionAge 防止被回收。
            // ⚠️ 关键：不能使用 TimeSpan.MaxValue，否则 ActivationCollector.MakeTicketFromTimeSpan
            // 执行 now + TimeSpan.MaxValue 时会 DateTime 溢出，抛出 ArgumentOutOfRangeException，
            // 导致 grain 激活失败、fanout 订阅全部失败、角色互相看不见。
            // 使用 50 年（安全范围内，不会溢出 DateTime.MaxValue）。
            var zoneShardGrainName = typeof(Horizon.Orleans.Grains.World.ZoneShardGrain).FullName;
            if (zoneShardGrainName != null)
                options.ClassSpecificCollectionAge[zoneShardGrainName] = TimeSpan.FromDays(365 * 50);
        });
        
        // 配置线程池优化 - 基于当前系统核心数
        siloBuilder.ConfigureServices(services =>
        {
            // 优化线程池配置以提高并发性能
            ThreadPool.SetMinThreads(Environment.ProcessorCount * 2, Environment.ProcessorCount * 2);
        });

        return siloBuilder;
    }

    /// <summary>
    /// 创建默认的Horizon超时配置
    /// </summary>
    /// <returns>默认超时配置对象</returns>
    public static HorizonTimeoutConfiguration CreateDefaultTimeoutConfiguration()
    {
        return new HorizonTimeoutConfiguration();
    }
    
    /// <summary>
    /// 从应用程序配置中创建超时配置对象
    /// </summary>
    /// <param name="configuration">配置对象</param>
    /// <param name="sectionName">配置节名称，默认为"HorizonTimeoutConfiguration"</param>
    /// <returns>从配置中创建的超时配置对象</returns>
    public static HorizonTimeoutConfiguration CreateFromConfiguration(
        IConfiguration configuration,
        string sectionName = "HorizonTimeoutConfiguration")
    {
        var timeoutConfig = new HorizonTimeoutConfiguration();
        var section = configuration.GetSection(sectionName);

        // 使用配置绑定器绑定配置值
        if (section.Exists())
        {
            // 使用ConfigurationBinder绑定配置
            Microsoft.Extensions.Configuration.ConfigurationBinder.Bind(section, timeoutConfig);
        }        return timeoutConfig;
    }
}