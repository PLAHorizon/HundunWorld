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

    /// <summary>
    /// 角色昵称（仅玩家实体有效，NPC 为空）。随 Identity 于 Spawn 一次性下发（Identity 为 spawn-only，
    /// 不进每 tick 的 Transform/State 通道），故对带宽影响可忽略。旧字段 NetworkId/EntityType/OwnerId
    /// 编号 0/1/2 保持不变，新字段仅追加于末尾以维持向后兼容。
    /// </summary>
    [MemoryPackOrder(3)]
    [Id(3)]
    public string Nickname;

    /// <summary>
    /// 角色称号名（仅玩家实体有效，NPC 为空）。与 <see cref="Nickname"/> 同为 spawn-only。
    /// 空表示"未佩戴称号"——客户端据此回退到本地默认表现，不得把空值当成"无名"。
    /// </summary>
    [MemoryPackOrder(4)]
    [Id(4)]
    public string Title;

    /// <summary>
    /// 怪物模板编号（仅怪物/NPC 实体有效，玩家为 0）。spawn-only。
    /// <para>客户端拿它做"这只怪长什么样"的键。<b>不用显示名当键</b>：名字会被改、会被本地化，
    /// 而外观一旦按名字匹配，改个名字就会让整只怪换模型。</para>
    /// </summary>
    [MemoryPackOrder(5)]
    [Id(5)]
    public int TemplateId;

    /// <summary>模型键（编辑器里配的 <c>model_key</c>，客户端外观目录的查表键；空 = 未配置）。</summary>
    [MemoryPackOrder(6)]
    [Id(6)]
    public string ModelKey;

    /// <summary>
    /// 体型缩放（精英常见 1.0/1.2/1.5）。
    /// <para><c>0</c> 与负数都表示"服务端未下发"，客户端按 1.0 处理 —— 绝不能拿 0 当默认值，
    /// 那会把怪压成看不见的一-个点。</para>
    /// </summary>
    [MemoryPackOrder(7)]
    [Id(7)]
    public float ModelScale;

    /// <summary>
    /// 待机姿态：0站 1坐 2卧 3潜水；<c>-1 = 服务端未下发</c>（客户端保留自己的默认）。
    /// <para>必须能表达"未下发"：0 本身就是合法姿态（站立），用 0 当哨兵会让所有存量怪被迫显式配姿态。</para>
    /// </summary>
    [MemoryPackOrder(8)]
    [Id(8)]
    public int StandState;

    /// <summary>
    /// 怪物档位 rank：0普通 1精英 2稀有精英 3首领 4世界BOSS（玩家实体为 0）。
    /// <para>客户端据此把骷髅级显示成 "??" 并给精英加边框——让分色本身由等级差算，
    /// 但"首领恒为骷髅"这条判据需要 rank（见 <c>MonsterDifficultyCore.Of</c>）。</para>
    /// </summary>
    [MemoryPackOrder(9)]
    [Id(9)]
    public int Rank;

    /// <summary>
    /// 起手时就在这只怪身上的吟唱（**给晚进场的人**； Spawn 是补发的唯一通路）。
    /// <para>为什么要占 spawn-only 的槽位而不是新开一条事件：施法起手事件只在"开始"那个瞬间发给
    /// 当时看得见的人。玩家走进一片正在施法的怪中间时，那些怪早已读条过半，
    /// 而它们的起手事件对这个会话从来没有发出过——表现就是"这只怪明明在读条，我看到的却是一根空的血条"，
    /// 直到它下一次起手才恢复正常。补发路径只有 Spawn，所以这份状态必须随 Spawn 一起给。</para>
    /// <para>"有没有在读条"的判据是 <see cref="CastTotalSeconds"/> &gt; 0（瞬发本来就没有条，不发），
    /// 而不是 <see cref="CastSkillId"/> != 0：运行态与内容包对不上时挂载查不出技能 id，
    /// 那种情况下条子照样该画（时长在运行态里），只是没有技能身份。</para>
    /// </summary>
    [MemoryPackOrder(10)]
    [Id(10)]
    public int CastSkillId;

    /// <summary>本次吟唱读满需要的总秒数（&lt;=0 = 这只怪此刻没在读条，客户端其余四栏一律不读）。</summary>
    [MemoryPackOrder(11)]
    [Id(11)]
    public float CastTotalSeconds;

    /// <summary>服务端认为已经过去了多少秒（客户端据此把条子画在半程，而不是从 0 重放）。</summary>
    [MemoryPackOrder(12)]
    [Id(12)]
    public float CastElapsedSeconds;

    /// <summary>系别掩码（与 <c>MonsterCastPayload.SchoolMask</c> 同一套位：位号==域号）。</summary>
    [MemoryPackOrder(13)]
    [Id(13)]
    public int CastSchoolMask;

    /// <summary>可否打断（1=可，画金边）。与起手事件同一个判据，晚进场的人看到的描边必须一致。</summary>
    [MemoryPackOrder(14)]
    [Id(14)]
    public int CastInterruptible;
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

    /// <summary>
    /// NPC 待机姿态（0站 1坐 2工作 3持械）：<b>每个快照都下发</b>，因为作息会随一天时段变
    /// （铁匠白天打铁、夜里收摊），而 <c>NetworkIdentityAuthComponent</c> 是 spawn-only。
    /// <para><c>-1 = 非 NPC / 未下发</c>（客户端保留自己的默认）。0 本身是合法姿态，
    /// 不能拿 0 当哨兵。</para>
    /// </summary>
    [MemoryPackOrder(9)] [Id(9)] public int NpcStandState;

    /// <summary>
    /// NPC 服务位图（与 <c>NpcServiceCore</c> 同位：Gossip=1 QuestGiver=2 QuestTurnIn=4
    /// Vendor=8 Trainer=16 CraftTrainer=32 CraftStation=64），随每个快照下发。
    /// <para><c>-1 = 非 NPC / 未下发</c>；<b>0 = 确知"没有任何服务"</b>。
    /// 两者必须能分开：山贼头目的服务位确实是 0，若也当成"未下发"，
    /// 客户端就会退到旧的本地目录去猜，猜错时表现为它头上多出一个点不开的对话按钮。</para>
    /// </summary>
    [MemoryPackOrder(10)] [Id(10)] public int NpcServiceFlags;

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

    /// <summary>
    /// 简化访问：这只怪正在<b>回位</b>，按 AzerothCore 的口径<b>根本打不到</b>
    /// （<code>Creature::IsEvadingAttacks()</code> 挂在受击方那侧，五处门共用它）。
    /// <para>为什么要下发：服务端在结算前就把这一手拒了，客户端如果不知道，玩家看见的是
    /// "我按了技能而什么都没有"——既没有伤害数字也没有解释。有了这一位，客户端能把
    /// 名牌/目标框描灰并在按下去时给人话原因（服务端仍然是唯一权威）。</para>
    /// <para>只有 <c>Dead</c> / <c>Evading</c> / <c>Stunned</c> / <c>Rooted</c> 四位真的被服务端写过；
    /// 其余几位仍然没有载体（`Hidden`/`Frozen` 等来源都还没做）。</para>
    /// </summary>
    public bool IsEvading
    {
        get => (StateBits & EntityStateBits.Evading) != 0;
        set
        {
            if (value) StateBits |= EntityStateBits.Evading;
            else StateBits &= ~EntityStateBits.Evading;
        }
    }

    /// <summary>
    /// 尸体可以拾取（还在地上且窗口没关）。与 <see cref="IsDead"/> 配合：死位让名牌收起、动画倒地，
    /// 这一位才让"拾取"入口可用。窗口一关这一位就收，玩家不会再对着取不了的尸体按 G。
    /// </summary>
    public bool IsCorpseInteractable
    {
        get => (StateBits & EntityStateBits.CorpseInteractable) != 0;
        set
        {
            if (value) StateBits |= EntityStateBits.CorpseInteractable;
            else StateBits &= ~EntityStateBits.CorpseInteractable;
        }
    }

    /// <summary>
    /// 被眩晕（控制状态层的 <c>Stun</c>）：动不了、也起不了手；自动近战不受影响
    /// （AC 的 `AttackerStateUpdate` 只查 `UNIT_FLAG_PACIFIED`）。
    /// <para>这一个位由 DS 按玩家身上的状态表写（切片 59）、客户端读进 `ControlStateManager`（期 2a）。</para>
    /// </summary>
    public bool IsStunned
    {
        get => (StateBits & EntityStateBits.Stunned) != 0;
        set
        {
            if (value) StateBits |= EntityStateBits.Stunned;
            else StateBits &= ~EntityStateBits.Stunned;
        }
    }

    /// <summary>被沉默（控制状态层的 <c>Silence</c>）：能走能打，只是放不出技能。</summary>
    public bool IsSilenced
    {
        get => (StateBits & EntityStateBits.Silenced) != 0;
        set
        {
            if (value) StateBits |= EntityStateBits.Silenced;
            else StateBits &= ~EntityStateBits.Silenced;
        }
    }

    /// <summary>被定身（控制状态层的 <c>Root</c>）：只拦移动，不拦施法/出手。</summary>
    public bool IsRooted
    {
        get => (StateBits & EntityStateBits.Rooted) != 0;
        set
        {
            if (value) StateBits |= EntityStateBits.Rooted;
            else StateBits &= ~EntityStateBits.Rooted;
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

    /// <summary>怪物正在回位（v1 的 Leash / v2 的 Evade）——这段时间打不到它。</summary>
    public const uint Evading = 1u << 5;

    /// <summary>
    /// 被定身（控制状态层的 <c>Root</c>）：这一位今天**有写方**（DS 按玩家身上的状态表写），
    /// 客户端据此禁掉移动输入的表现（施法仍允许——那是 <see cref="Stunned"/> 与沉默的差别）。
    /// <para>尾部追加位不升协议版（切片 18 的 <see cref="Evading"/> 就是这么加的）。</para>
    /// </summary>
    public const uint Rooted = 1u << 6;

    /// <summary>
    /// 被沉默（控制状态层的 <c>Silence</c>）：能走能打，只是**放不出技能**（AC 的
    /// `SPELL_FAILED_SILENCED`，`Spell.cpp:6863-6891`）。
    /// <para>与 <see cref="Stunned"/> 分开一位是必须的：合成一位就分不清"能不能走"与"能不能放"，
    /// 客户端只能二选一地犯错。尾部追加，不升协议版。</para>
    /// </summary>
    public const uint Silenced = 1u << 7;

    /// <summary>
    /// 尸体**可以拾取**（还留在世界里、且可拾取窗口没关）。
    /// <para>客户端只看得见快照，看不见网关那份实体描述符——只把可交互位写进描述符，
    /// 客户端就永远点不到尸体（"尸体还在但点不了"）。它与 <see cref="Dead"/> 是两位：
    /// 死了但窗口已关的尸体会带 Dead 不带这一位，客户端据此收起"可拾取"高亮而不是让它继续亮着。</para>
    /// <para>尾部追加位不升协议版（<see cref="Evading"/> / <see cref="Rooted"/> 同例）。</para>
    /// </summary>
    public const uint CorpseInteractable = 1u << 8;
}

/// <summary>
/// 玩家基础档案的**变更通道**（v13）。
/// <para>
/// 与 <see cref="NetworkIdentityAuthComponent"/> 里的 Nickname/Title 分工：Identity 是 spawn-only，
/// 只在实体首次出现时携带；本组件专供"运行中变了"的场合（改名、换称号、握手时档案没取到后来才取到），
/// 仅在字段与上次下发不一致时随 delta 出现，因此对稳态带宽几乎无影响。
/// </para>
/// <para>
/// 之所以不复用 Identity 承载变更：DS 通道只在 Spawn 那一帧构造 Identity，要在运行中改昵称就得重发
/// Spawn，而重复 Spawn 在客户端会销毁重建远程角色（视觉表现为闪一下/位置重置）。
/// </para>
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public partial struct ProfileAuthComponent
{
    /// <summary>角色昵称（null = 本次未变更，客户端保留原值）。</summary>
    [MemoryPackOrder(0)]
    [Id(0)]
    public string Nickname;

    /// <summary>角色称号名（null = 本次未变更；空串 = 明确"未佩戴"）。</summary>
    [MemoryPackOrder(1)]
    [Id(1)]
    public string Title;

    /// <summary>角色等级（0 = 本次未变更）。</summary>
    [MemoryPackOrder(2)]
    [Id(2)]
    public int Level;
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
