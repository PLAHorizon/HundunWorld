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

                if (string.IsNullOrWhiteSpace(adoNetOptions?.ConnectionString))
                    throw new InvalidOperationException("WebApi Orleans配置无效：AdoNetOptions ConnectionString为空");

                if (string.IsNullOrWhiteSpace(adoNetOptions?.Invariant))
                    throw new InvalidOperationException("WebApi Orleans配置无效：AdoNetOptions Invariant为空");

                client.UseAdoNetClustering(options =>
                {
                    options.ConnectionString = adoNetOptions.ConnectionString;
                    options.Invariant = adoNetOptions.Invariant;
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
