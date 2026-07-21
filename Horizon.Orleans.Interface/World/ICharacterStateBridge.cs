using System;
using System.Threading.Tasks;

namespace Horizon.Orleans.Interface.World;

/// <summary>
/// 角色状态桥接服务契约（P1.1 统一角色状态模型）。<br/>
/// 定义 <see cref="IZoneShardGrain"/>（空间权威）与 <see cref="ICharacterGrain"/>（RPG 权威）之间的
/// 双向通信协议，消除两套角色系统的语义重叠。<br/>
/// 本接口为<b>服务接口</b>（非 Grain 接口），通过 DI 注入 ZoneShardGrain，
/// 实现内部通过 <c>IGrainFactory</c> 调用 <see cref="ICharacterGrain"/>。
/// </summary>
/// <remarks>
/// <para><b>职责划分</b>：</para>
/// <list type="bullet">
///   <item><see cref="IZoneShardGrain"/>：空间权威（位置/速度/碰撞/AOI），持有 RPG 属性的<b>广播缓存</b>（用于 EntityDelta 快照）。</item>
///   <item><see cref="ICharacterGrain"/>：RPG 权威（属性/技能/背包/装备），是 HP/Mana/Level 等的<b>唯一权威来源</b>。</item>
/// </list>
/// <para><b>数据流</b>：</para>
/// <list type="bullet">
///   <item>ZoneShard → Character：生命周期通知（进入/离开空间）、战斗伤害请求。</item>
///   <item>Character → ZoneShard：属性变更推送（RPG 状态变化时通过 IZoneShardGrain.UpdateCharacterAttributesAsync 同步到广播缓存）。</item>
/// </list>
/// </remarks>
public interface ICharacterStateBridge
{
    // ===== ZoneShard → CharacterGrain 方向（生命周期通知） =====

    /// <summary>
    /// 角色实体进入空间（Spawn）时由 ZoneShard 调用。<br/>
    /// CharacterGrain 据此：(1) 将当前 RPG 属性推送给 ZoneShard 作为广播缓存初始值；
    /// (2) 更新内部"所在空间"标记。
    /// </summary>
    /// <param name="characterId">角色 ID。</param>
    /// <param name="zoneShardId">所在 ZoneShard 的 Grain Key。</param>
    /// <param name="initialHp">ZoneShard 侧的初始 HP（通常为 0，由 CharacterGrain 覆盖为权威值）。</param>
    Task OnEnterZoneAsync(long characterId, long zoneShardId, int initialHp);

    /// <summary>
    /// 角色实体离开空间（Despawn）时由 ZoneShard 调用。<br/>
    /// CharacterGrain 据此：(1) 持久化最终状态；(2) 清除"所在空间"标记。
    /// </summary>
    /// <param name="characterId">角色 ID。</param>
    /// <param name="zoneShardId">离开的 ZoneShard 的 Grain Key。</param>
    /// <param name="finalHp">离开时的最终 HP（由 ZoneShard 报告，CharacterGrain 持久化）。</param>
    /// <param name="reason">离开原因（正常下线/孤儿清理/传送等）。</param>
    Task OnLeaveZoneAsync(long characterId, long zoneShardId, int finalHp, ZoneLeaveReason reason);

    // ===== ZoneShard → CharacterGrain 方向（战斗/伤害请求） =====

    /// <summary>
    /// 请求 HP 变更（战斗伤害/治疗/环境伤害等）。<br/>
    /// ZoneShard 不直接修改 HP 权威值，而是通过此方法请求 CharacterGrain 裁决。<br/>
    /// CharacterGrain 计算实际伤害（含防御/减伤/Buff）后返回结果，并推送新 HP 到 ZoneShard 缓存。
    /// </summary>
    /// <param name="characterId">目标角色 ID。</param>
    /// <param name="hpDelta">HP 变化量（负数为伤害，正数为治疗）。</param>
    /// <param name="sourceId">伤害/治疗来源实体 ID（0 表示环境）。</param>
    /// <param name="damageType">伤害类型。</param>
    /// <returns>实际 HP 变化量（经防御/减伤计算后）及是否导致死亡。</returns>
    Task<HpChangeResult> RequestHpChangeAsync(long characterId, int hpDelta, ulong sourceId, DamageType damageType);

    // ===== CharacterGrain → ZoneShard 方向（属性推送） =====
    // 注意：此方向通过 IZoneShardGrain.UpdateCharacterAttributesAsync 实现（已有接口），
    // CharacterGrain 在 RPG 状态变化时主动调用 ZoneShard 更新广播缓存。
}

/// <summary>
/// HP 变更结果（由 CharacterGrain 裁决后返回）。
/// </summary>
[GenerateSerializer]
public readonly record struct HpChangeResult(
    /// <summary>实际 HP 变化量（经防御/减伤/Buff 计算后）。</summary>
    [property: Id(0)] int ActualDelta,
    /// <summary>变更后的当前 HP。</summary>
    [property: Id(1)] int CurrentHp,
    /// <summary>变更后的最大 HP。</summary>
    [property: Id(2)] int MaxHp,
    /// <summary>是否导致死亡。</summary>
    [property: Id(3)] bool IsDead,
    /// <summary>是否被完全抵抗/免疫。</summary>
    [property: Id(4)] bool IsResisted);

/// <summary>
/// 角色离开空间的原因。
/// </summary>
[GenerateSerializer]
public enum ZoneLeaveReason : byte
{
    /// <summary>正常下线（玩家主动退出/断线）。</summary>
    [Id(0)]
    NormalLogout = 0,

    /// <summary>孤儿实体清理（租约过期，网关崩溃/断线未清理）。</summary>
    [Id(1)]
    OrphanCleanup = 1,

    /// <summary>传送（离开当前 ZoneShard，即将进入另一个）。</summary>
    [Id(2)]
    Transfer = 2,

    /// <summary>进入副本（离开开放世界 ZoneShard）。</summary>
    [Id(3)]
    EnterInstance = 3,

    /// <summary>被踢出（GM 操作/反作弊封禁）。</summary>
    [Id(4)]
    Kicked = 4,
}

/// <summary>
/// 伤害类型枚举。
/// </summary>
[GenerateSerializer]
public enum DamageType : byte
{
    /// <summary>物理伤害。</summary>
    [Id(0)]
    Physical = 0,

    /// <summary>内功伤害（魔法）。</summary>
    [Id(1)]
    Internal = 1,

    /// <summary>环境伤害（跌落/毒雾/陷阱）。</summary>
    [Id(2)]
    Environmental = 3,

    /// <summary>治疗。</summary>
    [Id(3)]
    Healing = 4,

    /// <summary>真实伤害（无视防御）。</summary>
    [Id(4)]
    True = 5,
}
