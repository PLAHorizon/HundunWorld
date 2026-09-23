using Avalonia;
using Avalonia.iOS;

using Foundation;

using Horizon.Game.GengDi.Core.Services;

using UIKit;

namespace Horizon.Game.GengDi.iOS;

/// <summary>
/// iOS 应用入口 — 通过 AvaloniaAppDelegate 加载共享核心的 App.axaml。
/// 与 PC 端共享同一套 View/ViewModel/Service 层。
/// 注意：iOS 构建必须在 macOS + Xcode 环境下执行。
/// </summary>
[Register("AppDelegate")]
public partial class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // 初始化客户端运行时上下文（实例隔离、本地存储路径等）
        ClientRuntimeContext.Initialize(System.Array.Empty<string>());

        return base.CustomizeAppBuilder(builder);
    }
}

/// <summary>
/// iOS Main 入口 — UIApplication.Main 启动 AppDelegate。
/// </summary>
public class Application
{
    static void Main(string[] args)
    {
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
