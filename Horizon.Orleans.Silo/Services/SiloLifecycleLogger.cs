using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Horizon.Orleans.Silo.Services
{
    /// <summary>
    /// Silo生命周期日志记录器 - 使用IHostedService实现
    /// </summary>
    public class SiloLifecycleLogger : IHostedService
    {
        private readonly ILogger<SiloLifecycleLogger> _logger;
        private readonly string _logsDirectory;

        public SiloLifecycleLogger(ILogger<SiloLifecycleLogger> logger)
        {
            _logger = logger;
            _logsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("🚀 Silo正在启动...");
            await LogLifecycleEvent("Silo_Starting");
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("🛑 Silo正在停止...");
            await LogLifecycleEvent("Silo_Stopping");
        }

        private async Task LogLifecycleEvent(string eventName)
        {
            try
            {
                Directory.CreateDirectory(_logsDirectory);
                
                var logFile = Path.Combine(_logsDirectory, $"SiloLifecycle_{DateTime.Now:yyyyMMdd}.log");
                var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {eventName}{Environment.NewLine}";
                
                await File.AppendAllTextAsync(logFile, logEntry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "记录生命周期事件失败");
            }
        }
    }
}
