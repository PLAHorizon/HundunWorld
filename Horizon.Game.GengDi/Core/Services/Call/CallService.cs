using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using Horizon.IM.Message.Enums;
using Horizon.IM.Message.Network;

namespace Horizon.Game.GengDi.Core.Services.Call
{
    /// <summary>
    /// 通话服务（客户端单例）：编排语音/视频通话的完整生命周期。
    ///
    /// 状态流转：
    ///   发起：Idle → OutgoingRinging →（对方接听）Connecting →（媒体建立）InCall →（挂断/异常）Idle
    ///   接听：Idle → IncomingRinging →（本端接听）Connecting → InCall → Idle
    ///   拒绝/取消/忙线/超时：振铃阶段直接回到 Idle，并向对端发送对应终结信令
    ///
    /// 容错设计：
    ///   1. 信令走 IM 网关长连接（自动重连），媒体走 UDP 直连，两者互不阻塞；
    ///   2. 通话中每 15 秒发送 KeepAlive 信令，IM 连接闪断恢复后自动续传；
    ///   3. 媒体看门狗：长时间收不到对端媒体包判定异常断开并释放资源；
    ///   4. 主叫 30 秒无人接听自动取消，被叫 45 秒未应答自动拒绝。
    /// </summary>
    internal sealed class CallService
    {
        private const int OutgoingRingTimeoutMs = 30_000;
        private const int IncomingRingTimeoutMs = 45_000;
        private const int KeepAliveIntervalMs = 15_000;
        private const int WatchdogWarnMs = 15_000;
        private const int WatchdogLostMs = 30_000;
        private const int MonitorTickMs = 500;

        private static readonly Lazy<CallService> _instance = new(() => new CallService());
        public static CallService Instance => _instance.Value;

        private readonly object _sync = new();
        private readonly CallStateMachine _machine = new();

        private ImGatewayContactClient _gatewayClient;
        private ulong _localUserId;
        private bool _initialized;

        private AudioCallEngine _audio;
        private VideoCallEngine _video;
        private CallMediaTransport _transport;
        private CancellationTokenSource _sessionCts;
        private Task _monitorTask = Task.CompletedTask;

        private string _peerId = string.Empty;
        private string _peerDisplayName = string.Empty;
        private string _peerAvatar = string.Empty;
        private IMCallType _callType;
        private bool _isMuted;
        private bool _isCameraOff;
        private bool _remoteMuted;
        private bool _remoteCameraOff;
        private long _ringStartedMs;
        private long _connectedMs;
        private long _lastKeepAliveMs;
        private bool _mediaWarned;

        private CallSessionSnapshot _snapshot = new() { State = CallState.Idle };

        /// <summary>通话状态变更（UI 据此打开/更新/关闭通话窗口）。</summary>
        public event EventHandler<CallStateChangedEventArgs> StateChanged;

        /// <summary>需要 Toast 提示的消息（设备异常、网络异常等）。</summary>
        public event EventHandler<CallNoticeEventArgs> NoticeRaised;

        /// <summary>本地摄像头预览帧（JPEG）。</summary>
        public event EventHandler<CallVideoFrameEventArgs> LocalPreviewFrame;

        /// <summary>远端视频画面帧（JPEG）。</summary>
        public event EventHandler<CallVideoFrameEventArgs> RemoteVideoFrame;

        /// <summary>当前通话快照。</summary>
        public CallSessionSnapshot CurrentSnapshot => _snapshot;

        /// <summary>
        /// 绑定 IM 网关客户端与当前登录用户（由 SocialService 创建时调用，重复调用同一用户幂等）。
        /// </summary>
        public void Initialize(ImGatewayContactClient gatewayClient, ulong localUserId)
        {
            ArgumentNullException.ThrowIfNull(gatewayClient);
            if (localUserId == 0)
            {
                return;
            }

            lock (_sync)
            {
                if (_initialized && _localUserId == localUserId && _gatewayClient == gatewayClient)
                {
                    return;
                }

                // 账号切换：先结束残留通话再切换到新连接
                if (_initialized && _localUserId != localUserId && _machine.State != CallState.Idle)
                {
                    EndLocalSession(IMCallEndReason.Normal, "账号已切换，通话结束", sendSignal: false);
                }

                if (_gatewayClient != null)
                {
                    _gatewayClient.CallSignalReceived -= OnCallSignalReceived;
                }

                _gatewayClient = gatewayClient;
                _localUserId = localUserId;
                _gatewayClient.CallSignalReceived += OnCallSignalReceived;
                _initialized = true;
            }
        }

