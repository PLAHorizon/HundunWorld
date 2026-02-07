using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Horizon.Orleans.Silo.Services
{
    /// <summary>
    /// 后台服务，定期输出客户端连接统计信息
    /// </summary>
    public class ClientConnectionMonitorService : BackgroundService
    {
        private readonly IClientConnectionTracker _connectionTracker;
        private readonly ILogger<ClientConnectionMonitorService> _logger;
        private readonly ClientConnectionOptions _options;

        public ClientConnectionMonitorService(
            IClientConnectionTracker connectionTracker,
            ILogger<ClientConnectionMonitorService> logger,
            IOptions<ClientConnectionOptions> options)
        {
            _connectionTracker = connectionTracker;
            _logger = logger;
            _options = options.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.EnableDetailedLogging)
            {
                _logger.LogInformation("客户端连接监控已禁用（生产环境）");
                return;
            }

            _logger.LogInformation("客户端连接监控服务已启动，日志间隔: {Interval}", _options.LogInterval);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_options.LogInterval, stoppingToken);
                    _connectionTracker.LogCurrentConnections();
                }
                catch (TaskCanceledException)
                {
                    // 正常退出
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "客户端连接监控服务发生错误");
                }
            }

            _logger.LogInformation("客户端连接监控服务已停止");
        }
    }
}
