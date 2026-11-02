using System;

namespace Horizon.Game.Sim.Monsters;

/// <summary>
/// 一只怪的运行期状态（威胁表、施法、巡逻游标、路径、回位免疫窗）。
/// </summary>
/// <remarks>
/// 由宿主（<c>ZoneShardGrain</c> / <c>DsWorldSession</c>）持有，内核只读写它，不留进程内静态状态——
/// 否则两只怪会互相踩状态，而"同 tick 序列 → 同结果"的金轨迹前提当场失效。
/// </remarks>
public sealed class MonsterRuntimeState
{
    /// <summary>威胁表缓冲（升序、<see cref="ThreatCount"/> 个有效项）。</summary>
    public MonsterThreatEntry[] ThreatTable = new MonsterThreatEntry[MonsterThreatCore.MaxEntries];

    /// <summary>威胁表有效项数。</summary>
    public int ThreatCount;

    /// <summary>每个技能挂载的冷却就绪 tick（长度 = 挂载数）。</summary>
    public long[] ReadyAtTick = Array.Empty<long>();

    /// <summary>施法运行态。</summary>
    public MonsterCastingSelf Casting;

    /// <summary>巡逻运行态。</summary>
    public MonsterPatrolSelf Patrol;

    /// <summary>当前路径格序列（不含起点）。</summary>
    public int[] PathCells = Array.Empty<int>();

    /// <summary>路径长度。</summary>
    public int PathLength;

    /// <summary>路径游标。</summary>
    public int PathCursor;

    /// <summary>回位免疫窗截止 tick（在此之前不接新仇恨）。</summary>
    public long EvadeImmuneUntilTick;

    /// <summary>
    /// 已听见但**还没到期**的求援：要接手的那个人（敌人实体 id；0 = 没欠着这条）。
    /// <para>AC 把"收清单"与"执行"分成两拍——`CallAssistance` 当场把范围内合格的盟友收进
    /// `AssistDelayEvent`，两秒后才逐个 `CombatStart`（`Creature.cpp:2385` 起 +
    /// `CreatureFamilyAssistanceDelay = 2000`）。本作按"每只闲着的怪各自听一拍"表达同一件事，
    /// 所以这条延迟装在**听话的那一方**：听见 ⇒ 记账，到期 ⇒ 接手。</para>
    /// </summary>
    public ulong AssistPendingTarget;

    /// <summary>上面那条是谁叫的（盟友实体 id；0 = 没欠着）。到期那一刻交给宿主办"共享 leash 计时器"。</summary>
    public ulong AssistCallerId;

    /// <summary>上面那条的到期 tick（`ServerTick >= ` 它才生效）。</summary>
    public long AssistReadyAtTick;

    /// <summary>下一次"听求援"最早能在哪个 tick（AC 的 `m_assistanceTimer`，缺省 3 秒）。0 = 立刻可听。</summary>
    public long AssistNextTryTick;

    /// <summary>
    /// 控制状态表（<see cref="MonsterStatusCore"/>；null = 身上从来没挂过状态，
    /// 与"挂过但都到期了"在行为上同形，所以第一次用时才分配——站桩小怪不该背这一份数组）。
    /// </summary>
    public MonsterStatusEntry[]? Statuses;

    /// <summary>表内有效条目数（与 <see cref="Statuses"/> 一起构成唯一的"身上有什么"清单）。</summary>
    public int StatusCount;

    /// <summary>
    /// 施加控制状态时**打断**了在读的那条 —— 这个事实要先存一下，由下一拍 <c>Advance</c> 发成
    /// 一条 <c>InterruptedCast</c> 决策。
    /// <para>为什么要绕这一拍：施法的起手/进度/收条/打断四类信号在本作只有一个出口
    /// （<c>MonsterCastingCore.Advance</c> 的返回值），宿主把它转成下发。
    /// 如果在 <see cref="ApplyStatus"/> 里直接把 <c>Casting</c> 清掉而不发信号，
    /// 客户端的读条会**停在原地**直到自己超时——玩家看到的是"它读到一半僵住"，
    /// 而 AC 的 <c>CastStop()</c> 是会广播的。</para>
    /// </summary>
    public bool StatusInterruptPending;
    public int StatusInterruptMountIndex = -1;
    public ulong StatusInterruptTargetId;

    /// <summary>
    /// 给这只怪挂一条控制状态（期 1 由 DS 的伤害/技能结算调用；期 0 先用例驱动）。
    /// <returns>这一档是否要求"当场打断在读的那条施法"（AC 的眩晕=是）。</returns>
    /// </summary>
    public bool ApplyStatus(MonsterStatusKind kind, long expireAtTick, long nowTick, int magnitudeBps = 0)
    {
        var entries = Statuses ??= new MonsterStatusEntry[MonsterStatusCore.MaxEntries];
        var interrupt = MonsterStatusCore.Apply(entries, ref StatusCount, kind, expireAtTick, nowTick, magnitudeBps);
        if (interrupt && Casting.ActiveMountIndex >= 0)
        {
            // AC 的字面形状：`Unit::SetStunned` 里直接 `CastStop()`（Unit.cpp:18435）。
            // 这里只**收走**吟唱状态并把"要发一条打断"记下来；真正的信号归 Advance 那一个出口。
            StatusInterruptPending = true;
            StatusInterruptMountIndex = Casting.ActiveMountIndex;
            StatusInterruptTargetId = Casting.CastTargetId;
            MonsterCastingCore.ResetCastState(ref Casting);
        }
        return interrupt;
    }

    /// <summary>上次重算路径的 tick。</summary>
    public long LastRepathTick;

    /// <summary>重算间隔（tick）。30 tick = 0.5 秒：追击时每 tick 重算 A* 会把 tick 预算吃光，
    /// 而玩家的观感里"怪隔半秒纠一次路线"与"每帧纠"没有区别。</summary>
    public int RepathIntervalTicks = 30;

    /// <summary>
    /// 从哪一拍开始"够不到"（0 = 现在够得到）。计时按**服务端拍数差**算，不按调用次数累加：
    /// 路重算失败时 `needRepath` 每拍都为真，按次数累加会在 0.2 秒内就攒到 5 秒的量（实测过）。
    /// </summary>
    public long NoPathStartTick;

    /// <summary>
    /// 连续"够不到"累计的 tick 数（= 当前拍 − <see cref="NoPathStartTick"/>）。
    /// <para>它对应的 AC 口径是 <c>CanNotReachTarget</c> 计时器 + <c>NpcEvadeIfTargetIsUnreachable</c>（默认 5 秒）：
    /// 够不到目标就别一直贴着墙站——那在玩家眼里是"这只怪坏了"。</para>
    /// </summary>
    public int NoPathTicks;

    /// <summary>嘲讽锁定的**截止 tick**（不是"剩余"；在此之前目标被强制钉在 <see cref="TauntTargetId"/> 上）。</summary>
    public long TauntUntilTick;

    /// <summary>
    /// 当前被嘲讽钉住的人（实体 id；0 = 没有锁）。
    /// <para>只有截止时间没有对象，锁就没法执行：窗口内要压的是"换给谁"，
    /// 而窗口一到、或者这个人已经不在场（死亡/离开视野），都必须立刻把锁撤掉，
    /// 否则怪会朝一个查无此人的 id 狂奔。</para>
    /// </summary>
    public ulong TauntTargetId;

    /// <summary>
    /// "刚出现/刚复活"的宽限截止 tick：在此之前**不主动**接敌（可以被伤害拉进仇恨）。
    /// <para>逆向依据：AzerothCore `src/server/game/Entities/Creature/Creature.cpp:2606`
    /// （`m_respawnedTime` 在 <see cref="RespawnGraceSeconds"/> 秒内且对方没打过我 → 不能起手攻击），
    /// 那个 tick 在 `Creature::Respawn()` 里被刷新（同文件 2055 与 2088 两处）。</para>
    /// </summary>
    public long NoInitiateUntilTick;

    /// <summary>装配冷却数组（挂载数变化时）。</summary>
    public void EnsureMountCapacity(int mountCount)
    {
        if (ReadyAtTick != null && ReadyAtTick.Length == mountCount) return;
        var next = new long[Math.Max(0, mountCount)];
        if (ReadyAtTick != null)
        {
            var copy = Math.Min(next.Length, ReadyAtTick.Length);
            Array.Copy(ReadyAtTick, next, copy);
        }
        ReadyAtTick = next;
    }

    /// <summary>死亡/重生时复位（冷却与吟唱必须一起清，否则复活后"起手就站住"）。</summary>
    public void ResetForRespawn()
    {
        ThreatCount = 0;
        if (ThreatTable != null) Array.Clear(ThreatTable, 0, ThreatTable.Length);
        if (ReadyAtTick != null) Array.Clear(ReadyAtTick, 0, ReadyAtTick.Length);
        MonsterCastingCore.ResetCastState(ref Casting);
        Patrol = default;
        PathLength = 0;
        PathCursor = 0;
        EvadeImmuneUntilTick = 0;
        TauntUntilTick = 0;
        TauntTargetId = 0UL;
        // 欠着的求援也必须清：死过一次之后"我两秒后要帮谁打谁"已经不成立，
        // 留着会复活后凭空咬住一个上一世的敌人（AC 那份事件按 GUID 取盟友，取不到就什么也不做）。
        AssistPendingTarget = 0UL;
        AssistCallerId = 0UL;
        AssistReadyAtTick = 0L;
        AssistNextTryTick = 0L;
        StatusInterruptPending = false;
        StatusInterruptMountIndex = -1;
        StatusInterruptTargetId = 0UL;
        // 控制状态也必须一起清：AC 在死亡时把 aura 全摘（`Unit::RemoveAuras` 那条路），
        // 留着会出现"复活出来还是被眩晕着、但头顶什么都没有"这种表现。
        if (Statuses != null) Array.Clear(Statuses, 0, Statuses.Length);
        StatusCount = 0;
        // 宽限期在这里**清零**，由调用方紧接着 NoteRespawn 重新装上：
        // 清零与装载分成两步，是为了让"新建实体的那条通路"（宿主 spawn 时）和
        // "原地复活的那条通路"（这里）共用同一个装法，而不是各自抄一份秒数。
        NoInitiateUntilTick = 0;
    }
}

