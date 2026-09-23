using System;
using System.IO;

using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

using Horizon.Game.GengDi.Core.Services.Call;

using Horizon.IM.Message.Enums;

namespace Horizon.Game.GengDi.Core.Views
{
    /// <summary>
    /// 通话窗口：覆盖发起（取消呼叫）、来电（接听/拒绝）、接通中（静音/摄像头开关/挂断）
    /// 以及视频画面（远端画面 + 本地预览）与各类状态/异常提示。
    /// 所有 UI 更新均在 UI 线程执行；窗口由 <see cref="CallWindowHost"/> 统一管理生命周期。
    /// </summary>
    public partial class CallWindow : Window
    {
        // 麦克风/摄像头开合图标（Material Design，NonZero 填充规则）
        private static readonly StreamGeometry S_micOn = ParseIcon(
            "M12,14c1.66,0 2.99,-1.34 2.99,-3L15,5c0,-1.66 -1.34,-3 -3,-3S9,3.34 9,5v6c0,1.66 1.34,3 3,3zm5.3,-3c0,3 -2.54,5.1 -5.3,5.1S6.7,14 6.7,11L5,11c0,3.41 2.72,6.23 6,6.72L11,21h2v-3.28c3.28,-0.48 6,-3.3 6,-6.72h-1.7z");
        private static readonly StreamGeometry S_micOff = ParseIcon(
            "M15,10.6L9,5.18L9,5c0,-1.66 1.34,-3 3,-3s3,1.34 3,3v5.16zM4.27,3L3,4.27l6.01,6.01L9.01,11c0,1.66 1.33,3 2.99,3c0.22,0 0.44,-0.03 0.65,-0.08l1.66,1.66c-0.71,0.33 -1.5,0.52 -2.31,0.52 -2.76,0 -5.3,-2.1 -5.3,-5.1L5,11c0,3.41 2.72,6.23 6,6.72L11,21h2v-3.28c0.29,-0.04 0.57,-0.11 0.84,-0.19L19.73,21 21,19.73 4.27,3z");
        private static readonly StreamGeometry S_phoneOn = ParseIcon(
            "M6.62,10.79c1.44,2.83 3.76,5.14 6.59,6.59l2.2,-2.2c0.27,-0.27 0.67,-0.36 1.02,-0.24 1.12,0.37 2.33,0.57 3.57,0.57 0.55,0 1,0.45 1,1V20c0,0.55 -0.45,1 -1,1 -9.39,0 -17,-7.61 -17,-17 0,-0.55 0.45,-1 1,-1h3.5c0.55,0 1,0.45 1,1 0,1.25 0.2,2.45 0.57,3.57 0.11,0.35 0.03,0.74 -0.25,1.02l-2.2,2.2z");
        private static readonly StreamGeometry S_camOn = ParseIcon(
            "M17,10.5V7c0,-0.55 -0.45,-1 -1,-1H4c-0.55,0 -1,0.45 -1,1v10c0,0.55 0.45,1 1,1h12c0.55,0 1,-0.45 1,-1v-3.5l4,4v-11l-4,4z");
        private static readonly StreamGeometry S_camOff = ParseIcon(
            "M21,6.5l-4,4V7c0,-0.55 -0.45,-1 -1,-1L9.82,6 21,17.18L21,6.5zM3.27,2L2,3.27 4.73,6L4,6c-0.55,0 -1,0.45 -1,1v10c0,0.55 0.45,1 1,1h12c0.21,0 0.39,-0.08 0.55,-0.18L19.73,21 21,19.73 3.27,2z");

        private static StreamGeometry ParseIcon(string path)
        {
            // "F0" 前缀 = NonZero 填充规则（Material 图标标准；默认 EvenOdd 会渲染出空洞）
            return StreamGeometry.Parse("F0" + path);
        }

        private Image _remoteVideoImage;
        private Image _localPreviewImage;
        private Border _localPreviewContainer;
        private Border _localCameraOffPanel;
        private StackPanel _remotePlaceholderPanel;
        private TextBlock _remotePlaceholderText;
        private Border _remoteMediaStateBadge;
        private TextBlock _remoteMediaStateText;
        private StackPanel _infoPanel;
        private Grid _avatarHost;
        private Ellipse _ringOuter1;
        private Ellipse _ringOuter2;
        private TextBlock _peerAvatarInitial;
        private TextBlock _peerNameText;
        private TextBlock _statusText;
        private TextBlock _elapsedText;
        private Avalonia.Controls.Shapes.Path _callTypeIcon;
        private TextBlock _callTypeText;
        private Grid _incomingActions;
        private Ellipse _acceptGlow;
        private StackPanel _outgoingActions;
        private StackPanel _inCallActions;
        private Button _acceptButton;
        private Button _rejectButton;
        private Button _cancelButton;
        private Button _muteButton;
        private Avalonia.Controls.Shapes.Path _muteIcon;
        private TextBlock _muteLabel;
        private StackPanel _cameraButtonPanel;
        private Button _cameraButton;
        private Avalonia.Controls.Shapes.Path _cameraIcon;
        private TextBlock _cameraLabel;
        private Button _hangupButton;

