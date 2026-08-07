using System;

using Avalonia.Controls;
using Avalonia.Threading;

using Horizon.Game.GengDi.Core.Controls;
using Horizon.Game.GengDi.Core.Services.Call;

namespace Horizon.Game.GengDi.Core.Views
{
    /// <summary>
    /// 通话窗口宿主：全局唯一地监听 <see cref="CallService"/> 事件并管理 <see cref="CallWindow"/> 生命周期。
    /// 无论用户当前位于哪个页面，来电都会弹出通话窗口；设备/网络异常提示统一转 Toast。
    /// </summary>
    internal static class CallWindowHost
    {
        private static readonly object _sync = new();
        private static CallWindow _window;
        private static bool _attached;

        /// <summary>挂载到通话服务（幂等，多次调用安全）。</summary>
        public static void EnsureAttached()
        {
            lock (_sync)
            {
                if (_attached)
                {
                    return;
                }

                _attached = true;
            }

            var service = CallService.Instance;
            service.StateChanged += OnStateChanged;
            service.NoticeRaised += OnNoticeRaised;
            service.LocalPreviewFrame += (_, e) => PostToWindow(window => window.ShowLocalPreviewFrame(e.JpegData));
            service.RemoteVideoFrame += (_, e) => PostToWindow(window => window.ShowRemoteFrame(e.JpegData));
        }

        private static void OnStateChanged(object sender, CallStateChangedEventArgs e)
        {
            var snapshot = e.Snapshot;

            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (snapshot.State == CallState.Idle)
                    {
                        CloseWindow();
                        return;
                    }

                    var window = EnsureWindow();
                    window.ApplySnapshot(snapshot);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CallWindowHost] 更新通话窗口失败：{ex.Message}");
                }
            });
        }

        private static void OnNoticeRaised(object sender, CallNoticeEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (e.IsError)
                {
                    ToastService.Instance.Error(e.Message);
                }
                else
                {
                    ToastService.Instance.Warning(e.Message);
                }
            });
        }

        private static CallWindow EnsureWindow()
        {
            var window = _window;
            if (window != null)
            {
                return window;
            }

            window = new CallWindow();
            window.Closed += (_, _) =>
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_window, window))
                    {
                        _window = null;
                    }
                }
            };

            _window = window;

            var owner = App.MainWindow;
            if (owner != null)
            {
                try
                {
                    window.Show(owner);
                    return window;
                }
                catch
                {
                    // 主窗口已关闭/不可用时回退为独立窗口
                }
            }

            window.Show();
            return window;
        }

        private static void CloseWindow()
        {
            var window = _window;
            if (window == null)
            {
                return;
            }

            window.CloseByHost();
        }

        private static void PostToWindow(Action<CallWindow> action)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var window = _window;
                if (window == null)
                {
                    return;
                }

                try
                {
                    action(window);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CallWindowHost] 渲染通话画面失败：{ex.Message}");
                }
            });
        }
    }
}
