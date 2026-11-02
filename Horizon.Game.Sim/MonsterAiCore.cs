using System;
using System.Runtime.CompilerServices;
// v1 与 v2 共用同一套"起不起手 / 因不因距离回家"判据（见该类的存在理由）。
using MonsterAiGate = Horizon.Game.Sim.Monsters.MonsterAiGate;
using MonsterRank = Horizon.Game.Sim.Monsters.MonsterRank;
using MonsterFollowType = Horizon.Game.Sim.Monsters.MonsterFollowType;
using MonsterPatrolCore = Horizon.Game.Sim.Monsters.MonsterPatrolCore;
using MonsterPatrolRoute = Horizon.Game.Sim.Monsters.MonsterPatrolRoute;
using MonsterPatrolSelf = Horizon.Game.Sim.Monsters.MonsterPatrolSelf;
using MonsterNavGrid = Horizon.Game.Sim.Monsters.MonsterNavGrid;
using MonsterPathCore = Horizon.Game.Sim.Monsters.MonsterPathCore;

namespace Horizon.Game.Sim;

/// <summary>
/// 怪物 AI 状态（MMORPG 开发计划 M3.3）。
/// <para>
/// 状态语义（确定性状态机，无隐式转移）：
/// <list type="bullet">
///   <item><see cref="Idle"/>：出生/待机；超过 <see cref="MonsterAiConfig.IdleToPatrolSeconds"/> 转 <see cref="Patrol"/>。</item>
///   <item><see cref="Patrol"/>：围绕出生点游荡；发现仇恨目标转 <see cref="Chase"/>。</item>
///   <item><see cref="Chase"/>：向目标直线追击；进入 <see cref="MonsterAiConfig.AttackRange"/> 转 <see cref="Attack"/>；
///         超出 <see cref="MonsterAiConfig.LeashRadius"/> 转 <see cref="Leash"/>。</item>
///   <item><see cref="Attack"/>：在射程内按冷却攻击；目标离开射程转 <see cref="Chase"/>。</item>
///   <item><see cref="Leash"/>：脱战回归出生点（不再仇恨）；回到出生点半径内转 <see cref="Idle"/>。</item>
///   <item><see cref="Dead"/>：等待重生；<see cref="MonsterAiSelf.RespawnAtTick"/> 到达后由宿主转 <see cref="Idle"/>。</item>
/// </list>
/// </para>
/// </summary>
public enum MonsterAiState : byte
{
    /// <summary>待机。</summary>
    Idle = 0,

    /// <summary>游荡巡逻。</summary>
    Patrol = 1,

    /// <summary>追击。</summary>
    Chase = 2,

    /// <summary>攻击。</summary>
    Attack = 3,

    /// <summary>脱战回归。</summary>
    Leash = 4,

    /// <summary>死亡等待重生。</summary>
    Dead = 5,

    /// <summary>
    ///  evade（脱战回位）：与 <see cref="Leash"/> 的区别是它带"清威胁表 + 回满血 + 短期免疫新仇恨"。
    /// <para>AI v2 才产出这个状态；v1 仍只用 <see cref="Leash"/>，因此既有金轨迹不受影响。
    /// 保留 <see cref="Leash"/> 而不是复用它：v1 的"回位"只走路、不清表不刷血，
    /// 把新语义塞进旧枚举值会让所有钉住 v1 行为的断言变成"检查一件不存在的事"。</para>
    /// </summary>
    Evade = 6,
}

/// <summary>
/// 怪物 AI 静态配置（来自刷怪点/模板，可在运行期不变更）。
/// <para>所有距离单位为米，与 <c>MovementFormula</c> 的世界尺度一致。</para>
/// </summary>
public struct MonsterAiConfig
{
    /// <summary>巡逻/追击最大移动速度（米/秒）。</summary>
    public float MoveSpeed;

    /// <summary>
    /// 追击/回位速度（米/秒），NPC 内容表 <c>speed_run</c> 那一格。<b>0 = 没配，退回 <see cref="MoveSpeed"/></b>。
    /// <para>与 <see cref="WalkSpeed"/> 同一条纪律：缺省值必须让旧内容与金轨迹<b>逐字不变</b>；
    /// 只有内容 NPC 的刷点会填它（怪物模板没有这一格）。"原本就不能动"的（MoveSpeed ≤ 0）不会因为
    /// 多了跑速而开始追人——<see cref="ChaseSpeed"/> 先判可动性。</para>
    /// </summary>
    public float RunSpeed;

    /// <summary>
    /// 巡逻/游荡速度（米/秒），内容表 <c>walk_speed</c> 那一格。
    /// <para><b>0 = 没配</b>，退回 <see cref="MoveSpeed"/>——那正是接进这一格之前 v1 的行为，
    /// 所以金轨迹与全部旧夹具逐字不变。以前 v1 的游荡直接拿 MoveSpeed（那是"追你"的速度），
    /// 而 v2 拿 WalkSpeed ⇒ 种子内容 <c>walk=2.5 / move=4</c> 时默认档的怪散步比追你快，
    /// 而编辑器那一格对它根本不生效。速度选择在 <c>MonsterPatrolCore.WanderSpeed</c> 只有一份。</para>
    /// </summary>
    public float WalkSpeed;

    /// <summary>巡逻游荡半径（距出生点）。</summary>
    public float WanderRadius;

    /// <summary>仇恨半径：目标进入即开始追击。</summary>
    public float AggroRadius;

    /// <summary>
    /// 这只怪的<b>生效等级</b>（装配层从切片 27 那一个权威带过来）。<b>0 = 这一路没带等级 ⇒ 不参与等级差修正</b>。
    /// <para>用途只有一个：AC 的"等级差改视野"（<c>Creature::GetAggroRange</c>，
    /// 玩家比怪高 ⇒ 更容易被先看见；怪比人高 ⇒ 视野收窄），判据住在
    /// <c>MonsterAiGate.EffectiveAggroRadius</c>，v1/v2 同一份、且都<b>逐候选</b>问。</para>
    /// <para>缺省选 0（不调）而不是选 1：让"接入这一格"对旧内容、旧夹具与金轨迹<b>逐字零变化</b>，
    /// 涨的只是作用域不是手感（与 <see cref="FollowType"/> 缺省"会动"、<c>IsPassive</c> 缺省"主动"同一族选择）。</para>
    /// </summary>
    public int Level;

    /// <summary>
    /// 索敌扇形角度（编辑器"索敌角度"那一格）。<b>缺省 0 或 360 都是全向</b>——
    /// 这一格没配不等于"看不见人"（判据住在 <c>MonsterAiGate.InAggroCone</c>，v1/v2 同一份）。
    /// <para>以前 v1 只比半径、根本没这个输入，于是默认档（生产全部种子怪）的怪画了扇形
    /// 也从背后咬人；装配层现在把行为表那一格盖进来（<c>BuildAiConfigFromContent</c> 那一个出口）。</para>
    /// </summary>
    public float AggroConeDegrees;

    /// <summary>
    /// 检测位（bit0 视觉 / bit1 听觉 / bit2 隐蔽探测）。**今天只有 bit2 有判据**：
    /// 藏着的人（<see cref="MonsterAiTarget.IsHidden"/>）只有带 bit2 的怪才看得见
    /// （判据主人 <c>MonsterAiGate.CanDetectTarget</c>，v1/v2 同一份）。
    /// <para>缺省 <c>0x1</c>＝只有视觉、看不见隐身者——与"接进来之前"的行为一致
    /// （那时根本没人藏，所以这条判据不改变任何既有内容；金轨迹仍逐帧一致）。</para>
    /// </summary>
    public int DetectMask;

