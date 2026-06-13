using System;
using System.Windows.Input;
using Avalonia.Threading;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Enums;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    /// <summary>
    /// 统一的"当前活动操作"面板数据源：汇聚 <see cref="DownloadService"/> / <see cref="InstallService"/> / <see cref="UpdateService"/>
    /// 三者的进度事件到一个单一的 bindable surface，供 <c>MainView</c> 底部进度条绑定。
    ///
    /// 单例，进程内 <c>MainViewModel</c> / UI 共享同一状态。
    /// </summary>
    public sealed class ActiveOperationViewModel : ViewModelBase
    {
        private static readonly Lazy<ActiveOperationViewModel> _instance = new(() => new ActiveOperationViewModel());
        public static ActiveOperationViewModel Instance => _instance.Value;

        private string _title = string.Empty;
        private string _statusText = string.Empty;
        private string _progressText = string.Empty;
        private double _percent;
        private double _speedBytesPerSecond;
        private bool _isActive;
        private bool _isDownload;
        private bool _isInstall;
        private bool _isUpdate;
        private bool _isProgressIndeterminate;
        private string _currentTaskId;

        private readonly AsyncRelayCommand _pauseCommand;
        private readonly AsyncRelayCommand _resumeCommand;
        private readonly AsyncRelayCommand _cancelCommand;

        private ActiveOperationViewModel()
        {
            _pauseCommand = new AsyncRelayCommand(PauseAsync, () => IsActive && IsDownload);
            _resumeCommand = new AsyncRelayCommand(ResumeAsync, () => IsActive && IsDownload);
            _cancelCommand = new AsyncRelayCommand(CancelAsync, () => IsActive);

            PauseCommand = _pauseCommand;
            ResumeCommand = _resumeCommand;
            CancelCommand = _cancelCommand;

            DownloadService.Instance.DownloadProgressChanged += OnDownloadProgress;
            DownloadService.Instance.DownloadCompleted += OnDownloadTerminal;
            DownloadService.Instance.DownloadFailed += OnDownloadTerminal;
            DownloadService.Instance.DownloadCancelled += OnDownloadTerminal;

            InstallService.Instance.InstallProgressChanged += OnInstallProgress;
            InstallService.Instance.InstallCompleted += OnInstallCompleted;
            InstallService.Instance.InstallCancelled += OnInstallCancelled;
            InstallService.Instance.InstallFailed += OnInstallFailed;

            UpdateService.Instance.UpdateProgressChanged += OnUpdateProgress;
        }

        public string Title { get => _title; private set => SetProperty(ref _title, value); }
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
        public string ProgressText { get => _progressText; private set => SetProperty(ref _progressText, value); }
        public double Percent { get => _percent; private set => SetProperty(ref _percent, value); }
        public double SpeedBytesPerSecond { get => _speedBytesPerSecond; private set => SetProperty(ref _speedBytesPerSecond, value); }
        public bool IsProgressIndeterminate { get => _isProgressIndeterminate; private set => SetProperty(ref _isProgressIndeterminate, value); }
        public bool IsActive
        {
            get => _isActive;
            private set
            {
                if (SetProperty(ref _isActive, value))
                {
                    RaiseCommandsChanged();
                }
            }
        }
        public bool IsDownload { get => _isDownload; private set { if (SetProperty(ref _isDownload, value)) RaiseCommandsChanged(); } }
        public bool IsInstall { get => _isInstall; private set { if (SetProperty(ref _isInstall, value)) RaiseCommandsChanged(); } }
        public bool IsUpdate { get => _isUpdate; private set { if (SetProperty(ref _isUpdate, value)) RaiseCommandsChanged(); } }

        public ICommand PauseCommand { get; }
        public ICommand ResumeCommand { get; }
        public ICommand CancelCommand { get; }

        private void OnDownloadProgress(object sender, DownloadTask task)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var hasKnownTotal = task.TotalSize > 0;
                var downloadProgressText = hasKnownTotal
                    ? $"{task.Progress:F1}%"
                    : task.Status switch
                    {
                        DownloadStatus.Paused => "已暂停",
                        DownloadStatus.Pending => "排队中",
                        _ => "处理中"
                    };
                _currentTaskId = task.Id;
                Title = string.IsNullOrEmpty(task.GameName) ? "下载中" : $"下载 {task.GameName}";
                Percent = hasKnownTotal ? task.Progress : 0;
                ProgressText = downloadProgressText;
                SpeedBytesPerSecond = task.Speed;
                StatusText = hasKnownTotal
                    ? $"{task.Progress:F1}% · {FormatBytes(task.DownloadedSize)}/{FormatBytes(task.TotalSize)}"
                    : task.Status == DownloadStatus.Paused
                        ? $"已暂停 · 已下载 {FormatBytes(task.DownloadedSize)}"
                        : $"已下载 {FormatBytes(task.DownloadedSize)}";
                IsProgressIndeterminate = !hasKnownTotal && task.Status == DownloadStatus.Downloading;
                IsDownload = task.Status == DownloadStatus.Downloading || task.Status == DownloadStatus.Paused || task.Status == DownloadStatus.Pending;
                IsInstall = false;
                IsUpdate = task.Kind == DownloadTaskKind.GameUpdate || task.Kind == DownloadTaskKind.AppUpdate;
                IsActive = IsDownload || IsUpdate;
            });
        }

        private void OnDownloadTerminal(object sender, DownloadTask task)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_currentTaskId != task.Id) return;
                _currentTaskId = null;
                IsDownload = false;
                IsProgressIndeterminate = false;
                IsActive = IsInstall || IsUpdate;
                if (task.Status == DownloadStatus.Completed)
                {
                    Percent = 100;
                    ProgressText = "100.0%";
                    StatusText = "下载完成";
                }
                else if (task.Status == DownloadStatus.Cancelled)
                {
                    Percent = 0;
                    ProgressText = "0.0%";
                    StatusText = "下载已取消";
                }
                else if (task.Status == DownloadStatus.Failed)
                {
                    Percent = 0;
                    ProgressText = "0.0%";
                    StatusText = "下载失败";
                }
            });
        }

        private void OnInstallProgress(object sender, InstallProgress ip)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var hasKnownTotal = ip.TotalBytes > 0;
                Title = $"安装 {ip.Game?.Name ?? ip.Game?.Id}";
                Percent = hasKnownTotal ? ip.Percent : 0;
                ProgressText = hasKnownTotal ? $"{ip.Percent:F1}%" : "处理中";
                SpeedBytesPerSecond = 0;
                StatusText = hasKnownTotal
                    ? $"{ip.Percent:F1}% · {ip.CurrentEntry}"
                    : string.IsNullOrWhiteSpace(ip.CurrentEntry) ? "正在安装..." : $"正在安装 · {ip.CurrentEntry}";
                IsProgressIndeterminate = !hasKnownTotal;
                IsInstall = true;
                IsDownload = false;
                IsActive = true;
            });
        }

        private void OnInstallCompleted(object sender, GameInfo game)
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsInstall = false;
                IsProgressIndeterminate = false;
                Percent = 100;
                ProgressText = "100.0%";
                StatusText = "安装完成";
                IsActive = IsDownload || IsUpdate;
            });
        }

        private void OnInstallCancelled(object sender, GameInfo game)
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsInstall = false;
                IsProgressIndeterminate = false;
                Percent = 0;
                ProgressText = "0.0%";
                StatusText = "安装已取消";
                IsActive = IsDownload || IsUpdate;
            });
        }

        private void OnInstallFailed(object sender, (GameInfo Game, Exception Error) tuple)
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsInstall = false;
                IsProgressIndeterminate = false;
                Percent = 0;
                ProgressText = "0.0%";
                StatusText = "安装失败: " + tuple.Error?.Message;
                IsActive = IsDownload || IsUpdate;
            });
        }

        private void OnUpdateProgress(object sender, UpdateProgress up)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Title = $"更新 {up.Game?.Name ?? up.Game?.Id}";
                Percent = up.OverallPercent;
                ProgressText = $"{up.OverallPercent:F1}%";
                SpeedBytesPerSecond = 0;
                StatusText = $"{up.OverallPercent:F1}% · {up.Phase} {up.CurrentVersion}";
                IsProgressIndeterminate = false;
                IsUpdate = up.Phase == UpdatePhase.Download || up.Phase == UpdatePhase.Install;
                IsActive = IsUpdate || IsDownload || IsInstall;
                if (up.Phase == UpdatePhase.Completed) { StatusText = "更新完成"; ProgressText = "100.0%"; IsUpdate = false; IsActive = IsDownload || IsInstall; }
                if (up.Phase == UpdatePhase.Cancelled) { StatusText = "更新已取消"; ProgressText = "0.0%"; IsUpdate = false; IsActive = IsDownload || IsInstall; }
                if (up.Phase == UpdatePhase.Failed) { StatusText = "更新失败"; ProgressText = "0.0%"; IsUpdate = false; IsActive = IsDownload || IsInstall; }
            });
        }

        private async System.Threading.Tasks.Task PauseAsync()
        {
            if (!string.IsNullOrEmpty(_currentTaskId))
            {
                await DownloadService.Instance.PauseDownloadAsync(_currentTaskId).ConfigureAwait(false);
            }
        }

        private async System.Threading.Tasks.Task ResumeAsync()
        {
            if (!string.IsNullOrEmpty(_currentTaskId))
            {
                await DownloadService.Instance.ResumeDownloadAsync(_currentTaskId).ConfigureAwait(false);
            }
        }

        private async System.Threading.Tasks.Task CancelAsync()
        {
            // 取消前弹出确认对话框，提示"将清除已下载/已安装的部分数据"。
            var confirmed = await Controls.ConfirmDialog.ShowAsync(
                "确认取消",
                "取消后已下载/已安装的部分数据会被清理，确认继续吗？",
                primaryText: "取消任务",
                cancelText: "继续").ConfigureAwait(true);
            if (!confirmed) return;

            if (IsUpdate)
            {
                UpdateService.Instance.CancelActive();
                return;
            }

            if (!string.IsNullOrEmpty(_currentTaskId))
            {
                await DownloadService.Instance.CancelDownloadAsync(_currentTaskId, purgePartial: true).ConfigureAwait(false);
            }
        }

        private void RaiseCommandsChanged()
        {
            _pauseCommand.RaiseCanExecuteChanged();
            _resumeCommand.RaiseCanExecuteChanged();
            _cancelCommand.RaiseCanExecuteChanged();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            var unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return $"{size:F1} {units[unit]}";
        }
    }
}
