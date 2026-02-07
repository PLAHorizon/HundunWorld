using System;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// 消息路由器接口
    /// </summary>
    public interface IMessageRouter
    {
        /// <summary>
        /// 启动消息路由器
        /// </summary>
        Task StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 停止消息路由器
        /// </summary>
        Task StopAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 路由消息
        /// </summary>
        /// <param name="message">消息</param>
        /// <param name="connection">来源连接</param>
        Task RouteMessageAsync(byte[] message, IGameConnection connection);

        /// <summary>
        /// 获取路由统计信息
        /// </summary>
        MessageRouterStatistics GetStatistics();
    }

    /// <summary>
    /// 负载均衡器接口
    /// </summary>
    public interface ILoadBalancer
    {
        /// <summary>
        /// 启动负载均衡器
        /// </summary>
        Task StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 停止负载均衡器
        /// </summary>
        Task StopAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 选择最佳Silo
        /// </summary>
        /// <param name="messageType">消息类型</param>
        string? SelectBestSilo(string messageType);

        /// <summary>
        /// 获取负载均衡统计信息
        /// </summary>
        LoadBalancerStatistics GetStatistics();
    }

    /// <summary>
    /// 会话管理器接口
    /// </summary>
    public interface ISessionManager
    {
        /// <summary>
        /// 启动会话管理器
        /// </summary>
        Task StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 停止会话管理器
        /// </summary>
        Task StopAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 创建会话
        /// </summary>
        /// <param name="connection">连接</param>
        /// <param name="userId">用户ID</param>
        Task<IGameSession> CreateSessionAsync(IGameConnection connection, long userId);

        /// <summary>
        /// 获取会话
        /// </summary>
        /// <param name="sessionId">会话ID</param>
        IGameSession? GetSession(string sessionId);

        /// <summary>
        /// 根据用户ID获取会话
        /// </summary>
        /// <param name="userId">用户ID</param>
        IGameSession? GetSessionByUserId(long userId);

        /// <summary>
        /// 移除会话
        /// </summary>
        /// <param name="sessionId">会话ID</param>
        Task RemoveSessionAsync(string sessionId);

        /// <summary>
        /// 获取会话统计信息
        /// </summary>
        SessionManagerStatistics GetStatistics();
    }

    /// <summary>
    /// 游戏会话接口
    /// </summary>
    public interface IGameSession
    {
        /// <summary>
        /// 会话ID
        /// </summary>
        string SessionId { get; }

        /// <summary>
        /// 用户ID
        /// </summary>
        long UserId { get; }

        /// <summary>
        /// 连接
        /// </summary>
        IGameConnection Connection { get; }

        /// <summary>
        /// 创建时间
        /// </summary>
        DateTime CreatedTime { get; }

        /// <summary>
        /// 最后活跃时间
        /// </summary>
        DateTime LastActiveTime { get; set; }

        /// <summary>
        /// 是否已认证
        /// </summary>
        bool IsAuthenticated { get; set; }

        /// <summary>
        /// 会话数据
        /// </summary>
        System.Collections.Generic.Dictionary<string, object> Data { get; }

        /// <summary>
        /// 发送消息
        /// </summary>
        /// <param name="message">消息</param>
        Task SendMessageAsync(byte[] message);

        /// <summary>
        /// 关闭会话
        /// </summary>
        /// <param name="reason">关闭原因</param>
        Task CloseAsync(string reason = "");
    }

    /// <summary>
    /// 消息路由器统计信息
    /// </summary>
    public class MessageRouterStatistics
    {
        /// <summary>
        /// 每秒消息数
        /// </summary>
        public long MessagesPerSecond { get; set; }

        /// <summary>
        /// 总消息数
        /// </summary>
        public long TotalMessages { get; set; }

        /// <summary>
        /// 路由错误数
        /// </summary>
        public long RoutingErrors { get; set; }

        /// <summary>
        /// 平均响应时间（毫秒）
        /// </summary>
        public double AverageResponseTime { get; set; }

        /// <summary>
        /// 错误率
        /// </summary>
        public double ErrorRate { get; set; }
    }

    /// <summary>
    /// 负载均衡器统计信息
    /// </summary>
    public class LoadBalancerStatistics
    {
        /// <summary>
        /// 活跃Silo数量
        /// </summary>
        public int ActiveSilos { get; set; }

        /// <summary>
        /// 总请求数
        /// </summary>
        public long TotalRequests { get; set; }

        /// <summary>
        /// 平均负载
        /// </summary>
        public double AverageLoad { get; set; }

        /// <summary>
        /// 负载均衡错误数
        /// </summary>
        public long BalancingErrors { get; set; }
    }

    /// <summary>
    /// 会话管理器统计信息
    /// </summary>
    public class SessionManagerStatistics
    {
        /// <summary>
        /// 活跃会话数
        /// </summary>
        public int ActiveSessions { get; set; }

        /// <summary>
        /// 总会话数
        /// </summary>
        public long TotalSessions { get; set; }

        /// <summary>
        /// 已认证会话数
        /// </summary>
        public int AuthenticatedSessions { get; set; }

        /// <summary>
        /// 平均会话时长（秒）
        /// </summary>
        public double AverageSessionDuration { get; set; }

        /// <summary>
        /// 会话错误数
        /// </summary>
        public long SessionErrors { get; set; }
    }
}
