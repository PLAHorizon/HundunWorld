using System;

namespace Horizon.Game.Sim.Monsters;

/// <summary>技能挂载的触发时机（逆向规格 §6.2 的 slot_type）。</summary>
public enum MonsterSkillSlot : byte
{
    /// <summary>战斗中（默认）。</summary>
    Combat = 0,

    /// <summary>非战斗（巡逻路上顺手动一下，WoW 里某些怪会这样"自检"）。</summary>
    NonCombat = 1,

    /// <summary>残血应急（血量低于阈值才进候选）。</summary>
    EmergencyLowHp = 2,

    /// <summary>受控时（被减速/定身时使用，实践中很少配）。</summary>
    OnControlled = 3,
}

/// <summary>技能的目标挑选方式。</summary>
public enum MonsterTargetSelect : byte
{
    /// <summary>当前威胁目标（单体）。默认。</summary>
    CurrentThreat = 0,

    /// <summary>仇恨表内随机一个（确定性掷骰，"不打 T 打奶"的来源）。</summary>
    RandomInThreat = 1,

    /// <summary>最近的一个可攻击者。</summary>
    Nearest = 2,

    /// <summary>血量最低的一个（补刀怪）。</summary>
    LowestHp = 3,

    /// <summary>以当前威胁目标为圆心的范围内多目标（AOE）。</summary>
    AoeAroundThreat = 4,
}

/// <summary>
/// 怪物技能挂载（一行 = 这只怪会放的一个技能）。
/// </summary>
/// <remarks>
/// <b>复用玩家技能 id（决策 D8）</b>：<see cref="SkillId"/> 指向既有
/// <c>Game_HunduShijie_SkillTemplate</c>，效果链走服务端已有的 <c>WuxingSkillEffects</c>。
/// 不新造一套怪物技能定义表——那会把已经跑通的五行效果链旁路掉。
/// <para>
/// <b>为什么每个字段都要能配</b>：玩家能看见的每条表现都对应一个字段。
/// "这怪有时喷火有时近战" = <see cref="ChanceBasisPoints"/>；"施法条可打断" =
/// <see cref="Interruptible"/>；"它会边跑边放" = <see cref="AllowMovement"/>。
/// 少一个字段，编辑器就只能"看起来能配、实际改不动"。
/// </para>
/// </remarks>
public struct MonsterSkillMount
{
    /// <summary>技能 id（对齐玩家技能模板）。</summary>
    public int SkillId;

    /// <summary>触发时机。</summary>
    public MonsterSkillSlot Slot;

    /// <summary>
    /// 每次进入候选时的独立释放概率，基点（0..10000 = 0%..100%）。
    /// <para>用基点整数而不是 float 0..1：概率是策划直接写"35%"的东西，
    /// 而浮点 0.35f 参与比较会有平台侧的相对顺序问题。</para>
    /// </summary>
    public int ChanceBasisPoints;

    /// <summary>吟唱时长（毫秒；0 = 瞬发）。决定客户端施法条长度。</summary>
    public int CastMs;

    /// <summary>冷却（毫秒）。</summary>
    public int CooldownMs;

    /// <summary>最小射程（米）——远程怪会后退拉开的判据之一。</summary>
    public float MinRange;

    /// <summary>最大射程（米）。</summary>
    public float MaxRange;

    /// <summary>目标挑选方式。</summary>
    public MonsterTargetSelect TargetSelect;

    /// <summary>最大目标数（AOE 时也作为封顶）。</summary>
    public int TargetCount;

    /// <summary>AOE 半径（米）。</summary>
    public float AoeRadius;

    /// <summary>伤害系别位（金木水火土 + 物理），客户端据此给施法条分色。</summary>
    public int SchoolMask;

    /// <summary>是否可被打断（1 = 可）。决定客户端是否显示"可打断"标记与是否给打断手段。</summary>
    public bool Interruptible;

    /// <summary>是否允许移动施法。</summary>
    public bool AllowMovement;

    /// <summary>伤害倍率（相对技能模板的基础伤害；怪物用它做数值缩放而不改技能表）。</summary>
    public float DamageScale;

    /// <summary>附加状态 id（0 = 无）。</summary>
    public int StatusId;

    /// <summary>状态持续秒。</summary>
    public float StatusDurationSeconds;

