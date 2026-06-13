using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        private readonly AsyncRelayCommand _checkForUpdatesCommand;
        private ObservableCollection<DownloadTask> _downloadTasks;
        private ObservableCollection<DownloadTask> _completedTasks;
        private string _downloadSpeedLimit;
        private string _maxConcurrentDownloads;
        private string _downloadPackageDirectory;
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

        public ICommand PauseDownloadCommand { get; }
        public ICommand ResumeDownloadCommand { get; }
        public ICommand CancelDownloadCommand { get; }
        public ICommand CheckForUpdatesCommand { get; }

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
            _checkForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync, CanManageDownloads);

            PauseDownloadCommand = _pauseDownloadCommand;
            ResumeDownloadCommand = _resumeDownloadCommand;
            CancelDownloadCommand = _cancelDownloadCommand;
            CheckForUpdatesCommand = _checkForUpdatesCommand;
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
                if (task.Status == DownloadStatus.Completed || task.Status == DownloadStatus.Failed)
                {
                    CompletedTasks.Add(task);
                }
                else
                {
                    DownloadTasks.Add(task);
                }
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
            _checkForUpdatesCommand.RaiseCanExecuteChanged();
        }
    }
}