    /// <summary>
    /// 导航网格（服务端 Z-up 水平面）。**只有一处用处：索敌时的视线门**——
    /// 墙/悬崖挡住就看不见（AC 把 <c>IsWithinLOSInMap</c> 写在"能不能打"的基础判据里）。
    /// <para><c>IsValid=false</c>（缺省）= 不做视线过滤：今天生产世界还没有 MONSTERNAV 图，
    /// 这一条在那种状态下行为零变化；装了编辑器烘的图才看得出差别。
    /// 判据主人是 <c>MonsterPathCore.HasLineOfSight</c>（寻路拉直用的同一个），不另写一份。</para>
    /// </summary>
    public MonsterNavGrid NavGrid;

    /// <summary>脱战半径：距出生点超过该距离立即回归。</summary>
    public float LeashRadius;

    /// <summary>攻击射程。</summary>
    public float AttackRange;

    /// <summary>攻击冷却（秒）。</summary>
    public float AttackCooldownSeconds;

    /// <summary>待机转巡逻的时间（秒）。</summary>
    public float IdleToPatrolSeconds;

    /// <summary>最大生命值。</summary>
    public int MaxHp;

    /// <summary>攻击力（每次命中的基础伤害；最终伤害由战斗结算层决定）。</summary>
    public int AttackPower;

    /// <summary>死亡后重生延迟（秒）。</summary>
    public float RespawnDelaySeconds;

    /// <summary>
    /// 移动方式（编辑器"移动方式"那一格）。v1 以前<b>没有这个输入</b>，
    /// 于是"站桩"档的默认怪照样追击/回位——那一格对生产多数的怪是装饰。
    /// <para>缺省必须是 <c>GroundWalk</c>（会动）：写成 <c>Static</c> 会让所有没带这一格的旧内容
    /// 集体变成站桩怪，而那是不可见的整体手感变化。</para>
    /// </summary>
    public MonsterFollowType FollowType;

    /// <summary>
    /// 这只怪是不是<b>被动怪</b>（内容表 <c>is_aggressive</c> 那一格的反面）。
    /// <para>缺省必须是 <c>false</c>（＝主动，和接进这一格之前<b>逐字相同</b>）：
    /// bool 的零值是 <c>false</c>，所以这里用反向旗标。若写成 <c>IsAggressive</c>，
    /// 所有没带这一格的旧内容与旧夹具都会一夜之间变成"站着挨打不还手"的被动怪——
    /// 那是看不见的手感整体变化（与 <see cref="FollowType"/> 缺省选"会动"同一个理由）。</para>
    /// <para>以前这一格只有 v2 读（两处 inline），v1 根本没有这个输入 ⇒
    /// 默认档的怪配成被动也照样先咬人。判据住在 <c>MonsterAiGate.MayInitiateOnSight</c>。</para>
    /// </summary>
    public bool IsPassive;

    /// <summary>
    /// 这只怪的等级档（装配层从内容配置带过来，缺省 Normal）。
    /// <para>它唯一的用途是"世界 BOSS 整段不做距出生点检查"——判据本身住在
    /// <c>MonsterAiGate.LeashApplies</c>（v1/v2 共用一份），这里只带那个输入，
    /// 免得"哪一档豁免"变成两处各写一遍的清单。</para>
    /// </summary>
    public MonsterRank Rank;

    /// <summary>
    /// 回位（脱战跑回家）之后的"不接新敌"秒数。
    /// <para>这条以前只有 v2 那份状态机有：v1 的怪跑回家**不回血也不开免疫窗**，
    /// 于是远程风筝可以把一只种子怪磨到一丝血再耗死，而开了威胁表的怪会"唰"地满血。
    /// 装配层有内容包时按行为表那一份覆盖，没有时取 <see cref="MonsterBehaviorConfig.Default"/> 的同一数字。</para>
    /// </summary>
    public float EvadeImmunitySeconds;

    /// <summary>
    /// 生产可用的一组默认值（便于测试与快速接入）。
    /// 取值保守：4 米/秒移动、10 米仇恨、30 米脱战——与既有 AOI chunk(16m) 尺度相容。
    /// </summary>
    /// <summary>
    /// 追击/回位速度：配了跑速且**本来就能动**才用跑速，否则退回走速。
    /// <para>先判 <c>MoveSpeed &gt; 0</c>：NPC 站桩档的刷点把移动速度盖成 0，
    /// 那是"不会动"的产品语义；多一格跑速不该让它开始追人。</para>
    /// </summary>
    public static float ChaseSpeed(in MonsterAiConfig config)
    {
        if (config.MoveSpeed <= 0f) return 0f;
        return config.RunSpeed > 0f ? config.RunSpeed : config.MoveSpeed;
    }

    public static MonsterAiConfig Default => new MonsterAiConfig
    {
        MoveSpeed = 4f,
        WanderRadius = 6f,
        AggroRadius = 10f,
        // 缺省全向：与行为表那一份的缺省同一个数字（闸门按值对账），且判据把 0 也当全向，
        // 所以没带这一格的旧结构体（默认 0）不会因为"扇形=0°"而变成谁都不理。
        AggroConeDegrees = 360f,
        LeashRadius = 30f,
        AttackRange = 2.5f,
        AttackCooldownSeconds = 1.5f,
        IdleToPatrolSeconds = 3f,
        MaxHp = 100,
        AttackPower = 10,
        RespawnDelaySeconds = 15f,
        // 回位免疫窗的缺省与行为表那一份**同一个数字**（闸门按值对账）。
        EvadeImmunitySeconds = Horizon.Game.Sim.Monsters.MonsterBehaviorConfig.EvadeImmunityDefaultSeconds,
    };
}

/// <summary>
/// 怪物 AI 自身状态（**权威**，由宿主持久化并逐 tick 回传）。
/// <para>宿主（Orleans <c>ZoneShardGrain</c> / UE5 DS）持有本结构，AI 内核只做纯函数决策。</para>
/// </summary>
public struct MonsterAiSelf
{
    /// <summary>
    /// 从哪一拍开始"下一步就会踩进不可走的格子"（0 = 现在没被挡住）。
    /// <para>只有装了导航图时才可能非 0；窗口长度问 <c>MonsterAiGate.NoPathEvadeSeconds</c>（与 v2 同一格），
    /// 但判法不同：v1 不跑 A*，只看"这一步会不会踩进 0 代价格"——它没有绕路能力，挡住就是够不到。</para>
    /// </summary>
    public long NoPathStartTick;

    /// <summary>当前世界坐标 X（Z-up）。</summary>
    public float X;

    /// <summary>当前世界坐标 Y（Z-up）。</summary>
    public float Y;

    /// <summary>当前世界坐标 Z（Z-up）。</summary>
    public float Z;

    /// <summary>当前朝向（弧度，仅表现用；不参与位置数学）。</summary>
    public float Yaw;

    /// <summary>出生点 X（脱战回归锚点）。</summary>
    public float SpawnX;

    /// <summary>出生点 Y。</summary>
    public float SpawnY;

    /// <summary>出生点 Z。</summary>
    public float SpawnZ;

    /// <summary>当前状态。</summary>
    public MonsterAiState State;

