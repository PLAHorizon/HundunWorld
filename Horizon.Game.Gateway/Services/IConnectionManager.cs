using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// 连接管理器接口
    /// </summary>
    public interface IConnectionManager
    {
        /// <summary>
        /// 添加连接
        /// </summary>
        /// <param name="connection">连接对象</param>
        Task<bool> AddConnectionAsync(IGameConnection connection);

        /// <summary>
        /// 移除连接
        /// </summary>
        /// <param name="connectionId">连接ID</param>
        Task<bool> RemoveConnectionAsync(string connectionId);

        /// <summary>
        /// 获取连接
        /// </summary>
        /// <param name="connectionId">连接ID</param>
        IGameConnection? GetConnection(string connectionId);

        /// <summary>
        /// 获取所有连接
        /// </summary>
        IEnumerable<IGameConnection> GetAllConnections();

        /// <summary>
        /// 根据用户ID获取连接
        /// </summary>
        /// <param name="userId">用户ID</param>
        IGameConnection? GetConnectionByUserId(long userId);

        /// <summary>
        /// 广播消息给所有连接
        /// </summary>
        /// <param name="message">消息</param>
        Task BroadcastAsync(byte[] message);

        /// <summary>
        /// 按条件筛选并广播消息
        /// </summary>
        /// <param name="message">消息数据</param>
        /// <param name="predicate">筛选条件</param>
        Task BroadcastAsync(byte[] message, Func<IGameConnection, bool> predicate);

        /// <summary>
        /// 向指定用户组广播消息
        /// </summary>
        /// <param name="message">消息数据</param>
        /// <param name="userIds">用户ID列表</param>
        Task BroadcastToUserGroupAsync(byte[] message, IEnumerable<long> userIds);

        /// <summary>
        /// 根据连接属性筛选并广播消息
        /// </summary>
        /// <param name="message">消息数据</param>
        /// <param name="propertyFilter">属性筛选条件</param>
        Task BroadcastByPropertyAsync(byte[] message, Func<Dictionary<string, object>, bool> propertyFilter);

        /// <summary>
        /// 发送消息给指定连接
        /// </summary>
        /// <param name="connectionId">连接ID</param>
        /// <param name="message">消息</param>
        Task SendToConnectionAsync(string connectionId, byte[] message);

        /// <summary>
        /// 发送消息给指定用户
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="message">消息</param>
        Task SendToUserAsync(long userId, byte[] message);

        /// <summary>
        /// 获取连接统计信息
        /// </summary>
        ConnectionManagerStatistics GetStatistics();

        /// <summary>
        /// 获取网络统计信息
        /// </summary>
        NetworkStatistics GetNetworkStatistics();

        /// <summary>
        /// 清理超时连接
        /// </summary>
        Task CleanupTimeoutConnectionsAsync();
    }

    /// <summary>
    /// 游戏连接接口
    /// </summary>
    public interface IGameConnection
    {
        /// <summary>
        /// 连接ID
        /// </summary>
        string ConnectionId { get; }

        /// <summary>
        /// 用户ID（登录后设置）
        /// </summary>
        long? UserId { get; set; }

        /// <summary>
        /// 远程地址
        /// </summary>
        string RemoteAddress { get; }

        /// <summary>
        /// 连接时间
        /// </summary>
        DateTime ConnectedTime { get; }

        /// <summary>
        /// 最后活跃时间
        /// </summary>
        DateTime LastActiveTime { get; set; }

        /// <summary>
        /// 是否已连接
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 是否已认证
        /// </summary>
        bool IsAuthenticated { get; set; }

        /// <summary>
        /// 连接属性
        /// </summary>
        Dictionary<string, object> Properties { get; }

        /// <summary>
        /// 设置连接属性
        /// </summary>
        /// <param name="key">属性键</param>
        /// <param name="value">属性值</param>
        void SetProperty(string key, object value);

        /// <summary>
        /// 获取连接属性
        /// </summary>
        /// <param name="key">属性键</param>
        /// <returns>属性值</returns>
        object? GetProperty(string key);

        /// <summary>
        /// 移除连接属性
        /// </summary>
        /// <param name="key">属性键</param>
        bool RemoveProperty(string key);

        /// <summary>
        /// 发送数据
        /// </summary>
        /// <param name="data">数据</param>
        Task SendAsync(byte[] data);

        /// <summary>
        /// 关闭连接
        /// </summary>
        /// <param name="reason">关闭原因</param>
        Task CloseAsync(string reason = "");

        /// <summary>
        /// 连接关闭事件
        /// </summary>
        event EventHandler<ConnectionClosedEventArgs>? Closed;
    }

    /// <summary>
    /// 连接管理器统计信息
    /// </summary>
    public class ConnectionManagerStatistics
    {
        /// <summary>
        /// 活跃连接数
        /// </summary>
        public int ActiveConnections { get; set; }

        /// <summary>
        /// 总连接数
        /// </summary>
        public long TotalConnections { get; set; }

        /// <summary>
        /// 总断开连接数
        /// </summary>
        public long TotalDisconnections { get; set; }

        /// <summary>
        /// 错误连接数
        /// </summary>
        public long ErrorConnections { get; set; }

        /// <summary>
        /// 已认证连接数
        /// </summary>
        public int AuthenticatedConnections { get; set; }

        /// <summary>
        /// 峰值连接数
        /// </summary>
        public int PeakConnections { get; set; }

        /// <summary>
        /// 平均连接时长（秒）
        /// </summary>
        public double AverageConnectionDuration { get; set; }
    }

    /// <summary>
    /// 网络统计信息
    /// </summary>
    public class NetworkStatistics
    {
        /// <summary>
        /// 接收字节数
        /// </summary>
        public long BytesReceived { get; set; }

        /// <summary>
        /// 发送字节数
        /// </summary>
        public long BytesSent { get; set; }

        /// <summary>
        /// 接收消息数
        /// </summary>
        public long MessagesReceived { get; set; }

        /// <summary>
        /// 发送消息数
        /// </summary>
        public long MessagesSent { get; set; }

        /// <summary>
        /// 平均延迟（毫秒）
        /// </summary>
        public double AverageLatency { get; set; }

        /// <summary>
        /// 错误数
        /// </summary>
        public long Errors { get; set; }
    }

    /// <summary>
    /// 连接关闭事件参数
    /// </summary>
    public class ConnectionClosedEventArgs : EventArgs
    {
        /// <summary>
        /// 连接ID
        /// </summary>
        public string ConnectionId { get; }

        /// <summary>
        /// 关闭原因
        /// </summary>
        public string Reason { get; }

        /// <summary>
        /// 关闭时间
        /// </summary>
        public DateTime ClosedTime { get; }

        public ConnectionClosedEventArgs(string connectionId, string reason)
        {
            ConnectionId = connectionId;
            Reason = reason;
            ClosedTime = DateTime.UtcNow;
        }
    }
}
