using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using log4net;
using Horizon.Core;
using Horizon.Core.Abstract;
using Horizon.Core.Options;
using Horizon.WebApi.Middleware;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Serialization;
using Horizon.IoT.MQTT;
using StackExchange.Redis;

namespace Horizon.WebApi
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
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
                config.AddCommandLine(args);
            })
            .ConfigureServices((context, services) =>
            {
                services.Configure<MqttBrokerOptions>(context.Configuration.GetSection(MqttBrokerOptions.SectionName));
                services.AddSingleton<IMqttClientProvider, MqttClientProvider>();
            })
            .ConfigureLogging(config =>
            {
                config.AddEventLog();
                config.AddEventSourceLogger();
                Log.LogConfig();

            })
            .UseOrleansClient((context, client) =>
            {
                var adoNetOptions = context.Configuration.GetSection("AdoNetOptions").Get<AdoNetOptions>();
                var clusterOptions = context.Configuration.GetSection("ClusterOptions").Get<ClusterOptions>();

                // ===== Redis 集群配置（主方案） =====
                // 阶段 0.1：配置必需——删除 127.0.0.1 本地默认回退，缺失抛清晰错误。
                var redisConnectionStr = Horizon.Game.Message.Configuration.ConnectionStrings.Require(
                    context.Configuration.GetSection("Redis:ConnectionString").Value,
                    "Redis:ConnectionString", "WebApi Orleans 客户端 Redis 集群", "GATEWAY_REDIS_CONNECTION_STRING");
                var redisConfigOptions = StackExchange.Redis.ConfigurationOptions.Parse(redisConnectionStr);

                // 防御性兜底：确保连接失败时允许 ConnectionMultiplexer 继续重试，
                // 与 Horizon.Strategy.Storage.Redis.RedisConnection 行为一致。
                redisConfigOptions.AbortOnConnectFail = false;

                client.UseRedisClustering(options =>
                {
                    options.ConfigurationOptions = redisConfigOptions;
                })
                .Configure<ClusterOptions>(options =>
                {
                    options.ClusterId = clusterOptions?.ClusterId ?? "dev";
                    options.ServiceId = clusterOptions?.ServiceId ?? "BaseService";
                })
                .Configure<ClientMessagingOptions>(options =>
                {
                    options.ResponseTimeout = TimeSpan.FromSeconds(30);
                    options.ResponseTimeoutWithDebugger = TimeSpan.FromMinutes(5);
                })
                .Configure<GatewayOptions>(options =>
                {
                    options.GatewayListRefreshPeriod = TimeSpan.FromMinutes(10);
                })
                .Configure<ConnectionOptions>(options =>
                {
                    options.OpenConnectionTimeout = TimeSpan.FromSeconds(10);
                })
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IClientConnectionRetryFilter, OrleansStartupConnectionRetryFilter>();
                    services.AddSerializer(serializerBuilder =>
                    {
                        serializerBuilder.AddAssembly(typeof(Horizon.Game.Message.Network.HorizonMessagePacket).Assembly);
                        serializerBuilder.AddAssembly(typeof(Horizon.IM.Message.Network.IMGroupChatNotifyMessage).Assembly);
                        serializerBuilder.AddNewtonsoftJsonSerializer(
                            isSupported: type => type.Namespace != null && type.Namespace.StartsWith("Horizon.Share"));
                    });
                });
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
            });
    }
}
