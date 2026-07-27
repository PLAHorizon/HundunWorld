using System;
using System.Collections.Generic;
using MemoryPack;

namespace Horizon.Orleans.Grains.World;

/// <summary>
/// ZoneShardGrain 的 Orleans 持久化状态。
/// Grain 空闲回收或 Silo 重启后，从 Storage 恢复实体位置，避免角色被拉回初始坐标。
/// </summary>
[MemoryPackable(SerializeLayout.Explicit)]
[GenerateSerializer]
[Serializable]
public sealed partial class ZoneShardState
{
    /// <summary>所有实体的持久化快照（key = entityId）。</summary>
    [MemoryPackOrder(0)]
    [Id(0)]
    public Dictionary<ulong, SimulatedEntityState> Entities { get; set; } = new();

    /// <summary>持久化时的 tick 计数。</summary>
    [MemoryPackOrder(1)]
    [Id(1)]
    public long TickCount { get; set; }

    /// <summary>上次持久化时间戳（UTC）。</summary>
    [MemoryPackOrder(2)]
    [Id(2)]
    public DateTime LastPersistedUtc { get; set; }
}

/// <summary>
/// 单个实体的持久化状态（仅保留位置/朝向/关键模拟字段，不含瞬态缓冲）。
/// </summary>
[MemoryPackable(SerializeLayout.Explicit)]
[GenerateSerializer]
[Serializable]
public sealed partial class SimulatedEntityState
{
    [MemoryPackOrder(0)] [Id(0)] public float X { get; set; }
    [MemoryPackOrder(1)] [Id(1)] public float Y { get; set; }
    [MemoryPackOrder(2)] [Id(2)] public float Z { get; set; }
    [MemoryPackOrder(3)] [Id(3)] public float Vz { get; set; }
    [MemoryPackOrder(4)] [Id(4)] public float Yaw { get; set; }
    [MemoryPackOrder(5)] [Id(5)] public float MaxSpeed { get; set; }
    [MemoryPackOrder(6)] [Id(6)] public bool IsGrounded { get; set; }
    [MemoryPackOrder(7)] [Id(7)] public int JumpCount { get; set; }
    [MemoryPackOrder(8)] [Id(8)] public long LastSyncTick { get; set; }
    [MemoryPackOrder(9)] [Id(9)] public int Hp { get; set; }
    [MemoryPackOrder(10)] [Id(10)] public int MaxHp { get; set; }
    [MemoryPackOrder(11)] [Id(11)] public int Mana { get; set; }
    [MemoryPackOrder(12)] [Id(12)] public int MaxMana { get; set; }
    [MemoryPackOrder(13)] [Id(13)] public int Level { get; set; }
    [MemoryPackOrder(14)] [Id(14)] public long Exp { get; set; }
    [MemoryPackOrder(15)] [Id(15)] public int Stamina { get; set; }
    [MemoryPackOrder(16)] [Id(16)] public int MaxStamina { get; set; }
}
