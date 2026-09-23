using Android.App;
using Android.Content.PM;

using Avalonia;
using Avalonia.Android;

using Horizon.Game.GengDi.Core.Services;

namespace Horizon.Game.GengDi.Android;

/// <summary>
/// Android 主 Activity — Avalonia 单 Activity 宿主。
/// 通过 AvaloniaMainActivity&lt;App&gt; 加载共享核心的 App.axaml，
/// 与 PC 端共享同一套 View/ViewModel/Service 层。
/// </summary>
[Activity(
    Label = "耕地",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // 初始化客户端运行时上下文（实例隔离、本地存储路径等）
        ClientRuntimeContext.Initialize(System.Array.Empty<string>());

        return base.CustomizeAppBuilder(builder);
    }
}