    /// <summary>当前生命值。</summary>
    public int Hp;

    /// <summary>当前攻击冷却剩余（秒；≤0 表示可攻击）。</summary>
    public float AttackCooldownRemaining;

    /// <summary>进入当前状态的累计时长（秒），用于 Idle→Patrol 等时长驱动转移。</summary>
    public float StateElapsedSeconds;

    /// <summary>待机/巡逻的方向符号（+1/-1），由宿主以确定性方式翻转，避免内核使用随机源。</summary>
    public float PatrolDirection;

    /// <summary>
    /// 沿巡逻线走时的游标（当前点下标 / 往返方向 / 到点后已停的秒数）。
    /// <para>v1 以前没有这一格，因为 v1 **从来不读巡逻线**：路线只在 v2 那段被解析进
    /// <c>MonsterRuntimeInputs.Route</c>，于是默认档（<c>ai_profile=0</c>，生产里全部种子怪）
    /// 的怪画了线也只在出生点周围按 <c>wander_radius</c> 游荡。</para>
    /// <para>内核按值进出：入口读这一格，出口把推进后的同一格经
    /// <see cref="MonsterAiDecision.Patrol"/> 原样带回，宿主无条件写回。
    /// 游标本身由 <c>MonsterPatrolCore.Advance</c> 推进——<b>不在这里重写往返与停留判据</b>，
    /// 两份判据迟早会在"线的哪一段该停多久"上分家。</para>
    /// </summary>
    public MonsterPatrolSelf Patrol;

    /// <summary>死亡后允许重生的服务器 tick；仅 <see cref="MonsterAiState.Dead"/> 有意义。</summary>
    public long RespawnAtTick;

    /// <summary>当前仇恨目标实体（0 = 无）。</summary>
    public ulong ThreatTargetId;

    /// <summary>
    /// 嘲讽钉住的目标（0 = 没被钉）。与 <see cref="TauntUntilTick"/> 成对使用：
    /// <b>只有截止时间没有对象，锁就没法执行</b>（也才谈得上"那个人不在场时当场撤锁"）。
    /// </summary>
    public ulong TauntTargetId;

    /// <summary>嘲讽窗口的截止 tick（0 = 无窗口）。窗内不查脱战距离，见 <c>MonsterAiGate</c>。</summary>
    public long TauntUntilTick;

    /// <summary>
    /// 上一次"延长 leash 计时器"的 tick（<b>0 = 从没挨过打</b>）。
    /// <para>AC 在挨打时（<c>Unit.cpp</c> 的伤害结算里 <c>UpdateLeashExtensionTime()</c>）
    /// 与丢失控制时刷新这个时刻，随后 <c>Creature::CanCreatureAttack</c> 在
    /// <c>last + GetLeashTimer() &gt; now</c> 期间**整段跳过回家距离检查**——这就是"可以风筝"。
    /// 判据主人是 <c>MonsterAiGate.KitingGraceActive</c>（v1/v2 同一份），宿主是唯一写方：
    /// 只在伤害落点写，内核只读。</para>
    /// </summary>
    public long LastLeashExtensionTick;

    /// <summary>
    /// 共享 leash 计时器的组号（<b>0 = 自己一组</b>）。
    /// <para>AC 在 <c>Creature::CallAssistance</c> 里把首领那个 <c>shared_ptr&lt;time_t&gt;</c> 交给每只被叫来的盟友
    /// （`Creature.cpp:231`）：**打其中任何一只都会把整组的宽限窗刷新**，所以"拉一只，同伴一起回家"。
    /// 本作把这件事表达成组号：被叫来的盟友与首领同组；伤害落点刷新同组的所有成员（宿主那一层做）。
    /// 内核只读它判"我和它是不是同一组"以外的部分不关心——组号本身不参与 AI 决策，
    /// 参与者只有宿主与 <see cref="KitingGraceActive"/> 的输入（<see cref="LastLeashExtensionTick"/>）。</para>
    /// </summary>
    public ulong LeashGroupId;

    /// <summary>
    /// "刚刷出来/刚复活"不许主动起手的截止 tick（0 = 不在窗内）。
    /// <para>两个写方：刷怪点重建实体时宿主装一次（只有复活装，首次生成不装——AC 那条判据
    /// 本身就带 <c>if (m_respawnedTime && ...)</c>），内核里 <see cref="MonsterAiState.Dead"/>
    /// 翻回 <see cref="MonsterAiState.Idle"/> 的那一 tick 装一次。</para>
    /// </summary>
    public long NoInitiateUntilTick;

    /// <summary>
    /// 回位免疫窗的截止 tick（0 = 不在窗内）：脱战跑回家后的这几秒<b>不接新敌</b>，但照样挨打。
    /// <para>与 <see cref="NoInitiateUntilTick"/> 是两个不同的窗（复活宽限 / 回位免疫），
    /// 触发点、时长与判据来源都不同——混成一格会让"刚复活"与"刚回家"互相顶掉。</para>
    /// </summary>
    public long EvadeImmuneUntilTick;
}

/// <summary>AI 视野内的候选目标（顺序由宿主给定，必须是**稳定顺序**以保证确定性）。</summary>
public struct MonsterAiTarget
{
    /// <summary>目标实体 ID。</summary>
    public ulong EntityId;

    /// <summary>目标世界坐标 X。</summary>
    public float X;

    /// <summary>目标世界坐标 Y。</summary>
    public float Y;

    /// <summary>目标世界坐标 Z。</summary>
    public float Z;

    /// <summary>目标是否存活（死亡目标不构成仇恨）。</summary>
    public bool IsAlive;

    /// <summary>目标是否可被攻击（同阵营/无敌/NPC 为 false）。</summary>
    public bool IsAttackable;

    /// <summary>
    /// 目标此刻是否**藏着**（隐身/潜行）。藏着的人只有 <c>DetectMask</c> 带 bit2 的怪才看得见。
    /// <para>**今天恒为 false**：本作还没有任何隐身来源（没有隐身技能、没有潜行状态），
    /// 这一位是为了让 `detect_mask` 那一格先有判据可问（D32a 的"接闸口 + 隐身来源先做空实现"）。
    /// 宿主哪天开始填它，行为就自动生效，不需要再改内核。</para>
    /// </summary>
    public bool IsHidden;

    /// <summary>
    /// 目标的<b>等级</b>（玩家实体自己的等级）。<b>0 = 这一路没带等级 ⇒ 不参与等级差修正</b>。
    /// <para>为什么挂在候选身上而不是配置里：AC 的索敌半径是<b>按目标</b>算的
    /// （<c>GetAggroRange(Unit const* target)</c>），所以同一只怪对两个不同等级的人
    /// 本来就该有两个可见半径——把等级塞进配置会把"逐候选"这一半抹掉。</para>
    /// </summary>
    public int Level;
}

/// <summary>AI 单 tick 决策结果，由宿主应用。</summary>
public struct MonsterAiDecision
{
    /// <summary>决策后的状态（宿主写回 <see cref="MonsterAiSelf.State"/>）。</summary>
    public MonsterAiState NextState;

    /// <summary>本 tick 水平位移分量 X（已含速度与 dt，宿主直接加到坐标上）。</summary>
    public float DeltaX;

    /// <summary>本 tick 水平位移分量 Y。</summary>
    public float DeltaY;

    /// <summary>决策后的朝向（弧度，表现用）。</summary>
    public float NextYaw;

