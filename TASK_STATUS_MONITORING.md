# Task Status Monitoring System

## Overview
This implementation adds a comprehensive task status monitoring system to check the status of running tasks and provide feedback.

## Features

### 1. Task Status Monitor (`ITaskStatusMonitor`)
- **Tracks all running background services, hosted services, and startup tasks**
- **Real-time status updates** for each task
- **Comprehensive reporting** with detailed statistics

### 2. Task Status Reporter Service
- Automatically logs task status reports every 5 minutes
- Provides detailed information about:
  - Total number of tasks
  - Running, starting, stopped, and failed tasks
  - Individual task details with run time and status messages

### 3. Integration with Existing Services
All existing services now register with the task monitor:
- `ClientConnectionMonitorService` - Monitors client connections
- `SiloLifecycleLogger` - Logs Silo lifecycle events
- `StartupReportService` - Generates startup reports
- `StartupDiagnosticsTask` - Performs startup diagnostics
- `ClientConnectionStartupTask` - Initializes client connection tracking

## Task Status States

The system tracks tasks through the following states:

- **Starting** 🚀 - Task is initializing
- **Running** ✅ - Task is actively running
- **Paused** ⏸️ - Task is temporarily paused
- **Stopping** 🛑 - Task is shutting down
- **Stopped** ⏹️ - Task has stopped
- **Failed** ❌ - Task encountered an error
- **Completed** ✔️ - Task finished successfully

## Sample Output

When the system is running, you'll see periodic status reports like this:

```
================================================================================
📊 【任务状态报告】
生成时间: 2026-02-07 14:56:13
总任务数: 5
运行中: 3 | 启动中: 0 | 已停止: 1 | 失败: 0
--------------------------------------------------------------------------------
【任务详情】
  ✅ TaskStatusReporter (BackgroundService): Running | 运行时长: 00:05:23 | 正常
  ✅ ClientConnectionMonitor (BackgroundService): Running | 运行时长: 00:05:23 | 正常
  ✅ SiloLifecycleLogger (IHostedService): Running | 运行时长: 00:05:25 | 正常
  ✔️ StartupReport (IHostedService): Completed | 运行时长: 00:05:20 | 正常
  ✔️ StartupDiagnostics (IStartupTask): Completed | 运行时长: 00:00:02 | 正常
================================================================================
```

## Usage

### Registering a Task
```csharp
_taskMonitor?.RegisterTask("MyTask", "BackgroundService");
```

### Updating Task Status
```csharp
_taskMonitor?.UpdateTaskStatus("MyTask", TaskRunningStatus.Running);
_taskMonitor?.UpdateTaskStatus("MyTask", TaskRunningStatus.Failed, "Error message");
```

### Unregistering a Task
```csharp
_taskMonitor?.UnregisterTask("MyTask");
```

### Getting Task Status Report
```csharp
var report = _taskMonitor?.GetTaskStatusReport();
Console.WriteLine($"Total Tasks: {report.TotalTasks}");
Console.WriteLine($"Running Tasks: {report.RunningTasks}");
```

### Manual Logging
```csharp
_taskMonitor?.LogTaskStatus();
```

## Architecture

### Components

1. **TaskStatusMonitor.cs** - Core monitoring service
   - `ITaskStatusMonitor` - Interface for task monitoring
   - `TaskStatusMonitor` - Implementation of task monitoring
   - `TaskStatusReporterService` - Background service for periodic reporting
   - `TaskRunningStatus` - Enum for task states
   - `TaskStatusInfo` - Model for task information
   - `TaskStatusReport` - Model for status reports

2. **Service Integration** - Updated existing services
   - Modified to accept `ITaskStatusMonitor` in constructors
   - Report status changes throughout their lifecycle
   - Automatically register/unregister with the monitor

3. **Program.cs** - Service registration
   - Registers `ITaskStatusMonitor` as singleton
   - Registers `TaskStatusReporterService` as hosted service
   - Ensures task monitor is available to all services

## Benefits

1. **Visibility** - Clear view of all running tasks and their status
2. **Monitoring** - Easy identification of failed or stuck tasks
3. **Debugging** - Detailed logging helps diagnose issues
4. **Observability** - Comprehensive metrics for system health
5. **Feedback** - Real-time status updates to logs

## Future Enhancements

Possible improvements:
- Web API endpoint to query task status
- Metrics export to monitoring systems (Prometheus, etc.)
- Task performance metrics (CPU, memory usage per task)
- Email/Slack notifications for task failures
- Task dependency tracking
- Historical task execution data