    /// <summary>
    /// 状态强度（基点，10000 = 100%；今天只有减速用它：5000 = 走速打五折）。
    /// <para><b>0 = 没填</b>（与填 0 同义，都不生效）。数据来源是挂载表那一列
    /// `status_magnitude_bps`（<c>scripts/sql/026</c>，2026-10-28 已对两个库执行）。</para>
    /// </summary>
    public int StatusMagnitudeBps;

    /// <summary>起手喊话/表情 id（客户端表现，也是玩家的"预警"）。</summary>
    public int StartEmoteId;

    /// <summary>残血阈值（<see cref="MonsterSkillSlot.EmergencyLowHp"/> 专用，百分比 0..100）。</summary>
    public int EmergencyHpPercent;
}

/// <summary>怪物施法自身的运行态（宿主持有并逐 tick 回写）。</summary>
public struct MonsterCastingSelf
{
    /// <summary>正在吟唱的挂载下标；-1 = 未在吟唱。</summary>
    public int ActiveMountIndex;

    /// <summary>吟唱结束的服务器 tick（瞬发时 = 开始 tick）。</summary>
    public long CastEndTick;

    /// <summary>吟唱目标实体（起手时锁定，不随威胁表变化改判——对齐 WoW"起手后目标已定"）。</summary>
    public ulong CastTargetId;

    /// <summary>起手时记录的目标坐标（AOE 圆心；目标跑动不该让落点漂移）。</summary>
    public float CastTargetX;
    public float CastTargetY;
    public float CastTargetZ;

    /// <summary>吟唱已进行的累计秒（表现层进度条用，宿主直接下发）。</summary>
    public float CastElapsedSeconds;

    /// <summary>吟唱总时长（秒）。</summary>
    public float CastTotalSeconds;
    /// <summary>吟唱是否真的在进行（宿主与内核都以此为准）。</summary>
    /// <remarks>
    /// <b>为什么不能只看 <see cref="MonsterCastingSelf.ActiveMountIndex"/> &gt;= 0</b>：
    /// 结构体的默认值是 0，而 0 是合法挂载下标。于是"从未施法"与"正在施第 0 号技能"在数据上同形，
    /// 首 tick 会被判成"吟唱已到点"而立刻结算一次从未起手的技能（本工程的测试实测到过）。
    /// 真吟唱的必要条件是同时记下了起手目标。
    /// </remarks>
    /// <remarks>
    /// "有起手痕迹"的判据不能要求 <see cref="CastTargetId"/> 非零：<c>slot_type=1</c>（非战斗）那档
    /// 本来就没有敌方目标（给自己/同伴上个术），只认"有目标"会让这种施法在表现层彻底消失
    /// ——内核真的在读条，玩家头顶却什么都没有（这条是把它跑起来才发现的，不是读代码看出来的）。
    /// 默认值形态的坑仍然要防：全新结构体的 <c>ActiveMountIndex</c> 恰好是合法的 0，
    /// 但它的 <c>CastTotalSeconds</c> 与 <c>CastEndTick</c> 都是 0，所以下面两个条件都要求"留下过痕迹"。
    /// 真吟唱的必要条件是：有挂载下标，且**要么锁定了目标，要么记下了吟唱时长/结束 tick**。
    /// </remarks>
    public readonly bool IsCasting => ActiveMountIndex >= 0 &&
        (CastTargetId != 0UL || CastTotalSeconds > 0f || CastEndTick != 0L);
}
/// <summary>施法子状态机的单 tick 产出。</summary>
public struct MonsterCastDecision
{
    /// <summary>本 tick 是否起手了一个新技能。</summary>
    public bool BeginsCast;

    /// <summary>本 tick 是否吟唱完成（宿主据此结算伤害）。</summary>
    public bool FinishesCast;

    /// <summary>本 tick 是否被打断。</summary>
    public bool InterruptedCast;

    /// <summary>涉及的挂载下标。</summary>
    public int MountIndex;

    /// <summary>涉及的技能 id。</summary>
    public int SkillId;

    /// <summary>结算目标（<see cref="FinishesCast"/> 时有效；宿主按 <see cref="MonsterSkillMount.TargetSelect"/> 重算最终集合）。</summary>
    public ulong TargetEntityId;

    /// <summary>吟唱期间是否应当禁止位移（= !AllowMovement）。</summary>
    public bool ShouldHalt;

    /// <summary>当前吟唱进度 0..1（宿主下发给客户端施法条）。</summary>
    public float CastProgress;
}