        /// <summary>发起通话。返回 false 表示当前无法发起（已有通话/设备不可用/参数无效）。</summary>
        public async Task<bool> StartCallAsync(string peerId, IMCallType callType)
        {
            if (!ImIdentity.TryResolveUserId(peerId, out var peerUserId))
            {
                RaiseNotice("对方账号无效，无法发起通话。", isError: true);
                return false;
            }

            string callId;
            lock (_sync)
            {
                if (!_initialized || _gatewayClient == null)
                {
                    RaiseNotice("IM 连接尚未就绪，请稍后重试。", isError: true);
                    return false;
                }

                if (_machine.State != CallState.Idle)
                {
                    RaiseNotice("当前已有进行中的通话。", isError: true);
                    return false;
                }

                if (peerUserId == _localUserId)
                {
                    RaiseNotice("不能向自己发起通话。", isError: true);
                    return false;
                }

                // 麦克风是语音/视频通话的必备设备，先初始化，失败则不拨出
                _audio = new AudioCallEngine();
                WireAudioEngine(_audio);
                if (!_audio.StartCapture())
                {
                    DisposeMediaResources();
                    // 具体失败原因（权限/无设备/格式）已由 DeviceError 提示展示，此处仅告知通话已中止
                    RaiseNotice("麦克风初始化失败，通话发起已中止。请按提示处理设备问题后重试。", isError: true);
                    return false;
                }

                _audio.StartPlayback();

                callId = Guid.NewGuid().ToString("N");
                _callType = callType;
                _peerId = peerId;
                _peerDisplayName = peerId;
                _peerAvatar = string.Empty;
                _isMuted = false;
                _isCameraOff = false;
                _remoteMuted = false;
                _remoteCameraOff = false;
                _mediaWarned = false;

                if (!_machine.TryStartOutgoing(callId))
                {
                    DisposeMediaResources();
                    return false;
                }

                PrepareTransport(callId);

                if (callType == IMCallType.Video)
                {
                    _video = new VideoCallEngine();
                    WireVideoEngine(_video);
                    _video.Attach(_transport);
                    _video.StartCamera(); // 失败仅提示，不阻断通话
                }

                _ringStartedMs = NowMs();
                StartSessionMonitor();
                PublishSnapshot(CallState.OutgoingRinging, "正在呼叫…");
            }

            // 尝试解析对端昵称（失败不影响拨打）
            ResolvePeerDisplayName(peerId);

            try
            {
                var signal = BuildSignal(IMCallSignalType.Offer, peerUserId);
                var ack = await _gatewayClient.SendCallSignalAsync(signal).ConfigureAwait(false);

                lock (_sync)
                {
                    if (_machine.CallId != callId || _machine.State != CallState.OutgoingRinging)
                    {
                        return false; // 期间已被取消/超时
                    }

                    if (ack == null || !ack.Accepted)
                    {
                        var reason = ack?.EndReason == IMCallEndReason.Busy
                            ? IMCallEndReason.Busy
                            : IMCallEndReason.Lost;
                        EndLocalSession(reason, ack?.EndReason == IMCallEndReason.Busy
                            ? "对方正在通话中，请稍后再拨。"
                            : "呼叫失败，请稍后重试。");
                        return false;
                    }
                }

                return true;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    if (_machine.CallId == callId)
                    {
                        EndLocalSession(IMCallEndReason.Lost, $"呼叫失败：{ex.Message}");
                    }
                }

                return false;
            }
        }