/// <summary>
/// 同场其它怪物的"求援可见性"快照（求援判据要用同族 + 距离 + 它此刻在打谁）。
/// <para>由宿主每 tick 构造，顺序必须是 EntityId 升序：求援选的是"最近的同族"，
/// 同距离时按 id 破平，才能满足"同 tick 序列 → 同结果"的金轨迹前提。</para>
/// </summary>
public readonly struct MonsterAiAlly
{
    public readonly ulong EntityId;
    public readonly float X;
    public readonly float Y;

    /// <summary>物种编号（与 <c>MonsterRuntimeConfig.Species</c> 同源）；0 = 没配，不参与同族判定。</summary>
    public readonly int Species;

    /// <summary>这只盟友此刻是否已经在战斗里（它打的对象才值得求援）。</summary>
    public readonly bool InCombat;

    /// <summary>它正在打的玩家实体 id（不在战斗时为 0）。</summary>
    public readonly ulong TargetId;

    public MonsterAiAlly(ulong entityId, float x, float y, int species, bool inCombat, ulong targetId)
    {
        EntityId = entityId;
        X = x;
        Y = y;
        Species = species;
        InCombat = inCombat;
        TargetId = targetId;
    }
}

/// <summary>AI v2 的输入（模板配置 + 导航数据 + 打断信号 + 同场盟友）。</summary>
public readonly struct MonsterRuntimeInputs
{
    public readonly MonsterRuntimeConfig? Config;
    public readonly int Level;
    public readonly MonsterNavGrid Grid;
    public readonly MonsterNavScratch? Scratch;
    public readonly bool InterruptSignal;
    public readonly long ServerTick;
    public readonly int TicksPerSecond;
    public readonly MonsterPatrolRoute? Route;

    /// <summary>同场其它怪物（求援用）；null 或空 = 宿主没给，等价于"附近没有同族"。</summary>
    public readonly MonsterAiAlly[]? Allies;

    public MonsterRuntimeInputs(
        MonsterRuntimeConfig? config, int level, MonsterNavGrid grid, MonsterNavScratch? scratch,
        bool interruptSignal, long serverTick, int ticksPerSecond, MonsterPatrolRoute? route,
        MonsterAiAlly[]? allies = null)
    {
        Config = config;
        Level = level;
        Grid = grid;
        Scratch = scratch;
        InterruptSignal = interruptSignal;
        ServerTick = serverTick;
        TicksPerSecond = ticksPerSecond;
        Route = route;
        Allies = allies;
    }
}

/// <summary>一次 AI 推进的产出（宿主据此写回权威状态并广播）。</summary>
public struct MonsterRuntimeResult
{
    /// <summary>位移/状态/普攻（与 v1 同结构，宿主的写回代码不用改）。</summary>
    public MonsterAiDecision Decision;

    /// <summary>施法产出。</summary>
    public MonsterCastDecision Cast;

    /// <summary>本 tick 是否发生了"回位完成 → 血量回满"（要广播满血，玩家看到的是血条刷满）。</summary>
    public bool HealedToFull;

    /// <summary>回位完成后的满血值。</summary>
    public int HpAfterHeal;

    /// <summary>威胁表变动（宿主据此决定是否广播目标变化）。</summary>
    public bool ThreatChanged;

    /// <summary>
    /// 这一 tick 是被同族求援拉进战斗的（宿主计数用）。
    /// <para>必须有这个出口：求援发生在内核深处，宿主只看得到"它动了"，
    /// 出问题（半径配错、物种没配、清单被截断）时无法分辨"它自己是主动索敌的"还是"被人叫来的"。</para>
    /// </summary>
    public bool SeededByAssist;

    /// <summary>
    /// 这一拍是被**谁**叫起来的（盟友实体 id；0 = 这一拍不是求援接手的）。
    /// <para>宿主拿它做 AC 的"共享 leash 计时器"（`Creature.cpp:231` 把首领的指针交给盟友）：
    /// 没有这个 id，宿主只知道"它被人叫了"，没法把两只并进同一组。</para>
    /// </summary>
    public ulong AssistCallerId;

    /// <summary>
    /// 这一拍被"封移动"的控制状态拦住（位移被清零、巡逻/路径游标退回本拍入口）。
    /// <para>与 <see cref="SeededByAssist"/> 同一族理由：闸口发生在内核深处，宿主只看得到"它没动"，
    /// 而"没动"可能是定身、可能是站桩施法、也可能是已经走到点——没有这一格就分不出来。</para>
    /// </summary>
    public bool MovementBlockedByStatus;

    /// <summary>这一拍被"封施法"的控制状态拦住（起手与收条都不许产生）。</summary>
    public bool CastingBlockedByStatus;

    /// <summary>这一拍回收了几条到期的控制状态（宿主拿去进摘要；单点回收的证据就在这一格）。</summary>
    public int StatusesExpired;

    /// <summary>这一拍因为减速把位移按比例缩小了（数值类状态的效果层读数）。</summary>
    public bool SlowedByStatus;

    /// <summary>本 tick 普攻伤害（已按公式算好；0 = 未出手）。</summary>
    public int MeleeDamage;
}

/// <summary>
/// 怪物 AI v2 内核：在 v1 的六态机上组合威胁表、朝向锥索敌、求援、施法、走图与回位治疗。
/// <para>
/// <b>与 v1 的关系（刻意）</b>：v1 的 <see cref="MonsterAiCore.Decide"/> 一行没改，
/// 因为金轨迹、既有断言与两条权威通路都钉在它身上。本类只在<b>能力位开启</b>时被调用：
/// 全关时（当前 11 只种子怪的 <c>ai_profile=0</c>）宿主继续走 v1，行为与接入数据表之前<b>逐字相同</b>。
/// 这条纪律的验证写在 <c>MonsterAiRuntimeTests</c>：全关时 v2 必须复现 v1 的位移与状态序列。
/// </para>
/// <para>
/// <b>确定性</b>：整数威胁值、定点滞回、冷却以 tick 计、路径由 <see cref="MonsterPathCore"/> 的全序堆决定。
/// 随机只有技能概率，走 <see cref="CombatCore.Roll01"/>（splitmix64，与闪避/暴击同一支）。
/// </para>
/// </summary>
public static class MonsterAiRuntime
{
    /// <summary>
    /// 版本（行为变更就递增，作为金轨迹对照与人工排查的锚点）。
    /// <para>v3：<see cref="MonsterAiFlags.UseThreatTable"/> 打开时补上"主动怪按半径+朝向锥索敌并写入基础威胁"。
    /// 这一条改变了状态迁移（Idle/Patrol → Chase 的触发条件），按纪律必须递增版本；
    /// 在此之前，开了威胁表的主动怪**只能挨打才还手**，与 <see cref="MonsterBehaviorConfig.IsAggressive"/>
    /// 的字面意思相反。</para>
    /// <para>v4：<c>FollowType == Static</c> 的站在"无路线踱步"分支里补了不动判据。
    /// 那条分支直接写 <c>DeltaX</c>，绕开了位移出口 <c>MoveToward</c> 里的站桩判据，
    /// 于是配成站桩的怪照样左右漂移——这是"改了配置没反应"那一类，改的是行为所以要进版本。</para>
    /// <para>v5：新增**求援**（<c>flags.UseAssist</c> + <c>assist_radius</c> + <c>species</c>）：
    /// 自己没索敌成功时，半径内同族正在打的人会被记进威胁表（基础威胁，之后由伤害竞争接管）。
    /// 在此之前这三项一路存到内核却没人调用，档位面板照样打印"求援 开"——
    /// 玩家看到的永远是"一只一只单打"，与 WoW 的"拉一只带一片"相反。</para>
    /// <para>v6：<c>follow_type</c> 的四个档位第一次在移动上互相可分辨——
    /// 走图资格与"难走格代价"改由 <c>MonsterPatrolCore.NavEligibleFollow</c> /
    /// <c>IgnoresHardCellCost</c> 这一张表决定：飞行档也走图但把难走格折回基准代价（不绕陡坡/灌木，
    /// 墙仍然不穿），直线档继续完全不查图。以前 1（直线）与 2（飞行）在移动上完全同形。</para>
    /// <para>v7：路线自己那一列 <c>use_nav_mesh</c> 第一次有人读——走这条线时若它为 false，
    /// 这一段就按直线连（配 WoW 里"这条巡逻线不理会地形"的怪）；追击与脱战回位不受这条线开关影响。
    /// 在此之前该列装配、编辑器、库里都在，内核从不查，等于"策划关掉了寻路，怪照样绕"。</para>
    /// <para>v8：求援不再在**回位免疫窗**里留条目。以前是"先写进威胁表、再由下面那道门否掉"，
    /// 于是被动怪拿到了一条自己认不了的仇恨：条目让求援分支下次不再进入，而被动门只认
    /// <c>ThreatTargetId==0</c> 于是继续清零——两条门互相顶住，这只怪最长装聋到那条条目过期（10 秒）。
    /// 顺带让 <c>SeededByAssist</c> 只在这次求援真的成立时置位（宿主的求援计数从此与位移对得上账）。</para>
    /// <para>v9：<c>TauntUntilTick</c> 第一次有了读取方，嘲讽从此真的能"拉走"。
    /// 在此之前 <c>NoteTaunt</c> 没有任何调用方、那个字段只写不读（注释还把它写成"剩余 tick"），
    /// 而威胁表只抬"最高+1"的余量——别人平 A 一下就盖过去。玩家侧表现就是"坦克拉了个寂寞"。
    /// 现在窗口内目标被钉在施法者身上（DPS 手快也不该把怪带回他身上），
    /// 窗口过期或被钉的人不在场都立刻撤锁（否则怪会朝查无此人狂奔）。</para>
    /// <para>v10：三条从权威实现（本机 AzerothCore `src/server/game/Entities/Creature/Creature.cpp:2606`
    /// 与 `:2630`，函数 `Creature::CanCreatureAttack`）抄来的口径第一次落地——
    /// ① 复活后 5 秒**不主动**接敌（先被谁打过就还手谁，见 <see cref="RespawnGraceSeconds"/>）；
    /// ② 嘲讽窗口内**不查"距出生点是否超 leash"**（那段代码写着 <c>|| HasTauntAura()</c>，
    ///    注释还专门点明 "this should include taunt auras"）——否则坦克刚把怪拉开，怪跑一半就 evade 回去；
    /// ③ 世界 BOSS 完全不做这条距离检查（<c>!isWorldBoss() && …</c>）——把世界 BOSS 拉出几百米，
    ///    它不会半路回头满血。以前的实现三条都没有，而且三条都是玩家看得见的。</para>
    /// <para><b>诚实说明</b>：这个常量目前**没有任何读取方**（握手里的公式版本是另一套契约，
    /// 怪物模拟只跑在 DS 侧，客户端不重放 AI）。它是给人和金轨迹对照用的锚点，
    /// 不是运行期校验；以前的注释写着"进握手"是错的。</para>
    /// </summary>
    public const int Version = 12;