/// <summary>
/// 怪物施法的确定性内核（逆向规格 §6）。
/// <para>
/// <b>它填的是哪个洞</b>：<c>Horizon.Orleans.Interface/Combat/IMonsterGrain.cs</c> 里早就声明了
/// <c>MonsterAiAction.CastSkill</c> 与 <c>MonsterAiDecision.SkillId</c>，但**全仓没有任何生产者**；
/// 现役 <c>MonsterAiCore</c> 只有 <c>WantsAttack</c>（纯近战）。也就是说"怪物放技能"这件事
/// 一直停留在协议字段层面。本类把它变成真的有产出。
/// </para>
/// <para>
/// <b>候选顺序</b>：挂载表必须按数组下标升序遍历（宿主保证），概率掷骰逐条独立判定 ——
/// 这就是 WoW 观测到的"有时喷火有时近战"的实现方式（<c>chance</c> 是概率不是权重）。
/// </para>
/// </summary>
public static class MonsterCastingCore
{
    /// <summary>版本（进金轨迹锚点）。</summary>
    public const int Version = 1;

    /// <summary>概率基点上限。</summary>
    public const int ChanceBasisPointsMax = 10000;

    /// <summary>掷骰盐值（与 <c>CombatCore</c> 的闪避/暴击盐值区分开，避免同一 tick 的相关性）。</summary>
    private const uint CastSalt = 0x4D00u;

    /// <summary>被打断后的"重试静默"秒数（WoW 观测：被打断的怪不会每 tick 重试，否则玩家永远打不断）。</summary>
    public const float InterruptBackoffSeconds = 1.5f;

    /// <summary>该挂载本 tick 是否是候选（时机 + 残血阈值 + 冷却 + 射程）。</summary>
    public static bool IsCandidate(
        in MonsterSkillMount mount,
        int mountIndex,
        ReadOnlySpan<long> readyAtTick,
        long serverTick,
        float distanceToTarget,
        int hpPercent,
        bool inCombat)
    {
        // 时机过滤。
        switch (mount.Slot)
        {
            case MonsterSkillSlot.Combat:
                if (!inCombat) return false;
                break;
            case MonsterSkillSlot.NonCombat:
                if (inCombat) return false;
                break;
            case MonsterSkillSlot.EmergencyLowHp:
                if (!inCombat) return false;
                if (hpPercent > mount.EmergencyHpPercent) return false;
                break;
            case MonsterSkillSlot.OnControlled:
                if (!inCombat) return false;
                break;
        }

        // 冷却。
        if (readyAtTick.Length > mountIndex && readyAtTick[mountIndex] > serverTick) return false;

        // 射程。
        if (mount.MaxRange > 0f && distanceToTarget > mount.MaxRange) return false;
        if (mount.MinRange > 0f && distanceToTarget < mount.MinRange) return false;

        return mount.ChanceBasisPoints > 0;
    }