    /// <summary>攻击冷却剩余（宿主写回）。</summary>
    public float NextAttackCooldownRemaining;

    /// <summary>本 tick 是否发起一次攻击。</summary>
    public bool WantsAttack;

    /// <summary>本 tick 攻击的目标（<see cref="WantsAttack"/> 为 true 时有效）。</summary>
    public ulong AttackTargetId;

    /// <summary>仇恨目标（宿主写回 <see cref="MonsterAiSelf.ThreatTargetId"/>）。</summary>
    public ulong ThreatTargetId;

    /// <summary>
    /// "刚复活不许主动起手"的截止 tick（宿主写回 <see cref="MonsterAiSelf.NoInitiateUntilTick"/>）。
    /// <para>内核只在 <see cref="MonsterAiState.Dead"/> 翻回 <see cref="MonsterAiState.Idle"/> 的那一 tick
    /// 改写它，其余路径原样带回调用方给的值，所以宿主可以无条件覆盖。</para>
    /// </summary>
    public long NoInitiateUntilTick;

    /// <summary>回位免疫窗的截止 tick（宿主写回 <see cref="MonsterAiSelf.EvadeImmuneUntilTick"/>，待遇同上）。</summary>
    public long EvadeImmuneUntilTick;

    /// <summary>本 tick 是否要把血量刷回满（脱战跑回家；WoW 观感是血条"唰"地满）。</summary>
    public bool HealedToFull;

    /// <summary><see cref="HealedToFull"/> 为 true 时要写回的血量（≤0 表示不该写）。</summary>
    public int HpAfterHeal;

    /// <summary>本 tick 是否发生位移（用于宿主跳过无变化的快照 delta）。</summary>
    public bool Moved;

    /// <summary>
    /// 这一拍是"追不到路"触发的脱战（AC <c>EVADE_REASON_NO_PATH</c>：超过 5 秒够不到目标）。
    /// 只有 v2 且装了导航图时才可能为真（v1 与直线档无从判断"够不到"）。
    /// 宿主拿它计数——否则"怪站在墙前脱战了"和"它自己回家"在日志里同形。
    /// </summary>
    public bool EvadedByNoPath;

    /// <summary>本拍之后的"够不到"起点拍（0 = 没被挡住）。宿主原样写回 <see cref="MonsterAiSelf.NoPathStartTick"/>。</summary>
    public long NextNoPathStartTick;

    /// <summary>
    /// 巡逻游标的**下一拍形态**（原样透传：没走线时就是把入口那份带回）。
    /// <para>宿主无条件写回，所以内核要么带对的、要么一次都不改——不能"看情况才写"，
    /// 那种写法会让某一拍之后游标停在旧值上，表现是怪在同一个点之间来回抖。</para>
    /// </summary>
    public MonsterPatrolSelf Patrol;

    /// <summary>
    /// 这一拍**换了目的地**（= 到了上一个点、开始朝下一个点走）。两条 AI 通路都填这一格：
    /// v1 的游标在 <see cref="Patrol"/> 里，v2 的游标在 <c>MonsterRuntimeState.Patrol</c> 里
    /// （<c>MonsterAiRuntime</c> 那句 <c>ref state.Patrol</c>）⇒ 宿主只比下标会**只看得到 v1**，
    /// 实测形状是"配了 13 点环线的那只怪走满 30 秒，到点计数 0"。
    /// 判据不在这里重写：值直接来自 <c>MonsterPatrolStep.SwitchedPoint</c>（唯一主人）。
    /// </summary>
    public bool PatrolPointSwitched;
}

