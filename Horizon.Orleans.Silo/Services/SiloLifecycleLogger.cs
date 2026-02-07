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
        private readonly ITaskStatusMonitor? _taskMonitor;

        public SiloLifecycleLogger(
            ILogger<SiloLifecycleLogger> logger,
            ITaskStatusMonitor? taskMonitor = null)
        {
            _logger = logger;
            _logsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
            _taskMonitor = taskMonitor;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _taskMonitor?.RegisterTask("SiloLifecycleLogger", "IHostedService");
            _taskMonitor?.UpdateTaskStatus("SiloLifecycleLogger", TaskRunningStatus.Starting);
            
            _logger.LogInformation("🚀 Silo正在启动...");
            await LogLifecycleEvent("Silo_Starting");
            
            _taskMonitor?.UpdateTaskStatus("SiloLifecycleLogger", TaskRunningStatus.Running);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _taskMonitor?.UpdateTaskStatus("SiloLifecycleLogger", TaskRunningStatus.Stopping);
            
            _logger.LogInformation("🛑 Silo正在停止...");
            await LogLifecycleEvent("Silo_Stopping");
            
            _taskMonitor?.UpdateTaskStatus("SiloLifecycleLogger", TaskRunningStatus.Stopped);
            _taskMonitor?.UnregisterTask("SiloLifecycleLogger");
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