    /// <summary>仇恨条目的过期 tick 数（10 秒 @60tick；超时无新贡献即移出威胁表）。</summary>
    public const long ThreatExpireTicks = 600;

    /// <summary>
    /// 刚出现/刚复活的怪的**不主动接敌**宽限秒数。
    /// <para>逆向依据：AzerothCore `Entities/Creature/Creature.cpp:2606` 里那个字面 5 秒
    /// （`(now - m_respawnedTime) < 5 && !IsEngagedBy(victim)` → 不许起手攻击）。
    /// 玩家的观感是"贴着刷新点站也不会被刚冒出来的怪秒咬"，而它先动手的唯一条件是**你先用伤害拉过它**。</para>
    /// </summary>
    public const float RespawnGraceSeconds = MonsterAiGate.RespawnGraceSeconds;

    /// <summary>
    /// 装/刷"刚出现"的宽限期。**两个写方都必须走这里**（宿主新建实体时、内核原地复活时），
    /// 免得一处装了一处没装——那会变成"有些刷新出来的怪会立刻咬人"这种逐点不一致的表现。
    /// </summary>
    public static void NoteRespawn(MonsterRuntimeState state, long serverTick, int ticksPerSecond)
    {
        if (state == null) return;
        if (ticksPerSecond <= 0) ticksPerSecond = 60;
        state.NoInitiateUntilTick = MonsterAiGate.RespawnGraceUntil(serverTick, ticksPerSecond);
    }

    /// <summary>
    /// 主动怪"进门就被咬"时写入的**基础威胁**（WoW 观测：黄名怪接敌即入威胁表，之后才由伤害/治疗竞争）。
    /// <para>取 1 而不是"一坨固定仇恨"：它只要求"现在有个目标"，任何一次真实伤害都必须能立刻盖过它——
    /// 否则先动手的人反而抢不到仇恨，那是玩家看得见的错（"我打了半天它还追着路过的队友"）。</para>
    /// </summary>
    public const long AggroSeedThreat = 1L;

    /// <summary>
    /// 听见求援到真的接手之间要等多久（秒）。
    /// <para>逆向依据：AzerothCore `Creature::CallAssistance` 当场收清单，
    /// 然后 `m_Events.AddEventAtOffset(e, Milliseconds(CONFIG_CREATURE_FAMILY_ASSISTANCE_DELAY))`
    /// 由 `AssistDelayEvent::Execute`（`Creature.cpp:209`）两秒后才 `CombatStart`；
    /// 缺省值在 `worldserver.conf.dist:2903` 写作 `CreatureFamilyAssistanceDelay = 2000`（毫秒）。</para>
    /// <para>玩家的观感是差别：**"我打了这只，两秒后旁边那只才转身过来"**，
    /// 而不是当场一拉整队同时启动。这一格刻意**不做成怪物字段**：AC 也把它放在世界配置里，
    /// 装进表里就会变成"每只怪自己决定队友几秒后来救场"，那不是 WoW 的形状。</para>
    /// <para>AC 另有 `CreatureFamilyAssistancePeriod = 3000`（`WorldConfig.cpp:354`）：同一只怪
    /// **两次收清单之间至少隔三秒**（`Creature.cpp:816` 的 `m_assistanceTimer`，到点才 `CallAssistance`）。
    /// 本作按"每只闲着的怪各自听"表达同一件事，于是这一格对应"重听的频率"——
    /// 见 <see cref="AssistRetryPeriodSeconds"/>。</para>
    /// </summary>
    public const float AssistDelaySeconds = 2f;

    /// <summary>
    /// 同一只怪两次**听求援**之间至少隔多久（秒）。AC `CreatureFamilyAssistancePeriod = 3000`。
    /// <para>没有这一格时本作是"每拍都听"：一只暂时没有同族在场的怪会以 60Hz 反复扫盟友清单，
    /// 且同族一旦走进半径就**当拍**被叫——AC 最长要等三秒。差别只体现在"盟友后来才靠过来"这一拍上，
    /// 但那是玩家能看见的（"隔壁那只慢慢晃过来时会不会立刻炸毛"）。</para>
    /// </summary>
    public const float AssistRetryPeriodSeconds = 3f;


