#if DESKTOP
using Avalonia;
using Avalonia.Diagnostics;
using System;

using Horizon.Game.GengDi.Core.Services;
using WebViewControl;

namespace Horizon.Game.GengDi
{
    internal class Program
    {
        [STAThread]
        public static void Main(string[] args)
       {
#if DEBUG
            // 仅开发构建允许跳过 TLS 校验（连接自签名开发服务器）；发布构建必须严格校验，
            // 否则登录凭据、令牌与自动更新下载均可被中间人截获/替换（安全审查 2026-08）。
            SslConfiguration.ForceBypassSslValidation(true);
#endif
            ClientRuntimeContext.Initialize(args);

            // Configure WebView (CefGlue) global settings before any WebView instance is created.
            // EnableVideoAutoplay: allows muted inline autoplay so video cards play without a user-gesture prompt.
            // OsrEnabled: uses off-screen rendering so WebViews are composited by Avalonia instead of floating
            //             as native top-level windows — this prevents them from overflowing sibling controls
            //             such as the chat send box and the header information bar.
            // 必须通过 WebView.Settings 静态单例修改全局配置，直接 new GlobalSettings() 不会生效。
            WebView.Settings.EnableVideoAutoplay = true;
            WebView.Settings.OsrEnabled = true;

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                // Avalonia 11.3 无 AppBuilder 级 DevTools API（WithDevTools 已移除）；
                // DevTools 由 PC 启动器/验证壳在 TopLevel 打开后 AttachDevTools 附加。此块原为失效代码，已移除以修复 Debug 构建。
                .LogToTrace();
    }
}
#endif
