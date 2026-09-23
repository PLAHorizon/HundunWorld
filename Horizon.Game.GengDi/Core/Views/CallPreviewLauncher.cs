using System;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Threading;

using Horizon.Game.GengDi.Core.Services.Call;

using Horizon.IM.Message.Enums;

namespace Horizon.Game.GengDi.Core.Views
{
    /// <summary>
    /// 通话窗口 UI 预览启动器（开发调试用）。
    /// 用法：GengDi.exe --preview-call=scene
    /// scene ∈ incoming|outgoing|connecting|incall-audio|incall-video|cycle（cycle 轮播全部场景）。
    /// 启动后直接弹出通话窗口并以伪造快照驱动，不登录、不连接网关。
    /// </summary>
    public static class CallPreviewLauncher
    {
        public static Window Launch(string scene)
        {
            var window = new CallWindow();
            window.Topmost = true;

            var scenarios = BuildScenes(scene);
            // 先应用快照再显示：确保首帧合成即包含按钮/图标，
            // 便于截图工具（PrintWindow）抓到完整画面做像素级验证。
            window.ApplySnapshot(scenarios[0]);
            var index = 1;

            window.Show();
            window.Activate();

            if (scenarios.Length > 1)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
                timer.Tick += (_, _) =>
                {
                    if (index >= scenarios.Length)
                    {
                        index = 0;
                    }

                    window.ApplySnapshot(scenarios[index]);
                    index++;
                };
                timer.Start();

                window.Closed += (_, _) => timer.Stop();
            }

            return window;
        }

        private static CallSessionSnapshot[] BuildScenes(string scene)
        {
            var incoming = new CallSessionSnapshot
            {
                State = CallState.IncomingRinging,
                CallId = "preview",
                PeerId = "1010776",
                PeerDisplayName = "花小南",
                CallType = IMCallType.Video,
                StatusText = "邀请你视频通话"
            };
            var outgoing = new CallSessionSnapshot
            {
                State = CallState.OutgoingRinging,
                CallId = "preview",
                PeerId = "1010776",
                PeerDisplayName = "花小南",
                CallType = IMCallType.Audio,
                IsOutgoing = true,
                StatusText = "正在等待对方接受…"
            };
            var connecting = new CallSessionSnapshot
            {
                State = CallState.Connecting,
                CallId = "preview",
                PeerId = "1010776",
                PeerDisplayName = "花小南",
                CallType = IMCallType.Video,
                StatusText = "正在建立媒体通道…"
            };
            var incallAudio = new CallSessionSnapshot
            {
                State = CallState.InCall,
                CallId = "preview",
                PeerId = "1010776",
                PeerDisplayName = "花小南",
                CallType = IMCallType.Audio,
                IsMuted = true,
                StatusText = string.Empty
            };
            var incallVideo = new CallSessionSnapshot
            {
                State = CallState.InCall,
                CallId = "preview",
                PeerId = "1010776",
                PeerDisplayName = "花小南",
                CallType = IMCallType.Video,
                StatusText = string.Empty
            };

            return scene switch
            {
                "incoming" => new[] { incoming },
                "outgoing" => new[] { outgoing },
                "connecting" => new[] { connecting },
                "incall-audio" => new[] { incallAudio },
                "incall-video" => new[] { incallVideo },
                _ => new[] { incoming, outgoing, connecting, incallVideo, incallAudio }
            };
        }
    }
}
