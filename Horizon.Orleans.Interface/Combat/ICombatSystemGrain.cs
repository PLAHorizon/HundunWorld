using System;
using System.Threading.Tasks;
using Orleans;

namespace Horizon.Orleans.Interface.Combat;

/// <summary>
/// 战斗系统 Grain 契约（P1.4）。<br/>
/// Grain Primary Key = combatSessionId（由 ZoneShard 在战斗发起时分配）。<br/>
/// 负责：攻击判定（命中/闪避/暴击）、伤害计算（攻防公式/Buff 加成）、死亡裁决。<br/>
/// 设计原则：Grain-per-Session，复用 Orleans 单线程模型避免锁竞争。
/// </summary>
[global::Orleans.CodeGeneration.Version(1)]
public interface ICombatSystemGrain : IGrainWithIntegerKey
{
    /// <summary>
    /// 处理一次战斗动作（攻击/技能/道具）。
    /// 返回裁决结果（伤害值/是否暴击/是否击杀）。
    /// </summary>
    /// <param name="request">战斗动作请求。</param>
    /// <returns>战斗裁决结果。</returns>
    Task<CombatVerdict> ProcessActionAsync(CombatActionRequest request);

    /// <summary>
    /// 查询指定实体的战斗状态（HP/是否存活/Buff 列表）。
    /// </summary>
    Task<CombatEntityStatus> GetEntityStatusAsync(ulong entityId);

    /// <summary>
    /// 注册实体到本战斗会话（进入战斗范围时调用）。
    /// </summary>
    Task RegisterEntityAsync(CombatEntitySnapshot snapshot);

    /// <summary>
    /// 注销实体（离开战斗范围/死亡后移除）。
    /// </summary>
    Task UnregisterEntityAsync(ulong entityId);

    /// <summary>
    /// 获取本战斗会话的统计信息（参与实体数/总伤害/持续时间）。
    /// </summary>
    Task<CombatSessionStats> GetSessionStatsAsync();
}

/// <summary>
/// 战斗动作请求（由 SyncPacketHandler 从 CombatActionPacket 转换而来）。
/// </summary>
[GenerateSerializer]
public sealed class CombatActionRequest
{
    [Id(0)] public ulong AttackerId { get; set; }
    [Id(1)] public ulong TargetId { get; set; }
    [Id(2)] public byte ActionKind { get; set; }
    [Id(3)] public int SkillId { get; set; }
    [Id(4)] public long ClientTick { get; set; }
    [Id(5)] public float AttackerYaw { get; set; }
}

/// <summary>
/// 战斗裁决结果。
/// </summary>
[GenerateSerializer]
public sealed class CombatVerdict
{
    /// <summary>是否命中。</summary>
    [Id(0)] public bool IsHit { get; set; }
    /// <summary>伤害值（正数=伤害，负数=治疗）。</summary>
    [Id(1)] public int DamageAmount { get; set; }
    /// <summary>是否暴击。</summary>
    [Id(2)] public bool IsCritical { get; set; }
    /// <summary>目标是否死亡。</summary>
    [Id(3)] public bool IsTargetDead { get; set; }
    /// <summary>目标剩余 HP。</summary>
    [Id(4)] public int TargetRemainingHp { get; set; }
    /// <summary>目标最大 HP。</summary>
    [Id(5)] public int TargetMaxHp { get; set; }
    /// <summary>伤害类型。</summary>
    [Id(6)] public byte DamageType { get; set; }
    /// <summary>服务器 tick。</summary>
    [Id(7)] public long ServerTick { get; set; }
}

/// <summary>
/// 实体战斗快照（注册到战斗会话时的初始状态）。
/// </summary>
[GenerateSerializer]
public sealed class CombatEntitySnapshot
{
    [Id(0)] public ulong EntityId { get; set; }
    [Id(1)] public int CurrentHp { get; set; }
    [Id(2)] public int MaxHp { get; set; }
    [Id(3)] public int Attack { get; set; }
    [Id(4)] public int Defense { get; set; }
    [Id(5)] public float CritRate { get; set; }
    [Id(6)] public float DodgeRate { get; set; }
    [Id(7)] public bool IsPlayer { get; set; }
    [Id(8)] public int Level { get; set; }
}

/// <summary>
/// 实体战斗状态查询结果。
/// </summary>
[GenerateSerializer]
public sealed class CombatEntityStatus
{
    [Id(0)] public ulong EntityId { get; set; }
    [Id(1)] public int CurrentHp { get; set; }
    [Id(2)] public int MaxHp { get; set; }
    [Id(3)] public bool IsAlive { get; set; }
    [Id(4)] public long LastDamageTick { get; set; }
}

/// <summary>
/// 战斗会话统计。
/// </summary>
[GenerateSerializer]
public sealed class CombatSessionStats
{
    [Id(0)] public int ActiveEntityCount { get; set; }
    [Id(1)] public long TotalDamageDealt { get; set; }
    [Id(2)] public int TotalActionsProcessed { get; set; }
    [Id(3)] public DateTime SessionStartTime { get; set; }
}
