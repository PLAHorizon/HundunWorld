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
/// 服务器权威实体状态：HP / 法力 / 等级 / 经验 / 体力 / 状态位。
/// 旧字段 Health/MaxHealth/StateBits 编号（0/1/2）保持不变以确保向后兼容。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public partial struct EntityStateAuthComponent
{
    [MemoryPackOrder(0)] [Id(0)] public int Health;
    [MemoryPackOrder(1)] [Id(1)] public int MaxHealth;

    /// <summary>状态位掩码（IsDead, IsInvincible, IsStunned…）。</summary>
    [MemoryPackOrder(2)] [Id(2)] public uint StateBits;

    /// <summary>当前法力值。</summary>
    [MemoryPackOrder(3)] [Id(3)] public int Mana;

    /// <summary>最大法力值。</summary>
    [MemoryPackOrder(4)] [Id(4)] public int MaxMana;

    /// <summary>角色等级。</summary>
    [MemoryPackOrder(5)] [Id(5)] public int Level;

    /// <summary>累计经验值。</summary>
    [MemoryPackOrder(6)] [Id(6)] public long Exp;

    /// <summary>当前体力值（冲刺/闪避等消耗）。</summary>
    [MemoryPackOrder(7)] [Id(7)] public int Stamina;

    /// <summary>最大体力值。</summary>
    [MemoryPackOrder(8)] [Id(8)] public int MaxStamina;

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

/// <summary>
/// 服务器权威交互槽状态组件（阶段 1）：NarrativePro 交互槽的占用/进行中/结束/被抢占状态。
/// 由网络层从 <see cref="Horizon.Game.Message.Sync.InteractionSyncPacket"/> 写入，客户端 ECS 据此驱动交互表现。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public partial struct InteractionSyncComponent
{
    /// <summary>承载该交互槽的实体 NetworkId。</summary>
    [MemoryPackOrder(0)]
    [Id(0)]
    public long NetworkId;

    /// <summary>交互槽索引（同一 InteractableId 下可有多个槽位）。</summary>
    [MemoryPackOrder(1)]
    [Id(1)]
    public int SlotIdx;

    /// <summary>可交互对象的 NetworkId。</summary>
    [MemoryPackOrder(2)]
    [Id(2)]
    public long InteractableId;

    /// <summary>交互者（玩家）的 NetworkId。</summary>
    [MemoryPackOrder(3)]
    [Id(3)]
    public long InteractorId;

    /// <summary>交互状态位标志（占用/进行中/结束/被抢占等）。</summary>
    [MemoryPackOrder(4)]
    [Id(4)]
    public byte StateBits;

    /// <summary>采样时的服务器 tick（用于 reconciliation / 插值排序）。</summary>
    [MemoryPackOrder(5)]
    [Id(5)]
    public long ServerTick;
}

/// <summary>
/// 角色移动模式枚举（服务器权威）。取值与 UE5/客户端动画状态机对齐。
/// </summary>
public enum MovementMode : byte
{
    /// <summary>行走。</summary>
    Walk = 0,

    /// <summary>奔跑。</summary>
    Run = 1,

    /// <summary>跳跃中（上升阶段）。</summary>
    Jump = 2,

    /// <summary>下落。</summary>
    Fall = 3,

    /// <summary>游泳。</summary>
    Swim = 4,

    /// <summary>蹲伏。</summary>
    Crouch = 5,
}

/// <summary>
/// 服务器权威移动状态组件：移动模式 + 水平速度向量 + 落地标志，用于客户端动画混合。
/// 与 <see cref="AuthTransformComponent"/> 配合：Transform 负责位置，本组件负责动画状态机驱动。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public partial struct MovementStateAuthComponent
{
    /// <summary>当前移动模式（Walk/Run/Jump/Fall/Swim/Crouch）。</summary>
    [MemoryPackOrder(0)]
    [Id(0)]
    public MovementMode MovementMode;

    /// <summary>水平速度 X 分量（世界坐标系）。</summary>
    [MemoryPackOrder(1)]
    [Id(1)]
    public float VelocityXZ_X;

    /// <summary>水平速度 Y 分量（世界坐标系，UE5 中对应 Y 轴）。</summary>
    [MemoryPackOrder(2)]
    [Id(2)]
    public float VelocityXZ_Y;

    /// <summary>是否接触地面（true=地面，false=空中）。</summary>
    [MemoryPackOrder(3)]
    [Id(3)]
    public bool IsGrounded;

    /// <summary>采样时的服务器 tick（用于 reconciliation / 插值排序）。</summary>
    [MemoryPackOrder(4)]
    [Id(4)]
    public long ServerTick;
}

/// <summary>
/// 服务器权威动画状态组件：仅同步触发型动画状态（Montage）。
/// 循环动画（如 Idle/Run 循环）由客户端根据 <see cref="MovementStateAuthComponent"/> 自行驱动，
/// 不占用网络带宽；仅 Montage 触发/结束事件需服务器下发以保证多人一致性。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public partial struct AnimationStateAuthComponent
{
    /// <summary>动画 Montage 资源 ID（0 表示无 Montage 播放）。</summary>
    [MemoryPackOrder(0)]
    [Id(0)]
    public uint AnimMontageId;

    /// <summary>动画实例 ID（区分同一 Montage 的不同播放实例/槽位）。</summary>
    [MemoryPackOrder(1)]
    [Id(1)]
    public uint AnimInstanceId;

    /// <summary>播放速率（1.0=正常速度）。</summary>
    [MemoryPackOrder(2)]
    [Id(2)]
    public float PlayRate;

    /// <summary>当前播放时间位置（秒）。</summary>
    [MemoryPackOrder(3)]
    [Id(3)]
    public float TimePosition;

    /// <summary>是否循环播放。</summary>
    [MemoryPackOrder(4)]
    [Id(4)]
    public bool IsLooping;

    /// <summary>采样时的服务器 tick（用于 reconciliation / 插值排序）。</summary>
    [MemoryPackOrder(5)]
    [Id(5)]
    public long ServerTick;
}

// ---------------------------------------------------------------------------
// 阶段 C：场景交互对象同步组件。
// 承担场景对象（宝箱/开关/门/拉杆/传送门）的权威状态与可移动对象的 Transform。
// ---------------------------------------------------------------------------

/// <summary>
/// 场景对象类型枚举（阶段 C）。
/// 与服务器侧枚举对齐，用于驱动客户端表现选型。
/// </summary>
public enum SceneObjectType : byte
{
    /// <summary>宝箱。</summary>
    Chest = 0,
    /// <summary>开关。</summary>
    Switch = 1,
    /// <summary>门。</summary>
    Door = 2,
    /// <summary>拉杆。</summary>
    Lever = 3,
    /// <summary>传送门。</summary>
    Portal = 4,
}

/// <summary>
/// 场景对象状态位编码的单一事实源（阶段 C）。
/// 参考 <see cref="InteractionStateBits"/> 的模式：常量 + 辅助方法。
/// 位定义：Opened(bit0) / Activated(bit1) / Locked(bit2) / Reset(bit3)。
/// </summary>
public static class SceneObjectStateBits
{
    public const uint Opened = 0x01;       // bit0 = 已开启
    public const uint Activated = 0x02;    // bit1 = 已激活
    public const uint Locked = 0x04;       // bit2 = 已锁定
    public const uint Reset = 0x08;        // bit3 = 已重置

    /// <summary>状态位掩码（低 4 位）。</summary>
    public const uint StateMask = 0x0F;

    // 辅助方法
    public static bool HasOpened(uint bits) => (bits & Opened) != 0;
    public static bool HasActivated(uint bits) => (bits & Activated) != 0;
    public static bool HasLocked(uint bits) => (bits & Locked) != 0;
    public static bool HasReset(uint bits) => (bits & Reset) != 0;
}

/// <summary>
/// 服务器权威场景对象状态组件（阶段 C）。
/// 由网络层从 <see cref="Horizon.Game.Message.Sync.SceneObjectSyncPacket"/> 写入，
/// 客户端 ECS 据此驱动宝箱/开关/门/拉杆/传送门的表现。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public partial struct SceneObjectStateAuthComponent
{
    /// <summary>场景对象的全局唯一 ID。</summary>
    [MemoryPackOrder(0)]
    [Id(0)]
    public ulong ObjectId;

    /// <summary>场景对象类型。</summary>
    [MemoryPackOrder(1)]
    [Id(1)]
    public SceneObjectType ObjectType;

    /// <summary>状态位掩码（Opened/Activated/Locked/Reset）。</summary>
    [MemoryPackOrder(2)]
    [Id(2)]
    public uint StateBits;

    /// <summary>冷却结束的服务器 tick（0 表示无冷却）。</summary>
    [MemoryPackOrder(3)]
    [Id(3)]
    public long CooldownEndTick;

    /// <summary>当前归属角色 ID（0 表示无归属，可被任意玩家交互）。</summary>
    [MemoryPackOrder(4)]
    [Id(4)]
    public ulong OwnerCharacterId;

    /// <summary>采样时的服务器 tick（用于 reconciliation / 插值排序）。</summary>
    [MemoryPackOrder(5)]
    [Id(5)]
    public long ServerTick;

    /// <summary>是否已开启（参考 <see cref="EntityStateAuthComponent.IsDead"/> 模式）。</summary>
    public bool IsOpened
    {
        get => (StateBits & SceneObjectStateBits.Opened) != 0;
        set
        {
            if (value) StateBits |= SceneObjectStateBits.Opened;
            else StateBits &= ~SceneObjectStateBits.Opened;
        }
    }

    /// <summary>是否已激活。</summary>
    public bool IsActivated
    {
        get => (StateBits & SceneObjectStateBits.Activated) != 0;
        set
        {
            if (value) StateBits |= SceneObjectStateBits.Activated;
            else StateBits &= ~SceneObjectStateBits.Activated;
        }
    }

    /// <summary>是否已锁定。</summary>
    public bool IsLocked
    {
        get => (StateBits & SceneObjectStateBits.Locked) != 0;
        set
        {
            if (value) StateBits |= SceneObjectStateBits.Locked;
            else StateBits &= ~SceneObjectStateBits.Locked;
        }
    }

    /// <summary>是否已重置。</summary>
    public bool IsReset
    {
        get => (StateBits & SceneObjectStateBits.Reset) != 0;
        set
        {
            if (value) StateBits |= SceneObjectStateBits.Reset;
            else StateBits &= ~SceneObjectStateBits.Reset;
        }
    }
}

/// <summary>
/// 服务器权威场景对象 Transform 组件（阶段 C）。
/// 仅用于可移动场景对象（门、转盘、可推动石块）；
/// 静态场景对象（普通宝箱/开关）不需要本组件，由 <see cref="SceneObjectStateAuthComponent"/> 单独承载状态。
/// 参考 <see cref="AuthTransformComponent"/> 的字段布局，并新增 <see cref="ObjectId"/> 关联。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public partial struct SceneObjectTransformComponent
{
    /// <summary>关联的场景对象 ID（与 <see cref="SceneObjectStateAuthComponent.ObjectId"/> 对齐）。</summary>
    [MemoryPackOrder(0)]
    [Id(0)]
    public ulong ObjectId;

    [MemoryPackOrder(1)] [Id(1)] public float X;
    [MemoryPackOrder(2)] [Id(2)] public float Y;
    [MemoryPackOrder(3)] [Id(3)] public float Z;
    [MemoryPackOrder(4)] [Id(4)] public float Pitch;
    [MemoryPackOrder(5)] [Id(5)] public float Yaw;
    [MemoryPackOrder(6)] [Id(6)] public float Roll;

    /// <summary>采样时的服务器 tick（用于 reconciliation / 插值排序）。</summary>
    [MemoryPackOrder(7)] [Id(7)] public long ServerTick;
}
