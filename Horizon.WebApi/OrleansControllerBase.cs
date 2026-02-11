using Horizon.Core.Abstract;
using Horizon.Core.Options;
using Microsoft.AspNetCore.Mvc;
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

namespace Horizon.WebApi
{
    /// <summary>
    /// 提供Orleans 客户端访问能力的 Api基类 
    /// </summary>
    public class OrleansControllerBase : ControllerBase
    {
        protected readonly AdoNetOptions _options;
        protected readonly ClusterOptions _clusterOptions;
        private readonly ILogger<OrleansControllerBase> _logger;
        private IHost client = null;
        /// <summary>
        /// 
        /// </summary>
        public OrleansControllerBase() { }
        /// <summary>
        /// 初始化Orleans 客户端访问能力
        /// </summary>
        /// <param name="options"></param>
        /// <param name="clusterOptions"></param>
        /// <param name="logger"></param>
        public OrleansControllerBase(IOptions<AdoNetOptions> options, IOptions<ClusterOptions> clusterOptions, ILogger<OrleansControllerBase> logger)
        {
            _options = options.Value;
            _clusterOptions = clusterOptions.Value;
            _logger = logger;
        }
        /// <summary>
        /// Oleans 客户端
        /// </summary>
        /// <returns></returns>
        protected async Task<IClusterClient> OrleansConnectClient()
        {
            var invariant = _options.Invariant;
            var connectionString = _options.ConnectionString;

            try
            {
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
                _logger.LogError(ex.Message);
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
        ~OrleansControllerBase()
        {
            DisposeAsync();
        }

    }
}
