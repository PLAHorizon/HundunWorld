using System;
using System.Threading.Tasks;
using Orleans;

namespace Horizon.Orleans.Interface.World;

/// <summary>
/// P2.1 跨 Shard 传送协议。<br/>
/// 扩展 IZoneShardGrain 的传送能力：源 Shard Despawn → 目标 Shard Spawn。<br/>
/// 本接口定义传送请求/结果类型，实际传送逻辑由 ZoneShardGrain 实现。
/// </summary>
[GenerateSerializer]
public sealed class TransferZoneRequest
{
    /// <summary>传送目标实体 ID。</summary>
    [Id(0)] public ulong EntityId { get; set; }
    /// <summary>目标 ZoneShard ID。</summary>
    [Id(1)] public long TargetShardId { get; set; }
    /// <summary>目标出生点 X。</summary>
    [Id(2)] public float TargetX { get; set; }
    /// <summary>目标出生点 Y。</summary>
    [Id(3)] public float TargetY { get; set; }
    /// <summary>目标出生点 Z。</summary>
    [Id(4)] public float TargetZ { get; set; }
    /// <summary>传送原因。</summary>
    [Id(5)] public TransferReason Reason { get; set; }
    /// <summary>请求发起时间戳（用于超时检测）。</summary>
    [Id(6)] public long RequestTimestamp { get; set; }
}

/// <summary>
/// 传送结果。
/// </summary>
[GenerateSerializer]
public sealed class TransferZoneResult
{
    /// <summary>是否成功。</summary>
    [Id(0)] public bool Success { get; set; }
    /// <summary>失败原因（Success=false 时有效）。</summary>
    [Id(1)] public TransferFailReason FailReason { get; set; }
    /// <summary>目标 Shard 分配的实体 ID（可能与源 ID 不同）。</summary>
    [Id(2)] public ulong AssignedEntityId { get; set; }
}

/// <summary>
/// 传送原因。
/// </summary>
[GenerateSerializer]
public enum TransferReason : byte
{
    /// <summary>玩家主动传送（传送门/NPC）。</summary>
    PlayerRequest = 0,
    /// <summary>进入副本。</summary>
    EnterInstance = 1,
    /// <summary>离开副本（回到开放世界）。</summary>
    LeaveInstance = 2,
    /// <summary>GM 传送。</summary>
    GmCommand = 3,
    /// <summary>死亡复活传送。</summary>
    Respawn = 4,
}

/// <summary>
/// 传送失败原因。
/// </summary>
[GenerateSerializer]
public enum TransferFailReason : byte
{
    /// <summary>无（成功）。</summary>
    None = 0,
    /// <summary>目标 Shard 不存在/不可达。</summary>
    TargetShardUnavailable = 1,
    /// <summary>目标 Shard 已满。</summary>
    TargetShardFull = 2,
    /// <summary>实体不存在于源 Shard。</summary>
    EntityNotFound = 3,
    /// <summary>传送超时。</summary>
    Timeout = 4,
    /// <summary>传送冷却中。</summary>
    Cooldown = 5,
}