        private DispatcherTimer _elapsedTimer;
        private DateTime _connectedAtUtc;
        private bool _isVideoCall;
        private bool _suppressClosingHook;

        public CallWindow()
        {
            AvaloniaXamlLoader.Load(this);
            FindControls();
            WireEvents();
        }

        /// <summary>应用通话状态快照（必须在 UI 线程调用）。</summary>
        public void ApplySnapshot(CallSessionSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            _isVideoCall = snapshot.CallType == IMCallType.Video;

            _peerNameText.Text = string.IsNullOrWhiteSpace(snapshot.PeerDisplayName)
                ? snapshot.PeerId
                : snapshot.PeerDisplayName;
            _peerAvatarInitial.Text = ResolveInitial(snapshot.PeerDisplayName);
            _callTypeText.Text = _isVideoCall ? "视频通话" : "语音通话";
            _callTypeIcon.Data = _isVideoCall ? S_camOn : S_phoneOn;

            _statusText.Text = snapshot.StatusText;

            var isRingingOut = snapshot.State == CallState.OutgoingRinging;
            var isRingingIn = snapshot.State == CallState.IncomingRinging;
            var isConnecting = snapshot.State == CallState.Connecting;
            var isInCall = snapshot.State == CallState.InCall;
            var isEnding = snapshot.State == CallState.Ending;

            // 操作面板切换
            _incomingActions.IsVisible = isRingingIn;
            _outgoingActions.IsVisible = isRingingOut;
            _inCallActions.IsVisible = isConnecting || isInCall || isEnding;

            // 接听按钮呼吸光晕（仅来电时）
            _acceptGlow.IsVisible = isRingingIn;

            // 窗口标题随通话阶段变化（任务栏更直观）
            Title = isRingingIn
                ? $"来电 · {_peerNameText.Text}"
                : isRingingOut
                    ? $"正在呼叫 {_peerNameText.Text}"
                    : (isInCall || isConnecting || isEnding) && _isVideoCall
                        ? $"视频通话 · {_peerNameText.Text}"
                        : (isInCall || isConnecting || isEnding)
                            ? $"语音通话 · {_peerNameText.Text}"
                            : "通话";

            // 来电呼吸光环
            SetRinging(_ringOuter1, isRingingIn, "ringing");
            SetRinging(_ringOuter2, isRingingIn, "ringing2");

            _cameraButtonPanel.IsVisible = _isVideoCall;
            _localPreviewContainer.IsVisible = _isVideoCall && (isConnecting || isInCall);
            _localCameraOffPanel.IsVisible = snapshot.IsCameraOff;

            // 视频通话接通后：远端画面为主，信息面板顶部化（只留计时）
            var videoInCall = _isVideoCall && (isInCall || isEnding);
            if (videoInCall)
            {
                _infoPanel.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
                _infoPanel.Margin = new Avalonia.Thickness(0, 58, 0, 0);
                _avatarHost.IsVisible = false;
                _peerNameText.IsVisible = false;
                _statusText.IsVisible = false;
            }
            else
            {
                _infoPanel.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
                _infoPanel.Margin = new Avalonia.Thickness(0);
                _avatarHost.IsVisible = true;
                _peerNameText.IsVisible = true;
                _statusText.IsVisible = true;
            }

            if (!videoInCall)
            {
                _remoteVideoImage.IsVisible = false;
                _remotePlaceholderPanel.IsVisible = !_isVideoCall || !isInCall;
                _remotePlaceholderText.Text = isRingingOut
                    ? "正在呼叫对方…"
                    : isRingingIn
                        ? "向你发起通话"
                        : isConnecting
                            ? "正在接通…"
                            : "等待媒体通道建立…";
                if (_isVideoCall && (isConnecting || isInCall))
                {
                    _remotePlaceholderPanel.IsVisible = true;
                }
            }

            if (isInCall && !_elapsedTimer.IsEnabled)
            {
                _connectedAtUtc = DateTime.UtcNow;
                _elapsedText.IsVisible = true;
                _elapsedTimer.Start();
                _statusText.Text = string.Empty;
            }

            if (!isInCall && _elapsedTimer.IsEnabled)
            {
                _elapsedTimer.Stop();
            }

            // 对端媒体状态角标（持续显示，开关对称：状态清除时角标同步消失）
            var showRemoteBadge = isInCall || isConnecting;
            var badgeParts = new List<string>();
            if (showRemoteBadge && snapshot.IsRemoteMuted)
            {
                badgeParts.Add("对方已静音");
            }

            if (showRemoteBadge && _isVideoCall && snapshot.IsRemoteCameraOff)
            {
                badgeParts.Add("对方摄像头已关闭");
            }

            _remoteMediaStateBadge.IsVisible = badgeParts.Count > 0;
            _remoteMediaStateText.Text = string.Join(" · ", badgeParts);

            // 对端摄像头占位（开关对称：关闭时显示占位；重新开启后等待视频帧恢复画面）
            if (isInCall || isConnecting)
            {
                if (_isVideoCall && snapshot.IsRemoteCameraOff)
                {
                    _remoteVideoImage.IsVisible = false;
                    _remotePlaceholderPanel.IsVisible = true;
                    _remotePlaceholderText.Text = "对方已关闭摄像头";
                }
                else if (_isVideoCall
                    && _remotePlaceholderPanel.IsVisible
                    && !_remoteVideoImage.IsVisible
                    && _remotePlaceholderText.Text.Contains("摄像头"))
                {
                    // 对方重新开启了摄像头：占位文案切换为等待画面（视频帧到达后由 ShowRemoteFrame 收起占位）
                    _remotePlaceholderText.Text = "对方已开启摄像头，等待画面…";
                }
            }

            // 静音/摄像头按钮状态
            _muteIcon.Data = snapshot.IsMuted ? S_micOff : S_micOn;
            _muteLabel.Text = snapshot.IsMuted ? "取消静音" : "静音";
            ToggleActive(_muteButton, snapshot.IsMuted);

            if (_isVideoCall)
            {
                _cameraIcon.Data = snapshot.IsCameraOff ? S_camOff : S_camOn;
                _cameraLabel.Text = snapshot.IsCameraOff ? "开启摄像头" : "摄像头";
                ToggleActive(_cameraButton, snapshot.IsCameraOff);
            }
        }

