using System.Reflection;

using Horizon.Core.Options;
using Horizon.Core.Security;
using Horizon.IM.Core;
using Horizon.IM.Core.Adapters;
using Horizon.IM.Gateway.Configuration;
using Horizon.IM.Gateway.Network;
using Horizon.IM.Gateway.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;

using TouchSocket.Core;
using StackExchange.Redis;

namespace Horizon.IM.Gateway;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var host = CreateHostBuilder(args).Build();
        await host.RunAsync().ConfigureAwait(false);
    }

    private static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true);
                config.AddEnvironmentVariables();
                config.AddCommandLine(args);
            })
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.AddConfiguration(context.Configuration.GetSection("Logging"));
            })
            .ConfigureServices((context, services) =>
            {
                services.Configure<NetworkOptions>(context.Configuration.GetSection("Network"));
                services.Configure<OrleansOptions>(context.Configuration.GetSection("Orleans"));
                services.PostConfigure<OrleansOptions>(options => ApplyClusterOptionOverrides(context.Configuration, options));
                services.Configure<OrleansClusteringDbOptions>(context.Configuration.GetSection("ClusteringSiloOptions"));
                services.Configure<Configuration.GatewayOptions>(context.Configuration.GetSection("Gateway"));
                services.PostConfigure<Configuration.GatewayOptions>(options => ApplyGatewayRegistryDefaults(context.Configuration, options));

                services.AddSingleton<ILog>(_ => ConsoleLogger.Default);
                services.AddSingleton<IMMessageAdapter>();
                services.AddSingleton<IIMConnectionManager, IMConnectionManager>();
                services.AddSingleton<IMGatewayPushService>();
                
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

                services.AddAllIMMessageHandlers(Assembly.GetAssembly(typeof(IMMessageHandlerBase))!);
                services.AddSingleton<IMNetworkServer>();
                services.AddHostedService<IMGatewayHostedService>();
                services.AddHostedService<GatewayRegistryHostedService>();
            })
            .UseOrleansClient((context, client) =>
            {
                var dbOptions = new OrleansClusteringDbOptions();
                context.Configuration.GetSection("ClusteringSiloOptions").Bind(dbOptions);

                var options = ResolveOrleansOptions(context.Configuration);

                // ===== Redis 集群配置（主方案） =====
                var redisConnectionStr = context.Configuration.GetSection("Redis:ConnectionString").Value
                    ?? "127.0.0.1:9379,password=DB65F7F9C,abortConnect=false,syncTimeout=5000,asyncTimeout=10000";
                var redisConfigOptions = StackExchange.Redis.ConfigurationOptions.Parse(redisConnectionStr);

                client.UseRedisClustering(clustering =>
                    {
                        clustering.ConfigurationOptions = redisConfigOptions;
                    })
                    .Configure<ClusterOptions>(cluster =>
                    {
                        cluster.ClusterId = options.ClusterId;
                        cluster.ServiceId = options.ServiceId;
                    })
                    .Configure<ClientMessagingOptions>(messaging =>
                    {
                        messaging.ResponseTimeout = TimeSpan.FromSeconds(Math.Max(5, options.ResponseTimeoutSeconds));
                        messaging.ResponseTimeoutWithDebugger = TimeSpan.FromMinutes(5);
                    })
                    .Configure<global::Orleans.Configuration.GatewayOptions>(gateway =>
                    {
                        gateway.PreferredGatewayIndex = 0;
                        gateway.GatewayListRefreshPeriod = TimeSpan.FromSeconds(15);
                    })
                    .Configure<ConnectionOptions>(connection =>
                    {
                        connection.OpenConnectionTimeout = TimeSpan.FromSeconds(10);
                    })
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton<IClientConnectionRetryFilter, OrleansStartupConnectionRetryFilter>();
                    });
            })
            .UseConsoleLifetime();
    }

    private static OrleansOptions ResolveOrleansOptions(IConfiguration configuration)
    {
        var options = new OrleansOptions();
        configuration.GetSection("Orleans").Bind(options);
        ApplyClusterOptionOverrides(configuration, options);

        if (string.IsNullOrWhiteSpace(options.ClusterId))
        {
            throw new InvalidOperationException("IM Gateway Orleans 配置无效：ClusterId 为空");
        }

        if (string.IsNullOrWhiteSpace(options.ServiceId))
        {
            throw new InvalidOperationException("IM Gateway Orleans 配置无效：ServiceId 为空");
        }

        return options;
    }

    private static void ApplyClusterOptionOverrides(IConfiguration configuration, OrleansOptions options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        var clusterId = configuration["ClusterOptions:ClusterId"];
        var serviceId = configuration["ClusterOptions:ServiceId"];

        ValidateClusterOptionConsistency("ClusterId", configuration["Orleans:ClusterId"], clusterId);
        ValidateClusterOptionConsistency("ServiceId", configuration["Orleans:ServiceId"], serviceId);

        if (!string.IsNullOrWhiteSpace(clusterId))
        {
            options.ClusterId = clusterId;
        }

        if (!string.IsNullOrWhiteSpace(serviceId))
        {
            options.ServiceId = serviceId;
        }
    }

    private static void ValidateClusterOptionConsistency(string optionName, string? orleansValue, string? clusterValue)
    {
        if (!string.IsNullOrWhiteSpace(orleansValue)
            && !string.IsNullOrWhiteSpace(clusterValue)
            && !string.Equals(orleansValue, clusterValue, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"IM Gateway Orleans 配置冲突：Orleans.{optionName}=\"{orleansValue}\" 与 ClusterOptions.{optionName}=\"{clusterValue}\" 不一致。");
        }
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
    /// 为 IM 网关的 <see cref="GatewayOptions"/> 补齐默认值：
    /// - ClusterId 若未显式配置则回退到 Orleans:ClusterId；
    /// - RedisConnectionString 若未显式配置则根据 DataBase:RedisMasters 构造。
    /// </summary>
    private static void ApplyGatewayRegistryDefaults(IConfiguration configuration, Configuration.GatewayOptions options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ClusterId))
        {
            options.ClusterId =
                configuration["Orleans:ClusterId"] ??
                configuration["ClusterOptions:ClusterId"] ??
                string.Empty;
        }

        if (string.IsNullOrWhiteSpace(options.RedisConnectionString)
            || string.Equals(options.RedisConnectionString, "localhost:6379", StringComparison.Ordinal))
        {
            var configured = configuration["Gateway:RedisConnectionString"];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                options.RedisConnectionString = configured;
                return;
            }

            var primaryRedisMaster = configuration.GetSection("DataBase:RedisMasters").GetChildren().FirstOrDefault();
            if (primaryRedisMaster == null)
            {
                return;
            }

            var host = primaryRedisMaster["Host"];
            var port = primaryRedisMaster["Port"];
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(port))
            {
                return;
            }

            var password = primaryRedisMaster["Password"];
            options.RedisConnectionString = string.IsNullOrWhiteSpace(password)
                ? $"{host}:{port}"
                : $"{host}:{port},password={password}";
        }
    }
}

public static class ServiceCollectionExtensions
{
    public static void AddAllIMMessageHandlers(this IServiceCollection services, params Assembly[] assemblies)
    {
        if (assemblies == null || assemblies.Length == 0)
        {
            assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                .ToArray();
        }

        var handlerTypes = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsClass && !type.IsAbstract && typeof(IIMMessageHandler).IsAssignableFrom(type))
            .ToList();

        foreach (var handlerType in handlerTypes)
        {
            services.AddSingleton(typeof(IIMMessageHandler), handlerType);
            services.AddSingleton(handlerType);
        }
    }
}