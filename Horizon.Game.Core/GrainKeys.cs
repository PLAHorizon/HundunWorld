using System;

namespace Horizon.Game.Core;

/// <summary>
/// 角色维度 Grain 的 key 约定（服务端唯一出处）。
/// <para>
/// 同一角色的 SkillGrain / CombatGrain 必须按角色稳定寻址：早期实现把 key 写成
/// <c>Guid.NewGuid()</c>，导致每次请求都激活一个新 Grain、<c>CombatState.CombatParticipants</c>
/// 缓存与技能冷却全部失效（表现为"同一角色状态不连续"）。所有按角色/实体取 Grain 的地方
/// 一律走本方法，不要在调用方各自拼字节序。
/// </para>
/// </summary>
public static class GrainKeys
{
    /// <summary>实体/角色 ID → Grain key（低 8 字节承载 ulong，其余留零）。</summary>
    public static Guid ForEntity(ulong entityId)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(entityId).CopyTo(bytes, 0);
        return new Guid(bytes);
    }

    /// <summary>
    /// 一场对抗的 Grain key：以受击方为主键，使并发攻击同一目标的多名攻方串行落在同一个
    /// CombatGrain 上（判死与 HP 回写只发生一次）。目标为 0 时退回 <paramref name="fallback"/>。
    /// </summary>
    public static Guid ForEngagement(ulong targetId, ulong fallback)
        => ForEntity(targetId != 0 ? targetId : fallback);
}
