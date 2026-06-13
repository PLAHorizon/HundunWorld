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
        private readonly IClusterClient _clusterClient;

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
        /// <param name="clusterClient">已注册为单例的Orleans集群客户端</param>
        public OrleansControllerBase(IOptions<AdoNetOptions> options, IOptions<ClusterOptions> clusterOptions, ILogger<OrleansControllerBase> logger, IClusterClient clusterClient)
        {
            _options = options.Value;
            _clusterOptions = clusterOptions.Value;
            _logger = logger;
            _clusterClient = clusterClient;
        }
        /// <summary>
        /// 获取Orleans集群客户端（单例，无需每次请求创建新连接）
        /// </summary>
        /// <returns></returns>
        protected Task<IClusterClient> OrleansConnectClient()
        {
            return Task.FromResult(_clusterClient);
        }
    }
}