        /// <summary>接听来电。</summary>
        public async Task AcceptAsync()
        {
            IMCallSignalMessage acceptSignal;
            ulong peerUserId;

            lock (_sync)
            {
                if (_machine.State != CallState.IncomingRinging || !_machine.TryAccept())
                {
                    return;
                }

                if (!ImIdentity.TryResolveUserId(_peerId, out peerUserId))
                {
                    EndLocalSession(IMCallEndReason.Lost, "对方账号无效，通话已结束。");
                    return;
                }

                // 被叫在接听时初始化音频设备
                _audio ??= CreateAudioEngine();
                if (!_audio.IsCaptureRunning && !_audio.StartCapture())
                {
                    RaiseNotice("麦克风初始化失败，请检查音频设备。通话将仅能收听。", isError: true);
                }

                if (!_audio.IsPlaybackRunning)
                {
                    _audio.StartPlayback();
                }

                if (_callType == IMCallType.Video)
                {
                    _video ??= new VideoCallEngine();
                    WireVideoEngine(_video);
                    _video.Attach(_transport);
                    _video.StartCamera();
                }

                acceptSignal = BuildSignal(IMCallSignalType.Accept, peerUserId);
                PublishSnapshot(CallState.Connecting, "正在接通…");
            }

            try
            {
                await _gatewayClient.SendCallSignalAsync(acceptSignal).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    if (_machine.State == CallState.Connecting)
                    {
                        EndLocalSession(IMCallEndReason.Lost, $"接听失败：{ex.Message}");
                    }
                }

                return;
            }

