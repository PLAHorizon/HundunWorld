using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 网关连接器，负责与游戏网关建立和维护连接
    /// </summary>
    public class GatewayConnector : IDisposable
    {
        private readonly NetworkManager _networkManager;
        private string _gatewayIp;
        private int _gatewayPort;
        private bool _disposed;

        /// <summary>
        /// 连接状态变化事件
        /// </summary>
        public event Action<ConnectionStatus> ConnectionStatusChanged;

        /// <summary>
        /// 收到消息事件
        /// </summary>
        public event Action<HorizonMessagePacket> MessageReceived;

        /// <summary>
        /// 连接错误事件
        /// </summary>
        public event Action<string> ConnectionError;

        public GatewayConnector(NetworkManager network,List<GatewayInfo> gatewayList = null)
        {
            _networkManager = network;
            
            // 订阅网络管理器事件
            _networkManager.ConnectionStatusChanged += OnConnectionStatusChanged;
            _networkManager.ConnectionError += OnConnectionError;
           // _networkManager.MessageReceived += OnMessageReceived;
        }

        /// <summary>
        /// 连接到游戏网关
        /// </summary>
        /// <param name="ip">网关IP地址</param>
        /// <param name="port">网关端口</param>
        public async Task<bool> ConnectToGatewayAsync(string ip, int port)
        {
            _gatewayIp = ip;
            _gatewayPort = port;

            FlaxEngine.Debug.Log($"[INFO] [连接尝试] 尝试连接到网关 {_gatewayIp}:{_gatewayPort}");
            return await _networkManager.ConnectAsync(ip, port);
        }

        /// <summary>
        /// 断开与网关的连接
        /// </summary>
        public async Task DisconnectFromGatewayAsync()
        {
            FlaxEngine.Debug.Log("[INFO] [断开连接] 主动断开与网关的连接");
            await _networkManager.DisconnectAsync();
        }

        /// <summary>
        /// 发送消息到网关
        /// </summary>
        /// <param name="message">消息包</param>
        public async Task<bool> SendMessageToGatewayAsync<T>(T message) where T : MessageUnion, INetworkMessage
        {
            // 将消息包装成 HorizonMessagePacket
            var packet = new HorizonMessagePacket
            {
                Header = new MessageHeader
                {
                    MessageId = Guid.NewGuid().ToString(),
                    MessageType = message.Type,
                    ServiceType = ServiceType.Game
                },
                Body = message
            };
            
            return await _networkManager.SendMessageAsync(packet);
        }

        /// <summary>
        /// 处理连接状态变化
        /// </summary>
        private void OnConnectionStatusChanged(ConnectionStatus status)
        {
            ConnectionStatusChanged?.Invoke(status);
        }

        /// <summary>
        /// 处理收到的消息
        /// </summary>
        private void OnMessageReceived(HorizonMessagePacket message)
        {
            MessageReceived?.Invoke(message);
        }

        /// <summary>
        /// 处理连接错误
        /// </summary>
        private void OnConnectionError(string error)
        {
            ConnectionError?.Invoke(error);
            FlaxEngine.Debug.LogWarning($"[WARN] [连接错误] {error}");
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // 取消订阅事件
            if (_networkManager != null)
            {
                _networkManager.ConnectionStatusChanged -= OnConnectionStatusChanged;
                _networkManager.ConnectionError -= OnConnectionError;
            }

            // 清理自身事件委托，防止外部引用残留
            ConnectionStatusChanged = null;
            MessageReceived = null;
            ConnectionError = null;
        }
    }
}
