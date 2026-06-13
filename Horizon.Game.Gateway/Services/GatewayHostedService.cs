using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Horizon.Game.Gateway.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Services
{
    public class GatewayHostedService : BackgroundService
    {
        private readonly ILogger<GatewayHostedService> _logger;
        private readonly IGatewayService _gatewayService;
        private readonly IHostApplicationLifetime _appLifetime;

        public GatewayHostedService(
            ILogger<GatewayHostedService> logger,
            IGatewayService gatewayService,
            IHostApplicationLifetime appLifetime)
        {
            _logger = logger;
            _gatewayService = gatewayService;
            _appLifetime = appLifetime;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("网关后台服务开始启动");

                await _gatewayService.StartAsync(stoppingToken);

                _logger.LogInformation("网关后台服务启动成功，等待关闭信号");

                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("网关后台服务收到停止信号");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "网关后台服务运行时发生致命错误，将停止应用程序");
                _appLifetime.StopApplication();
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("正在停止网关后台服务");

            try
            {
                await _gatewayService.StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止网关服务时发生错误");
            }

            await base.StopAsync(cancellationToken);
            _logger.LogInformation("网关后台服务已停止");
        }
    }
}
