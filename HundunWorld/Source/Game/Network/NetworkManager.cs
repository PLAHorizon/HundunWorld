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
        private readonly CancellationTokenSource _connectionCts;
        private readonly CancellationTokenSource _gatewayCheckCts;
        private readonly object _connectionLock = new object();
        private readonly object _sendLock = new object();
        private readonly MessageProcessor _messageProcessor;
        private readonly HorizonMessageAdapter _messageAdapter;
        private ConnectionStatus _connectionStatus = ConnectionStatus.Disconnected;
        private GatewayInfo _currentGateway;
        private volatile bool _isInitialized = false;
        private volatile bool _isDisposing = false;
        List<GatewayInfo> _gatewayList = new List<GatewayInfo>();
        public ConnectionStatus ConnectionStatus => _connectionStatus;

        public event Action<ConnectionStatus> ConnectionStatusChanged;
        public event Action<string> ConnectionError;

        #endregion

        #region Constructor and Initialization

        public NetworkManager(List<GatewayInfo> gatewayList, NetworkStateMonitor networkStateMonitor = null)
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
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
            if (_isDisposing)
            {
                EnhancedLogging.LogWarning("网络管理器正在释放，无法连接");
                return false;
            }

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

            UpdateConnectionStatus(ConnectionStatus.Connected);
            
            // 通知重连管理器连接成功
            _reconnectionManager?.MarkConnected();

            // 开始发送心跳包
            if (_heartbeatManager != null)
            {
                _heartbeatManager.StartHeartbeat();
            }

            // 发送心跳消息来测试连接
            var heartbeatMessage = new HeartbeatMessage
            {
                Timestamp = DateTime.UtcNow.Ticks,
                ClientTime = DateTime.UtcNow.Ticks
            };

            // 发送心跳消息测试连接
            RunBackground(async () =>
            {
                await Task.Delay(1000); // 延迟1秒发送心跳
                await SendAsync(heartbeatMessage);
            });
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
                        if (messagePacket.Header.GameId <= 0)
                        {
                            EnhancedLogging.LogWarning($"[OnDataReceived] 收到无效消息: GameId必须为正数");
                            return;
                        }

                        if (messagePacket.Header.ServerId <= 0)
                        {
                            EnhancedLogging.LogWarning($"[OnDataReceived] 收到无效消息: ServerId必须为正数");
                            return;
                        }

                        if (messagePacket.Header.ZoneId <= 0)
                        {
                            EnhancedLogging.LogWarning($"[OnDataReceived] 收到无效消息: ZoneId必须为正数");
                            return;
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
        /// <param name="assemblies">要扫描的程序集，如果为空则扫描当前程序集</param>
        public void AddAllMessageHandlers()
        {
            // 扫描所有已加载的程序集
            var handlerTypes = Assembly.GetExecutingAssembly()
                  .GetTypes()
                  .Where(type => type.IsClass && !type.IsAbstract && typeof(IMessageHandler).IsAssignableFrom(type))
                  .ToList();

            foreach (var handlerType in handlerTypes)
            {
                var handlerInstance = Activator.CreateInstance(handlerType) as IMessageHandler;
                foreach (var item in handlerInstance.MessageTypes)
                {
                    _messageProcessor.RegisterHandler(item, handlerInstance);
                    Debug.Log($"  - {item.GetDescription()}");
                }
            }


            // 输出调试信息
            Debug.Log($"[DEBUG] 自动注册了 {handlerTypes.Count} 个消息处理器:");
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
                        await HandleErrorMessageAsync(sender, (ErrorMessage)messagePacket.Body);
                        break;
                    default:
                        await _messageProcessor.ProcessMessageAsync(messagePacket);
                        EnhancedLogging.LogInfo($"[ProcessMessageAsync] 收到未处理的消息类型: {messagePacket.Header.MessageType}");
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
        private async Task<bool> HandleErrorMessageAsync(ITcpClient sender, ErrorMessage message)
        {
            EnhancedLogging.LogWarning($"[HandleErrorMessageAsync] 收到错误消息: {message.Message}");

            // 通知错误
            ConnectionError?.Invoke(message.Message);

            return true;
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

            return await SendAsync(messagePacket.Body);
        }

        /// <summary>
        /// 检查是否可以发送消息
        /// </summary>
        public bool CanSendMessage()
        {
            return _client != null && _client.Online && _connectionStatus == ConnectionStatus.Connected;
        }

        #endregion

        #region Status Management

        /// <summary>
        /// 更新连接状态
        /// </summary>
        private void UpdateConnectionStatus(ConnectionStatus status)
        {
            var oldStatus = _connectionStatus;
            _connectionStatus = status;

            EnhancedLogging.LogInfo($"[UpdateConnectionStatus] 连接状态从 {oldStatus} 更新为 {status}");
            EnhancedDiagnostics.LogDiagnostic($"连接状态从 {oldStatus} 更新为 {status}");

            try
            {
                ConnectionStatusChanged?.Invoke(status);
            }
            catch (Exception ex)
            {
                EnhancedLogging.LogError($"[UpdateConnectionStatus] 触发连接状态变化事件时发生错误: {ex.Message}");
                EnhancedDiagnostics.LogException(ex, "触发连接状态变化事件");
            }
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
            while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
            {
                if (_connectionStatus == ConnectionStatus.Connected)
                {
                    EnhancedLogging.LogInfo("[WaitForConnectionAsync] 连接已建立");
                    return true;
                }

                await Task.Delay(100);
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
            switch (state)
            {
                case ReconnectionManager.ReconnectState.Reconnecting:
                    UpdateConnectionStatus(ConnectionStatus.Connecting);
                    break;
                case ReconnectionManager.ReconnectState.Connected:
                    // 连接状态已在OnClientConnected中更新
                    break;
                case ReconnectionManager.ReconnectState.Disconnected:
                case ReconnectionManager.ReconnectState.Failed:
                    UpdateConnectionStatus(ConnectionStatus.Disconnected);
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
                
                // 停止重连管理器
                _reconnectionManager?.CancelReconnect();
                _reconnectionManager?.Dispose();

                // 取消网关状态检查
                if (_gatewayCheckCts != null && !_gatewayCheckCts.Token.IsCancellationRequested)
                {
                    _gatewayCheckCts.Cancel();
                }

                // 取消连接令牌
                if (_connectionCts != null)
                {
                    if (!_connectionCts.IsCancellationRequested)
                    {
                        _connectionCts.Cancel();
                    }
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
