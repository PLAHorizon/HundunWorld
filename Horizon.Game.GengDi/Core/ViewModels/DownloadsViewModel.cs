using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Enums;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class DownloadsViewModel : ViewModelBase
    {
        private readonly DownloadService _downloadService;
        private readonly AppSettingsService _settingsService;
        private readonly AsyncRelayCommand<string> _pauseDownloadCommand;
        private readonly AsyncRelayCommand<string> _resumeDownloadCommand;
        private readonly AsyncRelayCommand<string> _cancelDownloadCommand;
        private readonly AsyncRelayCommand<string> _deleteTaskCommand;
        private readonly AsyncRelayCommand<string> _openFolderCommand;
        private readonly AsyncRelayCommand _checkForUpdatesCommand;
        private readonly AsyncRelayCommand _pauseAllCommand;
        private readonly AsyncRelayCommand _clearCompletedCommand;
        private ObservableCollection<DownloadTask> _downloadTasks;
        private ObservableCollection<DownloadTask> _completedTasks;
        private string _downloadSpeedLimit;
        private string _maxConcurrentDownloads;
        private string _downloadPackageDirectory;
        private string _totalDownloadSpeedValue = "—";
        private string _totalDownloadSpeedUnit = "MB/s";
        private string _usedDiskSpaceValue = "—";
        private string _usedDiskSpaceUnit = "GB";
        private bool _isLoading;
        private bool _isInitialized;

        public ObservableCollection<DownloadTask> DownloadTasks
        {
            get => _downloadTasks;
            set => SetProperty(ref _downloadTasks, value);
        }

        public ObservableCollection<DownloadTask> CompletedTasks
        {
            get => _completedTasks;
            set => SetProperty(ref _completedTasks, value);
        }

        public string DownloadSpeedLimit
        {
            get => _downloadSpeedLimit;
            set
            {
                if (SetProperty(ref _downloadSpeedLimit, value))
                {
                    if (long.TryParse(value, out var limit))
                    {
                        _downloadService.DownloadSpeedLimit = limit;
                        PersistDownloadPreferences();
                    }
                }
            }
        }

        public string MaxConcurrentDownloads
        {
            get => _maxConcurrentDownloads;
            set
            {
                if (SetProperty(ref _maxConcurrentDownloads, value))
                {
                    if (int.TryParse(value, out var limit))
                    {
                        _downloadService.MaxConcurrentDownloads = limit;
                        PersistDownloadPreferences();
                    }
                }
            }
        }

        public string DownloadPackageDirectory
        {
            get => _downloadPackageDirectory;
            private set => SetProperty(ref _downloadPackageDirectory, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    RaiseCommandStateChanged();
                }
            }
        }

        /// <summary>总下载速度数值（如 "24.6"）</summary>
        public string TotalDownloadSpeedValue
        {
            get => _totalDownloadSpeedValue;
            private set => SetProperty(ref _totalDownloadSpeedValue, value);
        }

        /// <summary>总下载速度单位（如 "MB/s"）</summary>
        public string TotalDownloadSpeedUnit
        {
            get => _totalDownloadSpeedUnit;
            private set => SetProperty(ref _totalDownloadSpeedUnit, value);
        }

        /// <summary>已用磁盘空间数值（如 "186.4"）</summary>
        public string UsedDiskSpaceValue
        {
            get => _usedDiskSpaceValue;
            private set => SetProperty(ref _usedDiskSpaceValue, value);
        }

        /// <summary>已用磁盘空间单位（如 "GB"）</summary>
        public string UsedDiskSpaceUnit
        {
            get => _usedDiskSpaceUnit;
            private set => SetProperty(ref _usedDiskSpaceUnit, value);
        }

        public ICommand PauseDownloadCommand { get; }
        public ICommand ResumeDownloadCommand { get; }
        public ICommand CancelDownloadCommand { get; }
        public ICommand DeleteTaskCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand CheckForUpdatesCommand { get; }
        public ICommand PauseAllCommand { get; }
        public ICommand ClearCompletedCommand { get; }

        public DownloadsViewModel()
        {
            _settingsService = AppSettingsService.Instance;
            _downloadService = DownloadService.Instance;
            _downloadService.DownloadProgressChanged += OnDownloadProgressChanged;
            _downloadService.DownloadCompleted += OnDownloadCompleted;
            _downloadService.DownloadFailed += OnDownloadFailed;

            DownloadTasks = new ObservableCollection<DownloadTask>();
            CompletedTasks = new ObservableCollection<DownloadTask>();

            _pauseDownloadCommand = new AsyncRelayCommand<string>(PauseDownloadAsync, CanManageDownloads);
            _resumeDownloadCommand = new AsyncRelayCommand<string>(ResumeDownloadAsync, CanManageDownloads);
            _cancelDownloadCommand = new AsyncRelayCommand<string>(CancelDownloadAsync, CanManageDownloads);
            _deleteTaskCommand = new AsyncRelayCommand<string>(DeleteTaskAsync, CanManageDownloads);
            _openFolderCommand = new AsyncRelayCommand<string>(OpenFolderAsync, CanManageDownloads);
            _checkForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync, CanManageDownloads);
            _pauseAllCommand = new AsyncRelayCommand(PauseAllAsync, CanManageDownloads);
            _clearCompletedCommand = new AsyncRelayCommand(ClearCompletedAsync, CanManageDownloads);

            PauseDownloadCommand = _pauseDownloadCommand;
            ResumeDownloadCommand = _resumeDownloadCommand;
            CancelDownloadCommand = _cancelDownloadCommand;
            DeleteTaskCommand = _deleteTaskCommand;
            OpenFolderCommand = _openFolderCommand;
            CheckForUpdatesCommand = _checkForUpdatesCommand;
            PauseAllCommand = _pauseAllCommand;
            ClearCompletedCommand = _clearCompletedCommand;
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized)
            {
                return;
            }

            _isInitialized = true;
            IsLoading = true;
            try
            {
                await ApplySavedPreferencesAsync();
                await LoadTasksAsync();
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadTasksAsync()
        {
            var allTasks = await _downloadService.GetAllTasksAsync();
            DownloadTasks.Clear();
            CompletedTasks.Clear();

            foreach (var task in allTasks)
            {
                if (task.Status == DownloadStatus.Completed || task.Status == DownloadStatus.Failed || task.Status == DownloadStatus.Cancelled)
                {
                    CompletedTasks.Add(task);
                }
                else
                {
                    DownloadTasks.Add(task);
                }
            }

            RefreshStatistics();
        }

        /// <summary>
        /// 刷新总下载速度和已用磁盘空间统计。
        /// </summary>
        private void RefreshStatistics()
        {
            // 总下载速度 = 所有活跃任务速度之和
            var totalSpeed = DownloadTasks.Sum(t => t.Speed);
            if (totalSpeed > 0)
            {
                var mbSpeed = totalSpeed / 1024.0 / 1024.0;
                if (mbSpeed >= 1)
                {
                    TotalDownloadSpeedValue = $"{mbSpeed:F1}";
                    TotalDownloadSpeedUnit = "MB/s";
                }
                else
                {
                    var kbSpeed = totalSpeed / 1024.0;
                    TotalDownloadSpeedValue = $"{kbSpeed:F0}";
                    TotalDownloadSpeedUnit = "KB/s";
                }
            }
            else
            {
                TotalDownloadSpeedValue = "—";
                TotalDownloadSpeedUnit = "MB/s";
            }

            // 已用空间 = 下载目录占用的磁盘空间
            CalculateUsedDiskSpace();
        }

        /// <summary>
        /// 计算下载包目录的已用磁盘空间，设置分离的数值与单位属性。
        /// </summary>
        private void CalculateUsedDiskSpace()
        {
            try
            {
                var dir = _downloadService.GetDownloadPackageDirectory();
                if (!Directory.Exists(dir))
                {
                    UsedDiskSpaceValue = "0";
                    UsedDiskSpaceUnit = "GB";
                    return;
                }

                long totalBytes = 0;
                var dirInfo = new DirectoryInfo(dir);
                foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                {
                    totalBytes += file.Length;
                }

                var gb = totalBytes / 1024.0 / 1024.0 / 1024.0;
                if (gb >= 1)
                {
                    UsedDiskSpaceValue = $"{gb:F1}";
                    UsedDiskSpaceUnit = "GB";
                }
                else
                {
                    var mb = totalBytes / 1024.0 / 1024.0;
                    UsedDiskSpaceValue = $"{mb:F0}";
                    UsedDiskSpaceUnit = "MB";
                }
            }
            catch
            {
                UsedDiskSpaceValue = "—";
                UsedDiskSpaceUnit = "GB";
            }
        }

        private void OnDownloadProgressChanged(object sender, DownloadTask task)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var existingTask = DownloadTasks.FirstOrDefault(t => t.Id == task.Id);
                if (existingTask != null)
                {
                    var index = DownloadTasks.IndexOf(existingTask);
                    DownloadTasks[index] = task;
                }
                RefreshStatistics();
            });
        }

        private void OnDownloadCompleted(object sender, DownloadTask task)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var existingTask = DownloadTasks.FirstOrDefault(item => item.Id == task.Id);
                if (existingTask != null)
                {
                    DownloadTasks.Remove(existingTask);
                }

                CompletedTasks.Add(task);
            });
        }

        private void OnDownloadFailed(object sender, DownloadTask task)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var existingTask = DownloadTasks.FirstOrDefault(item => item.Id == task.Id);
                if (existingTask != null)
                {
                    DownloadTasks.Remove(existingTask);
                }

                CompletedTasks.Add(task);
            });
        }

        private async Task PauseDownloadAsync(string taskId)
        {
            await _downloadService.PauseDownloadAsync(taskId);
            await LoadTasksAsync();
        }

        private async Task ResumeDownloadAsync(string taskId)
        {
            // 任务自身持久化了 URL/SavePath，续传时不再需要调用方传入。
            await _downloadService.ResumeDownloadAsync(taskId);
            await LoadTasksAsync();
        }

        private async Task CancelDownloadAsync(string taskId)
        {
            await _downloadService.CancelDownloadAsync(taskId);
            await LoadTasksAsync();
        }

        /// <summary>
        /// 删除指定的已完成 / 已取消 / 已失败任务记录。
        /// </summary>
        private async Task DeleteTaskAsync(string taskId)
        {
            await _downloadService.DeleteTaskAsync(taskId);
            await LoadTasksAsync();
        }

        /// <summary>
        /// 打开已完成任务所在文件夹。
        /// </summary>
        private async Task OpenFolderAsync(string taskId)
        {
            var task = CompletedTasks.FirstOrDefault(t => t.Id == taskId)
                ?? DownloadTasks.FirstOrDefault(t => t.Id == taskId);
            if (task == null || string.IsNullOrWhiteSpace(task.SavePath)) return;

            try
            {
                var dir = Path.GetDirectoryName(task.SavePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    // 选中目标文件（如果存在）
                    var fileName = Path.GetFileName(task.SavePath);
                    var process = new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = File.Exists(task.SavePath) ? $"/select,\"{task.SavePath}\"" : $"\"{dir}\"",
                        UseShellExecute = true
                    };
                    Process.Start(process);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DownloadsViewModel] 打开文件夹失败: {ex}");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 暂停所有活跃下载任务。
        /// </summary>
        private async Task PauseAllAsync()
        {
            var activeIds = DownloadTasks
                .Where(t => t.Status == DownloadStatus.Downloading || t.Status == DownloadStatus.Pending)
                .Select(t => t.Id)
                .ToList();

            foreach (var id in activeIds)
            {
                await _downloadService.PauseDownloadAsync(id);
            }

            await LoadTasksAsync();
        }

        /// <summary>
        /// 清空所有已完成 / 已取消 / 已失败的任务记录。
        /// </summary>
        private async Task ClearCompletedAsync()
        {
            var completedIds = CompletedTasks.Select(t => t.Id).ToList();
            foreach (var id in completedIds)
            {
                await _downloadService.DeleteTaskAsync(id);
            }

            await LoadTasksAsync();
        }

        private async Task CheckForUpdatesAsync()
        {
            // 客户端/游戏更新检查已迁移至 UpdateService；此处保留入口以兼容旧 UI。
            await Task.CompletedTask;
        }

        public async Task StartDownloadAsync(string gameId, string gameName, string downloadUrl, string savePath)
        {
            var resolvedSavePath = string.IsNullOrWhiteSpace(savePath)
                ? _downloadService.BuildDefaultPackageSavePath(gameName)
                : savePath;

            await _downloadService.StartDownloadAsync(gameId, gameName, downloadUrl, resolvedSavePath);
            await LoadTasksAsync();
        }

        private async Task ApplySavedPreferencesAsync()
        {
            var settings = await _settingsService.LoadSettingsAsync();
            _downloadService.DownloadSpeedLimit = settings.DownloadSpeedLimit;
            _downloadService.MaxConcurrentDownloads = settings.MaxConcurrentDownloads;
            _downloadSpeedLimit = settings.DownloadSpeedLimit.ToString();
            _maxConcurrentDownloads = settings.MaxConcurrentDownloads.ToString();
            DownloadPackageDirectory = _downloadService.GetDownloadPackageDirectory();
            OnPropertyChanged(nameof(DownloadSpeedLimit));
            OnPropertyChanged(nameof(MaxConcurrentDownloads));
        }

        private async void PersistDownloadPreferences()
        {
            await PersistDownloadPreferencesAsync();
        }

        private async Task PersistDownloadPreferencesAsync()
        {
            var settings = await _settingsService.LoadSettingsAsync();
            settings.DownloadSpeedLimit = _downloadService.DownloadSpeedLimit;
            settings.MaxConcurrentDownloads = _downloadService.MaxConcurrentDownloads;
            await _settingsService.SaveSettingsAsync(settings);
        }

        private bool CanManageDownloads()
        {
            return !IsLoading;
        }

        private void RaiseCommandStateChanged()
        {
            _pauseDownloadCommand.RaiseCanExecuteChanged();
            _resumeDownloadCommand.RaiseCanExecuteChanged();
            _cancelDownloadCommand.RaiseCanExecuteChanged();
            _deleteTaskCommand.RaiseCanExecuteChanged();
            _openFolderCommand.RaiseCanExecuteChanged();
            _checkForUpdatesCommand.RaiseCanExecuteChanged();
            _pauseAllCommand.RaiseCanExecuteChanged();
            _clearCompletedCommand.RaiseCanExecuteChanged();
        }
    }
}