    /// <summary>
    /// 推进一 tick。
    /// </summary>
    /// <param name="self">v1 的运行态（位置/状态/冷却）；本函数就地更新它需要写回的部分并通过结果回传。</param>
    /// <param name="state">v2 运行态（威胁表/施法/巡逻/路径）。</param>
    /// <param name="targets">视野内候选目标（必须按 EntityId 升序）。</param>
    /// <param name="dt">步长（秒）。</param>
    /// <param name="legacy">v1 的数值配置（宿主已有的那份，含 MaxHp/AttackPower）。</param>
    /// <param name="inputs">模板配置 + 导航 + 打断信号。</param>
    public static MonsterRuntimeResult Advance(
        in MonsterAiSelf self,
        MonsterRuntimeState state,
        ReadOnlySpan<MonsterAiTarget> targets,
        float dt,
        in MonsterAiConfig legacy,
        in MonsterRuntimeInputs inputs)
    {
        var result = new MonsterRuntimeResult();
        var decision = new MonsterAiDecision
        {
            NextState = self.State,
            NextYaw = self.Yaw,
            NextAttackCooldownRemaining = self.AttackCooldownRemaining,
            ThreatTargetId = self.ThreatTargetId,
            Moved = false,
        };
        result.Decision = decision;

        if (dt <= 0f) return result;
        if (state == null) return result;

        var config = inputs.Config;
        var behavior = config != null ? config.Behavior : MonsterBehaviorConfig.Default;
        var flags = behavior.Flags;
        var ticksPerSecond = inputs.TicksPerSecond > 0 ? inputs.TicksPerSecond : 60;

        // --- 控制状态：单点回收 + 本拍入口快照 ---
        // 回收只在这里（AC 也只在 `Unit::_UpdateSpells` 一处扫 IsExpired → RemoveOwnedAura，
        // `Unit.cpp:3885-3900`）。将来任何"驱散/解除"通路都必须走这同一句，否则会长出第二份"还剩几条"。
        // 这里**不分配**：身上从来没挂过状态的怪（`Statuses == null`）不该为这一族背一份数组，
        // 所以判据一律按 `null + count=0` 走"没有状态"那一支。
        var statusEntries = state.Statuses;
        if (statusEntries != null && state.StatusCount > 0)
        {
            result.StatusesExpired = MonsterStatusCore.Expire(statusEntries, ref state.StatusCount, inputs.ServerTick);
        }
        var movementBlocked = MonsterStatusCore.BlocksMovement(statusEntries!, state.StatusCount);
        var castingBlocked = MonsterStatusCore.BlocksCasting(statusEntries!, state.StatusCount);
        // 快照的作用是让"被拦住的那一拍"**逐字退回本拍入口**，而不是"照旧推进游标、只是不出位移"——
        // 后者会让定身中的怪把巡逻线走完，松开那一瞬间瞬移到线尾（玩家看得见）。
        var patrolAtEntry = state.Patrol;
        var pathLengthAtEntry = state.PathLength;
        var pathCursorAtEntry = state.PathCursor;
        var castingAtEntry = state.Casting;

        // --- 冷却递减（与 v1 同：脱战期间也推进）---
        if (decision.NextAttackCooldownRemaining > 0f)
        {
            decision.NextAttackCooldownRemaining -= dt;
            if (decision.NextAttackCooldownRemaining < 0f) decision.NextAttackCooldownRemaining = 0f;
        }

        // --- 死亡 ---
        if (self.State == MonsterAiState.Dead)
        {
            if (self.RespawnAtTick > 0 && inputs.ServerTick >= self.RespawnAtTick)
            {
                state.ResetForRespawn();
                // 复活也要装"刚出现的怪不主动咬人"那 5 秒（与宿主新建实体时同一条纪律，
                // 两个写方都得装——漏一个就会出现"原地复活的怪有宽限期、刷怪点重生的没有"）
                NoteRespawn(state, inputs.ServerTick, ticksPerSecond);
                decision.NextState = MonsterAiState.Idle;
                decision.ThreatTargetId = 0UL;
                decision.NextAttackCooldownRemaining = 0f;
            }
            result.Decision = decision;
            return result;
        }

        if (self.Hp <= 0)
        {
            decision.NextState = MonsterAiState.Dead;
            decision.ThreatTargetId = 0UL;
            state.Casting = default;
            state.ThreatCount = 0;
            result.Decision = decision;
            return result;
        }

        var distToSpawn = Distance2D(self.X, self.Y, self.SpawnX, self.SpawnY);
        // 脱战距离判据有两条**依据**的豁免（都不是"半径配错了"，而是 WoW 本身就不这么干）：
        // ① 世界 BOSS 不因距离回家（AzerothCore 的 `Creature::CanCreatureAttack`
        //    把整段"距出生点"检查写在 `!isWorldBoss()` 里）——玩家看得见的事实：
        //    把世界 BOSS 拉出几百米它也不会半路回头满血。
        // ② 被嘲讽钉住的这段窗口不查距离（同一处代码的 `|| HasTauntAura()`，
        //    注释还专门写着 "this should include taunt auras"）——否则坦克刚把怪从队友身边拉开，
        //    怪跑一半就 evade 回去了，那一嗓子等于白喊。
        //    而这里的判据必须与下面那条锁**同一个条件**（含"被钉的人还在场"），
        //    否则会出现"锁已经撤了、距离检查还被锁挡着"的组合：怪既没目标又不脱战，站在原地当木桩。
        var tauntHolds = MonsterAiGate.TauntHolds(state.TauntTargetId, state.TauntUntilTick,
                                                  inputs.ServerTick, targets);
        // 站桩档不查"离家过远"（它走不回去：进了回位就是死锁，而切片 18 之后还会顺带变成"打不到"）。
        // 这一半与"有没有装配内容包"无关（behavior 一定在），等级档那一半才需要 config 里的 Rank，
        // 所以两条各问一次主人：config 为 null 的兜底路径由外面这一条补齐。
        var leashApplies = MonsterPatrolCore.FollowProducesMovement(behavior.FollowType)
                           && (config is null || MonsterAiGate.LeashApplies(config.Rank, behavior.FollowType));
        var kitingGrace = MonsterAiGate.KitingGraceActive(
            inputs.ServerTick, self.LastLeashExtensionTick,
            MonsterAiGate.LeashExtensionSeconds(inputs.Level) * (inputs.TicksPerSecond > 0 ? inputs.TicksPerSecond : 60));
        var exceededLeash = MonsterAiGate.ExceededLeash(leashApplies, tauntHolds, kitingGrace, distToSpawn, behavior.LeashRadius);
        // 回位免疫窗 + **回位整段路**：AC 把这件事做成状态（进 evade 时挂 UNIT_STATE_EVADE，
        // 到达 OOC 点位才清），本作的 EvadeImmunitySeconds 只盖"到家之后"那几秒，
        // 路上这一整段以前没人挡 ⇒ 怪正往家跑时，同族求援能把它叫回去打、嘲讽能把它拽走。
        var returningHome = MonsterAiGate.ReturningHome(self.State);
        var blockedFromNewEnemy = MonsterAiGate.BlockedFromNewEnemy(
            inputs.ServerTick, state.NoInitiateUntilTick, state.EvadeImmuneUntilTick, returningHome);
        // "把已经选中的目标清零"比"不许接新敌"窄一格：复活宽限**不**清它（AC 的 !IsEngagedBy 例外，
        // 先挨的那一手必须还手），只有回位（整段路或免疫窗）才清。
        var wipesTarget = MonsterAiGate.WipesSelectedTarget(
            inputs.ServerTick, state.EvadeImmuneUntilTick, returningHome);
        // 刚出现/刚复活的怪有 5 秒**不主动**接敌（AzerothCore: `(now - m_respawnedTime) < 5 && !IsEngagedBy(victim)`）。
        // 只挡"主动索敌/被同族叫起来"这两条**发起**通路；伤害写进威胁表的那一路照常——
        // 这正是 AC 那个 `IsEngagedBy` 例外："你先打我，我就还手"。

        // --- 威胁表或 v1 单槽索敌 ---
        var hpPercent = legacy.MaxHp > 0 ? (int)(self.Hp * 100L / legacy.MaxHp) : 100;
        // 求援豁免位：这一 tick 是被同族叫起来的（下面"被动怪不接新敌"那条要放过它）。
        var calledByAlly = false;
        ulong threatId;
        if (flags.UseThreatTable)
        {
            // 先按"是否还在目标集合里"清表（离开 AOI/死亡的条目必须马上走，否则怪会朝 (0,0) 冲），
            // 再按超时清表。存在性检查写成独立循环而不是 lambda 闭包：targets 是 Span，
            // 而闭包不能捕获 ref 结构（编译器会拦，这一拦也正是"确定性优先于顺手"的提醒）。
            PruneByPresence(state, targets);
            MonsterThreatCore.Prune(state.ThreatTable, ref state.ThreatCount, inputs.ServerTick, ThreatExpireTicks);
            threatId = MonsterThreatCore.SelectCurrent(
                state.ThreatTable, state.ThreatCount, self.ThreatTargetId);

            // 嘲讽锁：窗口内把目标钉死在施法者身上——这就是"拉走"两个字的含义。
            // 三条出口必须都在这里判掉，否则"锁"会退化成"永久粘住"：
            // ① 窗口过期 → 撤锁，交回威胁表（此后由伤害竞争决定谁挨打）；
            // ② 被钉的人已经不在目标集合里（死亡/离开视野）→ 立刻撤锁，
            //    不然怪会朝一个查无此人的 id 狂奔（玩家看到的是"怪对着空气冲刺"）；
            // ③ 锁着的人还在、但威胁表里别人仇恨更高 → **以锁为准**，
            //    这条判据存在的全部意义就是"DPS 手快也不该把怪带回他身上"。
            // 位置放在索敌与求援之前：那两条都以 `threatId == 0` 为前提，锁着时本来就不该再索新敌。
            if (state.TauntTargetId != 0UL)
            {
                if (!MonsterAiGate.TauntHolds(state.TauntTargetId, state.TauntUntilTick, inputs.ServerTick, targets))
                {
                    state.TauntTargetId = 0UL;
                    state.TauntUntilTick = 0;
                }
                else
                {
                    threatId = state.TauntTargetId;
                }
            }

            // 表里没人 ≠ 这只怪不该打人。威胁表只由伤害/治疗写入，所以没有这一步时
            // "开了威胁表的主动怪"必须**先挨一下**才会进战斗——与 IsAggressive 的字面意思相反，
            // 玩家看到的是一地站着不动的怪。这里按"半径 + 朝向锥"补一次索敌，并写进一条基础威胁，
            // 之后仍由伤害竞争接管（见 AggroSeedThreat 的取值理由）。
            if (threatId == 0UL && !blockedFromNewEnemy && MonsterAiGate.MayInitiateOnSight(!behavior.IsAggressive))
            {
                var seeded = SelectByAggroRangeAndCone(self, targets, behavior, legacy, inputs.Grid);
                if (seeded != 0UL)
                {
                    MonsterThreatCore.AddThreat(
                        state.ThreatTable, ref state.ThreatCount, seeded, AggroSeedThreat, inputs.ServerTick);
                    threatId = seeded;
                }
            }

            // 求援：自己没索敌成功时，听一听"附近同族正在打谁"。
            // WoW 观测：拉一只怪常会把旁边同族一起带进来，而不同种的怪无动于衷。
            // 这条以前完全不存在——档位面板会打印"求援 开"、`assist_radius` 一路存到内核，但没人调用，
            // 于是策划配了半径、玩家看到的仍然是"一只一只单打"。
            // 判据要点：**物种为 0（没配）一律不求援**。把 0==0 当成同族会让所有没配物种的怪
            // 互相拉成一片，那是比"没有求援"糟得多的表现。
            // 判据要点二：**免疫窗里连听都不听**（而不是"听了再被下面的门否掉"）。
            // 后者会留下一条**写了却没人认的仇恨条目**：条目在表里，所以下一拍 `threatId != 0`，
            // 求援分支不再进入；而被动怪那道门只看"我锁定的目标是不是 0"，于是把它清零。
            // 两条门互相顶住，这只怪就在窗后最长装聋到那条条目过期（<see cref="ThreatExpireTicks"/>，10 秒），
            // 表现是"队友都跟它同族打起来了，刚 evade 回来的那只站着看戏好几秒"。
            // 同时这也让 <see cref="MonsterRuntimeResult.SeededByAssist"/> 说实话：它只在这次求援真的成立时置位。
            if (threatId == 0UL && !blockedFromNewEnemy && flags.UseAssist && behavior.AssistRadius > 0f)
            {
                // 物种从配置里取（`MonsterAiSelf` 不带 id/物种，宿主构造盟友清单时负责把自己剔除）。
                // Config 是引用类型（不是 Nullable<T>），所以判 null 而不是 HasValue。
                var assistSpecies = inputs.Config != null ? inputs.Config.Species : 0;
                // 听求援有**节流**：AC 每 `CreatureFamilyAssistancePeriod`（3 秒）才重收一次清单
                // （`Creature.cpp:816` 的 `m_assistanceTimer`）。已欠着一条时不必再听——那份清单钉在
                // `AssistPendingTarget` 上，到期那一拍照旧要执行，所以"到期检查"必须留在节流门外面。
                var heard = 0UL;
                var callerId = 0UL;
                if (state.AssistPendingTarget == 0UL && inputs.ServerTick >= state.AssistNextTryTick)
                {
                    heard = SelectAssistTarget(self, inputs.Allies, behavior, targets, assistSpecies, out callerId);
                    state.AssistNextTryTick = inputs.ServerTick + (long)(AssistRetryPeriodSeconds * ticksPerSecond);
                }
                // 听见 ≠ 马上动手：AC 是"当场收清单、两秒后执行"（<see cref="AssistDelaySeconds"/>）。
                // 已经欠着一条的时候**不重新计时、也不换人**（AC 那份事件里钉的是当时那个 victim 的 GUID，
                // 首领中途换目标不会把已排程的那次改道），所以这里只在 `AssistPendingTarget == 0` 时装账。
                if (heard != 0UL && state.AssistPendingTarget == 0UL)
                {
                    state.AssistPendingTarget = heard;
                    // 谁叫的也要记下来：AC 在那一刻把首领的 leash 计时器指针交给盟友（`Creature.cpp:231`），
                    // 宿主拿这个 id 把两只并进同一组（见 `MonsterAiSelf.LeashGroupId`）。
                    state.AssistCallerId = callerId;
                    state.AssistReadyAtTick = inputs.ServerTick
                        + (long)(AssistDelaySeconds * ticksPerSecond);
                }

                if (state.AssistPendingTarget != 0UL && inputs.ServerTick >= state.AssistReadyAtTick)
                {
                    // 到期。AC 那一刻**不复查距离**（`AssistDelayEvent` 按 GUID 找回盟友，
                    // 只重跑 `CanAssistTo` 那一串门），所以这里也只复查"那个人还打得着"——
                    // 首领在这两秒里把人拖开是常态，那一队照样要追上去，这才是玩家看到的形状。
                    var stillThere = HasAttackableTarget(targets, state.AssistPendingTarget);
                    var helped = state.AssistPendingTarget;
                    var caller = state.AssistCallerId;
                    state.AssistPendingTarget = 0UL;
                    state.AssistCallerId = 0UL;
                    state.AssistReadyAtTick = 0L;
                    if (stillThere)
                    {
                        MonsterThreatCore.AddThreat(
                            state.ThreatTable, ref state.ThreatCount, helped, AggroSeedThreat, inputs.ServerTick);
                        threatId = helped;
                        result.SeededByAssist = true;
                        result.AssistCallerId = caller;
                        calledByAlly = true;
                    }
                }
            }

            if (threatId != self.ThreatTargetId) result.ThreatChanged = true;
        }
        else
        {
            threatId = LegacySelectThreat(self, targets, legacy, wipesTarget, inputs.Grid);
        }

        // 被动怪 / 免疫窗：不主动接敌（但可以被打）
        // 被动怪：没有已锁定目标时不接新敌（必须先被打）。
        // **求援是个例外**：`assist_radius` 这套机制存在的唯一意义，就是把旁边那些
        // 自己不会索敌的同类叫起来；不加这条豁免，求援对所有"需要被叫"的怪都是空转
        // （实测：内核确实写进了威胁表，却在这一行被清零，玩家看到的仍然是一只一只单打）。
        // 免疫窗不给这个例外——回位途中被叫起来，与"evade 后不该回头再咬"的口径相反。
        // （现在这条在求援分支入口就挡住了，走到这里时 `calledByAlly` 与免疫窗不会同时为真；
        // 留在这里是给"表里本来就有条目"的那一路兜底，两个位置缺一个都会露出上面那种装聋。）
        if (MonsterAiGate.WipesThreatForPassive(!behavior.IsAggressive, self.ThreatTargetId != 0UL, calledByAlly)) threatId = 0UL;
        // 回位免疫窗：不接新敌，但**已锁定的目标保留**（否则"回位途中被追上"会立刻变成重新索敌，
        // 玩家看到的是怪"回头又咬一口"，与 WoW 的 evade 表现相反）。
        if (wipesTarget && self.ThreatTargetId == 0UL) threatId = 0UL;

        decision.ThreatTargetId = threatId;

        var hasTarget = threatId != 0UL;
        var targetPos = FindTarget(targets, threatId);

        // --- 状态机 ---
        switch (self.State)
        {
            case MonsterAiState.Idle:
            case MonsterAiState.Patrol:
            {
                if (hasTarget && !exceededLeash)
                {
                    decision.NextState = MonsterAiState.Chase;
                    break;
                }

                if (self.State == MonsterAiState.Idle && self.StateElapsedSeconds < behavior.IdleToPatrolSeconds)
                {
                    decision.NextState = MonsterAiState.Idle;
                    break;
                }

                decision.NextState = MonsterAiState.Patrol;
                if (inputs.Route != null && inputs.Route.Value.IsValid)
                {
                    var step = MonsterPatrolCore.Advance(ref state.Patrol, inputs.Route.Value, self.X, self.Y, dt, -1);
                    // 到点信号要经 decision 带回：v2 的游标住在 state.Patrol（就地推进），
                    // 宿主拿不到它，只比 Ai.Patrol 的下标会把"走了线"读成 0 次。
                    decision.PatrolPointSwitched = step.SwitchedPoint;
                    if (step.SwitchedPoint) { state.PathLength = 0; state.PathCursor = 0; }
                    if (step.Waiting)
                    {
                        decision.NextYaw = YawTowards(self.X, self.Y, step.TargetX, step.TargetY, self.Yaw);
                        break;
                    }
                    MoveToward(in behavior, in inputs, state, self, step.TargetX, step.TargetY, dt, ticksPerSecond, ref decision);
                }
                else
                {
                    // 站桩怪在这里也必须不动：位移出口 `MoveToward` 里那条"站桩不产生位移"的判据
                    // 只覆盖经过它的通路，而"无路线踱步"这一支是**直接写 DeltaX** 的（为了不引入
                    // 导航与速度换算、保持与 v1 逐字同形）。漏掉这一判的表现很具体：
                    // 一只配成"站桩"的怪（炮台、绑在柱子上祭司之类）会在原地左右漂移，玩家看不出为什么。
                    if (!MonsterPatrolCore.FollowProducesMovement(behavior.FollowType))
                    {
                        decision.Moved = false;
                        break;
                    }

                    // 无路线：与 v1 一样按 WanderRadius 往返（速度那条选择只在 MonsterPatrolCore.WanderSpeed 有一份）
                    var dir = self.PatrolDirection >= 0f ? 1f : -1f;
                    var nextX = self.X + dir * MonsterPatrolCore.WanderSpeed(behavior.WalkSpeed, behavior.MoveSpeed) * dt;
                    var nextDist = Distance2D(nextX, self.Y, self.SpawnX, self.SpawnY);
                    if (nextDist > behavior.WanderRadius)
                    {
                        decision.NextYaw = YawTowards(self.SpawnX, self.SpawnY, self.X, self.Y, self.Yaw);
                        break;
                    }
                    decision.DeltaX = nextX - self.X;
                    decision.Moved = true;
                    decision.NextYaw = YawTowards(self.X, self.Y, nextX, self.Y, self.Yaw);
                }
                break;
            }

            case MonsterAiState.Chase:
            case MonsterAiState.Attack:
            {
                if (!hasTarget)
                {
                    decision.NextState = distToSpawn > 0.5f ? MonsterAiState.Evade : MonsterAiState.Idle;
                    if (decision.NextState == MonsterAiState.Evade)
                    {
                        state.ThreatCount = 0;
                        MonsterCastingCore.ResetCastState(ref state.Casting);
                        state.PathLength = 0;
                        state.PathCursor = 0;
                    }
                    break;
                }

                if (exceededLeash)
                {
                    decision.NextState = MonsterAiState.Evade;
                    state.ThreatCount = 0;
                    MonsterCastingCore.ResetCastState(ref state.Casting);
                    decision.ThreatTargetId = 0UL;
                    state.PathLength = 0;
                    state.PathCursor = 0;
                    break;
                }

                var distToTarget = Distance2D(self.X, self.Y, targetPos.X, targetPos.Y);
                var attackRange = legacy.AttackRange > 0f ? legacy.AttackRange : 2.5f;

                // 残血逃跑（战术位）：先于攻击判定，否则"要逃的怪仍在打你"
                if (flags.UseTactics && behavior.FleeHpPercent > 0 && hpPercent <= behavior.FleeHpPercent)
                {
                    decision.NextState = MonsterAiState.Evade;
                    state.ThreatCount = 0;
                    decision.ThreatTargetId = 0UL;
                    MonsterCastingCore.ResetCastState(ref state.Casting);
                    break;
                }

                // 远程怪的保持距离（战术位）
                if (flags.UseTactics && behavior.RangedMaxDistance > 0f && distToTarget < behavior.RangedMinDistance)
                {
                    MoveToward(in behavior, in inputs, state, self, self.X * 2f - targetPos.X, self.Y * 2f - targetPos.Y, dt, ticksPerSecond, ref decision);
                }
                else if (distToTarget <= attackRange)
                {
                    decision.NextState = MonsterAiState.Attack;
                    decision.NextYaw = YawTowards(self.X, self.Y, targetPos.X, targetPos.Y, self.Yaw);
                    if (decision.NextAttackCooldownRemaining <= 0f)
                    {
                        decision.WantsAttack = true;
                        decision.AttackTargetId = threatId;
                        decision.NextAttackCooldownRemaining = behavior.MeleeSwingSeconds > 0f
                            ? behavior.MeleeSwingSeconds
                            : legacy.AttackCooldownSeconds;
                        result.MeleeDamage = legacy.AttackPower;
                    }
                }
                else
                {
                    decision.NextState = MonsterAiState.Chase;
                    MoveToward(in behavior, in inputs, state, self, targetPos.X, targetPos.Y, dt, ticksPerSecond, ref decision);
                }

                // 施法（技能位）：站桩施法会吃掉本 tick 的位移。
                // 注意 inCombat 现在传的是**真实战斗态**，不再是常量 true：
                // 写死 true 时 slot_type=1（非战斗）永远不可能进候选，
                // 巡逻路上给自己上个buff的那类怪就配不出来（编辑器里能选，进游戏什么都不发生）。
                if (flags.UseSkills && config != null)
                {
                    var mounts = config.Skills;
                    if (mounts != null && mounts.Length > 0)
                    {
                        state.EnsureMountCapacity(mounts.Length);
                        var cast = MonsterCastingCore.Advance(
                            ref state.Casting, mounts, state.ReadyAtTick,
                            self.ThreatTargetId == 0UL ? threatId : self.ThreatTargetId,
                            threatId, targetPos.X, targetPos.Y, targetPos.Z,
                            distToTarget, hpPercent, true,
                            inputs.InterruptSignal, inputs.ServerTick, ticksPerSecond, dt);
                        result.Cast = cast;
                        if (cast.ShouldHalt)
                        {
                            decision.DeltaX = 0f;
                            decision.DeltaY = 0f;
                            decision.Moved = false;
                        }
                    }
                }
                break;
            }

            case MonsterAiState.Evade:
            {
                var distHome = Distance2D(self.X, self.Y, self.SpawnX, self.SpawnY);
                if (distHome <= 0.5f)
                {
                    decision.NextState = MonsterAiState.Idle;
                    state.ThreatCount = 0;
                    decision.ThreatTargetId = 0UL;
                    MonsterCastingCore.ResetCastState(ref state.Casting);
                    state.PathLength = 0;
                    state.PathCursor = 0;
                    state.EvadeImmuneUntilTick = MonsterAiGate.EvadeImmunityUntil(
                        inputs.ServerTick, behavior.EvadeImmunitySeconds, ticksPerSecond);
                    // WoW 观测：回位后血条"唰"地满。不清这一条，脱战回来的怪还是残血，玩家能无伤磨。
                    result.HealedToFull = legacy.MaxHp > 0;
                    result.HpAfterHeal = legacy.MaxHp;
                    break;
                }
                MoveToward(in behavior, in inputs, state, self, self.SpawnX, self.SpawnY, dt, ticksPerSecond, ref decision);
                break;
            }

            default:
                decision.NextState = MonsterAiState.Idle;
                break;
        }

        // 非战斗施法（slot_type=1）。战斗态（Chase/Attack）那条分支自己推进过施法，这里只补它没覆盖的状态：
        // 写死 inCombat=true 时"非战斗"这一档永远进不了候选，巡逻路上上个自我增益的怪配不出来
        // （编辑器里选得到，进游戏什么都不发生）。
        if (flags.UseSkills && config != null
            && !MonsterAiGate.InCombat(self.State))
        {
            var idleMounts = config.Skills;
            if (idleMounts != null && idleMounts.Length > 0)
            {
                state.EnsureMountCapacity(idleMounts.Length);
                // 没有敌方目标：圆心就是自己，距离按 0 放行（射程判据对它没意义）。
                // ShouldHalt 在这里刻意不采纳：巡逻位移归路线管，为上个 buff 站住会让队伍看起来卡住。
                result.Cast = MonsterCastingCore.Advance(
                    ref state.Casting, idleMounts, state.ReadyAtTick,
                    0UL, 0UL, self.X, self.Y, self.Z,
                    0f, hpPercent, false,
                    inputs.InterruptSignal, inputs.ServerTick, ticksPerSecond, dt);
            }
        }

        // --- 控制状态的两条闸口：收在**同一个出口** ---
        // 为什么不在每个 MoveToward/施法点各守一遍：那正是"两份清单"的形状——
        // 位移与施法在这里都已经有唯一一个汇合点，而移动类分支有四五处（追击/巡逻/游荡/回位/路径），
        // 逐处守必然漏一处（本项目已经为"漏一处静默"付过几次学费）。
        if (movementBlocked)
        {
            decision.DeltaX = 0f;
            decision.DeltaY = 0f;
            decision.Moved = false;
            state.Patrol = patrolAtEntry;
            state.PathLength = pathLengthAtEntry;
            state.PathCursor = pathCursorAtEntry;
            result.MovementBlockedByStatus = true;
        }

        if (castingBlocked)
        {            // 封施法 = 这一拍不许产生任何起手或收条。
            // 在飞的条本来就该在**施加那一刻**被打断（AC：`SetStunned` 里直接 `CastStop()`，
            // `Unit.cpp:18435`；沉默那一半本作是刻意退化，见 <see cref="MonsterStatusCore.InterruptsCastOnApply"/>），
            // 所以这里回滚掉的通常就是本拍刚起的手。
            // 若有人绕过 `MonsterStatusCore.Apply` 直接往表里塞状态，那条在飞的会被**冻住**（不出进度也不收条）——
            // 这不是产品通路，但比"照旧收条"更保守：宁可少出手，不可偷偷出手。
            state.Casting = castingAtEntry;
            result.Cast = new MonsterCastDecision { MountIndex = -1 };
            result.CastingBlockedByStatus = true;
        }

        // 施加控制状态时收走的那条吟唱，在**这一个出口**发成打断信号（起手/进度/收条/打断四类信号
        // 只从 `result.Cast` 走一次；在 ApplyStatus 里悄悄清掉吟唱会让客户端的读条僵在原地）。
        if (state.StatusInterruptPending)
        {
            state.StatusInterruptPending = false;
            var interruptIndex = state.StatusInterruptMountIndex;
            var interruptSkillId = 0;
            if (config?.Skills != null && interruptIndex >= 0 && interruptIndex < config.Skills.Length)
                interruptSkillId = config.Skills[interruptIndex].SkillId;
            result.Cast = new MonsterCastDecision
            {
                MountIndex = interruptIndex,
                SkillId = interruptSkillId,
                TargetEntityId = state.StatusInterruptTargetId,
                InterruptedCast = true,
            };
            state.StatusInterruptMountIndex = -1;
            state.StatusInterruptTargetId = 0UL;
        }

        // 减速：数值类，不走闸口而走**速度缩放**（AC 的 `SPELL_AURA_MOD_DECREASE_SPEED` 同样是改速度，
        // 不是禁行动）。三条规则都由 MonsterStatusCore 定：多条取最强、上限 9000、强度 0 等于没填。
        var slowScale = MonsterStatusCore.SpeedScale(statusEntries!, state.StatusCount);
        if (slowScale < 1f && (decision.DeltaX != 0f || decision.DeltaY != 0f))
        {
            decision.DeltaX *= slowScale;
            decision.DeltaY *= slowScale;
            result.SlowedByStatus = true;
        }

        result.Decision = decision;
        return result;
    }

