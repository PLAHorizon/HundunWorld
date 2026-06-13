using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Horizon.Game.GengDi.Core.Views;
using Horizon.Game.GengDi.Core.ViewModels;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Models;
using System.Threading.Tasks;

namespace Horizon.Game.GengDi;

public partial class App : Application
{
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
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
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