/// <summary>
/// 确定性怪物 AI 内核（MMORPG 开发计划 M3.3 —— 消灭缺口 G10「无怪物移动模型」与 G9「玩法不可对拍」）。
/// <para>
/// <b>为什么放在 <c>Horizon.Game.Sim</c></b>：与 <see cref="MovementFormula"/> 同源——Orleans
/// 权威路径（<c>ZoneShardGrain.TickAsync</c>）与 UE5 DS 权威路径必须消费**同一份决策数学**，
/// 否则「双份 AI 实现」会像当年的移动公式一样产生跨端漂移，且无法做金轨迹对拍。
/// </para>
/// <para>
/// <b>确定性约束</b>（与 <see cref="MovementFormula"/> 同级）：
/// <list type="bullet">
///   <item>纯静态函数：无全局状态、无 <c>DateTime.Now</c>、无随机源（随机性由宿主以确定性种子注入，
///         内核只消费 <see cref="MonsterAiSelf.PatrolDirection"/> 这类已确定的值）。</item>
///   <item>不使用 SIMD/向量类型；全程 <c>float</c>，运算顺序固定。</item>
///   <item>目标集合的遍历顺序由宿主保证稳定（<c>MonsterAiTarget[]</c> 按 EntityId 升序传入）。</item>
///   <item><c>MathF.Atan2</c> 仅用于 <see cref="MonsterAiDecision.NextYaw"/>（表现层朝向），
///         **永不回灌到位移数学**，故不构成位级一致性风险。</item>
/// </list>
/// </para>
/// <para>
/// 任何改变实体位置/状态迁移的修改必须递增 <see cref="Version"/>，并由宿主在握手中协商，
/// 与 <see cref="MovementFormula.FormulaVersion"/> 同一纪律。
/// </para>
/// </summary>
public static class MonsterAiCore
{
    /// <summary>
    /// 怪物 AI 内核版本。任何影响状态迁移或位移的改动都必须递增（金轨迹对拍锚点）。
    /// <para>v1：首版——Idle/Patrol/Chase/Attack/Leash/Dead 六态机 + 直线追击 + 冷却攻击。</para>
    /// <para>v4：回位整段路（v1 的 Leash / v2 的 Evade）都不再接新敌、也不被外力拉回战斗。
    /// AzerothCore 把这件事做成<b>状态</b>：<c>CreatureAI::EnterEvadeMode</c> 挂
    /// <c>UNIT_STATE_EVADE</c>、<c>SmartAI::MovementInform</c> 到达点位才清，
    /// 而本作此前只有"到家后 N 秒"那一半——于是怪正往家跑时，同族求援能把它叫回去打、
    /// 嘲讽能把它半路拽走（玩家看得见"坦克喊了一嗓子，怪掉头跑了半步又继续回家"）。
    /// 判据住在 <c>MonsterAiGate.ReturningHome</c>／<c>BlockedFromNewEnemy</c>／<c>CanBeForcedIntoCombat</c>。</para>
    /// <para>v3：v1 的怪回位也刷满血并开免疫窗（此前只有 v2 那份状态机做这两件事，
    /// 于是"远程风筝把怪磨到残血再耗死"对全部种子怪成立，而开了威胁表的怪会唰地满血）。
    /// 免疫秒数与行为表同一个主人（<c>MonsterBehaviorConfig.EvadeImmunityDefaultSeconds</c>）。
    /// 金轨迹这一版<b>实测逐帧重放仍然一致</b>，但那<b>不算</b>"回位刷血有对拍资产"：
    /// 场景里两份 SPAWN 的脱战半径都是 50 米、玩家固定在 -5 且整场不移动，
    /// 怪最多离自己出生点 45 米 ⇒ <b>Leash 分支一次都没被走到</b>。
    /// 这两件事的证据只有内核与宿主那两条真跑用例（详见设计文档 §11.3 那条覆盖空洞）。</para>
    /// <para>v2：三条 AzerothCore 口径接进来（判据住在 <c>MonsterAiGate</c>，与 v2 状态机共用）：
    /// 刚复活的 5 秒不许<b>主动</b>起手、被嘲讽钉住的窗口不查脱战距离、世界 BOSS 整段不查距离。
    /// 三格都填缺省（0 / 0 / Normal）时判据与 v1 逐字相同——<c>Traces/gameplay_trace_600.txt</c>
    /// 在改动前后逐帧重放一致，所以这一版涨的是<b>能力</b>而不是手感。</para>
    /// <para>v5：站桩档（<c>follow_type=3</c>）的"不产生位移"判据接进来（主人是
    /// <c>MonsterPatrolCore.FollowProducesMovement</c>，与 v2 共用），并因此让站桩档不参与
    /// "离家过远→回位"那段判定（走不回去的怪进了回位就是死锁，而切片 18 之后那还会变成打不到）。
    /// <c>FollowType</c> 的缺省就是 <c>GroundWalk</c>（0），旧内容与旧夹具一律不带这一格
    /// ⇒ 位移收口那条 <c>if</c> 一次都不进，<c>Traces/gameplay_trace_600.txt</c> 仍逐帧一致。</para>
    /// <para>v6：巡逻线接进 v1（<c>Decide</c> 尾部新增可选的 route 入参，游标存在
    /// <c>MonsterAiSelf.Patrol</c> 并经 <c>MonsterAiDecision.Patrol</c> 原样带回）。
    /// 判据仍是 v2 那份主人 <c>MonsterPatrolCore.Advance</c>，这里只把线给出的目的地换成直线位移。
    /// <b>没带线（route=null，即所有既有调用方与全部旧内容）时逐字不变</b>：走的是原来那条
    /// "按 wander_radius 绕出生点游荡"的分支，金轨迹仍可当护栏。</para>
    /// <para>v7：v1 的索敌也查扇形（<c>MonsterAiConfig.AggroConeDegrees</c> 新格 +
    /// <c>MonsterAiGate.InAggroCone</c> 那一个主人，v2 的 inline 式子删掉）。
    /// <b>缺省即全向</b>（0 或 360 都放行，旧内容与旧夹具一次都不进扇形分支），
    /// 所以金轨迹仍逐帧一致；涨的是"编辑器那一格对默认档也成立"。</para>
    /// <para>v9：v1 的**游荡速度**也问主人（<c>MonsterAiConfig.WalkSpeed</c> +
    /// <c>MonsterPatrolCore.WanderSpeed</c>，缺省 0＝退回 move_speed＝接进来之前的行为）；
    /// v2 那一处 inline 的 <c>behavior.WalkSpeed</c> 同时换成问主人，两边从此同一个式子。</para>
    /// <para>v8：v1 也读"被动怪"那一格（<c>MonsterAiConfig.IsPassive</c> +
    /// <c>MonsterAiGate.MayInitiateOnSight</c>，与 v2 同一个主人；v2 那两处 inline 已删）。
    /// <b>缺省 false＝主动</b>（bool 的零值必须等于既有行为，所以这一格用反向旗标而不是
    /// <c>IsAggressive</c>）⇒ 旧内容与金轨迹一次都不进新分支。</para>
    /// <para>v10：索敌半径开始<b>按目标的等级</b>算（AC <c>Creature::GetAggroRange</c>：等级差每级约 1 码、
    /// 只往负方向截到 −25、上下限 45/5 码）。新增两根输入：<c>MonsterAiConfig.Level</c>（这只怪的生效等级，
    /// 由切片 27 那一个权威递过来）与 <c>MonsterAiTarget.Level</c>（候选自己的等级），
    /// 判据只有一个主人 <c>MonsterAiGate.EffectiveAggroRadius</c>，v1/v2 都<b>逐候选</b>问它。
    /// <b>任一侧等级为 0（＝这一路没带等级）就整条不调</b> ⇒ 旧内容、旧夹具与金轨迹逐字不变，
    /// 涨的是"高等级玩家更容易被先看见"这条 WoW 手感终于对两条通路都成立。</para>
    /// </summary>
    public const int Version = 10;