    /// <summary>宿主记一次伤害进威胁表（近战/技能同一入口，按 <see cref="MonsterThreatCore.ThreatFromDamage"/> 转威胁）。</summary>
    public static void NoteDamage(MonsterRuntimeState state, ulong attackerId, long damage, long serverTick)
    {
        if (state == null || attackerId == 0UL) return;
        MonsterThreatCore.AddThreat(state.ThreatTable, ref state.ThreatCount, attackerId,
            MonsterThreatCore.ThreatFromDamage(damage), serverTick);
    }

    /// <summary>宿主记一次治疗进威胁表（0.5 系数——"奶妈 OT"的来源）。</summary>
    public static void NoteHealing(MonsterRuntimeState state, ulong healerId, long healing, long serverTick)
    {
        if (state == null || healerId == 0UL) return;
        MonsterThreatCore.AddThreat(state.ThreatTable, ref state.ThreatCount, healerId,
            MonsterThreatCore.ThreatFromHealing(healing), serverTick);
    }

    /// <summary>
    /// 嘲讽：设为最高 + 锁定窗口（窗口内目标被钉在这个人身上，不因滞回换回去）。
    /// <para>两件事都要落：<b>威胁表</b>（窗口过后由伤害竞争接管，所以抬到最高+margin 而不是只钉死目标）
    /// 与<b>锁本身</b>（<see cref="TauntTargetId"/> + <see cref="TauntUntilTick"/>）。
    /// 以前只有这一句在写 tick，而<b>没有任何人读它</b>——玩家侧表现是"坦克拉了个寂寞"，
    /// 嘲讽的 1 点余量被别人随便一下平A 就盖过去了。</para>
    /// </summary>
    public static void NoteTaunt(MonsterRuntimeState state, ulong casterId, long serverTick, int ticksPerSecond, float durationSeconds)
    {
        if (state == null || casterId == 0UL) return;
        MonsterThreatCore.Taunt(state.ThreatTable, ref state.ThreatCount, casterId, serverTick);
        if (ticksPerSecond <= 0) ticksPerSecond = 60;
        state.TauntUntilTick = serverTick + (long)(Math.Max(0.5f, durationSeconds) * ticksPerSecond);
        state.TauntTargetId = casterId;
    }

