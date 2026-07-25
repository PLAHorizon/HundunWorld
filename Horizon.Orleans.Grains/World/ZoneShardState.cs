using System;
using System.Collections.Generic;

namespace Horizon.Orleans.Grains.World;

/// <summary>
/// ZoneShardGrain 的 Orleans 持久化状态。
/// Grain 空闲回收或 Silo 重启后，从 Storage 恢复实体位置，避免角色被拉回初始坐标。
/// </summary>
[GenerateSerializer]
[Serializable]
public sealed class ZoneShardState
{
    /// <summary>所有实体的持久化快照（key = entityId）。</summary>
    [Id(0)]
    public Dictionary<ulong, SimulatedEntityState> Entities { get; set; } = new();

    /// <summary>持久化时的 tick 计数。</summary>
    [Id(1)]
    public long TickCount { get; set; }

    /// <summary>上次持久化时间戳（UTC）。</summary>
    [Id(2)]
    public DateTime LastPersistedUtc { get; set; }
}

/// <summary>
/// 单个实体的持久化状态（仅保留位置/朝向/关键模拟字段，不含瞬态缓冲）。
/// </summary>
[GenerateSerializer]
[Serializable]
public sealed class SimulatedEntityState
{
    [Id(0)] public float X { get; set; }
    [Id(1)] public float Y { get; set; }
    [Id(2)] public float Z { get; set; }
    [Id(3)] public float Vz { get; set; }
    [Id(4)] public float Yaw { get; set; }
    [Id(5)] public float MaxSpeed { get; set; }
    [Id(6)] public bool IsGrounded { get; set; }
    [Id(7)] public int JumpCount { get; set; }
    [Id(8)] public long LastSyncTick { get; set; }
    [Id(9)] public int Hp { get; set; }
    [Id(10)] public int MaxHp { get; set; }
    [Id(11)] public int Mana { get; set; }
    [Id(12)] public int MaxMana { get; set; }
    [Id(13)] public int Level { get; set; }
    [Id(14)] public long Exp { get; set; }
    [Id(15)] public int Stamina { get; set; }
    [Id(16)] public int MaxStamina { get; set; }
}
