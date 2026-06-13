using System;
using System.IO;
using System.Threading;
using System.Windows.Input;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.ViewModels;

public sealed class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly AppSettingsService _settingsService;
    private readonly AutoUpdateService _updateService;
    private readonly AsyncRelayCommand _saveSettingsCommand;
    private readonly AsyncRelayCommand _useSystemThemeCommand;
    private readonly AsyncRelayCommand _useLightThemeCommand;
    private readonly AsyncRelayCommand _useDarkThemeCommand;
    private readonly AsyncRelayCommand _checkForUpdatesCommand;
    private readonly AsyncRelayCommand _downloadAndApplyUpdateCommand;
    private string _customInstallPath;
    private string _statusMessage;
    private AppThemePreference _themePreference;
    private GameInstallPathMode _installPathMode;
    private SettingsPreviewState _themePreview;
    private SettingsPreviewState _installPathPreview;
    private bool _isLoading;
    private bool _isInitialized;
    private ClientAppSettings _loadedSettings;

    // 更新相关状态
    private string _updateStatusMessage = "点击「检查更新」以查找可用版本。";
    private bool _isCheckingForUpdate;
    private bool _isDownloadingUpdate;
    private double _updateDownloadProgress;
    private bool _isUpdateAvailable;
    private AppUpdateInfo _pendingUpdate;
    private CancellationTokenSource _updateCts;

    public SettingsViewModel()
    {
        _settingsService = AppSettingsService.Instance;
        _updateService = AutoUpdateService.Instance;
        _updateService.DownloadProgressChanged += OnUpdateDownloadProgressChanged;

        _saveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, CanEditSettings);
        _useSystemThemeCommand = new AsyncRelayCommand(() => SetThemePreferenceAsync(AppThemePreference.System), CanEditSettings);
        _useLightThemeCommand = new AsyncRelayCommand(() => SetThemePreferenceAsync(AppThemePreference.Light), CanEditSettings);
        _useDarkThemeCommand = new AsyncRelayCommand(() => SetThemePreferenceAsync(AppThemePreference.Dark), CanEditSettings);
        _checkForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync, CanCheckForUpdates);
        _downloadAndApplyUpdateCommand = new AsyncRelayCommand(DownloadAndApplyUpdateAsync, CanDownloadAndApplyUpdate);

        SaveSettingsCommand = _saveSettingsCommand;
        UseSystemThemeCommand = _useSystemThemeCommand;
        UseLightThemeCommand = _useLightThemeCommand;
        UseDarkThemeCommand = _useDarkThemeCommand;
        UseDefaultInstallPathCommand = new RelayCommand(() => SetInstallPathMode(GameInstallPathMode.Default));
        UseCustomInstallPathCommand = new RelayCommand(() => SetInstallPathMode(GameInstallPathMode.Custom));
        CheckForUpdatesCommand = _checkForUpdatesCommand;
        DownloadAndApplyUpdateCommand = _downloadAndApplyUpdateCommand;
    }

    public string DefaultInstallPath => AppSettingsService.GetDefaultInstallDirectory();

    public string CustomInstallPath
    {
        get => _customInstallPath;
        set
        {
            if (SetProperty(ref _customInstallPath, value))
            {
                RefreshPreviewStates();
            }
        }
    }

    public string ResolvedInstallPath => _installPathMode == GameInstallPathMode.Custom && !string.IsNullOrWhiteSpace(CustomInstallPath)
        ? CustomInstallPath.Trim()
        : DefaultInstallPath;

    public bool IsCustomInstallPathMode => _installPathMode == GameInstallPathMode.Custom;

    public bool IsDefaultInstallPathMode => _installPathMode == GameInstallPathMode.Default;

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
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

    public SettingsPreviewState ThemePreview
    {
        get => _themePreview;
        private set => SetProperty(ref _themePreview, value);
    }

    public SettingsPreviewState InstallPathPreview
    {
        get => _installPathPreview;
        private set => SetProperty(ref _installPathPreview, value);
    }

    public ICommand SaveSettingsCommand { get; }

    public ICommand UseSystemThemeCommand { get; }

    public ICommand UseLightThemeCommand { get; }

    public ICommand UseDarkThemeCommand { get; }

    public ICommand UseDefaultInstallPathCommand { get; }

    public ICommand UseCustomInstallPathCommand { get; }

    public ICommand CheckForUpdatesCommand { get; }

    public ICommand DownloadAndApplyUpdateCommand { get; }

    /// <summary>更新状态提示文字，显示在 UI 中。</summary>
    public string UpdateStatusMessage
    {
        get => _updateStatusMessage;
        private set => SetProperty(ref _updateStatusMessage, value);
    }

    /// <summary>是否正在检查更新。</summary>
    public bool IsCheckingForUpdate
    {
        get => _isCheckingForUpdate;
        private set
        {
            if (SetProperty(ref _isCheckingForUpdate, value))
            {
                RaiseUpdateCommandStateChanged();
            }
        }
    }

    /// <summary>是否正在下载更新。</summary>
    public bool IsDownloadingUpdate
    {
        get => _isDownloadingUpdate;
        private set
        {
            if (SetProperty(ref _isDownloadingUpdate, value))
            {
                RaiseUpdateCommandStateChanged();
                OnPropertyChanged(nameof(UpdateDownloadProgressText));
            }
        }
    }

    /// <summary>更新下载进度（0–100）。</summary>
    public double UpdateDownloadProgress
    {
        get => _updateDownloadProgress;
        private set
        {
            if (SetProperty(ref _updateDownloadProgress, value))
            {
                OnPropertyChanged(nameof(UpdateDownloadProgressText));
            }
        }
    }

    /// <summary>格式化后的下载进度文字（如 "下载中 42%"）。</summary>
    public string UpdateDownloadProgressText =>
        IsDownloadingUpdate ? $"下载中 {_updateDownloadProgress:F0}%" : string.Empty;

    /// <summary>是否有可用更新。</summary>
    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        private set
        {
            if (SetProperty(ref _isUpdateAvailable, value))
            {
                RaiseUpdateCommandStateChanged();
            }
        }
    }

    /// <summary>当前版本号。</summary>
    public string CurrentVersion => AutoUpdateService.CurrentVersion;

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        await LoadSettingsAsync();
    }

    public void SetCustomInstallPath(string path)
    {
        CustomInstallPath = path?.Trim() ?? string.Empty;
        StatusMessage = string.IsNullOrWhiteSpace(CustomInstallPath)
            ? "未选择自定义目录。"
            : "已选择自定义目录，点击保存后会写入本地设置。";
    }

    public async Task SaveSettingsAsync()
    {
        if (_installPathMode == GameInstallPathMode.Custom && string.IsNullOrWhiteSpace(CustomInstallPath))
        {
            StatusMessage = "自定义安装路径不能为空。";
            return;
        }

        if (_installPathMode == GameInstallPathMode.Custom)
        {
            await ClientAsyncDispatcher.RunBackgroundAsync(() => Directory.CreateDirectory(CustomInstallPath.Trim()));
        }

        var snapshot = BuildSnapshot();
        await _settingsService.SaveSettingsAsync(snapshot);
        _loadedSettings = snapshot;
        await _settingsService.ApplyThemePreferenceAsync();
        await LoadSettingsAsync();
        StatusMessage = "设置已保存，并会在下次启动时自动加载。";
    }

    private async Task LoadSettingsAsync()
    {
        IsLoading = true;

        var settings = await _settingsService.LoadSettingsAsync();
        _loadedSettings = settings;
        _themePreference = settings.ThemePreference;
        _installPathMode = settings.InstallPathMode;
        CustomInstallPath = settings.CustomInstallPath;

        RefreshPreviewStates();
        IsLoading = false;
    }

    private async Task SetThemePreferenceAsync(AppThemePreference preference)
    {
        if (_themePreference == preference)
        {
            return;
        }

        _themePreference = preference;
        RefreshPreviewStates();
        var snapshot = BuildSnapshot();
        await _settingsService.SaveSettingsAsync(snapshot);
        _loadedSettings = snapshot;
        await _settingsService.ApplyThemePreferenceAsync();
        StatusMessage = "主题设置已保存。";
    }

    private void SetInstallPathMode(GameInstallPathMode mode)
    {
        if (_installPathMode == mode)
        {
            return;
        }

        _installPathMode = mode;
        RefreshPreviewStates();
        StatusMessage = mode == GameInstallPathMode.Default
            ? "已切换到默认安装目录，点击保存后生效。"
            : "已切换到自定义安装目录模式，选择目录后点击保存。";
    }

    private ClientAppSettings BuildSnapshot()
    {
        return new ClientAppSettings
        {
            ThemePreference = _themePreference,
            InstallPathMode = _installPathMode,
            CustomInstallPath = CustomInstallPath?.Trim() ?? string.Empty,
            MaxConcurrentDownloads = _loadedSettings?.MaxConcurrentDownloads ?? 2,
            DownloadSpeedLimit = _loadedSettings?.DownloadSpeedLimit ?? 0
        };
    }

    private void RefreshPreviewStates()
    {
        ThemePreview = _themePreference switch
        {
            AppThemePreference.Light => new SettingsPreviewState(
                "浅色主题",
                "立即应用明亮界面并写入本地设置。",
                "下次启动会自动恢复浅色模式。",
                CreateIcon(Symbol.WeatherSunny)),
            AppThemePreference.Dark => new SettingsPreviewState(
                "深色主题",
                "使用更接近 Battle.net 的深蓝界面层次。",
                "下次启动会自动恢复深色模式。",
                CreateIcon(Symbol.DarkTheme)),
            _ => new SettingsPreviewState(
                "跟随系统",
                "启动时自动读取系统主题偏好。",
                "当前设置会在应用启动时自动加载。",
                CreateIcon(Symbol.Settings))
        };

        InstallPathPreview = _installPathMode switch
        {
            GameInstallPathMode.Custom => new SettingsPreviewState(
                "自定义安装目录",
                string.IsNullOrWhiteSpace(CustomInstallPath)
                    ? "尚未选择目录，请先浏览一个本地文件夹。"
                    : "当前已切换到自定义目录模式。",
                string.IsNullOrWhiteSpace(CustomInstallPath) ? DefaultInstallPath : CustomInstallPath.Trim(),
                CreateIcon(Symbol.OpenFolder)),
            _ => new SettingsPreviewState(
                "默认安装目录",
                "使用文档目录下的 HorizonGames 作为游戏默认安装根目录。",
                DefaultInstallPath,
                CreateIcon(Symbol.Folder))
        };

        OnPropertyChanged(nameof(IsCustomInstallPathMode));
        OnPropertyChanged(nameof(IsDefaultInstallPathMode));
        OnPropertyChanged(nameof(ResolvedInstallPath));
    }

    private static SymbolIconSource CreateIcon(Symbol symbol)
    {
        return new SymbolIconSource { Symbol = symbol };
    }

    private bool CanEditSettings()
    {
        return !IsLoading;
    }

    private bool CanCheckForUpdates()
    {
        return !IsCheckingForUpdate && !IsDownloadingUpdate;
    }

    private bool CanDownloadAndApplyUpdate()
    {
        return IsUpdateAvailable && !IsDownloadingUpdate && !IsCheckingForUpdate;
    }

    private async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdate = true;
        UpdateStatusMessage = "正在检查更新…";
        IsUpdateAvailable = false;
        _pendingUpdate = null;

        try
        {
            _updateCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var updateInfo = await _updateService.CheckForUpdatesAsync(_updateCts.Token).ConfigureAwait(true);

            if (updateInfo != null)
            {
                _pendingUpdate = updateInfo;
                IsUpdateAvailable = true;
                UpdateStatusMessage = $"发现新版本 {updateInfo.LatestVersion}！{(string.IsNullOrWhiteSpace(updateInfo.ReleaseNotes) ? string.Empty : $"\n{updateInfo.ReleaseNotes}")}";
            }
            else
            {
                UpdateStatusMessage = $"当前已是最新版本（{AutoUpdateService.CurrentVersion}）。";
            }
        }
        catch (OperationCanceledException)
        {
            UpdateStatusMessage = "更新检查已取消。";
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = $"检查更新失败：{ex.Message}";
        }
        finally
        {
            _updateCts?.Dispose();
            _updateCts = null;
            IsCheckingForUpdate = false;
        }
    }

    private async Task DownloadAndApplyUpdateAsync()
    {
        if (_pendingUpdate == null)
        {
            return;
        }

        IsDownloadingUpdate = true;
        UpdateDownloadProgress = 0;
        UpdateStatusMessage = "正在下载更新包…";

        string? installerPath = null;
        try
        {
            _updateCts = new CancellationTokenSource();
            installerPath = await _updateService.DownloadUpdatePackageAsync(_pendingUpdate, _updateCts.Token).ConfigureAwait(true);
            UpdateStatusMessage = "下载完成，正在启动安装程序…";
            _updateService.ApplyUpdate(installerPath);
        }
        catch (OperationCanceledException)
        {
            UpdateStatusMessage = "更新下载已取消。";
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = $"下载更新失败：{ex.Message}";
        }
        finally
        {
            _updateCts?.Dispose();
            _updateCts = null;
            IsDownloadingUpdate = false;
        }
    }

    private void OnUpdateDownloadProgressChanged(object sender, double progress)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateDownloadProgress = progress;
            UpdateStatusMessage = $"下载中 {progress:F0}%…";
        });
    }

    public void Dispose()
    {
        _updateService.DownloadProgressChanged -= OnUpdateDownloadProgressChanged;
        _updateCts?.Dispose();
    }

    private void RaiseUpdateCommandStateChanged()
    {
        _checkForUpdatesCommand.RaiseCanExecuteChanged();
        _downloadAndApplyUpdateCommand.RaiseCanExecuteChanged();
    }

    private void RaiseCommandStateChanged()
    {
        _saveSettingsCommand.RaiseCanExecuteChanged();
        _useSystemThemeCommand.RaiseCanExecuteChanged();
        _useLightThemeCommand.RaiseCanExecuteChanged();
        _useDarkThemeCommand.RaiseCanExecuteChanged();
    }
}

public sealed class SettingsPreviewState
{
    public SettingsPreviewState(string title, string description, string detail, IconSource icon)
    {
        Title = title;
        Description = description;
        Detail = detail;
        Icon = icon;
    }

    public string Title { get; }

    public string Description { get; }

    public string Detail { get; }

    public IconSource Icon { get; }
}