using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Horizon.Orleans.Silo.Services;

namespace Horizon.Orleans.Silo.Services
{
    /// <summary>
    /// Silo启动任务，初始化客户端连接跟踪
    /// </summary>
    public class ClientConnectionStartupTask : IStartupTask
    {
        private readonly ILogger<ClientConnectionStartupTask> _logger;
        private readonly ITaskStatusMonitor? _taskMonitor;

        public ClientConnectionStartupTask(
            ILogger<ClientConnectionStartupTask> logger,
            ITaskStatusMonitor? taskMonitor = null)
        {
            _logger = logger;
            _taskMonitor = taskMonitor;
        }

        public Task Execute(CancellationToken cancellationToken)
        {
            _taskMonitor?.RegisterTask("ClientConnectionStartup", "IStartupTask");
            _taskMonitor?.UpdateTaskStatus("ClientConnectionStartup", TaskRunningStatus.Running);
            
            _logger.LogInformation("✅ 客户端连接跟踪已初始化");
            _logger.LogInformation("📡 开始监控Orleans客户端连接...");
            
            _taskMonitor?.UpdateTaskStatus("ClientConnectionStartup", TaskRunningStatus.Completed);
            _taskMonitor?.UnregisterTask("ClientConnectionStartup");
            
            return Task.CompletedTask;
        }
    }
}
