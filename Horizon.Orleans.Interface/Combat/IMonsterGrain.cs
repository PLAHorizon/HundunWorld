using System;
using System.Threading.Tasks;
using Orleans;

namespace Horizon.Orleans.Interface.Combat;

/// <summary>
/// 怪物 Grain 契约（P1.4）。<br/>
/// Grain Primary Key = monsterInstanceId（由 ZoneShard 在怪物刷新时分配）。<br/>
/// 负责：怪物 AI 状态机（巡逻/追击/攻击/返回）、仇恨表管理、技能释放决策。<br/>
/// ZoneShardGrain 的 Tick 循环中驱动本 Grain 的 AI 更新。
/// </summary>
[global::Orleans.CodeGeneration.Version(1)]
public interface IMonsterGrain : IGrainWithIntegerKey
{
    /// <summary>
    /// 初始化怪物实例（刷新时调用）。
    /// </summary>
    /// <param name="config">怪物配置（模板 ID/等级/属性/巡逻路径）。</param>
    Task InitializeAsync(MonsterConfig config);

    /// <summary>
    /// AI Tick：由 ZoneShard 每 tick 调用，驱动 AI 状态机推进。
    /// 返回本 tick 的 AI 决策（移动目标/攻击目标/释放技能）。
    /// </summary>
    /// <param name="deltaMs">距上次 tick 的毫秒数。</param>
    /// <param name="currentPosition">当前位置。</param>
    /// <param name="nearbyEntities">AOI 内实体快照（用于仇恨/目标选择）。</param>
    /// <returns>AI 决策结果。</returns>
    Task<MonsterAiDecision> TickAsync(float deltaMs, MonsterPosition currentPosition, NearbyEntity[] nearbyEntities);

    /// <summary>
    /// 受到伤害（由 CombatSystemGrain 裁决后调用）。
    /// 更新仇恨表并可能触发 AI 状态切换（巡逻→追击）。
    /// </summary>
    /// <param name="attackerId">攻击者 ID。</param>
    /// <param name="damage">伤害值。</param>
    /// <param name="isCritical">是否暴击。</param>
    Task OnDamagedAsync(ulong attackerId, int damage, bool isCritical);

    /// <summary>
    /// 怪物死亡处理（掉落/经验/仇恨清空）。
    /// </summary>
    /// <param name="killerId">击杀者 ID。</param>
    Task OnDeathAsync(ulong killerId);

    /// <summary>
    /// 怪物复活（重置状态/HP/位置回到出生点）。
    /// </summary>
    Task RespawnAsync();

    /// <summary>
    /// 获取怪物当前状态（HP/AI 状态/位置/仇恨目标）。
    /// </summary>
    Task<MonsterState> GetStateAsync();
}

/// <summary>
/// 怪物配置（刷新时注入）。
/// </summary>
[GenerateSerializer]
public sealed class MonsterConfig
{
    /// <summary>怪物模板 ID（对应数值表）。</summary>
    [Id(0)] public int TemplateId { get; set; }
    /// <summary>怪物名称。</summary>
    [Id(1)] public string Name { get; set; } = string.Empty;
    /// <summary>等级。</summary>
    [Id(2)] public int Level { get; set; }
    /// <summary>最大 HP。</summary>
    [Id(3)] public int MaxHp { get; set; }
    /// <summary>攻击力。</summary>
    [Id(4)] public int Attack { get; set; }
    /// <summary>防御力。</summary>
    [Id(5)] public int Defense { get; set; }
    /// <summary>移动速度（m/s）。</summary>
    [Id(6)] public float MoveSpeed { get; set; }
    /// <summary>攻击范围（米）。</summary>
    [Id(7)] public float AttackRange { get; set; }
    /// <summary>仇恨范围（米）。</summary>
    [Id(8)] public float AggroRange { get; set; }
    /// <summary>巡逻路径点（世界坐标）。</summary>
    [Id(9)] public MonsterPosition[] PatrolPoints { get; set; } = Array.Empty<MonsterPosition>();
    /// <summary>出生点。</summary>
    [Id(10)] public MonsterPosition SpawnPoint { get; set; } = new();
    /// <summary>复活延迟（秒）。</summary>
    [Id(11)] public float RespawnDelaySeconds { get; set; } = 30f;
    /// <summary>所属 ZoneShard ID。</summary>
    [Id(12)] public long ZoneShardId { get; set; }
}

/// <summary>
/// 怪物位置。
/// </summary>
[GenerateSerializer]
public sealed class MonsterPosition
{
    [Id(0)] public float X { get; set; }
    [Id(1)] public float Y { get; set; }
    [Id(2)] public float Z { get; set; }
    [Id(3)] public float Yaw { get; set; }
}

/// <summary>
/// AOI 内实体快照（用于怪物 AI 目标选择）。
/// </summary>
[GenerateSerializer]
public sealed class NearbyEntity
{
    [Id(0)] public ulong EntityId { get; set; }
    [Id(1)] public float X { get; set; }
    [Id(2)] public float Y { get; set; }
    [Id(3)] public float Z { get; set; }
    [Id(4)] public bool IsPlayer { get; set; }
    [Id(5)] public bool IsAlive { get; set; }
}

/// <summary>
/// 怪物 AI 决策结果（每 tick 返回给 ZoneShard 执行）。
/// </summary>
[GenerateSerializer]
public sealed class MonsterAiDecision
{
    /// <summary>AI 行为类型。</summary>
    [Id(0)] public MonsterAiAction Action { get; set; }
    /// <summary>移动目标位置（Action=MoveTo 时有效）。</summary>
    [Id(1)] public MonsterPosition? MoveTarget { get; set; }
    /// <summary>攻击目标 ID（Action=Attack 时有效）。</summary>
    [Id(2)] public ulong AttackTargetId { get; set; }
    /// <summary>释放技能 ID（Action=CastSkill 时有效）。</summary>
    [Id(3)] public int SkillId { get; set; }
}

/// <summary>
/// 怪物 AI 行为枚举。
/// </summary>
[GenerateSerializer]
public enum MonsterAiAction : byte
{
    /// <summary>待机/无操作。</summary>
    Idle = 0,
    /// <summary>移动到目标点。</summary>
    MoveTo = 1,
    /// <summary>攻击目标。</summary>
    Attack = 2,
    /// <summary>释放技能。</summary>
    CastSkill = 3,
    /// <summary>返回出生点（脱战）。</summary>
    ReturnToSpawn = 4,
    /// <summary>已死亡（等待复活）。</summary>
    Dead = 5,
}

/// <summary>
/// 怪物当前状态快照。
/// </summary>
[GenerateSerializer]
public sealed class MonsterState
{
    [Id(0)] public ulong MonsterId { get; set; }
    [Id(1)] public int TemplateId { get; set; }
    [Id(2)] public string Name { get; set; } = string.Empty;
    [Id(3)] public int CurrentHp { get; set; }
    [Id(4)] public int MaxHp { get; set; }
    [Id(5)] public bool IsAlive { get; set; }
    [Id(6)] public MonsterAiAction CurrentAction { get; set; }
    [Id(7)] public MonsterPosition Position { get; set; } = new();
    [Id(8)] public ulong CurrentTargetId { get; set; }
    [Id(9)] public int Level { get; set; }
}