    /// <summary>
    /// 推进施法子状态机一 tick。
    /// </summary>
    /// <remarks>
    /// 调用顺序纪律：
    /// <list type="number">
    ///   <item>若正在吟唱且 <paramref name="interruptSignal"/> 为 true 且该技能可打断 → 打断并进静默。</item>
    ///   <item>若正在吟唱且 tick 到达 <see cref="MonsterCastingSelf.CastEndTick"/> → 完成。</item>
    ///   <item>若空闲 → 按下标升序找第一个"候选且掷中概率"的挂载起手。</item>
    /// </list>
    /// <b>正在吟唱时不再起新手</b>：否则一只怪会同时挂两条吟唱，客户端施法条会互相覆盖。
    /// </remarks>
    public static MonsterCastDecision Advance(
        ref MonsterCastingSelf self,
        in ReadOnlySpan<MonsterSkillMount> mounts,
        Span<long> readyAtTick,
        ulong monsterEntityId,
        ulong threatTargetId,
        float threatTargetX,
        float threatTargetY,
        float threatTargetZ,
        float distanceToThreat,
        int hpPercent,
        bool inCombat,
        bool interruptSignal,
        long serverTick,
        int ticksPerSecond,
        float dt)
    {
        var decision = new MonsterCastDecision { MountIndex = -1 };
        if (mounts.Length == 0) return decision;
        if (ticksPerSecond <= 0) ticksPerSecond = 60;
        if (self.ActiveMountIndex < 0 || self.ActiveMountIndex >= mounts.Length)
        {
            self.ActiveMountIndex = -1;
            self.CastTargetId = 0UL;
        }
        else if (self.CastTargetId == 0UL && self.CastEndTick == 0 && self.CastElapsedSeconds <= 0f)
        {
            // 结构体默认值形态（ActiveMountIndex 恰好是合法的 0，却没有任何起手痕迹）：收归"未在施法"。
            self.ActiveMountIndex = -1;
        }

        // --- 吟唱中 ---
        if (self.ActiveMountIndex >= 0)
        {
            var idx = self.ActiveMountIndex;
            var mount = mounts[idx];
            decision.MountIndex = idx;
            decision.SkillId = mount.SkillId;
            decision.TargetEntityId = self.CastTargetId;
            decision.ShouldHalt = !mount.AllowMovement;

            if (self.CastTotalSeconds > 0f)
            {
                self.CastElapsedSeconds += dt > 0f ? dt : 0f;
                var p = self.CastElapsedSeconds / self.CastTotalSeconds;
                decision.CastProgress = p < 0f ? 0f : (p > 1f ? 1f : p);
            }
            else
            {
                decision.CastProgress = 1f;
            }

            if (interruptSignal && mount.Interruptible)
            {
                decision.InterruptedCast = true;
                ResetCastState(ref self);
                // 打断后给本技能进一段静默，避免下一 tick 立刻重试。
                if (idx >= 0 && idx < readyAtTick.Length)
                {
                    var backoffTicks = (long)(InterruptBackoffSeconds * ticksPerSecond);
                    readyAtTick[idx] = serverTick + Math.Max(1L, backoffTicks);
                }
                return decision;
            }

            if (serverTick >= self.CastEndTick)
            {
                decision.FinishesCast = true;
                ResetCastState(ref self);
                if (idx >= 0 && idx < readyAtTick.Length)
                {
                    var cdTicks = (long)(mount.CooldownMs / 1000.0 * ticksPerSecond);
                    readyAtTick[idx] = serverTick + (cdTicks > 0 ? cdTicks : 1L);
                }
            }
            return decision;
        }

        // --- 空闲：找候选 ---
        for (var i = 0; i < mounts.Length; i++)
        {
            var mount = mounts[i];
            if (!IsCandidate(in mount, i, readyAtTick, serverTick, distanceToThreat, hpPercent, inCombat)) continue;

            // 独立掷骰：(怪物, 目标, tick, 挂载下标盐)。概率是基点整数，比较两侧同为整数域。
            var roll = CombatCore.Roll01(monsterEntityId, threatTargetId, serverTick, CastSalt + (uint)i);
            var threshold = (float)mount.ChanceBasisPoints / ChanceBasisPointsMax;
            if (roll >= threshold) continue;

            self.ActiveMountIndex = i;
            self.CastTargetId = threatTargetId;
            self.CastTargetX = threatTargetX;
            self.CastTargetY = threatTargetY;
            self.CastTargetZ = threatTargetZ;

            var castSeconds = mount.CastMs > 0 ? mount.CastMs / 1000.0f : 0f;
            self.CastTotalSeconds = castSeconds;
            self.CastElapsedSeconds = 0f;
            self.CastEndTick = castSeconds > 0f
                ? serverTick + (long)(castSeconds * ticksPerSecond)
                : serverTick;

            decision.BeginsCast = true;
            decision.MountIndex = i;
            decision.SkillId = mount.SkillId;
            decision.TargetEntityId = threatTargetId;
            decision.ShouldHalt = !mount.AllowMovement;
            decision.CastProgress = castSeconds > 0f ? 0f : 1f;

            // 瞬发：起手即完成（同一 tick 内先 Begin 后 Finish 会让宿主结算两次，故显式拆成两 tick）。
            if (castSeconds <= 0f)
            {
                self.ActiveMountIndex = -1;
                self.CastEndTick = 0;
                if (i >= 0 && i < readyAtTick.Length)
                {
                    var cdTicks = (long)(mount.CooldownMs / 1000.0 * ticksPerSecond);
                    readyAtTick[i] = serverTick + (cdTicks > 0 ? cdTicks : 1L);
                }
                decision.FinishesCast = true;
            }
            return decision;
        }

        return decision;
    }

    /// <summary>施法进度（宿主在 AI tick 之外单独要值时用，例如下发快照）。</summary>
    public static float ProgressOf(in MonsterCastingSelf self)
    {
        if (self.ActiveMountIndex < 0) return 0f;
        if (self.CastTotalSeconds <= 0f) return 1f;
        var p = self.CastElapsedSeconds / self.CastTotalSeconds;
        return p < 0f ? 0f : (p > 1f ? 1f : p);
    }