    /// <summary>
    /// 推进一 tick 的怪物 AI 决策。
    /// </summary>
    /// <param name="self">自身状态（引用只读传入，内核不改动调用方结构）。</param>
    /// <param name="targets">视野内候选目标（**必须按 EntityId 升序**，保证确定性）。</param>
    /// <param name="dt">时间步长（秒），典型 1/60。</param>
    /// <param name="config">静态配置。</param>
    /// <param name="serverTick">当前服务器 tick（仅用于计算 <see cref="MonsterAiSelf.RespawnAtTick"/>）。</param>
    /// <returns>本 tick 决策；宿主负责应用到权威状态与快照。</returns>
    public static MonsterAiDecision Decide(
        in MonsterAiSelf self,
        ReadOnlySpan<MonsterAiTarget> targets,
        float dt,
        in MonsterAiConfig config,
        long serverTick,
        int ticksPerSecond = 60,
        // 画好的巡逻线（缺省 null = 这只怪没有线，沿出生点游荡，与接进这一格之前逐字相同）。
        // 做成**尾部可选参数**是为了：所有既有调用方（含金轨迹夹具与二十来条内核用例）
        // 不用改一行也能重编，而"没带线"与"带了一条线"在同一份夹具上只差这一格。
        MonsterPatrolRoute? route = null)
    {
        var decision = new MonsterAiDecision
        {
            NextState = self.State,
            NextYaw = self.Yaw,
            NextAttackCooldownRemaining = self.AttackCooldownRemaining,
            ThreatTargetId = self.ThreatTargetId,
            // 两个窗的截止都原样带回；只有各自的触发支会改写（见字段注释）。
            NoInitiateUntilTick = self.NoInitiateUntilTick,
            EvadeImmuneUntilTick = self.EvadeImmuneUntilTick,
            // 游标同样原样带回：宿主是无条件写回，任何一条早退路径都必须带"入口那一份"，
            // 否则"停在同一个点之间来回抖"会从这一格里长出来。
            Patrol = self.Patrol,
            DeltaX = 0f,
            DeltaY = 0f,
            Moved = false,
            WantsAttack = false,
            AttackTargetId = 0UL,
        };

        // 非法 dt：不做任何推进（与 MovementFormula 的 dt<=0 早退语义一致）。
        // 注意：早退发生在冷却递减之前——保持"零时长不改变任何状态"的纯函数语义，
        // 避免宿主在暂停/追帧场景下因重复调用而累积冷却漂移。
        if (dt <= 0f) return decision;

        // 冷却递减（无论何种状态都推进，避免脱战期间冷却"冻结"）。
        if (decision.NextAttackCooldownRemaining > 0f)
        {
            decision.NextAttackCooldownRemaining -= dt;
            if (decision.NextAttackCooldownRemaining < 0f) decision.NextAttackCooldownRemaining = 0f;
        }

        // --- 死亡分支：只等待重生 ---
        if (self.State == MonsterAiState.Dead)
        {
            if (self.RespawnAtTick > 0 && serverTick >= self.RespawnAtTick)
            {
                decision.NextState = MonsterAiState.Idle;
                decision.ThreatTargetId = 0UL;
                decision.NextAttackCooldownRemaining = 0f;
                // 复活（不是首次生成）才装宽限：AC 那条判据本身就写着 `if (m_respawnedTime && ...)`。
                // 另一个写方是宿主——刷怪点重建实体那一路（v1 的怪绝大多数从这里复活）。
                decision.NoInitiateUntilTick = MonsterAiGate.RespawnGraceUntil(serverTick, ticksPerSecond);
            }
            return decision;
        }

        // 血量归零 → 进入死亡（宿主据此广播 Despawn/DeathPacket）。
        if (self.Hp <= 0)
        {
            decision.NextState = MonsterAiState.Dead;
            decision.ThreatTargetId = 0UL;
            decision.Moved = false;
            return decision;
        }

        // --- 目标选择 ---
        // 语义区分（关键）：
        //   * 仇恨半径（AggroRadius）只负责"**获取**新目标"——Idle/Patrol 状态下的索敌范围。
        //   * 一旦进入 Chase/Attack，已锁定的目标在"仍存在/存活/可攻击"期间**持续有效**，
        //     不受仇恨半径约束（否则怪物追出 10 米就会立刻丢失目标回 Idle，表现为"追两步就发呆"）。
        //     追逐的终止条件由脱战半径（LeashRadius）单独负责。
        var threatId = ResolveThreat(self, targets, config);

        // 三条豁免与 v2 共用同一个主人（MonsterAiGate），依据 AzerothCore Creature::CanCreatureAttack：
        // ① 世界 BOSS 整段不查"距出生点"（`!isWorldBoss()`）；
        // ② 被嘲讽钉住的这段窗口不查距离（`|| HasTauntAura()`）——否则坦克刚把怪拉开，
        //    怪跑一半就 evade 回去，那一嗓子等于白喊；
        // ③ 刚复活的 5 秒不许**主动**起手，但先挨的那一手照样还手（`IsEngagedBy` 例外）；
        // ④ 站桩档不查距离——它走不回去，让它进"回位"就是把它锁进一段永远出不来的状态
        //    （切片 18 之后"回位中打不到"是生效的，那种怪会变成打不到的怪）。
        var tauntHolds = MonsterAiGate.TauntHolds(self.TauntTargetId, self.TauntUntilTick, serverTick, targets);
        var leashApplies = MonsterAiGate.LeashApplies(config.Rank, config.FollowType);
        // 两个窗（复活宽限 / 回位免疫）都只挡"**接新敌**"这一件事，判据合在主人那一条式子里：
        // 窗内从没锁定过目标的怪连 acquiring 都不做，否则窗口一过就凭空带着一个已经在追的人，
        // 那与"起手"没有区别；而已经挨过打的（宿主把攻击者写进 ThreatTargetId）、
        // 以及回位途中被追上的都不受这条约束。
        if (MonsterAiGate.BlocksNewInitiation(serverTick, self.NoInitiateUntilTick, self.EvadeImmuneUntilTick,
                                              MonsterAiGate.ReturningHome(self.State),
                                              self.ThreatTargetId != 0UL)) threatId = 0UL;
        decision.ThreatTargetId = threatId;

        var distToSpawn = Distance2D(self.X, self.Y, self.SpawnX, self.SpawnY);
        // 风筝宽限窗（AC 的 leash 计时器）：窗内不查回家距离。判据主人是 MonsterAiGate，v1/v2 同一份。
        var kitingGrace = MonsterAiGate.KitingGraceActive(
            serverTick, self.LastLeashExtensionTick,
            MonsterAiGate.LeashExtensionSeconds(config.Level) * (ticksPerSecond > 0 ? ticksPerSecond : 60));
        var exceededLeash = MonsterAiGate.ExceededLeash(leashApplies, tauntHolds, kitingGrace, distToSpawn, config.LeashRadius);

        switch (self.State)
        {
            case MonsterAiState.Idle:
            {
                if (threatId != 0UL && !exceededLeash)
                {
                    decision.NextState = MonsterAiState.Chase;
                    break;
                }

                if (self.StateElapsedSeconds >= config.IdleToPatrolSeconds)
                {
                    decision.NextState = MonsterAiState.Patrol;
                    break;
                }
                break;
            }

            case MonsterAiState.Patrol:
            {
                if (threatId != 0UL && !exceededLeash)
                {
                    decision.NextState = MonsterAiState.Chase;
                    break;
                }

                // 有画好的线就**沿线上走**（判据与 v2 同一个主人：游标推进、到点停留、往返/循环
                // 全在 `MonsterPatrolCore.Advance` 里，这里只把目的地换成位移）。
                // 以前 v1 根本不读线：路线只在 v2 那段被解析进 inputs.Route ⇒ 默认档的怪
                // 画了线也只在出生点周围按 wander_radius 游荡（编辑器那一格是装饰）。
                // 走线用的是直线位移：v1 没有导航图可查（与它追击/回位的走法同一口径），
                // 所以"v1 沿同一条线给出逐格相同的走法"这条对账只在**不开走图**的配置上成立。
                if (route is { IsValid: true })
                {
                    // 游标就地推进在 decision 的字段上（decision 是本地结构变量，ref 它的字段合法），
                    // 这样每一条出口带的都是"推进后的那一拍"，不需要再手动写回一次。
                    var step = MonsterPatrolCore.Advance(ref decision.Patrol, route.Value, self.X, self.Y, dt, -1);
                decision.PatrolPointSwitched = step.SwitchedPoint;
                    if (step.Waiting)
                    {
                        // 停在点上：只转头，不产生位移（目的地就是当前点）。
                        decision.NextYaw = YawTowards(self.X, self.Y, step.TargetX, step.TargetY, self.Yaw);
                        break;
                    }

                    var (pdx, pdy) = StepTowards(self.X, self.Y, step.TargetX, step.TargetY, config.MoveSpeed, dt);
                    decision.DeltaX = pdx;
                    decision.DeltaY = pdy;
                    decision.Moved = pdx != 0f || pdy != 0f;
                    decision.NextYaw = pdx != 0f || pdy != 0f
                        ? YawTowards(self.X, self.Y, self.X + pdx, self.Y + pdy, self.Yaw)
                        : YawTowards(self.X, self.Y, step.TargetX, step.TargetY, self.Yaw);
                    break;
                }

                // 围绕出生点往返巡逻：方向由宿主提供（确定性），到边界则反向。
                // 速度问主人（walk_speed 配了用它，没配退回 move_speed＝接进这一格之前的行为）。
                var wanderSpeed = MonsterPatrolCore.WanderSpeed(config.WalkSpeed, config.MoveSpeed);
                var dir = self.PatrolDirection >= 0f ? 1f : -1f;
                var nextX = self.X + dir * wanderSpeed * dt;
                var nextDist = Distance2D(nextX, self.Y, self.SpawnX, self.SpawnY);
                if (nextDist > config.WanderRadius)
                {
                    // 到达游荡边界：反向并停在边界上（不越界），本 tick 不移动。
                    decision.NextState = MonsterAiState.Patrol;
                    decision.NextYaw = YawTowards(self.SpawnX, self.SpawnY, self.X, self.Y, self.Yaw);
                    break;
                }

                decision.DeltaX = dir * wanderSpeed * dt;
                decision.DeltaY = 0f;
                decision.Moved = decision.DeltaX != 0f;
                decision.NextYaw = YawTowards(self.X, self.Y, nextX, self.Y, self.Yaw);
                break;
            }

            case MonsterAiState.Chase:
            case MonsterAiState.Attack:
            {
                if (threatId == 0UL)
                {
                    // 目标消失/死亡 → 回归（有出生点偏移则 Leash，否则直接 Idle）。
                    decision.NextState = exceededLeash ? MonsterAiState.Leash : MonsterAiState.Idle;
                    break;
                }

                if (exceededLeash)
                {
                    decision.NextState = MonsterAiState.Leash;
                    break;
                }

                var t = FindTarget(targets, threatId);
                var distToTarget = Distance2D(self.X, self.Y, t.X, t.Y);

                if (distToTarget <= config.AttackRange)
                {
                    decision.NextState = MonsterAiState.Attack;
                    decision.NextYaw = YawTowards(self.X, self.Y, t.X, t.Y, self.Yaw);
                    if (decision.NextAttackCooldownRemaining <= 0f)
                    {
                        decision.WantsAttack = true;
                        decision.AttackTargetId = threatId;
                        decision.NextAttackCooldownRemaining = config.AttackCooldownSeconds;
                    }
                    break;
                }

                decision.NextState = MonsterAiState.Chase;
                var (dx, dy) = StepTowards(self.X, self.Y, t.X, t.Y, MonsterAiConfig.ChaseSpeed(in config), dt);
                decision.DeltaX = dx;
                decision.DeltaY = dy;
                decision.Moved = dx != 0f || dy != 0f;
                decision.NextYaw = YawTowards(self.X, self.Y, self.X + dx, self.Y + dy, self.Yaw);
                break;
            }

            case MonsterAiState.Leash:
            {
                var distHome = Distance2D(self.X, self.Y, self.SpawnX, self.SpawnY);
                if (distHome <= 0.5f)
                {
                    decision.NextState = MonsterAiState.Idle;
                    decision.NextYaw = YawTowards(self.X, self.Y, self.SpawnX, self.SpawnY, self.Yaw);
                    // 到家：开回位免疫窗 + 把血刷满（与 v2 的 Evade 到家同一口径）。
                    // 不刷这一条，"远程风筝把怪磨到一丝血再耗死"对每一只种子怪都成立——
                    // 而 v2 那边早就写明了这条存在的理由："玩家可以无伤磨残血怪"。
                    decision.EvadeImmuneUntilTick = MonsterAiGate.EvadeImmunityUntil(
                        serverTick, config.EvadeImmunitySeconds, ticksPerSecond);
                    if (config.MaxHp > 0)
                    {
                        decision.HealedToFull = true;
                        decision.HpAfterHeal = config.MaxHp;
                    }
                    break;
                }

                var (dx, dy) = StepTowards(self.X, self.Y, self.SpawnX, self.SpawnY, MonsterAiConfig.ChaseSpeed(in config), dt);
                decision.DeltaX = dx;
                decision.DeltaY = dy;
                decision.Moved = dx != 0f || dy != 0f;
                decision.NextYaw = YawTowards(self.X, self.Y, self.X + dx, self.Y + dy, self.Yaw);
                decision.ThreatTargetId = 0UL;
                break;
            }

            default:
            {
                decision.NextState = MonsterAiState.Idle;
                break;
            }
        }

        // 站桩档的收口：位移只在这**一个出口**被抹掉，而不是每个状态分支各写一遍
        // （v2 的同一件事长在 MoveToward 那个位移出口，v1 以前一处都没有 ⇒ 那一格对默认档是装饰）。
        // 只抹位移、不抹 WantsAttack：炮台本来就该"不动而打得人"。
        // 写在 switch 之后而不是各分支之内，是因为将来新增一条写位移的分支时，
        // 漏写这条判据不会靠着"记得改三处"来保证。
        if (!MonsterPatrolCore.FollowProducesMovement(config.FollowType))
        {
            decision.DeltaX = 0f;
            decision.DeltaY = 0f;
            decision.Moved = false;
        }

        // 导航图挡墙（v1 不跑 A*，只问"下一步要踩的那格是不是 0 代价"）：
        // 以前 v1 完全不知道图的存在 ⇒ 装上图之后，它会**穿墙**追人（切片 86 在 v2 修掉的同一个形状）。
        // 两条出口按顺序试：先试只走 X、再试只走 Y（贴墙滑行）；都挡就站住并计时，
        // 到了 `MonsterAiGate.NoPathEvadeSeconds` 就脱战——v1 没有绕路能力，站着挨到窗口结束就是"够不到"。
        // **只有"走图档"才问图**：直线档（FollowType.Straight）的语义一直是"不查图、不看地形代价"，
        // v2 那边也是这么实现的（`useNav` 先问 `NavEligibleFollow`）。第一版这里漏了这层 ⇒
        // 同一张行为表在 v1 上会"撞墙"，在 v2 上却照穿——两条通路对同一格给出不同手感。
        // 站桩档本来就不产生位移（上面那支已把 delta 抹成 0），这里只拦 Straight 一档。
        if (config.NavGrid.IsValid
            && MonsterPatrolCore.NavEligibleFollow(config.FollowType)
            && (decision.DeltaX != 0f || decision.DeltaY != 0f))
        {
            var nextX = self.X + decision.DeltaX;
            var nextY = self.Y + decision.DeltaY;
            if (CellBlocked(config.NavGrid, nextX, nextY))
            {
                // 滑行必须是**真的有位移**：目标在正前方时 dy≈0，只放行 Y 轴等于原地不动，
                // 却会把"够不到"的计时清零——怪就会贴着墙永远站下去（第一版就是这样，6 秒也不脱战）。
                if (!CellBlocked(config.NavGrid, nextX, self.Y) && MathF.Abs(decision.DeltaX) > 1e-4f)
                {
                    decision.DeltaY = 0f;
                    decision.NextNoPathStartTick = 0;
                }
                else if (!CellBlocked(config.NavGrid, self.X, nextY) && MathF.Abs(decision.DeltaY) > 1e-4f)
                {
                    decision.DeltaX = 0f;
                    decision.NextNoPathStartTick = 0;
                }
                else
                {
                    decision.DeltaX = 0f;
                    decision.DeltaY = 0f;
                    decision.Moved = false;
                    var start = self.NoPathStartTick;
                    if (start == 0) start = serverTick;
                    decision.NextNoPathStartTick = start;
                    if (serverTick - start >= (long)(MonsterAiGate.NoPathEvadeSeconds * ticksPerSecond))
                    {
                        decision.NextState = MonsterAiState.Evade;
                        decision.ThreatTargetId = 0UL;
                        decision.EvadedByNoPath = true;
                        decision.NextNoPathStartTick = 0;
                    }
                }
            }
            else
            {
                decision.NextNoPathStartTick = 0;
            }
        }

        return decision;
    }

