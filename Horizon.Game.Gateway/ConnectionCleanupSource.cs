namespace Horizon.Game.Gateway;

/// <summary>
/// 连接清理来源枚举（连接精简治理，spec 4.4.1 / design.md 2.2.2.4）。
/// 承载连接生命周期清理来源，日志与治理统计共用同一枚举定义，
/// 后续新增来源类型只需扩展枚举成员。
/// </summary>
public enum ConnectionCleanupSource
{
    /// <summary>首包超时（幽灵连接）：连接建立后 N 秒未收到任何数据被判定为幽灵连接并清理。</summary>
    FirstPacketTimeout,

    /// <summary>空闲超时（闲置/离线连接）：收到数据后 N 秒无活动被判定离线并清理。</summary>
    IdleTimeout,

    /// <summary>发送损坏（MarkAsBroken）：发送数据遇致命异常（如 writer 已 completed）被标记损坏并清理。</summary>
    Corrupted,

    /// <summary>Closed 事件：TouchSocket 或 GameConnection 的 Closed 事件触发的连接清理。</summary>
    ClosedEvent,

    /// <summary>全局连接数上限：超出 <c>MaxConnections</c> 被拒绝的新连接。</summary>
    ConnectionLimit,

    /// <summary>每 IP 连接数上限：超出 <c>MaxConnectionsPerIp</c> 被拒绝的新连接。</summary>
    PerIpLimit,

    /// <summary>每用户连接数上限：同用户超出 <c>MaxConnectionsPerUser</c> 被拒绝的新连接。</summary>
    PerUserLimit,
}