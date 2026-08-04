namespace Horizon.Game.Gateway.Configuration;

/// <summary>
/// 服务端连接治理配置（连接精简治理，spec 5.1.3 / 4.4.4）。
/// </summary>
/// <remarks>
/// <para>
/// 用于服务端防滥用治理：首包超时清理、空闲超时清理、全局/每 IP/每用户连接数约束、
/// 重复连接检测与清理宽限期。全部参数可配置，非法值由 <see cref="ConnectionGovernanceOptionsValidator"/> 兜底回退默认。
/// </para>
/// <para>
/// 对齐关系：
/// </para>
/// <list type="bullet">
///   <item><see cref="FirstPacketTimeoutSeconds"/> 与既有 <c>NetworkOptions.FirstPacketTimeoutSeconds</c>（默认 5）对齐。</item>
///   <item><see cref="IdleTimeoutSeconds"/> 与既有 <c>NetworkOptions.IdleTimeoutSeconds</c>（默认 30）对齐。</item>
///   <item><see cref="MaxConnections"/> 与既有 <c>GatewayOptions.MaxConnections</c>（默认 10000）对齐。</item>
///   <item><see cref="DespawnGracePeriodSeconds"/> 与 <c>PlayerDespawnScheduler</c> 断线宽限期（默认 15）对齐。</item>
/// </list>
/// <para>
/// 合法区间：各值 &gt; 0；<see cref="MaxConnectionsPerIp"/> ≥ <see cref="MaxConnectionsPerUser"/>。
/// </para>
/// </remarks>
public sealed class ConnectionGovernanceOptions
{
    /// <summary>首包超时判定秒数（建立连接后 N 秒未收到任何数据判定为幽灵连接），默认 5，合法区间 (0, +∞)。</summary>
    public int FirstPacketTimeoutSeconds { get; set; } = 5;

    /// <summary>空闲超时秒数（收到数据后 N 秒无活动判定离线），默认 30，合法区间 (0, +∞)。</summary>
    public int IdleTimeoutSeconds { get; set; } = 30;

    /// <summary>全局最大连接数上限，默认 10000，合法区间 (0, +∞)。</summary>
    public int MaxConnections { get; set; } = 10000;

    /// <summary>每 IP 最大连接数（重复连接防护），默认 4，合法区间 &gt; 0 且 ≥ <see cref="MaxConnectionsPerUser"/>。</summary>
    public int MaxConnectionsPerIp { get; set; } = 4;

    /// <summary>每用户最大连接数（一用户一连接），默认 1，合法区间 &gt; 0 且 ≤ <see cref="MaxConnectionsPerIp"/>。</summary>
    public int MaxConnectionsPerUser { get; set; } = 1;

    /// <summary>清理宽限期秒数（断开清理时角色 Despawn 延迟），默认 15，合法区间 (0, +∞)。</summary>
    public int DespawnGracePeriodSeconds { get; set; } = 15;
}