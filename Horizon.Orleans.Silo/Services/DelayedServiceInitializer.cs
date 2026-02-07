using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Horizon.Orleans.Silo.Services
{
    /// <summary>
    /// 延迟初始化非关键服务
    /// </summary>
    public class DelayedServiceInitializer : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DelayedServiceInitializer> _logger;
        private readonly int _delaySeconds;

        public DelayedServiceInitializer(
            IServiceProvider serviceProvider,
            ILogger<DelayedServiceInitializer> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _delaySeconds = configuration.GetValue<int>("StartupOptimization:DelayedServicesStartupSeconds", 10);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("延迟 {Delay} 秒后初始化非关键服务", _delaySeconds);
            
            await Task.Delay(TimeSpan.FromSeconds(_delaySeconds), stoppingToken);
            
            if (stoppingToken.IsCancellationRequested)
                return;

            _logger.LogInformation("开始初始化延迟服务...");
            
            try
            {
                // 初始化启动报告服务
                var startupReport = _serviceProvider.GetService<StartupReportService>();
                if (startupReport != null)
                {
                    await startupReport.StartAsync(stoppingToken);
                }
                
                // 初始化其他非关键服务...
                
                _logger.LogInformation("延迟服务初始化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "延迟服务初始化失败");
            }
        }
    }
}
