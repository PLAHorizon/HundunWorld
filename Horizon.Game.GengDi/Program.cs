using Avalonia;
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
//#if DEBUG
           SslConfiguration.ForceBypassSslValidation(true);
//#endif
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
                .LogToTrace();
    }
}