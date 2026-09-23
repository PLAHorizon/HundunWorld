namespace Horizon.Game.Core.Interfaces;

/// <summary>
/// 协议版本匹配策略（阶段 0.6）：决定 <see cref="SyncProtocolVersion"/> 不匹配时
/// 握手是「拒绝」还是「告警放行」。
/// <para>
/// 默认实现 <see cref="ProtocolVersionPolicy"/> 为宽松模式（开发/过渡期：版本 &lt; MinimumSupported
/// 拒绝，版本 != Current 仅告警放行，对齐历史行为）；Gateway 侧按
/// <c>GatewayOptions.RequireStrictVersionMatch</c> 注册严格实现（生产：任何不匹配都拒绝）。
/// </para>
/// </summary>
public interface IProtocolVersionPolicy
{
    /// <summary>
    /// 判定客户端协议版本是否可接受。
    /// </summary>
    /// <param name="clientVersion">客户端上报的 <see cref="SyncProtocolVersion"/>。</param>
    /// <returns>true = 接受连接；false = 拒绝（调用方返回 <see cref="HandshakeRejectPacket"/>）。</returns>
    bool IsVersionAccepted(int clientVersion);

    /// <summary>
    /// 生成拒绝原因文案（仅当 <see cref="IsVersionAccepted"/> 返回 false 时使用）。
    /// </summary>
    string GetRejectionReason(int clientVersion);
}
