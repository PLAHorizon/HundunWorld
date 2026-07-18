using System;

namespace Horizon.Game.Message.Sync;

/// <summary>
/// 角色同步频率策略（Task B.4）。
/// 定义各同步类型的下发频率与触发策略，供 <c>ZoneShardGrain</c> 在快照生成时按策略裁剪字段，
/// 平衡带宽占用与表现一致性。
/// </summary>
/// <remarks>
/// 频率策略总览：
/// <list type="bullet">
///   <item><term>位置 (Transform)</term><description>20Hz（每 50ms）每 tick 下发，保证位置插值平滑。</description></item>
///   <item><term>移动状态 (MovementState)</term><description>10Hz 心跳 + 变化触发：移动模式/落地标志变化时立即下发，否则每 100ms 心跳一次。</description></item>
///   <item><term>动画状态 (AnimationState)</term><description>纯事件驱动：仅 Montage 触发/结束事件时下发，循环动画由客户端根据 MovementState 自行驱动。</description></item>
///   <item><term>属性 (EntityState 扩展字段)</term><description>1Hz 心跳 + 变化触发：Mana/Level/Exp/Stamina 等属性变化时立即下发，否则每秒强制下发一次完整属性保证一致性。</description></item>
/// </list>
/// 数值依据：
/// <list type="bullet">
///   <item>20Hz 位置 = 50ms 间隔，匹配客户端 100ms 插值延迟窗口，留 50ms 抖动余量。</item>
///   <item>10Hz 移动状态 = 100ms 间隔，足够驱动动画状态机过渡（Idle↔Run 混合时间通常 200ms）。</item>
///   <item>1Hz 属性心跳 = 1s 间隔，属性变化不频繁但需保证最终一致性（断线重连后 1s 内自愈）。</item>
/// </list>
/// </remarks>
public static class CharacterSyncConfig
{
    /// <summary>位置快照频率（Hz）：每秒 20 次，对应 50ms 间隔。</summary>
    public const int PositionSnapshotHz = 20;

    /// <summary>移动状态心跳频率（Hz）：每秒 10 次，对应 100ms 间隔。变化时立即触发额外下发。</summary>
    public const int MovementStateHeartbeatHz = 10;

    /// <summary>属性心跳频率（Hz）：每秒 1 次，对应 1s 间隔。属性变化时立即触发额外下发。</summary>
    public const int AttributeHeartbeatHz = 1;

    /// <summary>位置快照间隔（50ms），对应 <see cref="PositionSnapshotHz"/>。</summary>
    public static readonly TimeSpan PositionSnapshotInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>移动状态心跳间隔（100ms），对应 <see cref="MovementStateHeartbeatHz"/>。</summary>
    public static readonly TimeSpan MovementStateHeartbeatInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>属性心跳间隔（1s），对应 <see cref="AttributeHeartbeatHz"/>。</summary>
    public static readonly TimeSpan AttributeHeartbeatInterval = TimeSpan.FromSeconds(1);
}
