using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Horizon.Orleans.Silo.Services;

namespace Horizon.Orleans.Silo.Tasks
{
    /// <summary>
    /// 启动诊断任务
    /// </summary>
    public class StartupDiagnosticsTask : IStartupTask
    {
        private readonly ILogger<StartupDiagnosticsTask> _logger;
        private readonly IConfiguration _configuration;
        private readonly Stopwatch _stopwatch = new();
        private readonly ITaskStatusMonitor? _taskMonitor;

        public StartupDiagnosticsTask(
            ILogger<StartupDiagnosticsTask> logger,
            IConfiguration configuration,
            ITaskStatusMonitor? taskMonitor = null)
        {
            _logger = logger;
            _configuration = configuration;
            _taskMonitor = taskMonitor;
            _stopwatch.Start();
        }

        public async Task Execute(CancellationToken cancellationToken)
        {
            _taskMonitor?.RegisterTask("StartupDiagnostics", "IStartupTask");
            _taskMonitor?.UpdateTaskStatus("StartupDiagnostics", TaskRunningStatus.Running);
            
            try
            {
                _stopwatch.Stop();
                
                var diagnostics = new StringBuilder();
                diagnostics.AppendLine("【启动诊断信息】");
                diagnostics.AppendLine($"启动耗时: {_stopwatch.ElapsedMilliseconds} ms");
                diagnostics.AppendLine($"进程ID: {Process.GetCurrentProcess().Id}");
                diagnostics.AppendLine($"启动时间: {Process.GetCurrentProcess().StartTime:yyyy-MM-dd HH:mm:ss}");
                
                // 保存诊断信息
                var logsDir = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
                Directory.CreateDirectory(logsDir);
                
                var diagnosticsFile = Path.Combine(logsDir, $"StartupDiagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                await File.WriteAllTextAsync(diagnosticsFile, diagnostics.ToString(), cancellationToken);
                
                _logger.LogInformation($"启动诊断信息已保存: {diagnosticsFile}");
                _taskMonitor?.UpdateTaskStatus("StartupDiagnostics", TaskRunningStatus.Completed);
                _taskMonitor?.UnregisterTask("StartupDiagnostics");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动诊断任务失败");
                _taskMonitor?.UpdateTaskStatus("StartupDiagnostics", TaskRunningStatus.Failed, ex.Message);
                // Keep the task registered in failed state for monitoring purposes
                throw;
            }
        }
    }
}