    /// <summary>是否所有能力位都关着（宿主据此决定走 v1 还是 v2；全关时必须走 v1 以保持既有金轨迹）。</summary>
    public static bool AllCapabilitiesOff(in MonsterAiFlags flags)
        => !(flags.UseThreatTable || flags.UseSkills || flags.UseNavigation || flags.UseAssist || flags.UseTactics);


    // ---------- 内部 ----------

    private static void MoveToward(
        in MonsterBehaviorConfig behavior,
        in MonsterRuntimeInputs inputs,
        MonsterRuntimeState state,
        in MonsterAiSelf self,
        float destX,
        float destY,
        float dt,
        int ticksPerSecond,
        ref MonsterAiDecision decision)
    {
        var speed = behavior.MoveSpeed;
        // 走图资格按移动方式分档（表在 MonsterPatrolCore.NavEligibleFollow，巡逻侧共用同一张）：
        //   GroundWalk  走图，难走格付双倍代价（会绕开陡坡/灌木）；
        //   Straight    永远直线，不查图（WoW 的"直线冲"怪：不看地形代价，也不绕）；
        //   Flying      走图，但难走格折回基准代价——飞得不绕，墙（0）仍然不能穿；
        //   Static      在上面已经 return，不产生位移。
        // 以前 1 与 2 在移动上完全同形（都只走直线、都不查图），编辑器里那个下拉是假的。
        var ignoreHard = MonsterPatrolCore.IgnoresHardCellCost(behavior.FollowType);
        // 正在走巡逻线时，"这条线让不让走图"由路线表那一列 use_nav_mesh（默认 1）决定；
        // 这条规则以前在 MonsterPatrolCore.ShouldUseNavigation 里写了一份却**没有任何调用方**，
        // 内核另写一份，于是那一列是死列、两处还迟早分家。现在只有一处：走线时问那个 helper。
        // 追击/脱战回位不是在走这条线，所以只看这只怪自己的档位与移动方式。
        var onPatrolLine = self.State == MonsterAiState.Patrol && inputs.Route is { IsValid: true };
        var navAllowed = onPatrolLine
            ? MonsterPatrolCore.ShouldUseNavigation(inputs.Route!.Value, behavior)
            : behavior.Flags.UseNavigation && MonsterPatrolCore.NavEligibleFollow(behavior.FollowType);
        var useNav = navAllowed && inputs.Grid.IsValid && inputs.Scratch != null;

        // 站桩怪：不产生位移（判据在主人 `FollowProducesMovement` 那一条，v1 的位移出口也问它）
        if (!MonsterPatrolCore.FollowProducesMovement(behavior.FollowType))
        {
            decision.DeltaX = 0f;
            decision.DeltaY = 0f;
            decision.Moved = false;
            decision.NextYaw = YawTowards(self.X, self.Y, destX, destY, self.Yaw);
            return;
        }

        if (!useNav)
        {
            var (dx, dy) = StepTowards(self.X, self.Y, destX, destY, speed, dt);
            decision.DeltaX = dx;
            decision.DeltaY = dy;
            decision.Moved = dx != 0f || dy != 0f;
            decision.NextYaw = YawTowards(self.X, self.Y, self.X + dx, self.Y + dy, self.Yaw);
            return;
        }

        // 需要重算路径：没有路径 / 游标越界 / 到了重算间隔
        var needRepath = state.PathLength == 0
                         || state.PathCursor >= state.PathLength
                         || inputs.ServerTick - state.LastRepathTick >= state.RepathIntervalTicks;
        if (needRepath)
        {
            if (state.PathCells.Length < MonsterPathCore.MaxPathCells)
                state.PathCells = new int[MonsterPathCore.MaxPathCells];
            int length;
            // 第 9 个参数 maxExploreCells 传 0 = 用默认预算；第 10 个才是"飞行不付难走代价"的开关。
            var found = MonsterPathCore.FindPath(inputs.Grid, inputs.Scratch!,
                self.X, self.Y, destX, destY, state.PathCells, out length, 0, ignoreHard);
            // Direct = 视线直通或起终点同格：不设路径，下面退化成直线（WoW 的怪在空旷地就是直冲）
            state.PathLength = (found == MonsterPathResult.Found || found == MonsterPathResult.Partial) ? length : 0;
            state.PathCursor = 0;
            state.LastRepathTick = inputs.ServerTick;
            // "够不到"按重算周期累计（AC 那个计时器是每帧累加的，而这里只在重算时才知道结果；
            // 累加一个重算间隔与"每帧累加"在 5 秒尺度上差不到一个周期，且不必每 tick 跑 A*）。
            // **只看 Unreachable 是不够的**：目标在一堵墙后面时 A* 往往给出 Partial（走到墙边就没了），
            // 而那只怪会永远贴墙站着——那正是 AC 这条判据要治的形状。所以 Partial 还要量"路径终点离目标多远"：
            // 走到最近可达格仍差两米以上，就算够不到。
            // 判"够不到"的口径：**只走到 Partial 且这一刻又看不见目标**。
            // 为什么不用"路径终点离目标多远"：Partial 的终点就是最近可达格，与目标之间的距离
            // 恰好取决于隔着多厚的墙（3 米外也能是 Partial），拿一个米数当阈值等于凭空发明数字。
            // "看得见 ⇒ 够得到"是本作视线门（同一张图、同一个主人）已经回答过的问题，直接问它。
            var cannotReach = found == MonsterPathResult.Unreachable
                              || (found == MonsterPathResult.Partial
                                  && !MonsterPathCore.HasLineOfSight(in inputs.Grid, self.X, self.Y, destX, destY));
            if (cannotReach)
            {
                if (state.NoPathStartTick == 0) state.NoPathStartTick = inputs.ServerTick;
                var elapsed = inputs.ServerTick - state.NoPathStartTick;
                state.NoPathTicks = elapsed > int.MaxValue ? int.MaxValue : (int)elapsed;
            }
            else
            {
                state.NoPathTicks = 0;
                state.NoPathStartTick = 0;
            }
        }

        // 够不到目标超过 5 秒 ⇒ 脱战（AC EVADE_REASON_NO_PATH / CONFIG_NPC_EVADE_IF_NOT_REACHABLE=5）。
        // 没有这一条时，玩家躲到怪绕不过去的墙后，那只怪会永远贴着墙站着——lure 不走、也不脱战。
        if (useNav && state.NoPathTicks >= (int)(MonsterAiGate.NoPathEvadeSeconds * inputs.TicksPerSecond))
        {
            decision.NextState = MonsterAiState.Evade;
            decision.ThreatTargetId = 0UL;
            decision.EvadedByNoPath = true;
            decision.DeltaX = 0f;
            decision.DeltaY = 0f;
            decision.Moved = false;
            state.ThreatCount = 0;
            state.PathLength = 0;
            state.PathCursor = 0;
            state.NoPathTicks = 0;
            state.NoPathStartTick = 0;
            MonsterCastingCore.ResetCastState(ref state.Casting);
            return;
        }

        if (state.PathLength > 0 && state.PathCursor < state.PathLength)
        {
            var newX = self.X;
            var newY = self.Y;
            var moved = MonsterPathCore.AdvanceAlongPath(inputs.Grid, state.PathCells, state.PathLength,
                ref state.PathCursor, ref newX, ref newY, speed, dt);
            // 内核不修改调用方结构：回推增量，宿主仍按 DeltaX/DeltaY 应用（与 v1 的写回口径一致）。
            decision.DeltaX = newX - self.X;
            decision.DeltaY = newY - self.Y;
            decision.Moved = moved && (decision.DeltaX != 0f || decision.DeltaY != 0f);
            decision.NextYaw = decision.Moved ? YawTowards(self.X, self.Y, newX, newY, self.Yaw) : self.Yaw;
            return;
        }

        // 够不到（这次算出来的路是 Unreachable，或者 Partial 但终点离目标还远）：
        // **不许拿直线兜底**。以前这里一律退直线，而已知绕不过去时那条直线就等于"从墙里穿过去"——
        // 实测：整列是墙、目标在墙后，怪会一路平推到目标身上开打（x=17），玩家看到的是穿墙。
        // AC 的处置是"够不到就等 5 秒然后脱战"，这里照做：站着不动，攒够 NoPathTicks 由上面那一支踢出战斗。
        if (state.NoPathTicks > 0)
        {
            decision.DeltaX = 0f;
            decision.DeltaY = 0f;
            decision.Moved = false;
            decision.NextYaw = YawTowards(self.X, self.Y, destX, destY, self.Yaw);
            return;
        }

        // 走图没给出路径（起终点同格/直达）→ 退化为直线，与 v1 一致
        var (fdx, fdy) = StepTowards(self.X, self.Y, destX, destY, speed, dt);
        decision.DeltaX = fdx;
        decision.DeltaY = fdy;
        decision.Moved = fdx != 0f || fdy != 0f;
        decision.NextYaw = YawTowards(self.X, self.Y, self.X + fdx, self.Y + fdy, self.Yaw);
    }

