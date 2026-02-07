using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace Horizon.Orleans.Silo.Services
{
    /// <summary>
    /// Silo启动报告服务
    /// </summary>
    public class StartupReportService : IHostedService
    {
        private readonly ILogger<StartupReportService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;
        private readonly string _logsDirectory;
        private readonly ITaskStatusMonitor? _taskMonitor;

        public StartupReportService(
            ILogger<StartupReportService> logger,
            IConfiguration configuration,
            IHostEnvironment environment,
            ITaskStatusMonitor? taskMonitor = null)
        {
            _logger = logger;
            _configuration = configuration;
            _environment = environment;
            _logsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
            _taskMonitor = taskMonitor;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _taskMonitor?.RegisterTask("StartupReport", "IHostedService");
            _taskMonitor?.UpdateTaskStatus("StartupReport", TaskRunningStatus.Starting);
            
            try
            {
                // 确保日志目录存在
                Directory.CreateDirectory(_logsDirectory);

                // 等待Silo完全启动
                await Task.Delay(5000, cancellationToken);

                // 生成启动报告
                var report = GenerateStartupReport();
                
                // 保存报告
                await SaveReport(report);
                
                _logger.LogInformation("✅ Silo启动报告已生成");
                _taskMonitor?.UpdateTaskStatus("StartupReport", TaskRunningStatus.Completed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成启动报告时发生错误");
                _taskMonitor?.UpdateTaskStatus("StartupReport", TaskRunningStatus.Failed, ex.Message);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _taskMonitor?.UpdateTaskStatus("StartupReport", TaskRunningStatus.Stopped);
            _taskMonitor?.UnregisterTask("StartupReport");
            return Task.CompletedTask;
        }

        private string GenerateStartupReport()
        {
            var sb = new StringBuilder();
            var startTime = DateTime.Now;

            sb.AppendLine("================================================================================");
            sb.AppendLine("                        Orleans Silo 启动报告");
            sb.AppendLine("================================================================================");
            sb.AppendLine($"生成时间: {startTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"环境: {_environment.EnvironmentName}");
            sb.AppendLine($"应用名称: {_environment.ApplicationName}");
            sb.AppendLine();

            // 基本配置信息
            sb.AppendLine("【基本配置】");
            sb.AppendLine("--------------------------------------------------------------------------------");
            var clusterOptions = _configuration.GetSection("ClusterOptions");
            sb.AppendLine($"集群ID: {clusterOptions["ClusterId"]}");
            sb.AppendLine($"服务ID: {clusterOptions["ServiceId"]}");
            sb.AppendLine();

            // Orleans配置
            sb.AppendLine("【Orleans配置】");
            sb.AppendLine("--------------------------------------------------------------------------------");
            var orleansEndpoints = _configuration.GetSection("OrleansEndPoints");
            sb.AppendLine($"Silo端口: {orleansEndpoints["SiloPort"]}");
            sb.AppendLine($"网关端口: {orleansEndpoints["GatewayPort"]}");
            sb.AppendLine();

            // 数据库配置
            sb.AppendLine("【数据库配置】");
            sb.AppendLine("--------------------------------------------------------------------------------");
            var dbOptions = _configuration.GetSection("DatabaseOptions");
            foreach (var db in dbOptions.GetChildren())
            {
                sb.AppendLine($"{db.Key}: {MaskConnectionString(db["ConnectionString"] ?? string.Empty)}");
            }
            sb.AppendLine();

            // 系统信息
            sb.AppendLine("【系统信息】");
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine($"操作系统: {Environment.OSVersion}");
            sb.AppendLine($".NET版本: {Environment.Version}");
            sb.AppendLine($"处理器数: {Environment.ProcessorCount}");
            sb.AppendLine($"工作目录: {Environment.CurrentDirectory}");
            sb.AppendLine($"内存使用: {GC.GetTotalMemory(false) / 1024 / 1024:N0} MB");
            sb.AppendLine();

            // 启动耗时
            var endTime = DateTime.Now;
            sb.AppendLine($"报告生成耗时: {(endTime - startTime).TotalMilliseconds:N0} ms");
            sb.AppendLine("================================================================================");

            return sb.ToString();
        }

        private async Task SaveReport(string report)
        {
            var fileName = $"SiloStartup_{DateTime.Now:yyyyMMdd_HHmmss}.log";
            var filePath = Path.Combine(_logsDirectory, fileName);
            
            await File.WriteAllTextAsync(filePath, report);
            
            _logger.LogInformation($"📄 启动报告已保存至: {filePath}");
            
            // 同时输出到控制台（可选）
            if (_environment.IsDevelopment())
            {
                Console.WriteLine(report);
            }
        }

        private string MaskConnectionString(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
                return "N/A";

            // 隐藏敏感信息
            var parts = connectionString.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Contains("Password", StringComparison.OrdinalIgnoreCase) ||
                    parts[i].Contains("Pwd", StringComparison.OrdinalIgnoreCase))
                {
                    var keyValue = parts[i].Split('=');
                    if (keyValue.Length == 2)
                    {
                        parts[i] = $"{keyValue[0]}=****";
                    }
                }
            }
            return string.Join(";", parts);
        }
      
    }
}
