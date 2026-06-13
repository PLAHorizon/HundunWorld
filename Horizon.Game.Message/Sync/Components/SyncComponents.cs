using MemoryPack;
using Orleans;

namespace Horizon.Game.Message.Sync.Components;

/// <summary>
/// 服务器权威的网络身份组件：所有可同步实体必须携带。
/// 后缀 <c>Auth</c> 表示"服务器权威"，由网络层从 <see cref="Horizon.Game.Message.Sync.SnapshotPacket"/> 写入。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public partial struct NetworkIdentityAuthComponent
{
    /// <summary>服务器分配的全局唯一实体 ID。</summary>
    [MemoryPackOrder(0)]
    [Id(0)]
    public ulong NetworkId;

    /// <summary>实体类型（与服务器枚举对齐）。</summary>
    [MemoryPackOrder(1)]
    [Id(1)]
    public int EntityType;

    /// <summary>归属玩家/角色 ID（0 表示 NPC）。</summary>
    [MemoryPackOrder(2)]
    [Id(2)]
    public ulong OwnerId;
}

/// <summary>
/// 服务器权威 Transform：位置 + 旋转，blittable，便于 SoA 化与 SIMD 处理。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public partial struct AuthTransformComponent
{
    [MemoryPackOrder(0)] [Id(0)] public float X;
    [MemoryPackOrder(1)] [Id(1)] public float Y;
    [MemoryPackOrder(2)] [Id(2)] public float Z;
    [MemoryPackOrder(3)] [Id(3)] public float Pitch;
    [MemoryPackOrder(4)] [Id(4)] public float Yaw;
    [MemoryPackOrder(5)] [Id(5)] public float Roll;

    /// <summary>采样时的服务器 tick（用于 reconciliation / 插值排序）。</summary>
    [MemoryPackOrder(6)] [Id(6)] public long ServerTick;
}

/// <summary>
/// 客户端预测 Transform 副本：仅本地控制实体使用，由 LocalSimulationSystem 写入；
/// 与 <see cref="AuthTransformComponent"/> 比对超过阈值时由 ReconciliationSystem 回滚。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public partial struct PredictedTransformComponent
{
    [MemoryPackOrder(0)] [Id(0)] public float X;
    [MemoryPackOrder(1)] [Id(1)] public float Y;
    [MemoryPackOrder(2)] [Id(2)] public float Z;
    [MemoryPackOrder(3)] [Id(3)] public float Pitch;
    [MemoryPackOrder(4)] [Id(4)] public float Yaw;
    [MemoryPackOrder(5)] [Id(5)] public float Roll;

    /// <summary>对应的客户端 tick / 输入序号。</summary>
    [MemoryPackOrder(6)] [Id(6)] public long ClientTick;
}

/// <summary>
/// 服务器权威实体状态：HP / 状态位。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public partial struct EntityStateAuthComponent
{
    [MemoryPackOrder(0)] [Id(0)] public int Health;
    [MemoryPackOrder(1)] [Id(1)] public int MaxHealth;

    /// <summary>状态位掩码（IsDead, IsInvincible, IsStunned…）。</summary>
    [MemoryPackOrder(2)] [Id(2)] public uint StateBits;

    /// <summary>简化访问：StateBits 的最低位。</summary>
    public bool IsDead
    {
        get => (StateBits & EntityStateBits.Dead) != 0;
        set
        {
            if (value) StateBits |= EntityStateBits.Dead;
            else StateBits &= ~EntityStateBits.Dead;
        }
    }
}

/// <summary>实体状态位定义。</summary>
public static class EntityStateBits
{
    public const uint Dead = 1u << 0;
    public const uint Invincible = 1u << 1;
    public const uint Stunned = 1u << 2;
    public const uint Hidden = 1u << 3;
    public const uint Frozen = 1u << 4;
}

/// <summary>
/// 服务器下发的本帧输入回执（用于客户端比对预测）：
/// 服务器告诉客户端"我处理到了 ClientTick=N 的输入"，客户端可据此清理输入环形缓冲。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public partial struct InputAckAuthComponent
{
    /// <summary>服务器已处理到的最后一个客户端输入 tick。</summary>
    [MemoryPackOrder(0)] [Id(0)] public long LastProcessedClientTick;
}