    /// <summary>
    /// 主动怪索敌：仇恨半径内**最近**的可打目标；朝向锥 &lt; 360° 时只认正面扇区里的那些
    /// （逆向规格 §3.1："背后贴着它不一定会醒"，而 360° 是"全向怪"）。
    /// <para>
    /// <b>与 <see cref="LegacySelectThreat"/> 的分工</b>：后者必须逐字保持 v1 的判据（金轨迹钉着它），
    /// 所以朝向锥只作用在这条 v3 新增的接敌路径上。两者都用"距离严格小者胜、同距离保留先遇到者"，
    /// 而 targets 已按 EntityId 升序，故结果确定。
    /// </para>
    /// <para>朝向是 <see cref="MonsterAiSelf.Yaw"/>（atan2 约定，仅表现层回灌过位置的部分不受影响）。</para>
    /// </summary>
    /// <summary>
    /// 求援：半径内**同族**正在打谁，我就把那个人记进自己的威胁表（基础威胁，之后由伤害竞争接管）。
    /// <para>三条判据缺一不可：</para>
    /// <list type="number">
    /// <item><description>物种必须相等且都 &gt; 0。0 是"没配物种"，把 0==0 当同族会让全地图没配物种的怪互相拉成片。</description></item>
    /// <item><description>盟友必须**确实在战斗里**且目标还活着可攻击——否则会去追一个不存在的人（表现为朝 (0,0) 冲）。</description></item>
    /// <item><description>取最近者；同距离按盟友实体 id 小的赢。宿主给的清单是 EntityId 升序，
    /// 这条判据因此与遍历顺序无关，金轨迹可对拍。</description></item>
    /// </list>
    /// </summary>
    /// <summary>
    /// 那个人此刻还在"可打的目标集合"里吗（活着 + 可攻击）。
    /// <para>求援的两处复查共用这一条：听见召唤时挑盟友的目标、以及 AC 那两秒到期时要不要再接手。
    /// 分成两份判据的话，"听见时算能打、到期时算不能打"这种半路变卦会在玩家那边表现为
    /// "队友动了一半又站回去"。</para>
    /// </summary>
    private static bool HasAttackableTarget(ReadOnlySpan<MonsterAiTarget> targets, ulong entityId)
    {
        if (entityId == 0UL) return false;
        for (var t = 0; t < targets.Length; t++)
        {
            var cand = targets[t];
            if (cand.EntityId == entityId && cand.IsAlive && cand.IsAttackable) return true;
        }
        return false;
    }

