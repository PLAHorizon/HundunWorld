using Horizon.Game.Core.Interfaces;
using Horizon.Game.Message.Sync;

namespace Horizon.Game.Core.Handlers;

/// <summary>
/// 协议版本匹配策略——宽松模式（默认，阶段 0.6）。
/// <para>
/// 行为对齐历史语义（开发/过渡期）：
/// <list type="bullet">
/// <item>clientVersion &lt; <see cref="SyncProtocolVersion.MinimumSupported"/> → 拒绝（强制更新）；</item>
/// <item>clientVersion != <see cref="SyncProtocolVersion.Current"/> → 告警放行（过渡期软校验）；</item>
/// <item>其余 → 接受。</item>
/// </list>
/// 生产环境需拒绝任何不匹配时，用 Gateway 侧严格实现（按
/// <c>GatewayOptions.RequireStrictVersionMatch=true</c> 注册，见 Program.cs）。
/// </para>
/// </summary>
public sealed class ProtocolVersionPolicy : IProtocolVersionPolicy
{
    public static readonly ProtocolVersionPolicy Instance = new();

    /// <inheritdoc />
    public bool IsVersionAccepted(int clientVersion)
    {
        // 版本低于最低支持 → 拒绝（历史语义：强制更新）。
        return clientVersion >= SyncProtocolVersion.MinimumSupported;
    }

    /// <inheritdoc />
    public string GetRejectionReason(int clientVersion)
    {
        return $"协议版本过低（客户端 v{clientVersion}，服务器最低支持 v{SyncProtocolVersion.MinimumSupported}），请更新客户端。";
    }
}

/// <summary>
/// 协议版本匹配策略——严格模式（阶段 0.6，生产）。
/// <para>
/// 任何与 <see cref="SyncProtocolVersion.Current"/> 的不匹配（包括高于或低于但不低于最低支持的过渡期版本）
/// 都拒绝连接——协议字段布局/语义变更时旧客户端强行连接会导致字段错位/行为异常，
/// 生产环境应强制版本一致而非依赖告警。
/// </para>
/// </summary>
public sealed class StrictProtocolVersionPolicy : IProtocolVersionPolicy
{
    public static readonly StrictProtocolVersionPolicy Instance = new();

    /// <inheritdoc />
    public bool IsVersionAccepted(int clientVersion)
    {
        return clientVersion == SyncProtocolVersion.Current;
    }

    /// <inheritdoc />
    public string GetRejectionReason(int clientVersion)
    {
        return $"协议版本不匹配（客户端 v{clientVersion}，服务器要求 v{SyncProtocolVersion.Current}），请更新客户端。";
    }
}
