using System;
using System.IO;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
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
        private Image _remoteVideoImage;
        private Image _localPreviewImage;
        private TextBlock _localCameraOffText;
        private Border _localPreviewContainer;
        private StackPanel _remotePlaceholderPanel;
        private TextBlock _remotePlaceholderText;
        private StackPanel _infoPanel;
        private TextBlock _peerAvatarInitial;
        private TextBlock _peerNameText;
        private TextBlock _callTypeText;
        private TextBlock _statusText;
        private TextBlock _elapsedText;
        private Grid _incomingActions;
        private Grid _inCallActions;
        private Button _acceptButton;
        private Button _rejectButton;
        private Button _cancelButton;
        private Button _muteButton;
        private Button _cameraButton;
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
            _statusText.Text = snapshot.StatusText;

            var isRingingOut = snapshot.State == CallState.OutgoingRinging;
            var isRingingIn = snapshot.State == CallState.IncomingRinging;
            var isConnecting = snapshot.State == CallState.Connecting;
            var isInCall = snapshot.State == CallState.InCall;
            var isEnding = snapshot.State == CallState.Ending;

            _incomingActions.IsVisible = isRingingIn;
            _cancelButton.IsVisible = isRingingOut;
            _inCallActions.IsVisible = isConnecting || isInCall || isEnding;

            _cameraButton.IsVisible = _isVideoCall;
            _localPreviewContainer.IsVisible = _isVideoCall && (isConnecting || isInCall);
            _localCameraOffText.IsVisible = snapshot.IsCameraOff;

            if (!isInCall && !isEnding)
            {
                _remoteVideoImage.IsVisible = false;
                _remotePlaceholderPanel.IsVisible = true;
                _remotePlaceholderText.Text = isRingingOut
                    ? "正在呼叫…"
                    : isRingingIn
                        ? "向你发起通话"
                        : "等待媒体通道建立…";
                _elapsedText.IsVisible = false;
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

            // 对端状态提示
            if (isInCall || isConnecting)
            {
                if (snapshot.IsRemoteCameraOff && _isVideoCall)
                {
                    _remoteVideoImage.IsVisible = false;
                    _remotePlaceholderPanel.IsVisible = true;
                    _remotePlaceholderText.Text = "对方已关闭摄像头";
                }

                if (snapshot.IsRemoteMuted)
                {
                    _statusText.Text = "对方已静音";
                }
            }

            _muteButton.Content = snapshot.IsMuted ? "取消静音" : "静音";
            _cameraButton.Content = snapshot.IsCameraOff ? "开启摄像头" : "关闭摄像头";
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

        private void FindControls()
        {
            _remoteVideoImage = this.FindControl<Image>(nameof(RemoteVideoImage));
            _localPreviewImage = this.FindControl<Image>(nameof(LocalPreviewImage));
            _localCameraOffText = this.FindControl<TextBlock>(nameof(LocalCameraOffText));
            _localPreviewContainer = this.FindControl<Border>(nameof(LocalPreviewContainer));
            _remotePlaceholderPanel = this.FindControl<StackPanel>(nameof(RemotePlaceholderPanel));
            _remotePlaceholderText = this.FindControl<TextBlock>(nameof(RemotePlaceholderText));
            _infoPanel = this.FindControl<StackPanel>(nameof(InfoPanel));
            _peerAvatarInitial = this.FindControl<TextBlock>(nameof(PeerAvatarInitial));
            _peerNameText = this.FindControl<TextBlock>(nameof(PeerNameText));
            _callTypeText = this.FindControl<TextBlock>(nameof(CallTypeText));
            _statusText = this.FindControl<TextBlock>(nameof(StatusText));
            _elapsedText = this.FindControl<TextBlock>(nameof(ElapsedText));
            _incomingActions = this.FindControl<Grid>(nameof(IncomingActions));
            _inCallActions = this.FindControl<Grid>(nameof(InCallActions));
            _acceptButton = this.FindControl<Button>(nameof(AcceptButton));
            _rejectButton = this.FindControl<Button>(nameof(RejectButton));
            _cancelButton = this.FindControl<Button>(nameof(CancelButton));
            _muteButton = this.FindControl<Button>(nameof(MuteButton));
            _cameraButton = this.FindControl<Button>(nameof(CameraButton));
            _hangupButton = this.FindControl<Button>(nameof(HangupButton));

            _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _elapsedTimer.Tick += (_, _) =>
            {
                var elapsed = DateTime.UtcNow - _connectedAtUtc;
                _elapsedText.Text = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
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
