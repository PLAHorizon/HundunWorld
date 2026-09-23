using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Horizon.Game.GengDi.Core.Views;
using Horizon.Game.GengDi.Core.ViewModels;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Core.Helpers;
using Horizon.Game.GengDi.Models;
using System.Threading.Tasks;

namespace Horizon.Game.GengDi;

public partial class App : Application
{
    /// <summary>D1 — 服务组合根（ServiceRegistry）。测试与 ViewModel 经此解析服务。</summary>
    public static IServiceProvider Services { get; private set; } = Horizon.Game.GengDi.Core.Services.ServiceRegistry.BuildProvider();

    private static User _currentUser;

    public static event EventHandler CurrentUserChanged;

    public static User CurrentUser
    {
        get => _currentUser;
        set
        {
            if (!ReferenceEquals(_currentUser, value))
            {
                if (_currentUser != null)
                {
                    _currentUser.PropertyChanged -= CurrentUser_PropertyChanged;
                }

                _currentUser = value;

                if (_currentUser != null)
                {
                    _currentUser.PropertyChanged += CurrentUser_PropertyChanged;
                }
            }

            CurrentUserChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static Window MainWindow { get; private set; }

    private static void CurrentUser_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        CurrentUserChanged?.Invoke(null, EventArgs.Empty);
    }

    public static MainView CreateMainShell(string initialTag = "Home")
    {
        var viewModel = new MainViewModel(GameService.Instance, NavigationService.Instance, initialTag);
        return new MainView
        {
            DataContext = viewModel
        };
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        // 全局设置 LiveCharts2 中文字体，避免图表中文标签渲染为方块
        FlowerChartFontConfigurator.Configure();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 通话窗口 UI 预览模式（开发调试）：--preview-call=scene，跳过登录直接弹通话窗口
            var previewArg = Environment.GetCommandLineArgs()
                .FirstOrDefault(a => a.StartsWith("--preview-call", StringComparison.OrdinalIgnoreCase));
            if (previewArg != null)
            {
                var scene = previewArg.Contains('=') ? previewArg.Split('=', 2)[1] : "cycle";
                desktop.MainWindow = CallPreviewLauncher.Launch(scene);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            // 页面 UI 预览模式（开发调试）：--preview-view=FlowerShop，跳过登录直接进入主壳指定页
            // 可选叠加 --preview-mock：后端不可用时使用演示商品数据
            var previewViewArg = Environment.GetCommandLineArgs()
                .FirstOrDefault(a => a.StartsWith("--preview-view", StringComparison.OrdinalIgnoreCase));
            if (previewViewArg != null)
            {
                var viewTag = previewViewArg.Contains('=') ? previewViewArg.Split('=', 2)[1] : "Home";

                // 预览分支提前 return，需自行应用主题；可选 --dark 强制深色对照设计稿
                await ApplyClientThemeAsync();
                if (Environment.GetCommandLineArgs().Any(a => a.Equals("--dark", StringComparison.OrdinalIgnoreCase)))
                    RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;

                var previewWindow = new MainWindow
                {
                    Content = CreateMainShell(viewTag),
                    Title = $"预览 · {viewTag}"
                };
                desktop.MainWindow = previewWindow;
                previewWindow.Show();
                base.OnFrameworkInitializationCompleted();
                return;
            }

            var mainWindow = new MainWindow
            {
                Content = new LoginView(),
                Title = "登录"
            };
            desktop.MainWindow = mainWindow;
            MainWindow = mainWindow;
        }

        _ = ApplyClientThemeAsync();
        // 异步初始化游戏服务，不阻塞启动
        _ = InitializeServicesAsync();

        base.OnFrameworkInitializationCompleted();
    }

    private async Task ApplyClientThemeAsync()
    {
        try
        {
            await AppSettingsService.Instance.ApplyThemePreferenceAsync(this);
        }
        catch
        {
        }
    }

    private async Task InitializeServicesAsync()
    {
        try
        {
            await GameService.Instance.InitializeAsync();
        }
        catch { }

        _ = InitializeMqttClientAsync();
        _ = CheckForClientUpdateAsync();
    }

    private static async Task InitializeMqttClientAsync()
    {
        try
        {
            var host = Environment.GetEnvironmentVariable("HUNDUN_MQTT_HOST");
            var portStr = Environment.GetEnvironmentVariable("HUNDUN_MQTT_WS_PORT");

            if (!string.IsNullOrWhiteSpace(host) && int.TryParse(portStr, out var port))
            {
                await FlowerMqttClientService.Instance.ConnectAsync(host, port).ConfigureAwait(false);
            }
        }
        catch
        {
        }
    }

    private static async Task CheckForClientUpdateAsync()
    {
        try
        {
            await Core.Services.AutoUpdateService.Instance.CheckForUpdatesAsync().ConfigureAwait(false);
        }
        catch
        {
            // 静默忽略启动时更新检查失败
        }
    }
}