    /// <summary>计算死亡时应设置的 <see cref="MonsterAiSelf.RespawnAtTick"/>。</summary>
    /// <param name="serverTick">当前服务器 tick。</param>
    /// <param name="ticksPerSecond">宿主 tick 频率（Orleans/DS 均为 60）。</param>
    /// <param name="respawnDelaySeconds">重生延迟（秒）。</param>
    public static long ComputeRespawnTick(long serverTick, int ticksPerSecond, float respawnDelaySeconds)
    {
        if (ticksPerSecond <= 0) ticksPerSecond = 60;
        var delaySeconds = respawnDelaySeconds > 0f ? respawnDelaySeconds : 0f;
        var ticks = (long)(delaySeconds * ticksPerSecond);
        return serverTick + ticks;
    }

    /// <summary>按 EntityId 升序排序目标（宿主在传入前调用，保证内核确定性）。</summary>
    /// <remarks>
    /// 使用插入排序而非 <c>Array.Sort</c>/LINQ：小数组下更快，且**消除不稳定排序**带来的
    /// 遍历顺序不确定性（同距离目标的相对顺序会直接影响威胁选择结果）。
    /// </remarks>
    public static void SortTargetsByEntityId(Span<MonsterAiTarget> targets)
    {
        for (var i = 1; i < targets.Length; i++)
        {
            var key = targets[i];
            var j = i - 1;
            while (j >= 0 && targets[j].EntityId > key.EntityId)
            {
                targets[j + 1] = targets[j];
                j--;
            }
            targets[j + 1] = key;
        }
    }

