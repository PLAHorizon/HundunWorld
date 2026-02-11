using Horizon.Core.Abstract;
using Horizon.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Serialization;
using Orleans.Configuration;
using Orleans.Hosting;
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Consul;

namespace Horizon.Game.Core
{
    /// <summary>
    /// 提供Orleans 客户端访问能力的 Api基类 
    /// </summary>
    public class OrleansClient
    {
        protected readonly OrleansClusteringDbOptions _options;
        protected readonly ClusterOptions _clusterOptions;
        private readonly ILogger<OrleansClient> _logger;
        public static IConfiguration Configuration { get; private set; }
        private IHost client = null;
        private static OrleansClient _instance;
        public static OrleansClient Instance
        {
            get
            {
                return _instance ?? (_instance = new OrleansClient());
            }
        }
        /// <summary>
        /// 
        /// </summary>
        public OrleansClient()
        {
            GetConfiguration();

            _options = Configuration.GetSection("ClusteringSiloOptions").Get<OrleansClusteringDbOptions>();

            _clusterOptions = Configuration.GetSection("ClusterOptions").Get<ClusterOptions>();

        }

        /// <summary>
        /// 获取配置文件信息
        /// </summary>
        /// <returns></returns>
        public static async Task<IConfiguration> GetConfiguration()
        {
            try
            {
                Configuration = await Task.FromResult(new ConfigurationBuilder()
                                 .SetBasePath(Directory.GetCurrentDirectory())
                                 .AddJsonFile("appsettings.json")
                                 .Build());

            }
            catch (Exception ex)
            {
            }
            return Configuration;
        }
        /// <summary>
        /// 初始化Orleans 客户端访问能力
        /// </summary>
        /// <param name="options"></param>
        /// <param name="clusterOptions"></param>
        /// <param name="logger"></param>
        public OrleansClient(IOptions<OrleansClusteringDbOptions> options, IOptions<ClusterOptions> clusterOptions, ILogger<OrleansClient> logger)
        {
            _options = options.Value;
            _clusterOptions = clusterOptions.Value;
            _logger = logger;
        }
        /// <summary>
        /// Oleans 客户端
        /// </summary>
        /// <returns></returns>
        public async Task<IClusterClient> OrleansConnectClient()
        {
            var invariant = _options.SqlServer.Invariant;
            var connectionString = _options.SqlServer.ConnectionString;

            try
            {
                if (client != null)
                {
                    var orleansClient = client.Services.GetRequiredService<IClusterClient>();
                    if (orleansClient != null)
                        return await Task.FromResult(orleansClient);
                }
                client = new HostBuilder().UseOrleansClient(client =>
                {
                    //集群
                    client.UseAdoNetClustering(options =>
                    {
                        options.ConnectionString = connectionString;
                        options.Invariant = invariant;
                    }).Configure<ClusterOptions>(options =>
                    {
                        options.ClusterId = _clusterOptions.ClusterId;
                        options.ServiceId = _clusterOptions.ServiceId;
                    })                    // ******* 关键修复：客户端超时配置以解决30分钟超时问题 *******
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
                }).ConfigureLogging(logging => logging.AddConsole()).Build();
                _logger.LogInformation("Client successfully connected to silo host");
                await client.StartAsync();
                return await Task.FromResult(client.Services.GetRequiredService<IClusterClient>());

            }
            catch (Exception ex)
            {
                // _logger.LogError(ex.Message);
                return await Task.FromException<IClusterClient>(ex);
            }

        }
        /// <summary>
        /// 释放客服端对象
        /// </summary>
        protected async Task DisposeAsync()
        {
            if (client != null)
            {
                await client.StopAsync();
                client.Dispose();
                client = null;
            }
        }
        /// <summary>
        /// 析构函数
        /// </summary>
        ~OrleansClient()
        {
            DisposeAsync();
        }

    }
}
