using System;
using System.IO;

using Avalonia;

using Horizon.Game.GengDi.Core.Services;

using WebViewControl;

namespace Horizon.Game.GengDi.HarmonyOS;

/// <summary>
/// HarmonyOS 宿主入口。
///
/// HarmonyOS Next 不支持 .NET 原生绑定，本项目以 Avalonia + WebView 方式
/// 承载鸿蒙设计原型 HTML 页面，用于本地开发预览与 HarmonyOS WebView 集成参考。
/// 实际鸿蒙原生部署时，将 Assets/Pages 下的 HTML 资源移植到 ArkTS WebView 组件即可。
/// </summary>
internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // WebView (CefGlue) 全局配置：与 PC 端保持一致。
        // 仅开发构建允许跳过 TLS 校验；发布构建必须严格校验（安全审查 2026-08）。
#if DEBUG
        SslConfiguration.ForceBypassSslValidation(true);
#endif

        ClientRuntimeContext.Initialize(args);

        // 启用离屏渲染，使 WebView 由 Avalonia 合成，避免原生窗口溢出
        WebView.Settings.EnableVideoAutoplay = true;
        WebView.Settings.OsrEnabled = true;

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    /// <summary>
    /// 解析 HarmonyOS 设计原型 HTML 页面的本地文件路径。
    /// 用于 WebView 加载 Assets/Pages/harmony-*.html。
    /// </summary>
    public static string ResolveDesignPagePath(string pageName)
    {
        var baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "Assets", "Pages", pageName);
    }
}