        /// <summary>显示远端视频帧（必须在 UI 线程调用）。</summary>
        public void ShowRemoteFrame(byte[] jpeg)
        {
            if (!_isVideoCall || jpeg == null)
            {
                return;
            }

            var bitmap = TryDecode(jpeg, 640);
            if (bitmap == null)
            {
                // 画面解码失败：保留上一帧并提示异常，不中断通话
                if (!_remoteVideoImage.IsVisible)
                {
                    _remotePlaceholderPanel.IsVisible = true;
                    _remotePlaceholderText.Text = "对方画面异常，尝试恢复中…";
                }
                return;
            }

            var previous = _remoteVideoImage.Source as Bitmap;
            _remoteVideoImage.Source = bitmap;
            _remoteVideoImage.IsVisible = true;
            _remotePlaceholderPanel.IsVisible = false;
            previous?.Dispose();
        }

        /// <summary>显示本地摄像头预览帧（必须在 UI 线程调用）。</summary>
        public void ShowLocalPreviewFrame(byte[] jpeg)
        {
            if (!_isVideoCall || jpeg == null)
            {
                return;
            }

            var bitmap = TryDecode(jpeg, 240);
            if (bitmap == null)
            {
                return;
            }

            var previous = _localPreviewImage.Source as Bitmap;
            _localPreviewImage.Source = bitmap;
            previous?.Dispose();
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);

            if (_suppressClosingHook)
            {
                return;
            }

            // 通话进行中直接关闭窗口视为挂断；等待状态机回到 Idle 后由宿主统一关闭
            var state = CallService.Instance.CurrentSnapshot.State;
            if (state != CallState.Idle && state != CallState.Ending)
            {
                e.Cancel = true;
                _ = CallService.Instance.HangupAsync();
            }
        }

        internal void CloseByHost()
        {
            _suppressClosingHook = true;
            try
            {
                Close();
            }
            catch
            {
            }
            finally
            {
                _suppressClosingHook = false;
            }
        }

        private static void SetRinging(Ellipse ring, bool ringing, string animationClass)
        {
            if (ring == null)
            {
                return;
            }

            if (ringing && !ring.Classes.Contains(animationClass))
            {
                ring.Classes.Add(animationClass);
            }
            else if (!ringing && ring.Classes.Contains(animationClass))
            {
                ring.Classes.Remove(animationClass);
            }
        }

