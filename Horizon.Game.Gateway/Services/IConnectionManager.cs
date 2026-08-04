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
        /// 根据角色ID获取连接（fanout 推送使用 characterId 作为 sessionId）。
        /// </summary>
        /// <param name="characterId">角色ID</param>
        IGameConnection? GetConnectionByCharacterId(long characterId);

        /// <summary>
        /// 注册角色ID与连接的映射（角色进入游戏成功后调用）。
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <param name="connection">游戏连接</param>
        void RegisterCharacter(long characterId, IGameConnection connection);

        /// <summary>
        /// 注销角色ID与连接的映射（连接断开或切换角色时调用）。
        /// </summary>
        /// <param name="characterId">角色ID</param>
        void UnregisterCharacter(long characterId);

        /// <summary>
        /// 根据连接ID反查该连接绑定的所有角色ID。
        /// 用于客户端断连时获取需要延迟 Despawn 的角色列表。
        /// </summary>
        /// <param name="connectionId">连接ID</param>
        /// <returns>该连接绑定的所有角色ID（通常为 0 或 1 个）。</returns>
        IReadOnlyList<long> GetCharacterIdsByConnection(string connectionId);

        /// <summary>
        /// 获取所有已注册的 characterId（用于实体租约续约）。
        /// </summary>
        /// <returns>当前所有在线角色的 characterId 列表。</returns>
        IReadOnlyList<long> GetAllCharacterIds();

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

        /// <summary>
        /// [连接精简治理 spec 5.1.3] 尝试获取连接槽位（三级约束：全局上限 → 每 IP 上限 → 每用户上限）。
        /// </summary>
        /// <param name="connection">待注册的连接对象。</param>
        /// <param name="clientIp">客户端 IP（聚合计数依据）。</param>
        /// <param name="userId">用户 ID（可为 null，未登录时不参与每用户约束）。</param>
        /// <returns>null = 接受连接；非 null = 拒绝原因（含明确提示，如"每IP连接数超限"），调用方应在关闭连接前向客户端发送该提示。</returns>
        /// <remarks>
        /// 三级校验顺序：全局 <c>MaxConnections</c> → 每 IP <c>MaxConnectionsPerIp</c> → 每用户 <c>MaxConnectionsPerUser</c>。
        /// 同用户已有活跃连接时拒绝新连接并保留旧连接（spec 2.1.3.3）。
        /// </remarks>
        Task<string?> TryAcquireConnectionSlotAsync(IGameConnection connection, string clientIp, long? userId);

        /// <summary>[连接精简治理 spec 5.1.3] 获取指定 IP 的当前活跃连接数。</summary>
        /// <param name="clientIp">客户端 IP。</param>
        int GetActiveConnectionCountByIp(string clientIp);

        /// <summary>[连接精简治理 spec 5.1.3] 获取指定用户的当前活跃连接数。</summary>
        /// <param name="userId">用户 ID。</param>
        int GetActiveConnectionCountByUser(long userId);

        /// <summary>[连接精简治理 spec 6.3] 按清理来源累加治理统计（由清理/拒绝路径调用）。</summary>
        /// <param name="source">连接清理来源枚举（首包超时/空闲超时/损坏/Closed 事件/连接数上限）。</param>
        void RecordCleanup(ConnectionCleanupSource source);
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
        /// 当前鉴权令牌（登录后设置，角色进入游戏后更新为含角色Id的令牌）
        /// </summary>
        string AuthToken { get; set; }

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

        // ── [连接精简治理 spec 6.3] 治理统计字段 ──

        /// <summary>
        /// 幽灵连接清理次数：首包超时（<c>ConnectionCleanupSource.FirstPacketTimeout</c>）清理的连接累计数。
        /// 累加时机：<c>CleanupConnectionAsync</c> 按来源枚举分支执行完成后（幂等保护下只累加一次）。
        /// </summary>
        public long GhostConnectionCleanupCount { get; set; }

        /// <summary>
        /// 损坏连接次数：连接被标记为损坏（<c>ConnectionCleanupSource.Corrupted</c>）的累计数。
        /// 累加时机：<c>GameConnection.MarkAsBroken</c> 或 Closed 事件映射 Corrupted 清理路径。
        /// </summary>
        public long CorruptedConnectionCount { get; set; }

        /// <summary>
        /// 重复/超限连接拒绝次数：因全局上限/每 IP 上限/每用户上限（<c>ConnectionLimit</c>/<c>PerIpLimit</c>/<c>PerUserLimit</c>）
        /// 被拒绝的新连接累计数。
        /// </summary>
        public long DuplicateConnectionRejectedCount { get; set; }

        /// <summary>
        /// 当前未绑定角色连接数：连接已注册但 <c>UserId</c> 为 null（未完成认证/角色绑定）的活跃连接数。
        /// 维护时机：连接注册且 UserId 为 null 时递增，绑定角色或清理时递减。
        /// </summary>
        public int UnboundConnectionCount { get; set; }
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