            await SendMediaReadyAsync(peerUserId).ConfigureAwait(false);
        }

        /// <summary>拒绝来电 / 取消呼叫 / 挂断（按当前状态自动选择对应信令）。</summary>
        public async Task HangupAsync()
        {
            IMCallSignalMessage signal;
            ulong peerUserId;
            IMCallEndReason localReason;
            string localText;

            lock (_sync)
            {
                if (_machine.State == CallState.Idle || _machine.State == CallState.Ending)
                {
                    return;
                }

                if (!ImIdentity.TryResolveUserId(_peerId, out peerUserId))
                {
                    EndLocalSession(IMCallEndReason.Normal, "通话结束", sendSignal: false);
                    return;
                }

                switch (_machine.State)
                {
                    case CallState.OutgoingRinging:
                        signal = BuildSignal(IMCallSignalType.Cancel, peerUserId);
                        signal.EndReason = IMCallEndReason.Cancelled;
                        localReason = IMCallEndReason.Cancelled;
                        localText = "已取消呼叫";
                        break;

                    case CallState.IncomingRinging:
                        signal = BuildSignal(IMCallSignalType.Reject, peerUserId);
                        signal.EndReason = IMCallEndReason.Rejected;
                        localReason = IMCallEndReason.Rejected;
                        localText = "已拒绝来电";
                        break;

                    default:
                        signal = BuildSignal(IMCallSignalType.Hangup, peerUserId);
                        signal.EndReason = IMCallEndReason.Normal;
                        localReason = IMCallEndReason.Normal;
                        localText = "通话结束";
                        break;
                }
            }

            try
            {
                await _gatewayClient.SendCallSignalAsync(signal).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CallService] 发送终结信令失败（本端仍会结束通话）：{ex.Message}");
            }

            lock (_sync)
            {
                EndLocalSession(localReason, localText, sendSignal: false);
            }
        }

        /// <summary>切换静音/取消静音。</summary>
        public void ToggleMute()
        {
            lock (_sync)
            {
                if (_machine.State == CallState.Idle)
                {
                    return;
                }

                _isMuted = !_isMuted;
                _audio?.SetMuted(_isMuted);
                PublishSnapshot(_machine.State, _isMuted ? "已静音" : "已取消静音");

                if (ImIdentity.TryResolveUserId(_peerId, out var peerUserId))
                {
                    var signal = BuildSignal(IMCallSignalType.MediaState, peerUserId);
                    signal.IsMuted = _isMuted;
                    signal.IsCameraOff = _isCameraOff;
                    _ = SendFireAndForgetSafeAsync(signal);
                }
            }
        }

        /// <summary>开启/关闭摄像头（仅视频通话）。</summary>
        public void ToggleCamera()
        {
            lock (_sync)
            {
                if (_machine.State == CallState.Idle || _callType != IMCallType.Video)
                {
                    return;
                }

                _isCameraOff = !_isCameraOff;
                _video?.SetCameraEnabled(!_isCameraOff);
                PublishSnapshot(_machine.State, _isCameraOff ? "摄像头已关闭" : "摄像头已开启");

                if (ImIdentity.TryResolveUserId(_peerId, out var peerUserId))
                {
                    var signal = BuildSignal(IMCallSignalType.MediaState, peerUserId);
                    signal.IsMuted = _isMuted;
                    signal.IsCameraOff = _isCameraOff;
                    _ = SendFireAndForgetSafeAsync(signal);
                }
            }
        }

        // ==================== 信令接收 ====================

        private void OnCallSignalReceived(object sender, IMCallSignalMessage signal)
        {
            if (signal == null || signal.ReceiverId != _localUserId || string.IsNullOrEmpty(signal.CallId))
            {
                return;
            }

            try
            {
                HandleSignal(signal);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CallService] 处理通话信令异常：{ex.Message}");
            }
        }

        private void HandleSignal(IMCallSignalMessage signal)
        {
            // 新来电：仅空闲状态受理，忙线时向主叫回复 Busy 信令
            if (signal.SignalType == IMCallSignalType.Offer)
            {
                lock (_sync)
                {
                    if (_machine.CallId == signal.CallId)
                    {
                        return; // 重复 Offer，忽略
                    }

                    if (_machine.State != CallState.Idle || !_machine.TryReceiveOffer(signal.CallId))
                    {
                        RaiseNotice($"已拒绝一通来电（正在通话中）：{signal.SenderName}", isError: false);

                        if (ImIdentity.TryResolveUserId(signal.SenderId.ToString(), out var callerId))
                        {
                            var busy = new IMCallSignalMessage
                            {
                                CallId = signal.CallId,
                                SenderId = _localUserId,
                                ReceiverId = callerId,
                                SignalType = IMCallSignalType.Busy,
                                CallType = signal.CallType,
                                EndReason = IMCallEndReason.Busy,
                                Remark = "对方正在通话中"
                            };
                            _ = SendFireAndForgetSafeAsync(busy);
                        }

                        return;
                    }

                    _callType = signal.CallType;
                    _peerId = signal.SenderId.ToString();
                    _peerDisplayName = string.IsNullOrWhiteSpace(signal.SenderName)
                        ? _peerId
                        : signal.SenderName;
                    _peerAvatar = signal.SenderAvatar ?? string.Empty;
                    _isMuted = false;
                    _isCameraOff = false;
                    _remoteMuted = false;
                    _remoteCameraOff = false;
                    _mediaWarned = false;
                    _ringStartedMs = NowMs();

                    PrepareTransport(signal.CallId);
                    StartSessionMonitor();

                    PublishSnapshot(
                        CallState.IncomingRinging,
                        signal.CallType == IMCallType.Video ? "来电：视频通话" : "来电：语音通话");
                }

                return;
            }

            lock (_sync)
            {
                if (_machine.CallId != signal.CallId)
                {
                    return; // 过期/其他会话信令
                }

                if (!_machine.ShouldHandleSignal(signal.SignalType))
                {
                    return;
                }

                switch (signal.SignalType)
                {
                    case IMCallSignalType.Accept:
                        if (_machine.TryRemoteAccept())
                        {
                            PublishSnapshot(CallState.Connecting, "对方已接听，正在接通…");
                            _ = SendMediaReadyLockedAsync(signal.SenderId);
                        }
                        break;

                    case IMCallSignalType.Reject:
                        EndLocalSession(IMCallEndReason.Rejected, "对方拒绝了通话", sendSignal: false);
                        break;

                    case IMCallSignalType.Busy:
                        EndLocalSession(IMCallEndReason.Busy, "对方正在通话中，请稍后再拨", sendSignal: false);
                        break;

                    case IMCallSignalType.Cancel:
                        EndLocalSession(IMCallEndReason.Cancelled, "对方已取消呼叫", sendSignal: false);
                        break;

                    case IMCallSignalType.Timeout:
                        EndLocalSession(IMCallEndReason.Timeout, "呼叫超时", sendSignal: false);
                        break;

                    case IMCallSignalType.Hangup:
                        EndLocalSession(IMCallEndReason.Normal, "对方已挂断", sendSignal: false);
                        break;

                    case IMCallSignalType.MediaReady:
                        HandleMediaReady(signal);
                        break;

                    case IMCallSignalType.MediaState:
                        _remoteMuted = signal.IsMuted;
                        _remoteCameraOff = signal.IsCameraOff;
                        PublishSnapshot(_machine.State, signal.IsMuted ? "对方已静音" : string.Empty);
                        break;

                    case IMCallSignalType.KeepAlive:
                        // 信令层存活信号，媒体看门狗单独跟踪 UDP 包
                        break;
                }
            }
        }

        private void HandleMediaReady(IMCallSignalMessage signal)
        {
            if (string.IsNullOrWhiteSpace(signal.MediaEndpoint)
                || !TryParseEndpoint(signal.MediaEndpoint, out var remoteEndpoint))
            {
                RaiseNotice("对方媒体通道信息无效，画面/声音可能不可用。", isError: true);
                return;
            }

            _transport?.SetRemoteEndpoint(remoteEndpoint);

            if (_machine.State == CallState.Connecting && _machine.TryEnterInCall())
            {
                _connectedMs = NowMs();
                _mediaWarned = false;
                PublishSnapshot(CallState.InCall, "通话中");
            }
        }

        // ==================== 会话监视（超时/保活/看门狗） ====================

        private void StartSessionMonitor()
        {
            _sessionCts?.Cancel();
            _sessionCts?.Dispose();
            _sessionCts = new CancellationTokenSource();
            var token = _sessionCts.Token;
            _lastKeepAliveMs = NowMs();

            _monitorTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(MonitorTickMs, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    try
                    {
                        MonitorTick();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CallService] 会话监视异常：{ex.Message}");
                    }
                }
            }, token);
        }

        private void MonitorTick()
        {
            var now = NowMs();
            IMCallSignalMessage timeoutSignal = null;
            IMCallEndReason timeoutReason = IMCallEndReason.Timeout;
            string timeoutText = string.Empty;

            ulong peerUserId = 0;

            lock (_sync)
            {
                if (_machine.State == CallState.Idle || _machine.State == CallState.Ending)
                {
                    return;
                }

                ImIdentity.TryResolveUserId(_peerId, out peerUserId);

                // 主叫振铃超时 → 自动取消
                if (_machine.State == CallState.OutgoingRinging
                    && now - _ringStartedMs > OutgoingRingTimeoutMs)
                {
                    if (peerUserId != 0)
                    {
                        timeoutSignal = BuildSignal(IMCallSignalType.Timeout, peerUserId);
                        timeoutSignal.EndReason = IMCallEndReason.Timeout;
                    }

                    timeoutReason = IMCallEndReason.Timeout;
                    timeoutText = "对方无应答，呼叫已取消";
                }

                // 被叫振铃超时 → 自动拒绝
                if (_machine.State == CallState.IncomingRinging
                    && now - _ringStartedMs > IncomingRingTimeoutMs)
                {
                    if (peerUserId != 0)
                    {
                        timeoutSignal = BuildSignal(IMCallSignalType.Timeout, peerUserId);
                        timeoutSignal.EndReason = IMCallEndReason.Timeout;
                    }

                    timeoutReason = IMCallEndReason.Timeout;
                    timeoutText = "来电超时未接听";
                }

                // 保活信令
                if (_machine.State == CallState.Connecting || _machine.State == CallState.InCall)
                {
                    if (now - _lastKeepAliveMs >= KeepAliveIntervalMs && peerUserId != 0)
                    {
                        _lastKeepAliveMs = now;
                        var keepAlive = BuildSignal(IMCallSignalType.KeepAlive, peerUserId);
                        _ = SendFireAndForgetSafeAsync(keepAlive);
                    }
                }

                // 媒体看门狗：仅通话中检测
                if (_machine.State == CallState.InCall && _transport != null)
                {
                    var lastMedia = Interlocked.Read(ref _transport.LastPacketReceivedMs);
                    if (lastMedia > 0)
                    {
                        var silentMs = now - lastMedia;
                        if (silentMs > WatchdogLostMs)
                        {
                            timeoutReason = IMCallEndReason.Lost;
                            timeoutText = "与对方连接中断，通话已结束";
                            if (peerUserId != 0)
                            {
                                timeoutSignal = BuildSignal(IMCallSignalType.Hangup, peerUserId);
                                timeoutSignal.EndReason = IMCallEndReason.Lost;
                            }
                        }
                        else if (silentMs > WatchdogWarnMs && !_mediaWarned)
                        {
                            _mediaWarned = true;
                            RaiseNotice("对方连接不稳定，声音/画面可能中断…", isError: false);
                            return;
                        }
                        else if (silentMs <= WatchdogWarnMs)
                        {
                            _mediaWarned = false;
                        }
                    }
                }
            }

            if (timeoutSignal != null)
            {
                _ = SendFireAndForgetSafeAsync(timeoutSignal);
            }

            if (!string.IsNullOrEmpty(timeoutText))
            {
                lock (_sync)
                {
                    EndLocalSession(timeoutReason, timeoutText, sendSignal: false);
                }
            }
        }

        // ==================== 媒体资源管理 ====================

        private void PrepareTransport(string callId)
        {
            _transport?.Dispose();

            try
            {
                _transport = new CallMediaTransport(callId);
                _transport.AudioChunkReceived += pcm => _audio?.PushRemoteAudio(pcm);
                _transport.TransportError += message =>
                    System.Diagnostics.Debug.WriteLine($"[CallService] 媒体传输异常：{message}");
                _video?.Attach(_transport);
            }
            catch (Exception ex)
            {
                _transport = null;
                RaiseNotice($"媒体通道初始化失败：{ex.Message}", isError: true);
            }
        }

        private AudioCallEngine CreateAudioEngine()
        {
            var engine = new AudioCallEngine();
            WireAudioEngine(engine);
            return engine;
        }

        private void WireAudioEngine(AudioCallEngine engine)
        {
            engine.CapturedAudio += pcm => _transport?.SendAudio(pcm);
            engine.DeviceError += message => RaiseNotice(message, isError: true);
        }

        private void WireVideoEngine(VideoCallEngine engine)
        {
            engine.LocalPreviewFrame += jpeg => LocalPreviewFrame?.Invoke(this, new CallVideoFrameEventArgs(jpeg));
            engine.RemoteVideoFrame += jpeg => RemoteVideoFrame?.Invoke(this, new CallVideoFrameEventArgs(jpeg));
            engine.DeviceError += message => RaiseNotice(message, isError: true);
        }

        private async Task SendMediaReadyAsync(ulong peerUserId)
        {
            IMCallSignalMessage signal;
            lock (_sync)
            {
                if (_machine.State != CallState.Connecting)
                {
                    return;
                }

                signal = BuildSignal(IMCallSignalType.MediaReady, peerUserId);
            }

            try
            {
                await _gatewayClient.SendCallSignalAsync(signal).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CallService] 发送 MediaReady 失败，将重试一次：{ex.Message}");
                try
                {
                    await Task.Delay(800).ConfigureAwait(false);
                    await _gatewayClient.SendCallSignalAsync(signal).ConfigureAwait(false);
                }
                catch (Exception retryEx)
                {
                    RaiseNotice($"媒体通道协商失败：{retryEx.Message}", isError: true);
                }
            }
        }

        private Task SendMediaReadyLockedAsync(ulong peerUserId)
        {
            return SendMediaReadyAsync(peerUserId);
        }

        private IMCallSignalMessage BuildSignal(IMCallSignalType signalType, ulong receiverId)
        {
            var currentUser = App.CurrentUser;
            return new IMCallSignalMessage
            {
                CallId = _machine.CallId,
                SenderId = _localUserId,
                ReceiverId = receiverId,
                SignalType = signalType,
                CallType = _callType,
                SenderName = currentUser?.Username ?? _localUserId.ToString(),
                SenderAvatar = currentUser?.Avatar ?? string.Empty,
                MediaEndpoint = signalType == IMCallSignalType.MediaReady
                    ? BuildLocalEndpointString()
                    : string.Empty,
                IsMuted = _isMuted,
                IsCameraOff = _isCameraOff,
                Timestamp = NowMs()
            };
        }

        private string BuildLocalEndpointString()
        {
            if (_transport == null)
            {
                return string.Empty;
            }

            var ip = CallMediaTransport.ResolveLocalMediaAddress();
            return $"{ip}:{_transport.LocalEndpoint.Port}";
        }

        private static bool TryParseEndpoint(string endpoint, out IPEndPoint result)
        {
            result = null;
            var separatorIndex = endpoint.LastIndexOf(':');
            if (separatorIndex <= 0 || separatorIndex >= endpoint.Length - 1)
            {
                return false;
            }

            var host = endpoint[..separatorIndex];
            if (!int.TryParse(endpoint[(separatorIndex + 1)..], out var port) || port <= 0 || port > 65535)
            {
                return false;
            }

            if (IPAddress.TryParse(host, out var ip))
            {
                result = new IPEndPoint(ip, port);
                return true;
            }

            try
            {
                var addresses = System.Net.Dns.GetHostAddresses(host);
                foreach (var address in addresses)
                {
                    if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        result = new IPEndPoint(address, port);
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private async Task SendFireAndForgetSafeAsync(IMCallSignalMessage signal)
        {
            try
            {
                var client = _gatewayClient;
                if (client != null)
                {
                    await client.SendCallSignalFireAndForgetAsync(signal).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CallService] 单向信令发送失败（{signal.SignalType}）：{ex.Message}");
            }
        }

        // ==================== 会话结束与状态发布 ====================

        /// <summary>结束本端会话并释放全部媒体资源。需在 _sync 锁内调用（sendSignal=false 时）。</summary>
        private void EndLocalSession(IMCallEndReason reason, string text, bool sendSignal = true)
        {
            if (_machine.State == CallState.Idle)
            {
                return;
            }

            _machine.TryBeginEnding();

            _sessionCts?.Cancel();
            DisposeMediaResources();

            PublishSnapshot(CallState.Ending, text, reason);
            System.Diagnostics.Debug.WriteLine(
                $"[CallService] 通话结束：CallId={_snapshot.CallId}, Reason={reason}, Text={text}");

            // 短暂展示结束提示后复位到空闲（UI 据此关闭窗口）
            var endedCallId = _snapshot.CallId;
            _ = Task.Delay(1500).ContinueWith(_ =>
            {
                lock (_sync)
                {
                    if (_machine.State == CallState.Ending && _snapshot.CallId == endedCallId)
                    {
                        _machine.Reset();
                        PublishSnapshot(CallState.Idle, string.Empty, reason);
                    }
                }
            }, TaskScheduler.Default);
        }

        private void DisposeMediaResources()
        {
            var audio = _audio;
            _audio = null;
            audio?.Dispose();

            var video = _video;
            _video = null;
            video?.Dispose();

            var transport = _transport;
            _transport = null;
            transport?.Dispose();

            _connectedMs = 0;
        }

        private void PublishSnapshot(CallState state, string statusText, IMCallEndReason endReason = IMCallEndReason.Normal)
        {
            var elapsed = _connectedMs > 0 && (state == CallState.InCall || state == CallState.Ending)
                ? TimeSpan.FromMilliseconds(NowMs() - _connectedMs)
                : TimeSpan.Zero;

            _snapshot = new CallSessionSnapshot
            {
                State = state,
                CallId = _machine.CallId,
                PeerId = _peerId,
                PeerDisplayName = _peerDisplayName,
                PeerAvatar = _peerAvatar,
                CallType = _callType,
                IsOutgoing = _machine.IsOutgoing,
                Elapsed = elapsed,
                IsMuted = _isMuted,
                IsCameraOff = _isCameraOff,
                IsRemoteMuted = _remoteMuted,
                IsRemoteCameraOff = _remoteCameraOff,
                StatusText = statusText,
                EndReason = endReason
            };

            StateChanged?.Invoke(this, new CallStateChangedEventArgs(_snapshot));
        }

        private void ResolvePeerDisplayName(string peerId)
        {
            // 尽力而为：从本地社交缓存解析昵称失败时使用通行证号展示，不影响通话流程
            try
            {
                var socialViewModel = Horizon.Game.GengDi.Core.ViewModels.SocialViewModel.LastCreatedInstance;
                var user = socialViewModel?.ResolveKnownUser(peerId);
                if (user != null && !string.IsNullOrWhiteSpace(user.Username))
                {
                    lock (_sync)
                    {
                        if (_peerId == peerId)
                        {
                            _peerDisplayName = string.IsNullOrWhiteSpace(user.Nickname)
                                ? user.Username
                                : user.Nickname;
                            _peerAvatar = user.Avatar ?? string.Empty;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private void RaiseNotice(string message, bool isError)
        {
            System.Diagnostics.Debug.WriteLine($"[CallService] 提示：{message}");
            NoticeRaised?.Invoke(this, new CallNoticeEventArgs(message, isError));
        }

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
