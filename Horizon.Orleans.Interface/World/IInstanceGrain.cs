using System;
using System.Threading.Tasks;
using Orleans;
using Horizon.Game.Message.Sync;

namespace Horizon.Orleans.Interface.World;

/// <summary>
/// 副本实例 Grain 契约（P2.2）。<br/>
/// Grain Primary Key = instanceId（由 InstanceManagerGrain 分配）。<br/>
/// 与 ZoneShardGrain 同构但隔离：独立空间、独立实体表、独立 Tick 循环。<br/>
/// 副本类型：单人/组队（2-5人）/公会（10-25人）。
/// </summary>
[global::Orleans.CodeGeneration.Version(1)]
public interface IInstanceGrain : IGrainWithIntegerKey
{
    /// <summary>
    /// 初始化副本（由 InstanceManagerGrain 调用）。
    /// </summary>
    /// <param name="config">副本配置。</param>
    Task InitializeAsync(InstanceConfig config);

    /// <summary>
    /// 玩家进入副本。
    /// </summary>
    /// <param name="characterId">角色 ID。</param>
    /// <param name="spawnX">出生点 X。</param>
    /// <param name="spawnY">出生点 Y。</param>
    /// <param name="spawnZ">出生点 Z。</param>
    /// <returns>是否成功进入（副本可能已满/已关闭）。</returns>
    Task<bool> EnterAsync(long characterId, float spawnX, float spawnY, float spawnZ);

    /// <summary>
    /// 玩家离开副本（主动退出/通关/死亡退出）。
    /// </summary>
    Task LeaveAsync(long characterId);

    /// <summary>
    /// 接收客户端输入。
    /// </summary>
    Task AcceptInputAsync(ulong entityId, InputPacket input);

    /// <summary>
    /// 续约实体租约。
    /// </summary>
    Task RenewLeaseAsync(ulong entityId);

    /// <summary>
    /// 获取副本当前状态。
    /// </summary>
    Task<InstanceState> GetStateAsync();

    /// <summary>
    /// 关闭副本（超时/全部玩家离开/GM 命令）。
    /// </summary>
    Task CloseAsync(InstanceCloseReason reason);

    /// <summary>
    /// 生成 Boss/怪物（由副本脚本/AI 驱动调用）。
    /// </summary>
    Task SpawnMonsterAsync(ulong monsterId, int templateId, float x, float y, float z);

    /// <summary>
    /// 副本内战斗动作处理。
    /// </summary>
    Task<CombatVerdictResult> ProcessCombatActionAsync(ulong attackerId, ulong targetId, byte actionKind, int skillId);
}

/// <summary>
/// 副本配置。
/// </summary>
[GenerateSerializer]
public sealed class InstanceConfig
{
    /// <summary>副本模板 ID（对应配置表）。</summary>
    [Id(0)] public int TemplateId { get; set; }
    /// <summary>副本名称。</summary>
    [Id(1)] public string Name { get; set; } = string.Empty;
    /// <summary>副本类型。</summary>
    [Id(2)] public InstanceType Type { get; set; }
    /// <summary>最大玩家数。</summary>
    [Id(3)] public int MaxPlayers { get; set; }
    /// <summary>超时时间（秒）：超时后自动关闭。</summary>
    [Id(4)] public float TimeoutSeconds { get; set; } = 3600f;
    /// <summary>创建者角色 ID。</summary>
    [Id(5)] public long CreatorId { get; set; }
    /// <summary>所属 ZoneShard ID（用于传送回开放世界）。</summary>
    [Id(6)] public long OriginZoneShardId { get; set; }
    /// <summary>副本难度。</summary>
    [Id(7)] public int Difficulty { get; set; } = 1;
}

/// <summary>
/// 副本类型。
/// </summary>
[GenerateSerializer]
public enum InstanceType : byte
{
    /// <summary>单人副本。</summary>
    Solo = 0,
    /// <summary>组队副本（2-5人）。</summary>
    Party = 1,
    /// <summary>公会副本（10-25人）。</summary>
    Guild = 2,
    /// <summary>PVP 竞技场。</summary>
    Arena = 3,
}

/// <summary>
/// 副本当前状态。
/// </summary>
[GenerateSerializer]
public sealed class InstanceState
{
    [Id(0)] public long InstanceId { get; set; }
    [Id(1)] public int TemplateId { get; set; }
    [Id(2)] public string Name { get; set; } = string.Empty;
    [Id(3)] public InstanceType Type { get; set; }
    [Id(4)] public int CurrentPlayers { get; set; }
    [Id(5)] public int MaxPlayers { get; set; }
    [Id(6)] public InstancePhase Phase { get; set; }
    [Id(7)] public DateTime CreateTime { get; set; }
    [Id(8)] public long[] PlayerIds { get; set; } = Array.Empty<long>();
    [Id(9)] public int MonsterCount { get; set; }
    [Id(10)] public bool IsClosed { get; set; }
}

/// <summary>
/// 副本阶段。
/// </summary>
[GenerateSerializer]
public enum InstancePhase : byte
{
    /// <summary>等待中（玩家进入前）。</summary>
    Waiting = 0,
    /// <summary>进行中。</summary>
    InProgress = 1,
    /// <summary>Boss 战。</summary>
    BossFight = 2,
    /// <summary>通关。</summary>
    Completed = 3,
    /// <summary>失败。</summary>
    Failed = 4,
    /// <summary>已关闭。</summary>
    Closed = 5,
}

/// <summary>
/// 副本关闭原因。
/// </summary>
[GenerateSerializer]
public enum InstanceCloseReason : byte
{
    /// <summary>超时。</summary>
    Timeout = 0,
    /// <summary>全部玩家离开。</summary>
    AllPlayersLeft = 1,
    /// <summary>通关完成。</summary>
    Completed = 2,
    /// <summary>GM 命令。</summary>
    GmCommand = 3,
    /// <summary>服务器关闭。</summary>
    ServerShutdown = 4,
}

/// <summary>
/// 副本内战斗裁决结果（简化版，与 CombatVerdict 对齐）。
/// </summary>
[GenerateSerializer]
public sealed class CombatVerdictResult
{
    [Id(0)] public bool IsHit { get; set; }
    [Id(1)] public int DamageAmount { get; set; }
    [Id(2)] public bool IsCritical { get; set; }
    [Id(3)] public bool IsTargetDead { get; set; }
    [Id(4)] public int TargetRemainingHp { get; set; }
    [Id(5)] public int TargetMaxHp { get; set; }
}
