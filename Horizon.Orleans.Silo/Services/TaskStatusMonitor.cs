using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Horizon.Orleans.Silo.Services
{
    /// <summary>
    /// 任务状态监控器接口
    /// </summary>
    public interface ITaskStatusMonitor
    {
        /// <summary>
        /// 注册一个正在运行的任务
        /// </summary>
        void RegisterTask(string taskName, string taskType);
        
        /// <summary>
        /// 更新任务状态
        /// </summary>
        void UpdateTaskStatus(string taskName, TaskRunningStatus status, string? message = null);
        
        /// <summary>
        /// 注销任务
        /// </summary>
        void UnregisterTask(string taskName);
        
        /// <summary>
        /// 获取所有任务的状态
        /// </summary>
        TaskStatusReport GetTaskStatusReport();
        
        /// <summary>
        /// 记录当前任务状态到日志
        /// </summary>
        void LogTaskStatus();
    }

    /// <summary>
    /// 任务状态监控器实现
    /// </summary>
    public class TaskStatusMonitor : ITaskStatusMonitor
    {
        private readonly ILogger<TaskStatusMonitor> _logger;
        private readonly ConcurrentDictionary<string, TaskStatusInfo> _tasks = new();

        public TaskStatusMonitor(ILogger<TaskStatusMonitor> logger)
        {
            _logger = logger;
        }

        public void RegisterTask(string taskName, string taskType)
        {
            var now = DateTime.UtcNow;
            var taskInfo = new TaskStatusInfo
            {
                TaskName = taskName,
                TaskType = taskType,
                Status = TaskRunningStatus.Starting,
                RegisteredAt = now,
                LastUpdatedAt = now
            };

            _tasks.AddOrUpdate(taskName, taskInfo, (key, existing) => taskInfo);
            
            _logger.LogInformation("📝 [任务注册] 任务={TaskName}, 类型={TaskType}", taskName, taskType);
        }

        public void UpdateTaskStatus(string taskName, TaskRunningStatus status, string? message = null)
        {
            if (_tasks.TryGetValue(taskName, out var taskInfo))
            {
                taskInfo.Status = status;
                taskInfo.LastUpdatedAt = DateTime.UtcNow;
                taskInfo.StatusMessage = message;
                
                var emoji = status switch
                {
                    TaskRunningStatus.Starting => "🚀",
                    TaskRunningStatus.Running => "✅",
                    TaskRunningStatus.Paused => "⏸️",
                    TaskRunningStatus.Stopping => "🛑",
                    TaskRunningStatus.Stopped => "⏹️",
                    TaskRunningStatus.Failed => "❌",
                    TaskRunningStatus.Completed => "✔️",
                    _ => "❓"
                };
                
                _logger.LogInformation(
                    "{Emoji} [任务状态更新] 任务={TaskName}, 状态={Status}, 消息={Message}",
                    emoji, taskName, status, message ?? "无");
            }
        }

        public void UnregisterTask(string taskName)
        {
            if (_tasks.TryRemove(taskName, out var taskInfo))
            {
                _logger.LogInformation("🗑️ [任务注销] 任务={TaskName}, 运行时长={Duration:hh\\:mm\\:ss}",
                    taskName,
                    DateTime.UtcNow - taskInfo.RegisteredAt);
            }
        }

        public TaskStatusReport GetTaskStatusReport()
        {
            var now = DateTime.UtcNow;
            var tasks = _tasks.Values.ToList();

            return new TaskStatusReport
            {
                TotalTasks = tasks.Count,
                RunningTasks = tasks.Count(t => t.Status == TaskRunningStatus.Running),
                StartingTasks = tasks.Count(t => t.Status == TaskRunningStatus.Starting),
                StoppedTasks = tasks.Count(t => t.Status == TaskRunningStatus.Stopped),
                FailedTasks = tasks.Count(t => t.Status == TaskRunningStatus.Failed),
                Tasks = tasks,
                ReportGeneratedAt = now
            };
        }

        public void LogTaskStatus()
        {
            var report = GetTaskStatusReport();
            
            _logger.LogInformation("=".PadRight(80, '='));
            _logger.LogInformation("📊 【任务状态报告】");
            _logger.LogInformation("生成时间: {Time:yyyy-MM-dd HH:mm:ss}", DateTime.Now);
            _logger.LogInformation("总任务数: {TotalTasks}", report.TotalTasks);
            _logger.LogInformation("运行中: {RunningTasks} | 启动中: {StartingTasks} | 已停止: {StoppedTasks} | 失败: {FailedTasks}",
                report.RunningTasks, report.StartingTasks, report.StoppedTasks, report.FailedTasks);
            _logger.LogInformation("-".PadRight(80, '-'));

            if (report.Tasks.Any())
            {
                _logger.LogInformation("【任务详情】");
                foreach (var task in report.Tasks.OrderByDescending(t => t.LastUpdatedAt))
                {
                    var runningTime = DateTime.UtcNow - task.RegisteredAt;
                    var statusEmoji = task.Status switch
                    {
                        TaskRunningStatus.Running => "✅",
                        TaskRunningStatus.Starting => "🚀",
                        TaskRunningStatus.Stopped => "⏹️",
                        TaskRunningStatus.Failed => "❌",
                        TaskRunningStatus.Paused => "⏸️",
                        TaskRunningStatus.Stopping => "🛑",
                        TaskRunningStatus.Completed => "✔️",
                        _ => "❓"
                    };
                    
                    _logger.LogInformation(
                        "  {Emoji} {TaskName} ({TaskType}): {Status} | 运行时长: {RunningTime:hh\\:mm\\:ss} | {Message}",
                        statusEmoji,
                        task.TaskName,
                        task.TaskType,
                        task.Status,
                        runningTime,
                        task.StatusMessage ?? "正常");
                }
            }
            else
            {
                _logger.LogInformation("  (无活动任务)");
            }
            
            _logger.LogInformation("=".PadRight(80, '='));
        }
    }

    /// <summary>
    /// 任务运行状态
    /// </summary>
    public enum TaskRunningStatus
    {
        /// <summary>启动中</summary>
        Starting,
        /// <summary>运行中</summary>
        Running,
        /// <summary>暂停中</summary>
        Paused,
        /// <summary>停止中</summary>
        Stopping,
        /// <summary>已停止</summary>
        Stopped,
        /// <summary>失败</summary>
        Failed,
        /// <summary>已完成</summary>
        Completed
    }

    /// <summary>
    /// 任务状态信息
    /// </summary>
    public class TaskStatusInfo
    {
        public string TaskName { get; set; } = string.Empty;
        public string TaskType { get; set; } = string.Empty;
        public TaskRunningStatus Status { get; set; }
        public DateTime RegisteredAt { get; set; }
        public DateTime LastUpdatedAt { get; set; }
        public string? StatusMessage { get; set; }
    }

    /// <summary>
    /// 任务状态报告
    /// </summary>
    public class TaskStatusReport
    {
        public int TotalTasks { get; set; }
        public int RunningTasks { get; set; }
        public int StartingTasks { get; set; }
        public int StoppedTasks { get; set; }
        public int FailedTasks { get; set; }
        public List<TaskStatusInfo> Tasks { get; set; } = new();
        public DateTime ReportGeneratedAt { get; set; }
    }

    /// <summary>
    /// 任务状态监控后台服务 - 定期输出任务状态
    /// </summary>
    public class TaskStatusReporterService : BackgroundService
    {
        private readonly ITaskStatusMonitor _taskMonitor;
        private readonly ILogger<TaskStatusReporterService> _logger;
        private readonly TimeSpan _reportInterval;

        public TaskStatusReporterService(
            ITaskStatusMonitor taskMonitor,
            ILogger<TaskStatusReporterService> logger)
        {
            _taskMonitor = taskMonitor;
            _logger = logger;
            _reportInterval = TimeSpan.FromMinutes(5); // 每5分钟报告一次
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 注册自身到任务监控器
            _taskMonitor.RegisterTask("TaskStatusReporter", "BackgroundService");
            _taskMonitor.UpdateTaskStatus("TaskStatusReporter", TaskRunningStatus.Running);

            _logger.LogInformation("🎯 任务状态报告服务已启动，报告间隔: {Interval}", _reportInterval);

            // 等待一段时间后再开始第一次报告，让其他服务有时间启动
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _taskMonitor.LogTaskStatus();
                    await Task.Delay(_reportInterval, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    // 正常退出
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "任务状态报告服务发生错误");
                    _taskMonitor.UpdateTaskStatus("TaskStatusReporter", TaskRunningStatus.Failed, ex.Message);
                }
            }

            _taskMonitor.UpdateTaskStatus("TaskStatusReporter", TaskRunningStatus.Stopped);
            _taskMonitor.UnregisterTask("TaskStatusReporter");
            _logger.LogInformation("任务状态报告服务已停止");
        }
    }
}
