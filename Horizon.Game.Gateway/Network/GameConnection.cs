using Microsoft.Extensions.Logging;
using TouchSocket.Sockets;
using Horizon.Game.Gateway.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Network
{
    /// <summary>
    /// 游戏连接实现
    /// </summary>
    public class GameConnection : IGameConnection
    {
        private readonly ITcpSessionClient _client;
        private readonly ILogger _logger;
        private readonly Dictionary<string, object> _properties = new();

        /// <summary>
        /// 连接ID
        /// </summary>
        public string ConnectionId => _client.Id;

        /// <summary>
        /// 用户ID（登录后设置）
        /// </summary>
        public long? UserId { get; set; }

        /// <summary>
        /// 远程地址
        /// </summary>
        public string RemoteAddress => _client.GetIPPort() ?? "Unknown";

        /// <summary>
        /// 连接时间
        /// </summary>
        public DateTime ConnectedTime { get; }

        /// <summary>
        /// 最后活跃时间
        /// </summary>
        public DateTime LastActiveTime { get; set; }

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected => _client.Online;

        /// <summary>
        /// 是否已认证
        /// </summary>
        public bool IsAuthenticated { get; set; }

        /// <summary>
        /// 当前鉴权令牌（登录后设置，角色进入游戏后更新为含角色Id的令牌）
        /// </summary>
        public string AuthToken { get; set; } = "";

        /// <summary>
        /// 连接属性
        /// </summary>
        public Dictionary<string, object> Properties => _properties;

        /// <summary>
        /// 连接关闭事件
        /// </summary>
        public event EventHandler<ConnectionClosedEventArgs>? Closed;

        public GameConnection(ITcpSessionClient client, ILogger logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            ConnectedTime = DateTime.UtcNow;
            LastActiveTime = DateTime.UtcNow;

            // 监听客户端断开连接事件
            _client.Closed += OnClientDisconnected;
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        public async Task SendAsync(byte[] data)
        {
            try
            {
                if (!IsConnected)
                {
                    throw new InvalidOperationException("连接已断开");
                }

                await _client.SendAsync(data);
                LastActiveTime = DateTime.UtcNow;
                
                _logger.LogDebug("发送数据给客户端 {ConnectionId}: {DataLength} 字节", 
                    ConnectionId, data.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送数据失败: {ConnectionId}", ConnectionId);
                throw;
            }
        }

        /// <summary>
        /// 关闭连接
        /// </summary>
        public async Task CloseAsync(string reason = "")
        {
            try
            {
                if (IsConnected)
                {
                    await _client.CloseAsync(reason);
                    _logger.LogInformation("连接已关闭: {ConnectionId}, 原因: {Reason}", 
                        ConnectionId, reason);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "关闭连接时发生错误: {ConnectionId}", ConnectionId);
            }
        }

        /// <summary>
        /// 客户端断开连接事件处理
        /// </summary>
        private async Task OnClientDisconnected(ITcpSessionClient client, ClosedEventArgs e)
        {
            try
            {
                // 取消注册事件，避免重复触发
                _client.Closed -= OnClientDisconnected;
                
                // 触发连接关闭事件
                Closed?.Invoke(this, new ConnectionClosedEventArgs(ConnectionId, e.Message));
                
                _logger.LogDebug("客户端连接断开事件已处理: {ConnectionId}", ConnectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理客户端断开连接事件时发生错误: {ConnectionId}", ConnectionId);
            }
        }

        /// <summary>
        /// 设置连接属性
        /// </summary>
        public void SetProperty(string key, object value)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("属性键不能为空", nameof(key));

            _properties[key] = value;
            LastActiveTime = DateTime.UtcNow;
        }

        /// <summary>
        /// 获取连接属性
        /// </summary>
        public object? GetProperty(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("属性键不能为空", nameof(key));

            _properties.TryGetValue(key, out var value);
            return value;
        }

        /// <summary>
        /// 移除连接属性
        /// </summary>
        public bool RemoveProperty(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("属性键不能为空", nameof(key));

            return _properties.Remove(key);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _client.Closed -= OnClientDisconnected;
            _client?.Dispose();
        }

        public override string ToString()
        {
            return $"GameConnection[{ConnectionId}] User:{UserId} Remote:{RemoteAddress} " +
                   $"Connected:{ConnectedTime:HH:mm:ss} Active:{LastActiveTime:HH:mm:ss} " +
                   $"Auth:{IsAuthenticated}";
        }
    }
}