    /// <summary>
    /// 解析本 tick 的仇恨目标：优先沿用已锁定目标（追逐/攻击中），否则按仇恨半径获取新目标。
    /// </summary>
    private static ulong ResolveThreat(in MonsterAiSelf self, ReadOnlySpan<MonsterAiTarget> targets, in MonsterAiConfig config)
    {
        // 已锁定目标：仅校验"仍存在且可打"，不做距离约束（距离约束归 LeashRadius 管）。
        if (self.ThreatTargetId != 0UL && IsEngageable(self.ThreatTargetId, targets))
        {
            return self.ThreatTargetId;
        }

        // 被动怪不许**主动看上人**（内容表 `is_aggressive` 那一格，判据与 v2 同一个主人）。
        // 注意顺序：上面那条"已锁定的目标仍然生效"的例外必须在前面——被动管的是"谁先动"，
        // 先挨的那一手照样还手（宿主把攻击者写进单槽仇恨那条通路不经过这里）。
        if (!MonsterAiGate.MayInitiateOnSight(config.IsPassive)) return 0UL;

        // 仅在未接战时才受仇恨半径限制。
        if (self.State == MonsterAiState.Idle || self.State == MonsterAiState.Patrol)
        {
            return SelectThreat(self, targets, config);
        }

        // Chase/Attack/Leash 状态下锁定目标失效：允许立即按仇恨半径重新索敌，
        // 使"目标死亡后旁边还有别的敌人"能无缝转火，而不是先回到 Idle。
        return SelectThreat(self, targets, config);
    }

    /// <summary>目标是否仍可交战（存在 + 存活 + 可攻击）——判据住在 <c>MonsterAiGate</c>，与 v2 同一份。</summary>
    private static bool IsEngageable(ulong entityId, ReadOnlySpan<MonsterAiTarget> targets)
        => MonsterAiGate.IsEngageable(entityId, targets);

    /// <summary>
    /// 选择仇恨目标：仇恨半径内、存活且可攻击的**最近**目标；
    /// 距离相等时取 EntityId 较小者（消除顺序相关的歧义，保证确定性）。
    /// </summary>
    private static ulong SelectThreat(in MonsterAiSelf self, ReadOnlySpan<MonsterAiTarget> targets, in MonsterAiConfig config)
    {
        // 半径这一格的主人在 MonsterAiGate（v2 的索敌调同一个），不在这里再写一份判据。
        if (!MonsterAiGate.AggroRadiusUsable(config.AggroRadius)) return 0UL;

        var bestId = 0UL;
        var bestSq = float.MaxValue;

        for (var i = 0; i < targets.Length; i++)
        {
            var t = targets[i];
            if (!t.IsAlive || !t.IsAttackable || t.EntityId == 0UL) continue;

            // 半径按<b>这个候选</b>算一次（AC 的 GetAggroRange 就是按目标算的）：
            // 等级差那一格的判据住在 MonsterAiGate.EffectiveAggroRadius，v2 调的是同一个主人。
            var aggroSq = MonsterAiGate.EffectiveAggroRadius(config.AggroRadius, config.Level, t.Level);
            aggroSq *= aggroSq;

            var dx = t.X - self.X;
            var dy = t.Y - self.Y;
            var sq = dx * dx + dy * dy;
            if (sq > aggroSq) continue;

            // 扇形：以前 v1 这一段只比半径，于是"索敌角度"那一格对默认档的怪是装饰
            // （画了 90° 也从背后咬）。判据与 v2 同一个主人，不写第二份式子。
            if (!MonsterAiGate.InAggroCone(config.AggroConeDegrees, self.Yaw, dx, dy)) continue;
            // 藏着的人：只有带"隐蔽探测"那一位的怪才看得见（判据与 v2 同一个主人）。
            if (!MonsterAiGate.CanDetectTarget(config.DetectMask, t.IsHidden)) continue;
            // 墙挡住视线就看不见（与 v2 同一道门、同一个主人 `MonsterPathCore.HasLineOfSight`）。
            // 没装图（IsValid=false）时不过滤——与"没图就直线走"同口径，既有内容与金轨迹零变化。
            if (config.NavGrid.IsValid
                && !MonsterPathCore.HasLineOfSight(in config.NavGrid, self.X, self.Y, t.X, t.Y)) continue;

            // 严格小于：同距离保留先遇到者；因 targets 已按 EntityId 升序，等价于取较小 ID。
            if (sq < bestSq)
            {
                bestSq = sq;
                bestId = t.EntityId;
            }
        }

        return bestId;
    }

    private static MonsterAiTarget FindTarget(ReadOnlySpan<MonsterAiTarget> targets, ulong entityId)
    {
        for (var i = 0; i < targets.Length; i++)
        {
            if (targets[i].EntityId == entityId) return targets[i];
        }
        return default;
    }

    /// <summary>朝 (tx, ty) 直线移动一步；返回本 tick 的位移增量（不修改坐标）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    /// <summary>
    /// 这一格是不是"踩不进去"：**网格外一律放行**（图只覆盖烘过的区域，图外不归它管；
    /// 把图外当成墙会在边缘把怪钉死），图内只认 0 代价格是墙。
    /// </summary>
    private static bool CellBlocked(in MonsterNavGrid grid, float x, float y)
    {
        var cell = grid.CellIndexOf(x, y);
        return cell >= 0 && !grid.IsWalkable(cell);
    }

    private static (float DeltaX, float DeltaY) StepTowards(float x, float y, float tx, float ty, float speed, float dt)
    {
        var dx = tx - x;
        var dy = ty - y;
        var lenSq = dx * dx + dy * dy;
        if (lenSq <= 1e-8f)
        {
            return (0f, 0f);
        }

        var len = MathF.Sqrt(lenSq);
        var step = speed * dt;
        // 不越过目标：按剩余距离截断，避免在目标点附近抖动（determinism 无关，纯粹手感/稳定性）。
        if (step >= len)
        {
            return (dx, dy);
        }

        var inv = 1f / len;
        return (dx * inv * step, dy * inv * step);
    }

    /// <summary>XZ 平面距离（Z-up 世界，水平面为 XY）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Distance2D(float ax, float ay, float bx, float by)
    {
        var dx = ax - bx;
        var dy = ay - by;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// 计算朝向（弧度，atan2(dy, dx) 约定）。
    /// <para>仅用于表现层；调用方必须保证该值不回灌到位移数学。</para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float YawTowards(float fromX, float fromY, float toX, float toY, float fallbackYaw)
    {
        var dx = toX - fromX;
        var dy = toY - fromY;
        if (dx == 0f && dy == 0f) return fallbackYaw;
        return MathF.Atan2(dy, dx);
    }
}