    /// <summary>清吟唱态（脱战/Evade/死亡都必须调，否则怪回位途中"施法条"会一直挂在客户端头顶）。</summary>
    public static void ResetCastState(ref MonsterCastingSelf self)
    {
        self.ActiveMountIndex = -1;
        self.CastEndTick = 0;
        self.CastTargetId = 0UL;
        self.CastElapsedSeconds = 0f;
        self.CastTotalSeconds = 0f;
    }

    /// <summary>
    /// 施法可选目标（宿主按 EntityId 升序传入，保证确定性）。
    /// <para><see cref="InThreat"/> 表示"在怪物的威胁表里"——<see cref="MonsterTargetSelect.RandomInThreat"/>
    /// 只在这些人里挑，所以宿主必须把威胁表的成员关系带进来，否则"不打 T 打奶"就只是一句枚举注释。</para>
    /// </summary>
    public readonly struct MonsterCastCandidate
    {
        public readonly ulong EntityId;
        public readonly float X;
        public readonly float Y;

        /// <summary>当前生命值（<see cref="MonsterTargetSelect.LowestHp"/> 的判据）。</summary>
        public readonly int Hp;

        /// <summary>是否仍在存活且可被攻击。</summary>
        public readonly bool Engageable;

        /// <summary>是否在威胁表内。</summary>
        public readonly bool InThreat;

        public MonsterCastCandidate(ulong entityId, float x, float y, int hp, bool engageable, bool inThreat)
        {
            EntityId = entityId;
            X = x;
            Y = y;
            Hp = hp;
            Engageable = engageable;
            InThreat = inThreat;
        }
    }