        private static void ToggleActive(Button button, bool active)
        {
            if (button == null)
            {
                return;
            }

            if (active && !button.Classes.Contains("active"))
            {
                button.Classes.Add("active");
            }
            else if (!active && button.Classes.Contains("active"))
            {
                button.Classes.Remove("active");
            }
        }

        private void FindControls()
        {
            _remoteVideoImage = this.FindControl<Image>(nameof(RemoteVideoImage));
            _localPreviewImage = this.FindControl<Image>(nameof(LocalPreviewImage));
            _localPreviewContainer = this.FindControl<Border>(nameof(LocalPreviewContainer));
            _localCameraOffPanel = this.FindControl<Border>(nameof(LocalCameraOffPanel));
            _remotePlaceholderPanel = this.FindControl<StackPanel>(nameof(RemotePlaceholderPanel));
            _remotePlaceholderText = this.FindControl<TextBlock>(nameof(RemotePlaceholderText));
            _remoteMediaStateBadge = this.FindControl<Border>(nameof(RemoteMediaStateBadge));
            _remoteMediaStateText = this.FindControl<TextBlock>(nameof(RemoteMediaStateText));
            _infoPanel = this.FindControl<StackPanel>(nameof(InfoPanel));
            _avatarHost = this.FindControl<Grid>("AvatarRingHost");
            _ringOuter1 = this.FindControl<Ellipse>(nameof(RingOuter1));
            _ringOuter2 = this.FindControl<Ellipse>(nameof(RingOuter2));
            _peerAvatarInitial = this.FindControl<TextBlock>(nameof(PeerAvatarInitial));
            _peerNameText = this.FindControl<TextBlock>(nameof(PeerNameText));
            _statusText = this.FindControl<TextBlock>(nameof(StatusText));
            _elapsedText = this.FindControl<TextBlock>(nameof(ElapsedText));
            _callTypeIcon = this.FindControl<Avalonia.Controls.Shapes.Path>(nameof(CallTypeIcon));
            _callTypeText = this.FindControl<TextBlock>(nameof(CallTypeText));
            _incomingActions = this.FindControl<Grid>(nameof(IncomingActions));
            _acceptGlow = this.FindControl<Ellipse>(nameof(AcceptGlow));
            _outgoingActions = this.FindControl<StackPanel>(nameof(OutgoingActions));
            _inCallActions = this.FindControl<StackPanel>(nameof(InCallActions));
            _acceptButton = this.FindControl<Button>(nameof(AcceptButton));
            _rejectButton = this.FindControl<Button>(nameof(RejectButton));
            _cancelButton = this.FindControl<Button>(nameof(CancelButton));
            _muteButton = this.FindControl<Button>(nameof(MuteButton));
            _muteIcon = this.FindControl<Avalonia.Controls.Shapes.Path>(nameof(MuteIcon));
            _muteLabel = this.FindControl<TextBlock>(nameof(MuteLabel));
            _cameraButtonPanel = this.FindControl<StackPanel>(nameof(CameraButtonPanel));
            _cameraButton = this.FindControl<Button>(nameof(CameraButton));
            _cameraIcon = this.FindControl<Avalonia.Controls.Shapes.Path>(nameof(CameraIcon));
            _cameraLabel = this.FindControl<TextBlock>(nameof(CameraLabel));
            _hangupButton = this.FindControl<Button>(nameof(HangupButton));

            _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _elapsedTimer.Tick += (_, _) =>
            {
                var elapsed = DateTime.UtcNow - _connectedAtUtc;
                _elapsedText.Text = elapsed.TotalHours >= 1
                    ? $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
                    : $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
            };
        }

        private void WireEvents()
        {
            var service = CallService.Instance;

            _acceptButton.Click += (_, _) => _ = service.AcceptAsync();
            _rejectButton.Click += (_, _) => _ = service.HangupAsync();
            _cancelButton.Click += (_, _) => _ = service.HangupAsync();
            _hangupButton.Click += (_, _) => _ = service.HangupAsync();
            _muteButton.Click += (_, _) => service.ToggleMute();
            _cameraButton.Click += (_, _) => service.ToggleCamera();
        }

        private static string ResolveInitial(string displayName)
        {
            var trimmed = displayName?.Trim();
            return string.IsNullOrEmpty(trimmed) ? "?" : trimmed[..1].ToUpperInvariant();
        }

        private static Bitmap TryDecode(byte[] jpeg, int targetWidth)
        {
            try
            {
                using var stream = new MemoryStream(jpeg);
                return Bitmap.DecodeToWidth(stream, targetWidth);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CallWindow] 视频帧解码失败：{ex.Message}");
                return null;
            }
        }
    }
}
