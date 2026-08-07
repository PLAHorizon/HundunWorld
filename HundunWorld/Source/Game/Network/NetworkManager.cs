using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FlaxEngine;
using Game.Game.Network;
using Horizon.Game.ECS.Arch.SyncGuard.Contracts;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;
using HundunWorld.Game.Services;
using TouchSocket.Core;
using TouchSocket.Sockets;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 网络管理器
    /// </summary>
    public class NetworkManager : IDisposable
    {
        #region Fields and Properties

        private TcpClient _client;
        private readonly NetworkStateMonitor _networkStateMonitor;
        private GatewaySelector _gatewaySelector;
        private readonly HeartbeatManager _heartbeatManager;
        private readonly ReconnectionManager _reconnectionManager;
        private CancellationTokenSource _connectionCts;
        private CancellationTokenSource _gatewayCheckCts;
        private readonly object _connectionLock = new object();
        private readonly object _sendLock = new object();
        private readonly MessageProcessor _messageProcessor;
        private readonly HorizonMessageAdapter _messageAdapter;
        private ConnectionStatus _connectionStatus = ConnectionStatus.Disconnected;
        private GatewayInfo _currentGateway;
        private volatile bool _isInitialized = false;
        private volatile bool _isDisposing = false;
        private volatile bool _syncHandshakeComplete = false;
        private long _syncClientTick = 0;

        // [Phase C5] 断线重连增量恢复
        /// <summary>是否启用 ReconnectResumePacket 上行（默认关闭，待服务端配合验证后开启）。</summary>
        public bool EnableReconnectResume { get; set; } = true;

        /// <summary>
        /// 重连恢复协商发送钩子（由装配方注入 ReconnectResumeSender.SendResumeAsync 的受控包装）：
        /// 替代旧 fire-and-forget 的 SendReconnectResumeAsync（spec 5.6.1 规则 1、design 2.1.3(3)）。
        /// 返回发送结果（true=真实送达）。
        /// </summary>
        public Func<ulong, long, System.Threading.Tasks.Task<bool>>? ReconnectResumeSendHook { get; set; }

        /// <summary>客户端最近已应用的服务器快照 Tick（从 SnapshotPacket.ServerTick 更新）。</summary>
        private long _lastAppliedServerTick;

        /// <summary>更新最近已应用的服务器 Tick（由 HundunWorldGame.OnSnapshotReceived 调用）。</summary>
        public void UpdateLastAppliedServerTick(long serverTick)
        {
            System.Threading.Interlocked.Exchange(ref _lastAppliedServerTick, serverTick);
        }

        /// <summary>
        /// 客户端最后已应用的服务器快照 tick（供重连协商读取，Interlocked 保证跨线程可见性）。
        /// </summary>
        public long LastAppliedServerTick => System.Threading.Interlocked.Read(ref _lastAppliedServerTick);

        // 同步握手重试状态：记录上次发送的参数与时间，用于丢包或响应未达时重发。
        private long _lastHandshakeSentTicks = 0;
        /// <summary>握手首次尝试时间戳，用于全局超时判定（仅在真实出网失败时才应持续增长）。
        /// 与 _lastHandshakeSentTicks 分离：后者仍每次尝试都更新以驱动 3s 重试间隔。</summary>
        private long _handshakeFirstAttemptTicks = 0;
        private ulong _lastHandshakeCharacterId;
        private float _lastHandshakeX;
        private float _lastHandshakeY;
        private float _lastHandshakeZ;
        private static readonly TimeSpan HandshakeRetryInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(15);

        private UnhandledExceptionEventHandler _unhandledExceptionHandler;
        List<GatewayInfo> _gatewayList = new List<GatewayInfo>();
        private readonly List<IMessageHandler> _registeredHandlers = new List<IMessageHandler>();
        public ConnectionStatus ConnectionStatus => _connectionStatus;

        /// <summary>
        /// 同步握手是否已完成（握手完成后才能发送 InputPacket）
        /// </summary>
        public bool IsSyncHandshakeComplete => _syncHandshakeComplete;

        /// <summary>
        /// 同步握手状态变化事件（完成/重置时触发，驱动本地发送资格状态机）。
        /// </summary>
        public event Action<bool> SyncHandshakeStateChanged;

        /// <summary>
        /// 连接状态变化事件（仅 Connected/Disconnected 两态，供资格状态机订阅）。
        /// </summary>
        public event Action<bool> ConnectedStateChanged;

        /// <summary>
        /// 兜底守卫回调（注入 <see cref="IOutboundSyncGuard"/>）：拦截绕过受控发送入口的旁路发送。
        /// 仅针对实体同步类上行帧（<see cref="SyncFrameMessage"/>），连接层消息不受拦截。
        /// </summary>
        public IOutboundSyncGuard OutboundSyncGuard { get; set; }
        
        /// <summary>
        /// 游戏ID
        /// </summary>
        public uint GameId { get; set; } = 1;
        
        /// <summary>
        /// 分区ID
        /// </summary>
        public uint ZoneId { get; set; } = 1;
        
        /// <summary>
        /// 服务器ID
        /// </summary>
        public uint ServerId { get; set; } = 1;
        
        /// <summary>
        /// 用户ID
        /// </summary>
        public ulong UserId { get; set; }

        /// <summary>
        /// 当前角色ID（玩家选角进入游戏后设置）。
        /// 用于在消息头 MessageHeader.CharacterId 中携带，服务端依赖此字段路由
        /// SubscriptionUpdatePacket 等不携带 CharacterId 的同步包。
        /// </summary>
        public ulong CharacterId { get; set; }
        
        /// <summary>
        /// 用户鉴权令牌
        /// </summary>
        public string AuthToken { get; set; } = "";

        public event Action<ConnectionStatus> ConnectionStatusChanged;
        public event Action<string> ConnectionError;
        
        /// <summary>
        /// [修复] 消息处理器注册完成事件：AddAllMessageHandlers() 执行完毕后触发，
        /// 供 HundunWorldGame 立即订阅 SyncPacketMessageHandler 事件，避免轮询重试的时序竞争。
        /// </summary>
        public event Action HandlersRegistered;
        
        /// <summary>
        /// 断线超时事件：网关离线超过阈值时间后触发。
        /// 上层（HundunWorldGame）收到此事件后应退回角色选择界面，
        /// 不再发起任何网络连接请求，等待用户主动选择进入游戏时再按需拉起连接。
        /// </summary>
        public event Action DisconnectTimedOut;

        /// <summary>
        /// 鉴权令牌过期事件，触发时通知上层需要重新登录
        /// </summary>
        public event Action AuthTokenExpired;
        
        /// <summary>
        /// 令牌自动刷新中标志，防止并发刷新
        /// </summary>
        private volatile bool _isTokenRefreshing = false;

        #endregion

        #region Constructor and Initialization

        public NetworkManager(List<GatewayInfo> gatewayList, NetworkStateMonitor networkStateMonitor = null)
        {
            _unhandledExceptionHandler = (sender, e) =>
            {
                try
                {
                    var ex = e.ExceptionObject as Exception;
                    EnhancedLogging.LogError($"[UnhandledException] {ex?.Message}");
                    EnhancedDiagnostics.LogException(ex, "UnhandledException");
                }
                catch
                {
                    // swallow
                }
            };
            AppDomain.CurrentDomain.UnhandledException += _unhandledExceptionHandler;
            _gatewayList = gatewayList;
            _networkStateMonitor = networkStateMonitor ?? new NetworkStateMonitor();
            _gatewayCheckCts = new CancellationTokenSource();
            _heartbeatManager = new HeartbeatManager(this); // 初始化心跳包管理器
            _connectionCts = new CancellationTokenSource();
            _messageProcessor = new MessageProcessor();
            _messageAdapter = new HorizonMessageAdapter();

            // 初始化重连管理器，提供连接函数和断开函数。
            // 修复（客户端无限发送连接请求）：
            // 原实现 _connectFunction = ProbeGatewayAsync + ConnectAsync，探查成功后建立完整 TCP 连接，
            // 但客户端未在游戏内（无角色登录），不会发送任何游戏数据，
            // 服务端首包超时（5s）关闭连接 → OnClientDisconnected → HandleDisconnect → 重新探查 → 无限循环。
            // 修复：_connectFunction 仅做 RST 探查（不建立持久连接），探查成功即表示"网关可达"，
            // ReconnectionManager 收到 true 后停止循环。真正的持久连接仅在用户主动进入游戏时
            // 通过 ConnectOnDemandAsync 建立。
            _reconnectionManager = new ReconnectionManager(
                connectFunction: async () =>
                {
                    if (_currentGateway == null)
                        return false;

                    // 已连接且在线 → 无需探查
                    if (_connectionStatus == ConnectionStatus.Connected && _client != null && _client.Online)
                    {
                        EnhancedLogging.LogInfo("[ReconnectConnectFunction] 连接已在线，跳过探查");
                        return true;
                    }

                    // 仅做 RST 探查，不建立持久连接。
                    // 探查成功 = 网关可达，ReconnectionManager 停止循环，等待用户主动进入游戏。
                    // 探查失败 = 网关不可达，ReconnectionManager 继续退避重试。
                    var reachable = await ProbeGatewayAsync(_currentGateway.IP, _currentGateway.Port);
                    if (reachable)
                    {
                        EnhancedLogging.LogInfo("[ReconnectConnectFunction] 网关探查成功，停止自动重连循环，等待用户主动进入游戏");
                    }
                    return reachable;
                },
                disconnectFunction: DisconnectAsync,
                networkCheckFunction: async () => await _networkStateMonitor.IsNetworkAvailableAsync());

            // 订阅重连管理器事件
            _reconnectionManager.OnReconnected += OnReconnectionSucceeded;
            _reconnectionManager.OnReconnectFailed += OnReconnectionFailed;
            _reconnectionManager.OnStateChanged += OnReconnectionStateChanged;
            _reconnectionManager.OnDisconnectTimeout += OnDisconnectTimeout;
            // 修复（真正断网后无法恢复 — ConnectAsync 跳过连接）：
            // 订阅 OnDisconnected 事件。心跳超时路径下 OnClientDisconnected 不会被调用
            // （TCP 层未检测到断开），NetworkManager._connectionStatus 仍为 Connected。
            // 后续 StartReconnectAsync → ConnectAsync 检查 _connectionStatus == Connected →
            // 跳过连接（返回 true），实际未建立新 TCP 连接 → "重连成功"但无数据流动 → 幽灵连接。
            // 订阅 OnDisconnected 后，ReconnectionManager.HandleDisconnect 触发时同步更新
            // NetworkManager._connectionStatus 为 Disconnected，ConnectAsync 不再跳过连接。
            _reconnectionManager.OnDisconnected += OnReconnectionManagerDisconnected;
            _reconnectionManager.StartHeartbeat();
            // 订阅网络状态变化事件
            _networkStateMonitor.NetworkStatusChanged += OnNetworkStatusChanged;

            // 启动网络状态监控
            if (_networkStateMonitor is NetworkStateMonitor monitor)
            {
                monitor.StartMonitoring();
            }

            RunBackground(async () =>
            {
                try
                {
                    await InitializeClient(_gatewayList); // 空列表，将在连接时设置
                    AddAllMessageHandlers();
                    _connectionStatus = ConnectionStatus.Disconnected;

                    // 初始化完成后立即触发一次状态通知，确保UI能正确初始化按钮状态
                    UpdateConnectionStatus(ConnectionStatus.Disconnected);

                    EnhancedLogging.LogInfo("网络管理器初始化完成");
                    EnhancedDiagnostics.LogDiagnostic("网络管理器初始化完成");
                }
                catch (Exception ex)
                {
                    EnhancedLogging.LogError($"网络管理器初始化失败: {ex.Message}");
                    EnhancedDiagnostics.LogException(ex, "网络管理器初始化");
                }
            });

            _isInitialized = true;
        }

        /// <summary>
        /// 初始化TCP客户端
        /// </summary>
        private async Task InitializeClient(List<GatewayInfo> gatewayList)
        {
            // 释放旧客户端，防止幽灵连接/资源泄漏
            if (_client != null)
            {
                try
                {
                    _client.Connected -= OnClientConnected;
                    _client.Closed -= OnClientDisconnected;
                    _client.Received -= OnDataReceived;
                    _client.Dispose();
                    EnhancedLogging.LogInfo("[InitializeClient] 已释放旧TcpClient实例");
                }
                catch (Exception ex)
                {
                    EnhancedLogging.LogWarning($"[InitializeClient] 释放旧客户端时发生错误: {ex.Message}");
                }
            }

            _client = new TcpClient();
            EnhancedLogging.LogInfo("[InitializeClient] 创建新的TcpClient实例");

            // 如果提供了网关列表，初始化网关选择器
            if (gatewayList != null && gatewayList.Count > 0)
            {
                _gatewaySelector = new GatewaySelector(gatewayList);
                EnhancedLogging.LogInfo($"[InitializeClient] 初始化网关选择器，网关数量: {gatewayList.Count}");
            }

            // 注册事件处理程序
            EnhancedLogging.LogInfo("[InitializeClient] 注册事件处理程序");
            _client.Connected -= OnClientConnected; // 先移除可能存在的旧事件
            _client.Closed -= OnClientDisconnected;
            _client.Received -= OnDataReceived;

            _client.Connected += OnClientConnected;
            _client.Closed += OnClientDisconnected;
            _client.Received += OnDataReceived;

            EnhancedLogging.LogInfo("[InitializeClient] 事件处理程序注册完成");
            EnhancedLogging.LogInfo($"[InitializeClient] Received事件注册数量: {(_client.Received != null ? 1 : 0)}");
        }

        #endregion

        #region Connection Management

        /// <summary>
        /// 连接到网关
        /// </summary>
        public async Task<bool> ConnectAsync(string ip, int port, List<GatewayInfo> gatewayList = null)
        {
            if (string.IsNullOrEmpty(ip) || port <= 0)
            {
                ConnectionError?.Invoke("IP地址或端口无效");
                EnhancedLogging.LogWarning($"[ConnectAsync] IP地址或端口无效: {ip}:{port}");
                return false;
            }

            // [连接精简治理 spec 5.1.1.3] 原子夺锁 + 前置状态置位：
            // 在锁内完成"检查 + 置位 Connecting"，闭合"检查-置位"竞态窗口。
            // 并发触发多次 ConnectAsync 时，仅第一条路径将 Connecting 置位成功，
            // 其余路径在锁内看到 Connecting/Connected 直接返回，确保同一时刻仅一条 TCP 建连在途。
            lock (_connectionLock)
            {
                if (_connectionStatus == ConnectionStatus.Connecting)
                {
                    EnhancedLogging.LogInfo($"[ConnectAsync] 已有建连流程在途（Connecting），跳过本次建连");
                    return false;
                }
                if (_connectionStatus == ConnectionStatus.Connected)
                {
                    EnhancedLogging.LogInfo($"[ConnectAsync] 当前连接状态: {_connectionStatus}，复用已有连接");
                    return true;
                }
                // 前置置位：在发起任何 TCP 动作前先标记 Connecting，后续路径在锁外无法抢占。
                _connectionStatus = ConnectionStatus.Connecting;
            }

            try
            {
                EnhancedLogging.LogInfo($"[ConnectAsync] 开始连接到 {ip}:{port}");

                // 检查客户端是否已释放或未初始化，如果是则重新初始化
                if (_client == null || _client.DisposedValue)
                {
                    EnhancedLogging.LogInfo("[ConnectAsync] 检测到客户端已释放，正在重新初始化");
                    await InitializeClient(gatewayList ?? _gatewayList);
                }

                // 如果仍在释放过程中，则无法连接
                if (_isDisposing)
                {
                    EnhancedLogging.LogWarning("网络管理器正在释放，无法连接");
                    return false;
                }

                // 检查并重新创建CancellationTokenSource（如果已被释放）
                _connectionCts = EnsureCancellationTokenSource(_connectionCts, "连接取消令牌");
                _gatewayCheckCts = EnsureCancellationTokenSource(_gatewayCheckCts, "网关检查取消令牌");

                // 配置客户端 - 每次连接时创建新的适配器实例
                // [修复] 移除 UseReconnection<ITcpClient>() 插件：该插件与自定义 ReconnectionManager 形成双重重连，
                // 断线时两者同时创建新 TCP 连接，导致服务端出现幽灵连接（成对的未认证连接）。
                // 重连逻辑统一由 ReconnectionManager 管理。
                var config = new TouchSocketConfig()
                    .SetRemoteIPHost($"{ip}:{port}")
                    .SetTcpDataHandlingAdapter(() => new HorizonMessageAdapter());

                // 更新当前网关信息
                _currentGateway = new GatewayInfo { IP = ip, Port = port };

                // 更新状态为连接中
                UpdateConnectionStatus(ConnectionStatus.Connecting);

                EnhancedLogging.LogInfo("[ConnectAsync] 开始设置客户端");
                await _client.SetupAsync(config);
                EnhancedLogging.LogInfo("[ConnectAsync] 客户端设置完成，开始连接");
                await _client.ConnectAsync(_connectionCts.Token);
                EnhancedLogging.LogInfo("[ConnectAsync] 连接完成");
                // UpdateConnectionStatus 内部已直接更新 _connectionStatus 字段，
                // 并将事件通知调度到主线程，无需外层再包 InvokeOnUpdate，避免状态更新延迟导致 SendMessageAsync 拒绝发送
                if (_client.Online)
                    UpdateConnectionStatus(ConnectionStatus.Connected);
                // [修复] 不在此处设置 _reconnectionManager.CurrentState。
                // OnClientConnected 已通过 MarkConnected() 处理状态变更。
                // 直接设置 CurrentState = Connected 会绕过 ChangeState/OnStateChanged，
                // 导致 HandleDisconnect 无法判断当前是否在重试循环中（CurrentState == Reconnecting），
                // 从而在重试链中临时成功/断开时启动级联的新 StartReconnectAsync 循环。
                return true;
            }
            catch (OperationCanceledException)
            {
                ConnectionError?.Invoke("连接被取消");
                UpdateConnectionStatus(ConnectionStatus.Disconnected);
                EnhancedDiagnostics.LogNetworkOperation("连接", $"{ip}:{port}", false, "连接被取消");
                CleanupClient();
                return false;
            }
            catch (Exception ex)
            {
                // Log and handle socket aborts consistently
                EnhancedLogging.LogError($"[ConnectAsync] 连接到 {ip}:{port} 失败: {ex.Message}");
                EnhancedDiagnostics.LogException(ex, "连接");
                ConnectionError?.Invoke($"连接失败: {ex.Message}");
                UpdateConnectionStatus(ConnectionStatus.Disconnected);
                EnhancedDiagnostics.LogNetworkOperation("连接", $"{ip}:{port}", false, ex.Message);
                CleanupClient();
                return false;
            }
        }

        /// <summary>
        /// 探查网关是否可达。当前已禁用 TCP RST 探查，始终返回 true（视为可达）。
        /// <para>
        /// 原实现使用原始 TCP Socket + LingerOption(true, 0) 发送 RST 探查网关，
        /// 避免服务端创建幽灵 GameConnection。现已禁用探查，直接返回 true，
        /// 跳过实际 TCP 调用。连接流程不变：调用方收到 true 后继续后续逻辑
        /// （重连循环停止等待用户主动进入游戏 / ConnectOnDemandAsync 建立正式连接）。
        /// </para>
        /// <para>
        /// 如需恢复探查，恢复方法体内的 TCP RST 探查逻辑即可。
        /// </para>
        /// </summary>
        private async Task<bool> ProbeGatewayAsync(string ip, int port)
        {
            await Task.CompletedTask; // 保持 async 签名，避免编译警告 CS1998

            EnhancedLogging.LogInfo($"[ProbeGatewayAsync] 网关探查已禁用，假定 {ip}:{port} 可达");
            if (_currentGateway != null)
            {
                _currentGateway.IsAvailable = true;
            }
            return true;
        }

        /// <summary>
        /// 确保CancellationTokenSource有效，如果已释放则重新创建
        /// </summary>
        private CancellationTokenSource EnsureCancellationTokenSource(CancellationTokenSource cts, string tokenName)
        {
            bool needNew = false;

            if (cts == null)
            {
                needNew = true;
            }
            else
            {
                try
                {
                    needNew = cts.IsCancellationRequested;
                }
                catch (ObjectDisposedException)
                {
                    needNew = true;
                }
            }

            if (needNew)
            {
                EnhancedLogging.LogInfo($"[EnsureCancellationTokenSource] 重新创建{tokenName}");
                // 如果旧的CancellationTokenSource存在但未释放，则先释放它
                if (cts != null)
                {
                    try
                    {
                        cts.Dispose();
                    }
                    catch (ObjectDisposedException ex)
                    {
                        // 对象已经被释放，忽略此异常
                        EnhancedLogging.LogWarning($"[EnsureCancellationTokenSource] {tokenName}已被释放: {ex.Message}");
                    }
                }
                return new CancellationTokenSource();
            }

            return cts;
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        /// <summary>
        /// 断开连接并清理客户端实例。调用后 _client 被 Dispose + null，
        /// 下次 ConnectAsync 会通过 InitializeClient 创建全新的 TouchSocket TcpClient，
        /// 避免复用断开状态下的客户端导致内部 Socket 状态不一致。
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (_client != null && _client.Online)
            {
                EnhancedLogging.LogInfo("[DisconnectAsync] 开始断开连接");
                await _client.CloseAsync();
                EnhancedLogging.LogInfo("[DisconnectAsync] 连接已断开");
            }
            // 断开后清理客户端，确保下次连接使用全新的 TouchSocket TcpClient 实例
            CleanupClient();

            // 修复 BUG（客户端之间无法看到彼此）：断线后重置同步握手状态，
            // 使重连后重新发送 HandshakePacket 触发 EnterWorldAsync，在 ZoneShard 中重新注册实体。
            ResetSyncHandshake();
        }

        /// <summary>
        /// 清理客户端实例。在连接失败后调用，确保下次重连创建全新客户端，防止复用损坏实例导致幽灵连接。
        /// </summary>
        private void CleanupClient()
        {
            if (_client != null)
            {
                try
                {
                    _client.Dispose();
                }
                catch (Exception ex)
                {
                    EnhancedLogging.LogWarning($"[CleanupClient] 释放客户端时发生错误: {ex.Message}");
                }
                _client = null;
            }
        }

        /// <summary>
        /// 检查并重连
        /// </summary>
        public async Task<bool> CheckAndReconnectAsync()
        {
            if (_currentGateway == null)
            {
                EnhancedLogging.LogWarning("[CheckAndReconnectAsync] 无当前网关信息，无法重连");
                return false;
            }

            EnhancedLogging.LogInfo("[CheckAndReconnectAsync] 开始检查并重连");
            EnhancedDiagnostics.LogDiagnostic("开始检查并重连");

            await DisconnectAsync();
            return await ConnectAsync(_currentGateway.IP, _currentGateway.Port);
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// 客户端连接事件处理
        /// </summary>
        private async Task OnClientConnected(ITcpClient client, TouchSocketEventArgs e)
        {
            if (client != _client)
            {
                EnhancedLogging.LogInfo("[OnClientConnected] 忽略过时连接事件（来自旧客户端实例）");
                return;
            }

            EnhancedLogging.LogInfo("[OnClientConnected] 客户端连接成功");
            EnhancedDiagnostics.LogNetworkOperation("连接", $"{_currentGateway?.IP}:{_currentGateway?.Port}", true, "连接成功");
            // 立即更新状态为 Connected，确保 StartHeartbeat 中 CanSendMessage 返回 true
            UpdateConnectionStatus(ConnectionStatus.Connected);

            _reconnectionManager?.MarkConnected();

            if (_heartbeatManager != null)
            {
                _heartbeatManager.StartHeartbeat();
            }

            // 修复（首包超时 — 重连后未发送数据）：
            // 原实现仅在 OnReconnectionSucceeded 中 fire-and-forget 发送 ReconnectResumePacket，
            // 但 OnReconnectionSucceeded 在 ConnectAsync 返回后才触发，存在时序窗口。
            // 服务端 FirstPacketTimeout=15s 内未收到任何数据 → 首包超时 → 连接被清理。
            // 修复：在 OnClientConnected 中（TCP 连接刚建立时）立即发送 ReconnectResumePacket。
            // 重连场景下 CharacterId > 0，首次连接时 CharacterId == 0 跳过（由 EnterGame 流程发送 HandshakePacket）。
            if (CharacterId > 0)
            {
                try
                {
                    EnhancedLogging.LogInfo("[OnClientConnected] 重连场景，立即发送 ReconnectResumePacket");

                    // 优先经受控协商发送钩子（真实送达依据）；未装配时回退旧实现。
                    if (ReconnectResumeSendHook != null)
                    {
                        await ReconnectResumeSendHook(CharacterId, Interlocked.Read(ref _lastAppliedServerTick));
                    }
                    else
                    {
                        await SendReconnectResumeAsync();
                    }
                }
                catch (Exception ex)
                {
                    EnhancedLogging.LogWarning($"[OnClientConnected] 发送 ReconnectResumePacket 失败（忽略）: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 客户端断开连接事件处理
        /// </summary>
        private async Task OnClientDisconnected(ITcpClient client, ClosedEventArgs e)
        {
            // 跳过来自旧 _client 实例的过时 Closed 事件（新 _client 已在 InitializeClient 中创建并注册新事件）
            if (client != _client)
            {
                EnhancedLogging.LogInfo("[OnClientDisconnected] 忽略过时断开事件（来自旧客户端实例）");
                return;
            }

            EnhancedLogging.LogInfo("[OnClientDisconnected] 客户端断开连接");
            EnhancedDiagnostics.LogNetworkOperation("断开连接", $"{_currentGateway?.IP}:{_currentGateway?.Port}", true, "连接断开");

            UpdateConnectionStatus(ConnectionStatus.Disconnected);

            // 停止心跳包发送
            if (_heartbeatManager != null)
            {
                _heartbeatManager.StopHeartbeat();
            }

            // 如果不是主动断开，使用重连管理器进行重连
            bool shouldReconnect = !_isDisposing;
            if (shouldReconnect && _connectionCts != null)
            {
                try
                {
                    shouldReconnect = !_connectionCts.IsCancellationRequested;
                }
                catch (ObjectDisposedException)
                {
                    shouldReconnect = false;
                }
            }

            if (shouldReconnect)
            {
                EnhancedLogging.LogInfo("[OnClientDisconnected] 检测到非主动断开，触发重连管理器");
                _reconnectionManager?.HandleDisconnect();
            }
        }

        /// <summary>
        /// 数据接收事件处理
        /// </summary>
        /// <summary>
        /// [Phase C3] 网络数据接收入口，拆分为三个阶段：StageParse → StageValidate → StageDispatch。
        /// </summary>
        private async Task OnDataReceived(ITcpClient sender, ReceivedDataEventArgs e)
        {
            try
            {
                // 修复（远程角色同时不动+同时离线）：
                // 原实现 LastHeartbeatTime 仅在收到 HeartbeatMessage 响应时更新，
                // 不在收到其他消息（如快照）时更新。网络抖动导致心跳响应丢失时，
                // 即使快照持续到达（连接实际活跃），10 秒后仍触发 CheckHeartbeat 断线。
                // 断线后等待 60 秒才开始重连 → 快照通道断 60+ 秒 → 远程角色不动 →
                // 90 秒后 CleanupStaleEntities 同时清理所有远程实体 → "同时离线"。
                //
                // 修复：收到任何服务端消息都更新 LastHeartbeatTime，证明连接活跃。
                // 只有真正收不到任何消息（连接真的断了）时才触发心跳超时。
                _reconnectionManager?.UpdateHeartbeat();

                // Stage 1: 解析
                var messagePacket = StageParse(e);
                if (messagePacket == null)
                    return;

                // Stage 2: 验证
                if (!StageValidate(messagePacket))
                    return;

                // Stage 3: 分发
                await StageDispatch(sender, messagePacket);
            }
            catch (Exception ex)
            {
                ConnectionError?.Invoke($"解析消息失败: {ex.Message}");
                EnhancedDiagnostics.LogException(ex, "解析消息");
                EnhancedLogging.LogError($"[OnDataReceived] 处理数据时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// [Phase C3] Stage 1：从 ReceivedDataEventArgs 提取 HorizonMessagePacket。
        /// </summary>
        private HorizonMessagePacket StageParse(ReceivedDataEventArgs e)
        {
            if (e == null)
            {
                EnhancedLogging.LogWarning("[StageParse] 接收到空的事件参数");
                return null;
            }
        
            try
            {
                // 检查是否有 RequestInfo（通过自定义适配器解析的消息）
                if (e.RequestInfo is HorizonMessageInfo horizonRequest)
                {
                    return horizonRequest.Packet;
                }
        
                // 适配器未解析，尝试手动解析
                if (e.Memory.IsEmpty)
                {
                    EnhancedLogging.LogWarning("[StageParse] 接收到空数据");
                    return null;
                }
        
                var dataArray = e.Memory.ToArray();
                return _messageAdapter.UnpackMessage(dataArray);
            }
            catch (Exception ex)
            {
                EnhancedLogging.LogError($"[StageParse] 反序列化消息失败: {ex.Message}");
                EnhancedDiagnostics.LogException(ex, "反序列化消息");
                return null;
            }
        }
        
        /// <summary>
        /// [Phase C3] Stage 2：验证消息头必需字段（GameId/ServerId/ZoneId）。
        /// </summary>
        private bool StageValidate(HorizonMessagePacket messagePacket)
        {
            if (messagePacket.Header.IsResponse)
                return true; // 响应消息无需验证
        
            if (messagePacket.Header.GameId <= 0)
            {
                EnhancedLogging.LogWarning("[StageValidate] 收到无效请求消息: GameId必须为正数");
                return false;
            }
        
            if (messagePacket.Header.ServerId <= 0)
            {
                EnhancedLogging.LogWarning("[StageValidate] 收到无效请求消息: ServerId必须为正数");
                return false;
            }
        
            if (messagePacket.Header.ZoneId <= 0)
            {
                EnhancedLogging.LogWarning("[StageValidate] 收到无效请求消息: ZoneId必须为正数");
                return false;
            }
        
            return true;
        }
        
        /// <summary>
        /// [Phase C3] Stage 3：路由到 MessageProcessor。
        /// </summary>
        private async Task StageDispatch(ITcpClient sender, HorizonMessagePacket messagePacket)
        {
            try
            {
                await ProcessMessageAsync(sender, messagePacket);
            }
            catch (ArgumentException argEx)
            {
                EnhancedLogging.LogWarning($"[StageDispatch] 消息验证失败: {argEx.Message}");
                EnhancedDiagnostics.LogException(argEx, "消息验证");
            }
            catch (Exception ex)
            {
                EnhancedLogging.LogError($"[StageDispatch] 处理消息异常: {ex.Message}");
                EnhancedDiagnostics.LogException(ex, "处理消息");
            }
        }

        /// <summary>
        /// 网络状态变化事件处理
        /// </summary>
        private void OnNetworkStatusChanged(NetworkStatus status)
        {
            EnhancedLogging.LogInfo($"[OnNetworkStatusChanged] 网络状态变化: {status}");

            switch (status)
            {
                case NetworkStatus.Disconnected:
                    EnhancedLogging.LogWarning("[OnNetworkStatusChanged] 检测到网络离线");
                    EnhancedDiagnostics.LogDiagnostic("检测到网络离线");
                    // 网络离线时，不需要立即重连，等待网络恢复
                    break;
                case NetworkStatus.Connected:
                    EnhancedLogging.LogInfo("[OnNetworkStatusChanged] 检测到网络在线");
                    EnhancedDiagnostics.LogDiagnostic("检测到网络在线");
                    // 修复（客户端无限发送连接请求）：永久停止守卫。
                    // 5 轮探查失败后 _disconnectTimedOut=true，NetworkStateMonitor 网络恢复事件
                    // 不再自动触发重连，只有用户手动触发（ManualReconnectAsync/ConnectOnDemandAsync）才能重新发起连接。
                    if (_reconnectionManager.IsDisconnectTimedOut)
                    {
                        EnhancedLogging.LogInfo("[OnNetworkStatusChanged] 探查已永久停止（等待用户手动触发），跳过 NetworkStateMonitor 触发的重连");
                        break;
                    }
                    // 网络恢复时，检查是否需要重连
                    // [修复] 全面协调 NetworkStateMonitor 与 ReconnectionManager 的重连竞争：
                    // 1) ReconnectionManager 已在重连中 → 跳过
                    // 2) ReconnectionManager 处于 Disconnected 状态（已触发 HandleDisconnect，
                    //    正在等待 ReconnectDelayMs 延迟）→ 跳过，由 ReconnectionManager 负责重连
                    // 3) ReconnectionManager 处于 Connected 状态（连接正常）→ 跳过，无需重连
                    // 只有当 ReconnectionManager 处于 Unkonw/Failed 且连接确实断开时，
                    // NetworkStateMonitor 才作为后备触发重连。
                    // 这消除了"断线后 NetworkStateMonitor 3 秒 + ReconnectionManager 5 秒双重重连"的核心根因。
                    if (_connectionStatus == ConnectionStatus.Disconnected
                        && _currentGateway != null
                        && _reconnectionManager.CurrentState != ReconnectionManager.ReconnectState.Reconnecting
                        && _reconnectionManager.CurrentState != ReconnectionManager.ReconnectState.Disconnected
                        && _reconnectionManager.CurrentState != ReconnectionManager.ReconnectState.Connected)
                    {
                        RunBackground(async () =>
                        {
                            await Task.Delay(3000); // 等待3秒确保网络稳定
                            // 延迟后再次检查，防止 ReconnectionManager 在等待期间已启动重连
                            if (_reconnectionManager.IsDisconnectTimedOut
                                || _reconnectionManager.CurrentState == ReconnectionManager.ReconnectState.Reconnecting
                                || _reconnectionManager.CurrentState == ReconnectionManager.ReconnectState.Disconnected
                                || _reconnectionManager.CurrentState == ReconnectionManager.ReconnectState.Connected)
                            {
                                EnhancedLogging.LogInfo("[OnNetworkStatusChanged] ReconnectionManager 已在处理重连或已连接或已永久停止，跳过 NetworkStateMonitor 触发的重连");
                                return;
                            }
                            await CheckAndReconnectAsync();
                        });
                    }
                    else
                    {
                        EnhancedLogging.LogInfo(
                            $"[OnNetworkStatusChanged] 跳过 NetworkStateMonitor 重连：ConnectionStatus={_connectionStatus}, ReconnectState={_reconnectionManager.CurrentState}");
                    }
                    break;
            }
        }

        #endregion

        #region Message Handling
        /// <summary>
        /// 扩展方法：自动注册所有实现IMessageHandler接口的类型
        /// </summary>
        public void AddAllMessageHandlers()
        {
            // 扫描当前类型所在程序集（比 GetExecutingAssembly 更稳健，避免内联/委托导致程序集引用错误）
            Type[] handlerTypes;
            try
            {
                handlerTypes = typeof(NetworkManager).Assembly.GetTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException ex)
            {
                // 打包后可能因依赖缺失导致部分类型加载失败，使用已成功加载的类型
                handlerTypes = ex.Types.Where(t => t != null).ToArray();
                Debug.LogWarning($"[NetworkManager] 程序集扫描部分类型加载失败，已加载 {handlerTypes.Length} 个类型，失败 {ex.LoaderExceptions?.Length ?? 0} 个");
            }

            var handlerTypeList = handlerTypes
                  .Where(type => type.IsClass && !type.IsAbstract && typeof(IMessageHandler).IsAssignableFrom(type))
                  .ToList();

            foreach (var handlerType in handlerTypeList)
            {
                IMessageHandler handlerInstance = null;
                try
                {
                    // 优先检测需要 NetworkManager 参数的构造函数
                    var ctorWithNetworkManager = handlerType.GetConstructor(new[] { typeof(NetworkManager) });
                    if (ctorWithNetworkManager != null)
                    {
                        handlerInstance = Activator.CreateInstance(handlerType, this) as IMessageHandler;
                    }
                    else
                    {
                        handlerInstance = Activator.CreateInstance(handlerType) as IMessageHandler;
                    }

                    if (handlerInstance == null)
                    {
                        Debug.LogWarning($"  [跳过] 无法创建处理器实例: {handlerType.Name}");
                        continue;
                    }

                    _registeredHandlers.Add(handlerInstance);
                    foreach (var item in handlerInstance.MessageTypes)
                    {
                        _messageProcessor.RegisterHandler(item, handlerInstance);
                        Debug.Log($"  - {item.GetDescription()}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"  [跳过] 处理器 {handlerType.Name} 注册失败: {ex.Message}");
                }
            }


            // 输出调试信息
            Debug.Log($"[DEBUG] 自动注册了 {handlerTypes.Count()} 个消息处理器:");

            // [修复] 通知订阅者消息处理器已注册完成，避免 HundunWorldGame 轮询重试
            HandlersRegistered?.Invoke();
        }




        /// <summary>
        /// 获取已注册的指定类型消息处理器
        /// </summary>
        /// <typeparam name="T">处理器类型</typeparam>
        /// <returns>处理器实例，未找到返回 null</returns>
        public T GetHandler<T>() where T : class, IMessageHandler
        {
            return _registeredHandlers.OfType<T>().FirstOrDefault();
        }

        /// <summary>
        /// 按类型获取已注册的消息处理器（非泛型版本，供跨模块反射调用）
        /// </summary>
        /// <param name="handlerType">处理器类型</param>
        /// <returns>处理器实例，未找到返回 null</returns>
        public IMessageHandler GetHandler(Type handlerType)
        {
            return _registeredHandlers.FirstOrDefault(h => handlerType.IsInstanceOfType(h));
        }

        /// <summary>
        /// 处理消息
        /// </summary>
        // P-F4：SyncPacket 处理日志采样计数器（前 5 次 + 每 240 次输出一条）。
        private long _syncPacketProcessLogCount;

        // P-F5：SyncFrameMessage 发送日志采样计数器（前 5 次 + 每 240 次输出一条）。
        private long _syncFrameSendLogCount;

        private async Task ProcessMessageAsync(ITcpClient sender, HorizonMessagePacket messagePacket)
        {
            if (messagePacket == null)
            {
                EnhancedLogging.LogWarning("[ProcessMessageAsync] 消息包为空");
                return;
            }

            // P-F4 网络热路径日志治理：SyncPacket（快照/ACK/事件，20Hz+ × 多包类型）原每包一条
            // LogInfo，多角色同屏时字符串插值 + 日志 IO 直接阻塞网络接收线程，是客户端卡顿的
            // 重要诱因。高频类型改为采样输出（前 5 次 + 每 240 次一条），其余低频消息保留原日志。
            var msgType = messagePacket.Header.MessageType;
            if (msgType == MessageType.SyncPacket)
            {
                var cnt = Interlocked.Increment(ref _syncPacketProcessLogCount);
                if (cnt <= 5 || cnt % 240 == 1)
                {
                    EnhancedLogging.LogInfo($"[ProcessMessageAsync] 开始处理消息: {msgType}（累计 {cnt} 条，采样输出）");
                }
            }
            else
            {
                EnhancedLogging.LogInfo($"[ProcessMessageAsync] 开始处理消息: {msgType}");
            }

            try
            {
                // 根据消息类型处理不同消息
                switch (messagePacket.Header.MessageType)
                {
                    case MessageType.Heartbeat:
                        await HandleHeartbeatMessageAsync(sender, (HeartbeatMessage)messagePacket.Body);
                        break;
                    case MessageType.Error:
                        if (messagePacket.Body is AuthenticationError authError)
                            await HandleAuthenticationErrorAsync(sender, authError);
                        else if (messagePacket.Body is Horizon.Game.Message.Network.ErrorMessage errorMessage)
                            await HandleErrorMessageAsync(sender, errorMessage);
                        else
                            EnhancedLogging.LogWarning($"[ProcessMessageAsync] 未知的Error消息体类型: {messagePacket.Body?.GetType().Name}");
                        break;
                    default:
                        await _messageProcessor.ProcessMessageAsync(messagePacket);
                        break;
                }
            }
            catch (Exception ex)
            {
                EnhancedLogging.LogError($"[ProcessMessageAsync] 处理消息时发生错误: {ex.Message}");
                EnhancedDiagnostics.LogException(ex, "处理消息");
            }
        }

        /// <summary>
        /// 处理心跳消息
        /// </summary>
        private async Task<bool> HandleHeartbeatMessageAsync(ITcpClient sender, HeartbeatMessage message)
        {
            EnhancedLogging.LogInfo("[HandleHeartbeatMessageAsync] 收到心跳消息");

            // 更新重连管理器的心跳时间
            _reconnectionManager?.UpdateHeartbeat();

            // 回复心跳确认
            var heartbeatAck = new HeartbeatMessage
            {
                Timestamp = DateTime.UtcNow.Ticks,
                ClientTime = DateTime.UtcNow.Ticks,
                ServerTime = message.Timestamp
            };

            await SendAsync(heartbeatAck);

            return true;
        }

        /// <summary>
        /// 处理错误消息
        /// </summary>
        private async Task<bool> HandleErrorMessageAsync(ITcpClient sender, Horizon.Game.Message.Network.ErrorMessage message)
        {
            EnhancedLogging.LogWarning($"[HandleErrorMessageAsync] 收到错误消息: {message.Message}");

            // 通知错误
            ConnectionError?.Invoke(message.Message);

            return true;
        }

        /// <summary>
        /// 处理认证错误消息
        /// 当收到令牌过期错误（ErrorCode=1007）时，自动尝试刷新令牌并重新登录
        /// </summary>
        private async Task<bool> HandleAuthenticationErrorAsync(ITcpClient sender, AuthenticationError message)
        {
            EnhancedLogging.LogWarning($"[HandleAuthenticationErrorAsync] 收到认证错误消息: Code={message.ErrorCode}, Message={message.ErrorMessage}");

            // 鉴权令牌过期（ErrorCode=1007），尝试自动刷新令牌
            if (message.ErrorCode == 1007 && !_isTokenRefreshing)
            {
                _ = Task.Run(async () => await TryRefreshAuthTokenAsync());
            }
            else
            {
                // 非过期错误或正在刷新中，仅通知
                ConnectionError?.Invoke(message.ErrorMessage);
            }

            return true;
        }

        /// <summary>
        /// 尝试自动刷新鉴权令牌
        /// 流程：刷新HTTP令牌获取新ImAuthToken → 服务端已废弃TokenLoginRequest，新ImAuthToken直接作为AuthToken → 更新本地AuthToken → 恢复心跳
        /// </summary>
        private async Task TryRefreshAuthTokenAsync()
        {
            if (_isTokenRefreshing) return;
            _isTokenRefreshing = true;

            try
            {
                EnhancedLogging.LogInfo("[TokenRefresh] 检测到鉴权令牌过期，开始自动刷新令牌");

                // 1. 停止心跳，防止持续发送过期令牌导致日志刷屏
                _heartbeatManager?.StopHeartbeat();

                // 2. 通过HTTP刷新令牌，获取新的ImAuthToken
                var authService = GengDiAuthService.Instance;
                var refreshed = await authService.RefreshTokenAsync();

                if (!refreshed)
                {
                    EnhancedLogging.LogWarning("[TokenRefresh] HTTP令牌刷新失败，通知上层需要重新登录");
                    AuthTokenExpired?.Invoke();
                    ConnectionError?.Invoke("登录已过期，请重新登录");
                    return;
                }

                var newImAuthToken = GengDiAuthService.GetImAuthToken();
                var passportId = GengDiAuthService.GetPassportId();
                var newUserId = GengDiAuthService.GetUserId();

                if (string.IsNullOrEmpty(newImAuthToken) || string.IsNullOrEmpty(passportId))
                {
                    EnhancedLogging.LogWarning("[TokenRefresh] 刷新后令牌或PassportId为空，通知重新登录");
                    AuthTokenExpired?.Invoke();
                    ConnectionError?.Invoke("登录已过期，请重新登录");
                    return;
                }

                // 3. 服务端已废弃 TCP TokenLoginRequest，新的 ImAuthToken 直接作为 AuthToken 使用
                AuthToken = newImAuthToken;
                UserId = newUserId;
                EnhancedLogging.LogInfo($"[TokenRefresh] 已更新本地鉴权令牌: UserId={newUserId}");

                // 4. 恢复心跳，新的请求将使用更新后的 AuthToken
                _heartbeatManager?.StartHeartbeat();
                EnhancedLogging.LogInfo("[TokenRefresh] 令牌刷新完成，心跳已恢复");
            }
            catch (Exception ex)
            {
                EnhancedLogging.LogError($"[TokenRefresh] 令牌刷新过程中发生异常: {ex.Message}");
                EnhancedDiagnostics.LogException(ex, "令牌自动刷新");
                _heartbeatManager?.StartHeartbeat();
                ConnectionError?.Invoke("登录令牌刷新失败，请重新登录");
            }
            finally
            {
                _isTokenRefreshing = false;
            }
        }

        #endregion

        #region Message Sending

        /// <summary>
        /// 发送消息
        /// </summary>
        public async Task<bool> SendAsync<T>(T message) where T : MessageUnion, INetworkMessage
        {
            if (_client == null || !_client.Online)
            {
                EnhancedLogging.LogWarning($"[SendAsync] 客户端未连接，无法发送消息: {typeof(T).Name}");
                return false;
            }

            if (message == null)
            {
                EnhancedLogging.LogWarning("[SendAsync] 消息为空，无法发送");
                return false;
            }

            lock (_sendLock)
            {
                if (_connectionStatus != ConnectionStatus.Connected)
                {
                    EnhancedLogging.LogWarning($"[SendAsync] 连接状态异常: {_connectionStatus}，无法发送消息");
                    return false;
                }
            }

            try
            {
                // 兜底守卫（spec 5.4.3 异常 1）：实体同步类上行帧（SyncFrameMessage）在真正入网前
                // 必须已通过 GuardSyncSender 授权；未经授权调用被拦截并触发"旁路绕过"告警。
                // 登录握手、心跳等连接层消息不受守卫拦截（spec 1.4 职责边界）。
                if (message is SyncFrameMessage syncFrame && OutboundSyncGuard != null)
                {
                    var senderEntityId = ResolveSenderEntityId(syncFrame);
                    bool approved;
                    try
                    {
                        approved = OutboundSyncGuard.TryApprove(syncFrame, senderEntityId);
                    }
                    catch (Exception guardEx)
                    {
                        // 守卫内部异常 → 拒绝发送 + 告警，不扩散（spec 5.4.3 异常 2）。
                        EnhancedLogging.LogError($"[SendAsync] 兜底守卫异常，拒绝发送: {guardEx.Message}");
                        EnhancedDiagnostics.LogException(guardEx, "OutboundSyncGuard");
                        return false;
                    }

                    if (!approved)
                    {
                        EnhancedLogging.LogWarning($"[SendAsync] 旁路绕过拦截: SenderEntityId={senderEntityId}, PacketKind={syncFrame.PacketKind}");
                        return false;
                    }
                }

                // 使用消息适配器打包消息
                var packedData = _messageAdapter.PackMessage(message, ((INetworkMessage)message).Type, true);

                // P-F5 发送热路径日志治理：SyncFrameMessage（输入包，10Hz+ × 多角色）原每包两条
                // LogInfo（准备+成功），字符串插值 + 日志 IO 阻塞发送路径。高频类型改为采样输出。
                if (message is SyncFrameMessage)
                {
                    var cnt = Interlocked.Increment(ref _syncFrameSendLogCount);
                    if (cnt <= 5 || cnt % 240 == 1)
                    {
                        EnhancedLogging.LogInfo($"[SendAsync] 发送 SyncFrameMessage（累计 {cnt} 条，采样输出），数据长度: {packedData.Length} 字节");
                    }
                }
                else
                {
                    EnhancedLogging.LogInfo($"[SendAsync] 准备发送消息，数据长度: {packedData.Length} 字节");
                }

                // 发送数据
                await _client.SendAsync(packedData);

                if (!(message is SyncFrameMessage))
                {
                    EnhancedLogging.LogInfo($"[SendAsync] 消息发送成功: {typeof(T).Name}");
                }

                return true;
            }
            catch (Exception ex)
            {
                EnhancedLogging.LogError($"[SendAsync] 发送消息失败: {ex.Message}");
                EnhancedDiagnostics.LogException(ex, "发送消息");
                return false;
            }
        }

        /// <summary>
        /// 发送消息（别名方法，与SendAsync功能相同）
        /// </summary>
        public async Task<bool> SendMessageAsync<T>(T message) where T : MessageUnion, INetworkMessage
        {
            return await SendAsync(message);
        }

        /// <summary>
        /// 发送Horizon消息包
        /// </summary>
        public async Task<bool> SendMessageAsync(HorizonMessagePacket messagePacket)
        {
            if (messagePacket?.Body == null)
            {
                EnhancedLogging.LogWarning("[SendMessageAsync] 消息包或消息体为空，无法发送");
                return false;
            }

            if (_client == null || !_client.Online)
            {
                EnhancedLogging.LogWarning("[SendMessageAsync] 客户端未连接，无法发送消息");
                return false;
            }

            lock (_sendLock)
            {
                if (_connectionStatus != ConnectionStatus.Connected)
                {
                    EnhancedLogging.LogWarning($"[SendMessageAsync] 连接状态异常: {_connectionStatus}，无法发送消息");
                    return false;
                }
            }

            try
            {
                var packedData = _messageAdapter.PackPacket(messagePacket);

                EnhancedLogging.LogInfo($"[SendMessageAsync] 准备发送消息包，数据长度: {packedData.Length} 字节");

                await _client.SendAsync(packedData);
                EnhancedLogging.LogInfo($"[SendMessageAsync] 消息包发送成功: {messagePacket.Header.MessageType}");

                return true;
            }
            catch (Exception ex)
            {
                EnhancedLogging.LogError($"[SendMessageAsync] 发送消息包失败: {ex.Message}");
                EnhancedDiagnostics.LogException(ex, "发送消息包");
                return false;
            }
        }

        /// <summary>
        /// 检查是否可以发送消息
        /// </summary>
        public bool CanSendMessage()
        {
            return _client != null && _client.Online && _connectionStatus == ConnectionStatus.Connected;
        }

        /// <summary>
        /// 从同步帧中推导发起实体 ID（仅用于兜底守卫；推导失败时按无资格处理）。
        /// 优先解码帧头/包体提取 InputPacket.CharacterId / CombatActionPacket.AttackerId；
        /// 解码失败返回 0（守卫将按无资格拒绝）。
        /// </summary>
        private ulong ResolveSenderEntityId(SyncFrameMessage syncFrame)
        {
            if (syncFrame?.Frame == null || syncFrame.Frame.Length < SyncPacketCodec.FrameHeaderSize)
            {
                return 0;
            }

            try
            {
                // 仅解码实体同步类包（Input/CombatAction），其他同步包无发起实体身份。
                var kind = syncFrame.PacketKind;
                if (kind == (byte)SyncPacketKind.Input)
                {
                    if (SyncPacketCodec.Decode(syncFrame.Frame) is InputPacket input)
                    {
                        return input.CharacterId;
                    }
                }
                else if (kind == (byte)SyncPacketKind.CombatAction)
                {
                    if (SyncPacketCodec.Decode(syncFrame.Frame) is CombatActionPacket combat)
                    {
                        return combat.AttackerId;
                    }
                }
            }
            catch (Exception ex)
            {
                EnhancedDiagnostics.LogException(ex, "ResolveSenderEntityId");
            }

            return 0;
        }

        /// <summary>
        /// 发送同步握手包到服务器（进入游戏世界时必须调用）
        /// 握手完成后，服务器才会接受 InputPacket
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <param name="initialX">实体初始位置 X（来自 EnterGameResponse.CharacterInfo.Position.X）</param>
        /// <param name="initialY">实体初始位置 Y（来自 EnterGameResponse.CharacterInfo.Position.Y）</param>
        /// <param name="initialZ">实体初始位置 Z（来自 EnterGameResponse.CharacterInfo.Position.Z）</param>
        public async Task SendSyncHandshakeAsync(ulong characterId, float initialX, float initialY, float initialZ)
        {
            if (!CanSendMessage())
            {
                FlaxEngine.Debug.LogWarning("[NetworkManager] 无法发送同步握手：网络未连接");
                return;
            }

            // 跨线程保护：_syncClientTick 可能被 ResetSyncHandshake（主线程/重连流程）并发重置，
            // 使用 Interlocked.Exchange 写入保证可见性。局部变量 clientTick 用于后续构造握手包，
            // 避免在对象初始化器中重复调用 Interlocked.Read。
            var clientTick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Interlocked.Exchange(ref _syncClientTick, clientTick);

            // 记录握手参数与时间，供后续重试使用。
            _lastHandshakeCharacterId = characterId;
            _lastHandshakeX = initialX;
            _lastHandshakeY = initialY;
            _lastHandshakeZ = initialZ;
            // 每次尝试都更新 _lastHandshakeSentTicks 以驱动 3s 重试间隔；
            // 首次尝试时间戳仅在为 0 时设置，用于全局超时判定，避免每次重试都归零导致超时永不触发。
            Interlocked.Exchange(ref _lastHandshakeSentTicks, DateTimeOffset.UtcNow.Ticks);
            Interlocked.CompareExchange(ref _handshakeFirstAttemptTicks, DateTimeOffset.UtcNow.Ticks, 0);

            var handshake = new HandshakePacket
            {
                LocalCharacterId = characterId,
                InitialClientTick = clientTick,
                InitialX = initialX,
                InitialY = initialY,
                InitialZ = initialZ,
            };

            SyncPacketCodec.Encode(handshake, out var frame, out var frameLength);
            try
            {
                var payload = new byte[frameLength];
                System.Buffer.BlockCopy(frame, 0, payload, 0, frameLength);

                var syncFrame = new SyncFrameMessage
                {
                    Frame = payload,
                    PacketKind = (byte)handshake.Kind,
                    ProtocolVersion = handshake.ProtocolVersion,
                };

                await SendAsync(syncFrame);

                // [修复] 移除提前标记握手完成：发送握手包后不应立即标记完成，
                // 必须等待服务端返回 HandshakePacket 响应后才由 MarkSyncHandshakeComplete() 标记完成。
                // 旧代码在此处设置 _syncHandshakeComplete = true，导致握手尚未被服务端确认就认为已完成。
                // 跨线程读取 _syncClientTick：可能被 ResetSyncHandshake 并发重置，使用 Interlocked.Read 保证读到一致值。
                FlaxEngine.Debug.Log($"[NetworkManager] 同步握手已发送: CharacterId={characterId}, ClientTick={Interlocked.Read(ref _syncClientTick)}");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[NetworkManager] 同步握手发送失败: {ex.Message}");
            }
            finally
            {
                SyncPacketCodec.ReturnFrame(frame);
            }
        }

        /// <summary>
        /// 如果同步握手尚未完成且距上次发送已超过重试间隔，则重新发送上一次握手包。
        /// 由 ECSUpdateDriver 每帧调用，用于在响应丢失时自动恢复。
        /// </summary>
        /// <returns>true 表示已触发重试发送；false 表示无需重试。</returns>
        public async Task<bool> TryEnsureSyncHandshakeAsync()
        {
            if (_syncHandshakeComplete)
                return false;

            var lastSentTicks = Interlocked.Read(ref _lastHandshakeSentTicks);
            if (lastSentTicks == 0)
            {
                // 尚未发送过握手，无参数可重试。
                return false;
            }

            var elapsed = TimeSpan.FromTicks(DateTimeOffset.UtcNow.Ticks - lastSentTicks);
            if (elapsed < HandshakeRetryInterval)
                return false;

            // 超过全局超时仍未完成：不再重试，避免无限发送。
            // 全局超时基于首次尝试时间戳 _handshakeFirstAttemptTicks，而非 _lastHandshakeSentTicks：
            // 后者每次重试都刷新，会使超时判定永远不触发（修复 sync-handshake 无限重发问题）。
            var firstAttemptTicks = Interlocked.Read(ref _handshakeFirstAttemptTicks);
            if (firstAttemptTicks != 0)
            {
                var totalElapsed = TimeSpan.FromTicks(DateTimeOffset.UtcNow.Ticks - firstAttemptTicks);
                if (totalElapsed > HandshakeTimeout)
                {
                    FlaxEngine.Debug.LogError($"[NetworkManager] 同步握手超时 ({totalElapsed.TotalSeconds:F1}s)，停止重试。请检查服务端是否正常响应。");
                    return false;
                }
            }

            FlaxEngine.Debug.LogWarning($"[NetworkManager] 同步握手未完成，距上次发送 {elapsed.TotalSeconds:F1}s，尝试重发握手包...");
            await SendSyncHandshakeAsync(_lastHandshakeCharacterId, _lastHandshakeX, _lastHandshakeY, _lastHandshakeZ);
            return true;
        }

        /// <summary>
        /// 发送 AOI 订阅更新包到服务器（本地玩家跨 chunk 边界时调用）
        /// </summary>
        /// <param name="addedChunks">新增订阅的 chunk key 集合</param>
        /// <param name="removedChunks">移除订阅的 chunk key 集合</param>
        public async Task SendSubscriptionUpdateAsync(ulong[] addedChunks, ulong[] removedChunks, float posX = 0f, float posY = 0f, float posZ = 0f, bool hasPosition = false)
        {
            if (!CanSendMessage() || !IsSyncHandshakeComplete)
                return;

            if (!hasPosition && (addedChunks == null || addedChunks.Length == 0) && (removedChunks == null || removedChunks.Length == 0))
                return;

            var packet = new SubscriptionUpdatePacket
            {
                AddedChunks = addedChunks ?? Array.Empty<ulong>(),
                RemovedChunks = removedChunks ?? Array.Empty<ulong>(),
                PositionX = posX,
                PositionY = posY,
                PositionZ = posZ,
                HasPosition = hasPosition,
            };

            SyncPacketCodec.Encode(packet, out var frame, out var frameLength);
            try
            {
                var payload = new byte[frameLength];
                System.Buffer.BlockCopy(frame, 0, payload, 0, frameLength);

                var syncFrame = new SyncFrameMessage
                {
                    Frame = payload,
                    PacketKind = (byte)packet.Kind,
                    ProtocolVersion = packet.ProtocolVersion,
                };

                await SendAsync(syncFrame);
                FlaxEngine.Debug.Log($"[NetworkManager] AOI 订阅更新已发送: Added={packet.AddedChunks.Length}, Removed={packet.RemovedChunks.Length}");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[NetworkManager] AOI 订阅更新发送失败: {ex.Message}");
            }
            finally
            {
                SyncPacketCodec.ReturnFrame(frame);
            }
        }

        /// <summary>
        /// 标记同步握手为已完成（由收到服务端 HandshakePacket 响应时调用）。
        /// </summary>
        public void MarkSyncHandshakeComplete()
        {
            _syncHandshakeComplete = true;
            // 握手完成：重置首次尝试时间戳，确保下次握手周期从新开始。
            Interlocked.Exchange(ref _handshakeFirstAttemptTicks, 0);
            Debug.Log("[NetworkManager] 同步握手已确认完成");
            try { SyncHandshakeStateChanged?.Invoke(true); }
            catch (Exception ex) { EnhancedDiagnostics.LogException(ex, "SyncHandshakeStateChanged(true)"); }
        }

        /// <summary>
        /// 重置同步握手状态（断线重连时调用）
        /// </summary>
        public void ResetSyncHandshake()
        {
            _syncHandshakeComplete = false;
            // 跨线程保护：与 SendSyncHandshakeAsync 的写入并发，使用 Interlocked.Exchange 保证原子性。
            Interlocked.Exchange(ref _syncClientTick, 0);
            // 重置握手首次尝试时间戳，确保下次握手周期的全局超时从新开始计时。
            Interlocked.Exchange(ref _handshakeFirstAttemptTicks, 0);
            // 重置上次发送时间戳，使重连后握手重试节奏从新开始（避免基于旧时间戳立即触发重试）。
            Interlocked.Exchange(ref _lastHandshakeSentTicks, 0);
            try { SyncHandshakeStateChanged?.Invoke(false); }
            catch (Exception ex) { EnhancedDiagnostics.LogException(ex, "SyncHandshakeStateChanged(false)"); }
        }

        #endregion

        #region Status Management

        /// <summary>
        /// 上一次通知给订阅者的连接状态，用于避免重复通知
        /// </summary>
        private ConnectionStatus _lastNotifiedStatus = ConnectionStatus.Unknown;

        /// <summary>
        /// 更新连接状态
        /// </summary>
        private void UpdateConnectionStatus(ConnectionStatus status)
        {
            var oldStatus = _connectionStatus;
            if (oldStatus == status)
            {
                EnhancedLogging.LogInfo($"[UpdateConnectionStatus] 状态未变化，跳过: {status}");
                return;
            }

            _connectionStatus = status;

            EnhancedLogging.LogInfo($"[UpdateConnectionStatus] 连接状态从 {oldStatus} 更新为 {status}");
            EnhancedDiagnostics.LogDiagnostic($"连接状态从 {oldStatus} 更新为 {status}");

            // 将事件通知调度到主线程，确保UI更新在主线程执行
            // 使用 _connectionStatus 而非闭包捕获的 status，确保订阅者收到的是最新状态
            // 同时通过 _lastNotifiedStatus 避免向订阅者重复通知相同状态
            FlaxEngine.Scripting.InvokeOnUpdate(() =>
            {
                try
                {
                    var currentStatus = _connectionStatus;
                    if (currentStatus == _lastNotifiedStatus)
                    {
                        EnhancedLogging.LogInfo($"[UpdateConnectionStatus] 通知状态未变化，跳过通知: {currentStatus}");
                        return;
                    }
                    _lastNotifiedStatus = currentStatus;
                    EnhancedLogging.LogInfo($"[UpdateConnectionStatus] 通知订阅者连接状态: {currentStatus}");
                    ConnectionStatusChanged?.Invoke(currentStatus);
                    try
                    {
                        ConnectedStateChanged?.Invoke(currentStatus == ConnectionStatus.Connected);
                    }
                    catch (Exception ex)
                    {
                        EnhancedLogging.LogError($"[UpdateConnectionStatus] 触发连接资格状态事件时发生错误: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    EnhancedLogging.LogError($"[UpdateConnectionStatus] 触发连接状态变化事件时发生错误: {ex.Message}");
                    EnhancedDiagnostics.LogException(ex, "触发连接状态变化事件");
                }
            });
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// 在后台运行任务
        /// </summary>
        private void RunBackground(Func<Task> taskFunc)
        {
            Task.Run(async () =>
            {
                try
                {
                    await taskFunc();
                }
                catch (Exception ex)
                {
                    EnhancedLogging.LogError($"后台任务执行失败: {ex.Message}");
                    EnhancedDiagnostics.LogException(ex, "后台任务");
                }
            });
        }

        #endregion

        #region Connection Wait Helper

        /// <summary>
        /// 连接轮询间隔（毫秒）
        /// </summary>
        private const int ConnectionPollIntervalMs = 100;

        /// <summary>
        /// 等待连接建立（带超时）
        /// </summary>
        /// <param name="timeoutMs">超时时间（毫秒）</param>
        /// <returns>是否在超时前连接成功</returns>
        public async Task<bool> WaitForConnectionAsync(int timeoutMs = 10000)
        {
            if (_connectionStatus == ConnectionStatus.Connected)
            {
                return true;
            }

            var startTime = DateTime.UtcNow;
            var endTime = startTime.AddMilliseconds(timeoutMs);

            while (true)
            {
                if (_connectionStatus == ConnectionStatus.Connected)
                {
                    EnhancedLogging.LogInfo("[WaitForConnectionAsync] 连接已建立");
                    return true;
                }

                var currentTime = DateTime.UtcNow;
                if (currentTime >= endTime)
                {
                    break;
                }

                await Task.Delay(ConnectionPollIntervalMs);
            }

            EnhancedLogging.LogWarning($"[WaitForConnectionAsync] 等待连接超时 ({timeoutMs}ms)");
            return false;
        }

        #endregion

        #region Reconnection Event Handlers

        /// <summary>
        /// 重连成功事件处理
        /// </summary>
        private void OnReconnectionSucceeded()
        {
            EnhancedLogging.LogInfo("[OnReconnectionSucceeded] 重连成功");
            EnhancedDiagnostics.LogDiagnostic("重连成功");

            // [Phase C5] 重连成功后发送 ReconnectResumePacket（若启用）。
            // 改经受控协商发送钩子（真实送达依据），不再 fire-and-forget（spec 5.6.1 规则 1）。
            if (EnableReconnectResume && CharacterId > 0)
            {
                if (ReconnectResumeSendHook != null)
                {
                    // 受控 await：结果回传恢复状态机（异步异常被钩子内部隔离）。
                    _ = FireControlledResumeSendAsync();
                }
                else
                {
                    _ = SendReconnectResumeAsync(); // 钩子未装配时的回退（向后兼容）
                }
            }

            // [Phase C2] 记录重连成功指标
            ClientSyncMetrics.RecordReconnectSuccess();

            OnReconnectionStateChanged(ReconnectionManager.ReconnectState.Connected);
        }

        /// <summary>
        /// 经 ReconnectResumeSendHook 受控发送协商消息，结果回传恢复状态机（spec 5.6.1 规则 1）。
        /// 钩子内部已隔离异常（返回 false），本方法不向重连事件链抛异常。
        /// </summary>
        private async Task FireControlledResumeSendAsync()
        {
            try
            {
                if (ReconnectResumeSendHook == null || CharacterId == 0)
                {
                    return;
                }

                await ReconnectResumeSendHook(CharacterId, Interlocked.Read(ref _lastAppliedServerTick));
            }
            catch (Exception ex)
            {
                EnhancedLogging.LogWarning($"[OnReconnectionSucceeded] 受控协商发送异常被隔离: {ex.Message}");
            }
        }

        /// <summary>
        /// 修复（真正断网后无法恢复 — ConnectAsync 跳过连接）：
        /// ReconnectionManager.HandleDisconnect 触发时（心跳超时路径），
        /// 同步更新 NetworkManager._connectionStatus 为 Disconnected。
        /// 这确保后续 ConnectAsync 不会因 _connectionStatus == Connected 而跳过连接。
        /// </summary>
        private void OnReconnectionManagerDisconnected()
        {
            EnhancedLogging.LogInfo("[OnReconnectionManagerDisconnected] ReconnectionManager 触发断线，同步更新 _connectionStatus");
            UpdateConnectionStatus(ConnectionStatus.Disconnected);

            // 停止心跳包发送（连接已断，发送会失败）
            if (_heartbeatManager != null)
            {
                _heartbeatManager.StopHeartbeat();
            }
        }

        /// <summary>
        /// [Phase C5] 发送 ReconnectResumePacket，尝试增量恢复（服务端已支持）。
        /// 兜底实现（钩子未装配时的回退路径）：以 SendAsync 返回值为真实送达依据（spec 5.6.1 规则 6 禁止假成功）。
        /// </summary>
        private async Task SendReconnectResumeAsync()
        {
            try
            {
                var lastTick = System.Threading.Interlocked.Read(ref _lastAppliedServerTick);
                var resumePacket = new Horizon.Game.Message.Sync.ReconnectResumePacket
                {
                    LocalCharacterId = CharacterId,
                    LastAppliedSnapshotTick = lastTick,
                    LastAppliedDiffSeq = 0, // 待后续 WorldChunkDiff 支持后更新
                };

                Horizon.Game.Message.Sync.SyncPacketCodec.Encode(resumePacket, out var frame, out var frameLength);
                try
                {
                    var payload = new byte[frameLength];
                    System.Buffer.BlockCopy(frame, 0, payload, 0, frameLength);

                    var syncFrame = new Horizon.Game.Message.Network.SyncFrameMessage
                    {
                        Frame = payload,
                        PacketKind = (byte)resumePacket.Kind,
                        ProtocolVersion = resumePacket.ProtocolVersion,
                    };

                    // 以 SendAsync 返回值为真实送达依据，禁止假成功日志（spec 5.6.1 规则 6）。
                    var delivered = await SendAsync(syncFrame);
                    if (delivered)
                    {
                        EnhancedLogging.LogInfo($"[Phase C5] ReconnectResumePacket 已真实送达: CharacterId={CharacterId}, LastTick={lastTick}, Source=SendAsync=true");
                    }
                    else
                    {
                        EnhancedLogging.LogWarning($"[Phase C5] ReconnectResumePacket 发送失败（连接未就绪/被拦截），不输出已发送: CharacterId={CharacterId}, LastTick={lastTick}");
                    }
                }
                finally
                {
                    Horizon.Game.Message.Sync.SyncPacketCodec.ReturnFrame(frame);
                }
            }
            catch (Exception ex)
            {
                EnhancedLogging.LogWarning($"[Phase C5] ReconnectResumePacket 发送失败，回退到全量握手: {ex.Message}");
                // 回退：重新发送同步握手
                if (_lastHandshakeCharacterId > 0)
                {
                    await SendSyncHandshakeAsync(_lastHandshakeCharacterId, _lastHandshakeX, _lastHandshakeY, _lastHandshakeZ);
                }
            }
        }

        /// <summary>
        /// 重连失败事件处理
        /// </summary>
        private void OnReconnectionFailed()
        {
            EnhancedLogging.LogError("[OnReconnectionFailed] 重连失败，已达到最大重试次数");
            EnhancedDiagnostics.LogDiagnostic("重连失败");
            ConnectionError?.Invoke("无法连接到服务器，请检查网络连接或稍后重试");
        }

        /// <summary>
        /// 断线超时事件处理：网关离线超过阈值时间，停止所有自动重连，
        /// 通知上层退回角色选择界面。
        /// </summary>
        private void OnDisconnectTimeout()
        {
            EnhancedLogging.LogWarning("[OnDisconnectTimeout] 网关离线超过阈值时间，停止自动重连，通知上层退回角色选择界面");
            UpdateConnectionStatus(ConnectionStatus.Failed);
            DisconnectTimedOut?.Invoke();
        }

        /// <summary>
        /// 按需连接：用户主动选择进入游戏时调用。
        /// 先探查网关是否可达，可达则建立连接；不可达则返回 false，上层停留在角色选择界面并提示用户。
        /// 此方法会重置断线超时标记，允许本次连接尝试。
        /// </summary>
        /// <returns>true 表示连接成功；false 表示网关不可达。</returns>
        public async Task<bool> ConnectOnDemandAsync()
        {
            if (_currentGateway == null)
            {
                EnhancedLogging.LogWarning("[ConnectOnDemandAsync] 无当前网关信息，无法连接");
                return false;
            }

            EnhancedLogging.LogInfo($"[ConnectOnDemandAsync] 用户主动触发连接，探查网关 {_currentGateway.IP}:{_currentGateway.Port}");

            // 重置断线超时标记，允许本次连接尝试
            _reconnectionManager?.ResetDisconnectTimeout();

            // 探查网关可达性
            if (!await ProbeGatewayAsync(_currentGateway.IP, _currentGateway.Port))
            {
                EnhancedLogging.LogWarning("[ConnectOnDemandAsync] 网关不可达，停留在角色选择界面");
                return false;
            }

            // 网关可达，建立正式连接
            var connected = await ConnectAsync(_currentGateway.IP, _currentGateway.Port);
            if (connected)
            {
                EnhancedLogging.LogInfo("[ConnectOnDemandAsync] 按需连接成功");
                _reconnectionManager?.MarkConnected();
            }
            else
            {
                EnhancedLogging.LogWarning("[ConnectOnDemandAsync] 网关探查成功但正式连接失败");
            }

            return connected;
        }

        /// <summary>
        /// 经 ReconnectFlowGuard 安全包装重连成功事件（spec 5.6.1 规则 4、design 2.1.3(3)）。
        /// 在 NetworkManager 内部订阅之外叠加安全包装订阅，事件处理器异常不向 StartReconnectAsync 传播。
        /// </summary>
        public void SubscribeReconnectFlowGuard(HundunWorld.Game.RemoteVisibility.ReconnectFlowGuard guard)
        {
            if (guard == null || _reconnectionManager == null)
            {
                return;
            }

            // 包装 OnReconnected：重连成功结论确立后，任何处理器异常被隔离。
            _reconnectionManager.OnReconnected += () =>
            {
                guard.RaiseSafely();
            };
        }

        /// <summary>
        /// 重连成功事件处理器（经 ReconnectFlowGuard 安全包装调用）。
        /// 空引用防护：_client / 依赖组件为 null 时安全返回，不向重连流程抛异常（spec 5.6.3 异常 4）。
        /// </summary>
        public void RaiseReconnectionSucceededSafely()
        {
            try
            {
                // 空引用防护：访问 _client 前判空。
                if (_client == null)
                {
                    EnhancedLogging.LogWarning("[RaiseReconnectionSucceededSafely] _client 为空，跳过重连成功处理（空引用防护）");
                    return;
                }

                OnReconnectionSucceeded();
            }
            catch (Exception ex)
            {
                // 异常隔离：不向 ReconnectionManager.StartReconnectAsync 传播（spec 5.6.1 规则 4）。
                EnhancedLogging.LogWarning($"[RaiseReconnectionSucceededSafely] 重连成功事件链异常被隔离: {ex.Message}");
                EnhancedDiagnostics.LogException(ex, "重连成功事件链");
            }
        }

        /// <summary>
        /// 重连状态变化事件处理
        /// </summary>
        private void OnReconnectionStateChanged(ReconnectionManager.ReconnectState state)
        {
            EnhancedLogging.LogInfo($"[OnReconnectionStateChanged] 重连状态变化: {state}");

            // [修复] 直接调用 UpdateConnectionStatus（不包 InvokeOnUpdate），
            // 因为 UpdateConnectionStatus 内部已通过 InvokeOnUpdate 调度通知。
            // 外层再包一层 InvokeOnUpdate 会导致队列回调查到时连接状态已被较新的同步更新覆盖，
            // 造成状态回退（例如 Connected -> Disconnected -> Reconnecting -> Connected 循环）。
            switch (state)
            {
                case ReconnectionManager.ReconnectState.Reconnecting:
                    UpdateConnectionStatus(ConnectionStatus.Reconnecting);
                    break;
                case ReconnectionManager.ReconnectState.Connected:
                    // 空引用防护（spec 5.6.3 异常 4）：_client 在 ConnectAsync 失败路径（CleanupClient）后可能为 null，
                    // 直接访问 _client.Online 会抛 NullReferenceException 并被 StartReconnectAsync 误判为重连失败。
                    if (_client != null && _client.Online)
                        UpdateConnectionStatus(ConnectionStatus.Connected);
                    break;
                case ReconnectionManager.ReconnectState.Disconnected:
                    UpdateConnectionStatus(ConnectionStatus.Disconnected);
                    break;
                case ReconnectionManager.ReconnectState.Failed:
                    UpdateConnectionStatus(ConnectionStatus.Failed);
                    break;
            }
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_isDisposing) return;

            _isDisposing = true;

            try
            {
                // 停止心跳包发送
                _heartbeatManager?.StopHeartbeat();

                // 取消订阅重连管理器事件，然后停止并释放
                if (_reconnectionManager != null)
                {
                    _reconnectionManager.OnReconnected -= OnReconnectionSucceeded;
                    _reconnectionManager.OnReconnectFailed -= OnReconnectionFailed;
                    _reconnectionManager.OnStateChanged -= OnReconnectionStateChanged;
                    _reconnectionManager.OnDisconnected -= OnReconnectionManagerDisconnected;
                    _reconnectionManager.OnDisconnectTimeout -= OnDisconnectTimeout;
                    _reconnectionManager.CancelReconnect();
                    _reconnectionManager.Dispose();
                }

                // 取消网关状态检查
                if (_gatewayCheckCts != null)
                {
                    try
                    {
                        if (!_gatewayCheckCts.Token.IsCancellationRequested)
                        {
                            _gatewayCheckCts.Cancel();
                        }
                    }
                    catch (ObjectDisposedException) { }
                }

                // 取消连接令牌
                if (_connectionCts != null)
                {
                    try
                    {
                        if (!_connectionCts.IsCancellationRequested)
                        {
                            _connectionCts.Cancel();
                        }
                    }
                    catch (ObjectDisposedException) { }
                    _connectionCts.Dispose();
                }

                // 释放客户端资源
                if (_client != null)
                {
                    try
                    {
                        _client.Connected -= OnClientConnected;
                        _client.Closed -= OnClientDisconnected;
                        _client.Received -= OnDataReceived;
                        _client.Dispose();
                    }
                    catch (Exception ex)
                    {
                        EnhancedLogging.LogWarning($"[网络管理器] 释放客户端资源时发生错误: {ex.Message}");
                        EnhancedDiagnostics.LogException(ex, "释放客户端资源");
                    }
                }

                // 取消订阅网络状态变化事件
                if (_networkStateMonitor != null)
                {
                    try
                    {
                        _networkStateMonitor.NetworkStatusChanged -= OnNetworkStatusChanged;
                    }
                    catch (Exception ex)
                    {
                        EnhancedLogging.LogWarning($"[网络管理器] 取消订阅网络状态变化事件时发生错误: {ex.Message}");
                        EnhancedDiagnostics.LogException(ex, "取消订阅网络状态变化事件");
                    }
                    finally
                    {
                        // 只有在NetworkStateMonitor类型匹配时才调用Dispose
                        if (_networkStateMonitor is NetworkStateMonitor networkMonitor)
                        {
                            networkMonitor.CancellationTokenSource?.Cancel();
                            _networkStateMonitor.Dispose();
                        }
                    }
                }

                // 释放网关检查令牌
                if (_gatewayCheckCts != null)
                {
                    try
                    {
                        _gatewayCheckCts.Cancel();
                        _gatewayCheckCts.Dispose();
                    }
                    catch (ObjectDisposedException ex)
                    {
                        EnhancedDiagnostics.LogException(ex, "释放网关检查令牌");
                        // 忽略已释放的对象异常
                    }
                }

                // 释放心跳包管理器
                _heartbeatManager?.Dispose();

                // 取消订阅全局异常处理器，防止通过委托引用导致内存泄漏
                if (_unhandledExceptionHandler != null)
                {
                    AppDomain.CurrentDomain.UnhandledException -= _unhandledExceptionHandler;
                    _unhandledExceptionHandler = null;
                }

                // 清理自身事件委托，防止外部引用残留
                ConnectionStatusChanged = null;
                ConnectionError = null;
                AuthTokenExpired = null;
                DisconnectTimedOut = null;

                EnhancedDiagnostics.LogDiagnostic("网络管理器资源已释放");
            }
            catch (Exception ex)
            {
                EnhancedLogging.LogError($"[网络管理器] 释放资源时发生错误: {ex.Message}");
                EnhancedDiagnostics.LogException(ex, "释放资源");
            }
        }

        #endregion

        #region Diagnostic Methods

        /// <summary>
        /// 获取当前连接状态（用于诊断）
        /// </summary>
        /// <returns>当前连接状态</returns>
        public ConnectionStatus GetConnectionStatus()
        {
            return _connectionStatus;
        }

        /// <summary>
        /// 获取当前网关信息（用于诊断）
        /// </summary>
        /// <returns>当前网关信息</returns>
        public GatewayInfo GetCurrentGateway()
        {
            return _currentGateway;
        }

        /// <summary>
        /// 从已加载的网关列表中解析并设置当前网关。
        /// <para>
        /// 修复 BUG（登录建连报"无可用网关"）：ClientConnectionCoordinator.RequestConnectAsync 依赖
        /// <see cref="GetCurrentGateway"/> 返回的 _currentGateway，但该字段仅在 ConnectAsync 内部赋值，
        /// 首次建连时为 null → "无可用网关（Login），建连失败" → 等待 5s 超时 → 登录失败。
        /// 本方法在建连前从 <see cref="_gatewayList"/> 选择网关（优先 HorizonGame.ini 配置项，已在
        /// InitializeNetworkManager 中 Insert 到列表头部）并设置到 _currentGateway，
        /// 闭合"先有鸡还是先有蛋"缺口。重连场景下 _currentGateway 已存在，直接返回 true。
        /// </para>
        /// </summary>
        /// <returns>true 表示已解析到可用网关；false 表示网关列表为空。</returns>
        public bool TryResolveGateway()
        {
            if (_currentGateway != null)
                return true;

            if (_gatewayList == null || _gatewayList.Count == 0)
            {
                EnhancedLogging.LogWarning("[NetworkManager] TryResolveGateway: 网关列表为空，无法解析当前网关");
                return false;
            }

            // 优先使用列表首个网关（HorizonGame.ini 的网关在 InitializeNetworkManager 中已 Insert 到 index 0）。
            var source = _gatewayList[0];
            _currentGateway = new GatewayInfo
            {
                IP = source.IP,
                Port = source.Port,
                Region = source.Region,
                IsAvailable = true,
            };
            EnhancedLogging.LogInfo($"[NetworkManager] TryResolveGateway: 已解析当前网关 {_currentGateway.IP}:{_currentGateway.Port}");
            return true;
        }

        /// <summary>
        /// 手动触发重连（用户主动触发）。
        /// 修复（客户端无限发送连接请求）：手动触发时重置 _disconnectTimedOut，
        /// 允许重新发起连接。这是唯一能解除永久停止状态的路径之一
        /// （另一个是 ConnectOnDemandAsync）。
        /// </summary>
        public async Task<bool> ManualReconnectAsync()
        {
            try
            {
                EnhancedLogging.LogInfo("[手动重连] 用户手动触发重连，重置永久停止标记");
                EnhancedDiagnostics.LogDiagnostic("用户手动触发重连");

                // 重置永久停止标记，允许重新发起连接
                _reconnectionManager?.ResetDisconnectTimeout();

                return await CheckAndReconnectAsync();
            }
            catch (Exception ex)
            {
                EnhancedLogging.LogError($"[手动重连] 手动重连时发生错误: {ex.Message}");
                EnhancedDiagnostics.LogException(ex, "手动重连");
                return false;
            }
        }

        #endregion
    }
}