    private static ulong SelectAssistTarget(
        in MonsterAiSelf self,
        MonsterAiAlly[]? allies,
        in MonsterBehaviorConfig behavior,
        ReadOnlySpan<MonsterAiTarget> targets,
        int selfSpecies,
        out ulong callerId)
    {
        callerId = 0UL;
        if (allies == null || allies.Length == 0) return 0UL;
        if (selfSpecies <= 0) return 0UL;
        var radiusSq = behavior.AssistRadius * behavior.AssistRadius;
        if (radiusSq <= 0f) return 0UL;

        var bestTarget = 0UL;
        var bestSq = float.MaxValue;
        var bestAllyId = ulong.MaxValue;
        for (var i = 0; i < allies.Length; i++)
        {
            var ally = allies[i];
            if (!ally.InCombat || ally.TargetId == 0UL) continue;
            if (ally.Species != selfSpecies) continue;

            var dx = ally.X - self.X;
            var dy = ally.Y - self.Y;
            var sq = dx * dx + dy * dy;
            if (sq > radiusSq) continue;
            if (sq > bestSq || (sq == bestSq && ally.EntityId >= bestAllyId)) continue;

            // 盟友的目标必须此刻还在目标集合里且可打：求援传来传去最后追一个影子，
            // 是"怪朝空气狂奔"这类表现的根因。（判据与"到期复查"共用 <see cref="HasAttackableTarget"/>）
            if (!HasAttackableTarget(targets, ally.TargetId)) continue;

            bestSq = sq;
            bestAllyId = ally.EntityId;
            bestTarget = ally.TargetId;
        }
        callerId = bestAllyId == ulong.MaxValue ? 0UL : bestAllyId;
        return bestTarget;
    }

    private static ulong SelectByAggroRangeAndCone(
        in MonsterAiSelf self,
        ReadOnlySpan<MonsterAiTarget> targets,
        in MonsterBehaviorConfig behavior,
        in MonsterAiConfig legacy,
        in MonsterNavGrid grid)
    {
        // 半径这一格的主人在 MonsterAiGate（v1 的索敌调同一个）。
        // 这里以前写的是"内容没配（≤0）就退回宿主那份"，与 v1 的"≤0 = 不成立"是两种口径，
        // 后果是一只配成 0 的怪在两条通路里一个聋一个看得见；0 已由校验器与装载两侧当非法值拒收，
        // 运行期不再补一个凭空出现的距离（见 MonsterAiGate.AggroRadiusUsable 的注释）。
        if (!MonsterAiGate.AggroRadiusUsable(behavior.AggroRadius)) return 0UL;

        // 扇形判据不在这里写第二份：主人是 MonsterAiGate.InAggroCone（v1 的索敌也调它）。
        var best = 0UL;
        var bestSq = float.MaxValue;
        for (var i = 0; i < targets.Length; i++)
        {
            var t = targets[i];
            if (!t.IsAlive || !t.IsAttackable || t.EntityId == 0UL) continue;

            // 等级差那一格也是<b>逐候选</b>问同一个主人（半径住在 legacy 那份投影里，与 v1 同源）。
            var radius = MonsterAiGate.EffectiveAggroRadius(behavior.AggroRadius, legacy.Level, t.Level);
            var aggroSq = radius * radius;

            var dx = t.X - self.X;
            var dy = t.Y - self.Y;
            var sq = dx * dx + dy * dy;
            if (sq > aggroSq) continue;

            if (!MonsterAiGate.InAggroCone(behavior.AggroConeDegrees, self.Yaw, dx, dy)) continue;
            // 藏着的人：与 v1 同一个主人（`detect_mask` 的 bit2）。
            if (!MonsterAiGate.CanDetectTarget(behavior.DetectMask, t.IsHidden)) continue;
            // 墙/悬崖挡住视线就看不见（AC 把 `IsWithinLOSInMap` 写在索敌条件里：怪不会隔墙起手）。
            // 判据主人是 `MonsterPathCore.HasLineOfSight`（寻路拉直用的同一个），这里不另写一份。
            // **没装导航图时不过滤**：与"没图就直线走"同一口径——今天生产世界还没有 MONSTERNAV，
            // 这一条在那种状态下行为零变化，装了图才看得出差别。
            if (grid.IsValid && !MonsterPathCore.HasLineOfSight(in grid, self.X, self.Y, t.X, t.Y)) continue;

            if (sq < bestSq)
            {
                bestSq = sq;
                best = t.EntityId;
            }
        }

        return best;
    }

    private static ulong LegacySelectThreat(in MonsterAiSelf self, ReadOnlySpan<MonsterAiTarget> targets, in MonsterAiConfig cfg, bool blockedFromNewEnemy, in MonsterNavGrid grid)
    {
        if (blockedFromNewEnemy) return 0UL;
        if (self.ThreatTargetId != 0UL && IsEngageable(self.ThreatTargetId, targets)) return self.ThreatTargetId;
        var best = 0UL;
        var bestSq = float.MaxValue;
        var aggroSq = cfg.AggroRadius * cfg.AggroRadius;
        for (var i = 0; i < targets.Length; i++)
        {
            var t = targets[i];
            if (!t.IsAlive || !t.IsAttackable || t.EntityId == 0UL) continue;
            var dx = t.X - self.X;
            var dy = t.Y - self.Y;
            var sq = dx * dx + dy * dy;
            if (sq > aggroSq) continue;
            // 威胁表关着的那一支也吃同一道视线门（AC 把它写在基础的"能不能打"里，不分档位）。
            if (grid.IsValid && !MonsterPathCore.HasLineOfSight(in grid, self.X, self.Y, t.X, t.Y)) continue;
            if (sq < bestSq)
            {
                bestSq = sq;
                best = t.EntityId;
            }
        }
        return best;
    }

    private static bool IsEngageable(ulong entityId, ReadOnlySpan<MonsterAiTarget> targets)
        => MonsterAiGate.IsEngageable(entityId, targets);

    /// <summary>移出"已不在目标集合里 / 已死 / 不可攻击"的威胁条目（就地压缩，保持升序）。</summary>
    private static void PruneByPresence(MonsterRuntimeState state, ReadOnlySpan<MonsterAiTarget> targets)
    {
        if (state.ThreatCount <= 0) return;
        var w = 0;
        for (var i = 0; i < state.ThreatCount; i++)
        {
            var entry = state.ThreatTable[i];
            var keep = false;
            for (var t = 0; t < targets.Length; t++)
            {
                if (targets[t].EntityId != entry.EntityId) continue;
                keep = targets[t].IsAlive && targets[t].IsAttackable;
                break;
            }
            if (!keep) continue;
            if (w != i) state.ThreatTable[w] = entry;
            w++;
        }
        state.ThreatCount = w;
    }

    private static MonsterAiTarget FindTarget(ReadOnlySpan<MonsterAiTarget> targets, ulong entityId)
    {
        for (var i = 0; i < targets.Length; i++)
        {
            if (targets[i].EntityId == entityId) return targets[i];
        }
        return default;
    }

    private static (float DeltaX, float DeltaY) StepTowards(float x, float y, float tx, float ty, float speed, float dt)
    {
        var dx = tx - x;
        var dy = ty - y;
        var lenSq = dx * dx + dy * dy;
        if (lenSq <= 1e-8f) return (0f, 0f);
        var len = (float)Math.Sqrt(lenSq);
        var step = speed * dt;
        if (step >= len) return (dx, dy);
        var inv = 1f / len;
        return (dx * inv * step, dy * inv * step);
    }

    private static float Distance2D(float ax, float ay, float bx, float by)
    {
        var dx = ax - bx;
        var dy = ay - by;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    private static float YawTowards(float fromX, float fromY, float toX, float toY, float fallbackYaw)
    {
        var dx = toX - fromX;
        var dy = toY - fromY;
        if (dx == 0f && dy == 0f) return fallbackYaw;
        return (float)Math.Atan2(dy, dx);
    }
}
