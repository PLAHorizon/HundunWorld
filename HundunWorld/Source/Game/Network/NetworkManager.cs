using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FlaxEngine;
using Game.Game.Network;
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

        // 同步握手重试状态：记录上次发送的参数与时间，用于丢包或响应未达时重发。
        private long _lastHandshakeSentTicks = 0;
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

            // 初始化重连管理器，提供连接函数
            _reconnectionManager = new ReconnectionManager(async () =>
            {
                if (_currentGateway != null)
                {
                    return await ConnectAsync(_currentGateway.IP, _currentGateway.Port);
                }
                return false;
            });

            // 订阅重连管理器事件
            _reconnectionManager.OnReconnected += OnReconnectionSucceeded;
            _reconnectionManager.OnReconnectFailed += OnReconnectionFailed;
            _reconnectionManager.OnStateChanged += OnReconnectionStateChanged;
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

            lock (_connectionLock)
            {
                if (_connectionStatus == ConnectionStatus.Connecting || _connectionStatus == ConnectionStatus.Connected)
                {
                    EnhancedLogging.LogInfo($"[ConnectAsync] 当前连接状态: {_connectionStatus}，跳过连接");
                    return _connectionStatus == ConnectionStatus.Connected;
                }
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
                var config = new TouchSocketConfig()
                    .SetRemoteIPHost($"{ip}:{port}")
                    .SetTcpDataHandlingAdapter(() => new HorizonMessageAdapter()) // 每次创建新的适配器实例
                    .ConfigurePlugins(plugin => plugin.UseReconnection<ITcpClient>());
                // simplified: no complex plugin configuration

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
                _reconnectionManager.CurrentState = _client.Online? ReconnectionManager.ReconnectState.Connected:ReconnectionManager.ReconnectState.Failed;
                return true;
            }
            catch (OperationCanceledException)
            {
                ConnectionError?.Invoke("连接被取消");
                UpdateConnectionStatus(ConnectionStatus.Disconnected);
                EnhancedDiagnostics.LogNetworkOperation("连接", $"{ip}:{port}", false, "连接被取消");
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
                return false;
            }
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
        public async Task DisconnectAsync()
        {
            if (_client != null && _client.Online)
            {
                EnhancedLogging.LogInfo("[DisconnectAsync] 开始断开连接");
                await _client.CloseAsync();
                EnhancedLogging.LogInfo("[DisconnectAsync] 连接已断开");
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
            EnhancedLogging.LogInfo("[OnClientConnected] 客户端连接成功");
            EnhancedDiagnostics.LogNetworkOperation("连接", $"{_currentGateway?.IP}:{_currentGateway?.Port}", true, "连接成功");
            Scripting.InvokeOnUpdate(() =>
           UpdateConnectionStatus(ConnectionStatus.Connected));

            _reconnectionManager?.MarkConnected();

            if (_heartbeatManager != null)
            {
                _heartbeatManager.StartHeartbeat();
            }
        }

        /// <summary>
        /// 客户端断开连接事件处理
        /// </summary>
        private async Task OnClientDisconnected(ITcpClient client, ClosedEventArgs e)
        {
            EnhancedLogging.LogInfo("[OnClientDisconnected] 客户端断开连接");
            EnhancedDiagnostics.LogNetworkOperation("断开连接", $"{_currentGateway?.IP}:{_currentGateway?.Port}", true, "连接断开");

            UpdateConnectionStatus(ConnectionStatus.Disconnected);

            // 停止心跳包发送
            if (_heartbeatManager != null)
            {
                _heartbeatManager.StopHeartbeat();
            }

            // 如果不是主动断开，使用重连管理器进行重连
            if (!_connectionCts.Token.IsCancellationRequested && !_isDisposing)
            {
                EnhancedLogging.LogInfo("[OnClientDisconnected] 检测到非主动断开，触发重连管理器");
                _reconnectionManager?.HandleDisconnect();
            }
        }

        /// <summary>
        /// 数据接收事件处理
        /// </summary>
        private async Task OnDataReceived(ITcpClient sender, ReceivedDataEventArgs e)
        {
            try
            {
                EnhancedLogging.LogInfo("[OnDataReceived] 开始处理接收到的数据");

                // 检查数据是否有效
                if (e == null)
                {
                    EnhancedLogging.LogWarning("[OnDataReceived] 接收到空的事件参数");
                    return;
                }

                try
                {
                    HorizonMessagePacket messagePacket = null;

                    // 检查是否有RequestInfo（通过自定义适配器解析的消息）
                    if (e.RequestInfo is HorizonMessageInfo horizonRequest)
                    {
                        EnhancedLogging.LogInfo("[OnDataReceived] 检测到RequestInfo，处理解析后的消息");
                        messagePacket = horizonRequest.Packet;
                    }
                    else
                    {
                        // 如果适配器未解析数据，尝试手动解析
                        EnhancedLogging.LogInfo("[OnDataReceived] RequestInfo未解析，尝试手动解析数据");
                        if (e.Memory.IsEmpty)
                        {
                            EnhancedLogging.LogWarning("[OnDataReceived] 接收到空数据");
                            return;
                        }

                        var dataArray = e.Memory.ToArray();
                        EnhancedLogging.LogInfo($"[OnDataReceived] 准备解包原始数据，数据长度: {dataArray.Length}");

                        // 创建临时适配器实例进行解包
                        var tempAdapter = new HorizonMessageAdapter();
                        messagePacket = tempAdapter.UnpackMessage(dataArray);
                    }

                    if (messagePacket != null)
                    {
                        EnhancedLogging.LogInfo($"[OnDataReceived] 成功获取消息: Type={messagePacket.Header.MessageType}, Service={messagePacket.ServiceType}");

                        // 验证消息头的必需字段
                        if (!messagePacket.Header.IsResponse)
                        {
                            if (messagePacket.Header.GameId <= 0)
                            {
                                EnhancedLogging.LogWarning($"[OnDataReceived] 收到无效请求消息: GameId必须为正数");
                                return;
                            }

                            if (messagePacket.Header.ServerId <= 0)
                            {
                                EnhancedLogging.LogWarning($"[OnDataReceived] 收到无效请求消息: ServerId必须为正数");
                                return;
                            }

                            if (messagePacket.Header.ZoneId <= 0)
                            {
                                EnhancedLogging.LogWarning($"[OnDataReceived] 收到无效请求消息: ZoneId必须为正数");
                                return;
                            }
                        }

                        await ProcessMessageAsync(sender, messagePacket);
                    }
                    else
                    {
                        EnhancedLogging.LogWarning("[OnDataReceived] 反序列化消息失败");
                    }
                }
                catch (ArgumentException argEx)
                {
                    EnhancedLogging.LogWarning($"[OnDataReceived] 消息验证失败: {argEx.Message}");
                    EnhancedDiagnostics.LogException(argEx, "消息验证");
                }
                catch (Exception deserializeEx)
                {
                    EnhancedLogging.LogError($"[OnDataReceived] 反序列化消息时发生错误: {deserializeEx.Message}");
                    EnhancedLogging.LogError($"[OnDataReceived] 异常堆栈: {deserializeEx.StackTrace}");
                    EnhancedDiagnostics.LogException(deserializeEx, "反序列化消息");
                }
            }
            catch (Exception ex)
            {
                ConnectionError?.Invoke($"解析消息失败: {ex.Message}");
                EnhancedDiagnostics.LogException(ex, "解析消息");
                EnhancedLogging.LogError($"[OnDataReceived] 处理数据时发生异常: {ex.Message}");
                EnhancedLogging.LogError($"[OnDataReceived] 异常堆栈: {ex.StackTrace}");
            }
            await Task.CompletedTask;
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
                    // 网络恢复时，检查是否需要重连
                    if (_connectionStatus == ConnectionStatus.Disconnected && _currentGateway != null)
                    {
                        RunBackground(async () =>
                        {
                            await Task.Delay(3000); // 等待3秒确保网络稳定
                            await CheckAndReconnectAsync();
                        });
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
        private async Task ProcessMessageAsync(ITcpClient sender, HorizonMessagePacket messagePacket)
        {
            if (messagePacket == null)
            {
                EnhancedLogging.LogWarning("[ProcessMessageAsync] 消息包为空");
                return;
            }

            EnhancedLogging.LogInfo($"[ProcessMessageAsync] 开始处理消息: {messagePacket.Header.MessageType}");

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
                // 使用消息适配器打包消息
                var packedData = _messageAdapter.PackMessage(message, ((INetworkMessage)message).Type, true);

                EnhancedLogging.LogInfo($"[SendAsync] 准备发送消息，数据长度: {packedData.Length} 字节");

                // 发送数据
                await _client.SendAsync(packedData);

                EnhancedLogging.LogInfo($"[SendAsync] 消息发送成功: {typeof(T).Name}");

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
            Interlocked.Exchange(ref _lastHandshakeSentTicks, DateTimeOffset.UtcNow.Ticks);

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
            if (elapsed > HandshakeTimeout)
            {
                FlaxEngine.Debug.LogError($"[NetworkManager] 同步握手超时 ({elapsed.TotalSeconds:F1}s)，停止重试。请检查服务端是否正常响应。");
                return false;
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
        public async Task SendSubscriptionUpdateAsync(ulong[] addedChunks, ulong[] removedChunks)
        {
            if (!CanSendMessage() || !IsSyncHandshakeComplete)
                return;

            if ((addedChunks == null || addedChunks.Length == 0) && (removedChunks == null || removedChunks.Length == 0))
                return;

            var packet = new SubscriptionUpdatePacket
            {
                AddedChunks = addedChunks ?? Array.Empty<ulong>(),
                RemovedChunks = removedChunks ?? Array.Empty<ulong>(),
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
            Debug.Log("[NetworkManager] 同步握手已确认完成");
        }

        /// <summary>
        /// 重置同步握手状态（断线重连时调用）
        /// </summary>
        public void ResetSyncHandshake()
        {
            _syncHandshakeComplete = false;
            // 跨线程保护：与 SendSyncHandshakeAsync 的写入并发，使用 Interlocked.Exchange 保证原子性。
            Interlocked.Exchange(ref _syncClientTick, 0);
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
            OnReconnectionStateChanged(ReconnectionManager.ReconnectState.Connected);
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
        /// 重连状态变化事件处理
        /// </summary>
        private void OnReconnectionStateChanged(ReconnectionManager.ReconnectState state)
        {
            EnhancedLogging.LogInfo($"[OnReconnectionStateChanged] 重连状态变化: {state}");

            // 根据重连状态更新连接状态
            // 注意：Connected 和 Disconnected 状态已在 OnClientConnected/OnClientDisconnected 中更新，
            // 此处仅处理 Reconnecting 和 Failed 状态，避免重复触发导致UI状态混乱
            switch (state)
            {
                case ReconnectionManager.ReconnectState.Reconnecting:
                    Scripting.InvokeOnUpdate(() => UpdateConnectionStatus(ConnectionStatus.Reconnecting));
                    break;
                case ReconnectionManager.ReconnectState.Connected:
                    Scripting.InvokeOnUpdate(() =>
                    {
                        if (_client.Online)
                            UpdateConnectionStatus(ConnectionStatus.Connected);
                    });
                    break;
                case ReconnectionManager.ReconnectState.Disconnected:
                    Scripting.InvokeOnUpdate(() =>
                    {
                        
                            UpdateConnectionStatus(ConnectionStatus.Disconnected);
                    });
                    break;
                case ReconnectionManager.ReconnectState.Failed:
                    Scripting.InvokeOnUpdate(() => UpdateConnectionStatus(ConnectionStatus.Failed));
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
        /// 手动触发重连
        /// </summary>
        public async Task<bool> ManualReconnectAsync()
        {
            try
            {
                EnhancedLogging.LogInfo("[手动重连] 用户手动触发重连");
                EnhancedDiagnostics.LogDiagnostic("用户手动触发重连");

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