    /// <summary>
    /// 按挂载的挑选方式决定这次技能真正打谁（**结算前重算**，起手时锁定的只是"圆心/首要目标"）。
    /// <para>
    /// <b>填的哪个洞</b>：<see cref="MonsterTargetSelect"/> 与 <see cref="MonsterSkillMount.TargetCount"/>
    /// 一路从编辑器存进内容包，内核的注释写着"宿主按 TargetSelect 重算最终集合"，
    /// 但宿主从来没做——结果是"AOE 以目标为心"这种技能实际只打一个人，
    /// 玩家在编辑器里看到配了 3 目标 / 8 米，进游戏站在一起却只有一个人掉血。
    /// </para>
    /// <para>
    /// <b>确定性</b>：候选必须按 EntityId 升序；同距离/同血量一律取较小 ID；
    /// 随机只走 <see cref="CombatCore.Roll01"/>（splitmix64，与伤害掷骰同一支）且用输入量做种子，
    /// 不用 <c>Random</c>、不用时钟。输出顺序 = 挑选顺序，宿主按这个顺序逐个结算，
    /// 于是同一 tick 重放必然得到同一组受害者。
    /// </para>
    /// </summary>
    /// <param name="mount">技能挂载（决定方式、上限数、AOE 半径）。</param>
    /// <param name="primaryTargetId">起手锁定的首要目标（AOE 圆心 / 单体默认目标）。</param>
    /// <param name="casterX">施法者 X（服务端 Z-up 的水平轴）。</param>
    /// <param name="casterY">施法者 Y。</param>
    /// <param name="candidates">按 EntityId 升序的候选。</param>
    /// <param name="serverTick">当前 tick（随机种子的一部分）。</param>
    /// <param name="output">输出缓冲；容量至少 1，写回前 <paramref name="count"/> 项有效。</param>
    /// <param name="count">实际选中的目标数。</param>
    /// <param name="requested">挂载想要的目标数（截断前的需求，诊断用）。</param>
    /// <returns>是否至少选出一个目标。</returns>
    public static bool SelectTargets(
        in MonsterSkillMount mount,
        ulong primaryTargetId,
        float casterX,
        float casterY,
        ReadOnlySpan<MonsterCastCandidate> candidates,
        ulong casterEntityId,
        long serverTick,
        Span<ulong> output,
        out int count,
        out int requested)
    {
        count = 0;
        requested = mount.TargetCount > 0 ? mount.TargetCount : 1;
        if (output.Length == 0) return false;

        var cap = requested;
        if (cap > output.Length) cap = output.Length;

        switch (mount.TargetSelect)
        {
            case MonsterTargetSelect.AoeAroundThreat:
            {
                // 圆心 = 起手时锁定的目标坐标（WoW 观感：玩家跑开了，圈还在原地）。
                var center = default(MonsterCastCandidate);
                var haveCenter = false;
                for (var i = 0; i < candidates.Length; i++)
                {
                    if (candidates[i].EntityId != primaryTargetId) continue;
                    center = candidates[i];
                    haveCenter = true;
                    break;
                }

                var radius = mount.AoeRadius > 0f ? mount.AoeRadius : 5f;
                var radiusSq = radius * radius;
                for (var i = 0; i < candidates.Length && count < cap; i++)
                {
                    var c = candidates[i];
                    if (!c.Engageable) continue;
                    if (!haveCenter)
                    {
                        // 圆心目标已经没了：AOE 退化成"以施法者最近的一个人打一发"，
                        // 而不是"整圈没人挨打"——技能落空但一条伤害都没有是最难解释的表现。
                        continue;
                    }
                    var dx = c.X - center.X;
                    var dy = c.Y - center.Y;
                    if (dx * dx + dy * dy > radiusSq) continue;
                    output[count++] = c.EntityId;
                }

                if (count > 0) return true;
                // 走到这里 = 一个都没圈进（圆心目标消失，或圈内确实只有死人）。
                // 保底仍打首要目标可打时的那一位：单体退化，绝不"放了个空技能"。
                var fallback = PickNearest(candidates, casterX, casterY);
                if (fallback == 0UL) fallback = PickNearest(candidates, center.X, center.Y);
                if (fallback == 0UL) return false;
                output[count++] = fallback;
                return true;
            }

            case MonsterTargetSelect.Nearest:
            {
                var id = PickNearest(candidates, casterX, casterY);
                if (id == 0UL) return false;
                output[count++] = id;
                return true;
            }

            case MonsterTargetSelect.LowestHp:
            {
                var best = 0UL;
                var bestHp = int.MaxValue;
                for (var i = 0; i < candidates.Length; i++)
                {
                    var c = candidates[i];
                    if (!c.Engageable || c.EntityId == 0UL) continue;
                    // 严格小于：同血量保留先遇到者（候选已按 ID 升序 ⇒ 取较小 ID）。
                    if (c.Hp < bestHp)
                    {
                        bestHp = c.Hp;
                        best = c.EntityId;
                    }
                }
                if (best == 0UL) return false;
                output[count++] = best;
                return true;
            }

            case MonsterTargetSelect.RandomInThreat:
            {
                // 先数一遍威胁表内的成员，再按确定性掷骰取下标——
                // 不用"边扫边掷"是因为那会让结果依赖候选数组里非表内成员的数量。
                var total = 0;
                for (var i = 0; i < candidates.Length; i++)
                {
                    if (candidates[i].InThreat && candidates[i].Engageable) total++;
                }
                if (total == 0)
                {
                    var nearest = PickNearest(candidates, casterX, casterY);
                    if (nearest == 0UL) return false;
                    output[count++] = nearest;
                    return true;
                }
                var roll = CombatCore.Roll01(casterEntityId, (ulong)total, serverTick, salt: CastSalt ^ 0x52u);
                var index = (int)(roll * total);
                if (index >= total) index = total - 1;
                if (index < 0) index = 0;
                var seen = 0;
                for (var i = 0; i < candidates.Length; i++)
                {
                    if (!candidates[i].InThreat || !candidates[i].Engageable) continue;
                    if (seen != index) { seen++; continue; }
                    output[count++] = candidates[i].EntityId;
                    return true;
                }
                return false;
            }

            default:
            {
                // CurrentThreat（含未知值）：首要目标还能打就打它，否则退最近的一个。
                for (var i = 0; i < candidates.Length; i++)
                {
                    var c = candidates[i];
                    if (c.EntityId != primaryTargetId || !c.Engageable) continue;
                    output[count++] = c.EntityId;
                    return true;
                }
                var nearest = PickNearest(candidates, casterX, casterY);
                if (nearest == 0UL) return false;
                output[count++] = nearest;
                return true;
            }
        }
    }

    /// <summary>最近的 Engageable 候选；同距离取较小 EntityId（候选须已按 ID 升序）。</summary>
    private static ulong PickNearest(ReadOnlySpan<MonsterCastCandidate> candidates, float fromX, float fromY)
    {
        var best = 0UL;
        var bestSq = float.MaxValue;
        for (var i = 0; i < candidates.Length; i++)
        {
            var c = candidates[i];
            if (!c.Engageable || c.EntityId == 0UL) continue;
            var dx = c.X - fromX;
            var dy = c.Y - fromY;
            var sq = dx * dx + dy * dy;
            if (sq < bestSq)
            {
                bestSq = sq;
                best = c.EntityId;
            }
        }
        return best;
    }

}
