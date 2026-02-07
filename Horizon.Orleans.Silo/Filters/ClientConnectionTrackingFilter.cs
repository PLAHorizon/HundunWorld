using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Horizon.Orleans.Silo.Services;

namespace Horizon.Orleans.Silo.Filters
{
    /// <summary>
    /// Orleans Grain调用过滤器，用于跟踪客户端连接
    /// </summary>
    public class ClientConnectionTrackingFilter : IIncomingGrainCallFilter
    {
        private readonly IClientConnectionTracker _connectionTracker;
        private readonly ILogger<ClientConnectionTrackingFilter> _logger;

        public ClientConnectionTrackingFilter(
            IClientConnectionTracker connectionTracker,
            ILogger<ClientConnectionTrackingFilter> logger)
        {
            _connectionTracker = connectionTracker;
            _logger = logger;
        }

        public async Task Invoke(IIncomingGrainCallContext context)
        {
            try
            {
                // 获取客户端信息
                var clientId = RequestContext.Get("ClientId") as string ?? "Unknown";
                var clientEndpoint = RequestContext.Get("ClientEndpoint") as string;
                var grainType = context.Grain?.GetType().Name ?? "Unknown";
                var grainMethod = context.ImplementationMethod?.Name ?? "Unknown";

                // 跟踪客户端活动
                _connectionTracker.TrackConnection(clientId, ParseEndpoint(clientEndpoint), grainType);
                _connectionTracker.TrackActivity(clientId, $"{grainType}.{grainMethod}");

                // 继续执行Grain方法
                await context.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "客户端连接跟踪过滤器发生错误");
                await context.Invoke();
            }
        }

        private System.Net.IPEndPoint ParseEndpoint(string? endpoint)
        {
            if (string.IsNullOrEmpty(endpoint)) return new System.Net.IPEndPoint(System.Net.IPAddress.None, 0);

            try
            {
                var parts = endpoint.Split(':');
                if (parts.Length == 2 && System.Net.IPAddress.TryParse(parts[0], out var ip) && int.TryParse(parts[1], out var port))
                {
                    return new System.Net.IPEndPoint(ip, port);
                }
            }
            catch { }

            return new System.Net.IPEndPoint(System.Net.IPAddress.None, 0);
        }
    }
}
