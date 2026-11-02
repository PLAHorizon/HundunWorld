using System;
using System.Collections.Generic;

namespace Horizon.Game.Sim.Npcs;

/// <summary>NPC 的姿态（观测：旅店老板坐着、铁匠在砧前做动作、守卫立正）。<b>只到"玩家看得出来"的粒度</b>。</summary>
public enum NpcPosture : byte
{
    /// <summary>站立。</summary>
    Stand = 0,

    /// <summary>坐（旅店/桌边）。</summary>
    Sit = 1,

    /// <summary>工作（打铁/捣药/纺织）。<b>需要客户端有对应动作集</b>；没有时按 <see cref="Stand"/> 画并记诊断。</summary>
    Work = 2,

    /// <summary>持械待命（守卫）。</summary>
    Guard = 3,

    /// <summary>未登记姿态（按 <see cref="Stand"/> 画，并计入坏行回执——不当成"隐身"处理）。</summary>
    Unknown = 255,
}

/// <summary>
/// 一个作息时段。
/// <para><b>秒数口径：一天周期内的秒</b>，不是世界绝对秒、不是 <c>DateTime</c>。
/// 理由与商人补货同源——作息必须能由 (世界秒, 周期长度) 完全决定，
/// 才能被 <c>Horizon.DsReplayHarness</c> 重放（记忆 [分段计时的秒表不能嵌套] 那条纪律的延伸：内核不读宿主时钟）。</para>
/// </summary>
/// <param name="FromSeconds">起（含）。</param>
/// <param name="ToSeconds">止（不含）。<b>等于起点表示"全天"</b>（唯一的显式特例，别把它当成"零长度所以永不生效"）。</param>
/// <param name="RouteId">这一时段走哪条路线。<b>≤0 = 不覆盖</b>，用刷点上的默认路线；
/// 0 不当成"第 0 条路线"（结构体默认 0 撞合法下标的老坑，这里用负数/非正的显式语义绕开）。</param>
/// <param name="GossipGroupId">这一时段用哪一组台词。≤0 = 不覆盖。</param>
public readonly struct NpcScheduleSlot(
    int fromSeconds,
    int toSeconds,
    int routeId,
    NpcPosture posture,
    int gossipGroupId)
{
    public readonly int FromSeconds = fromSeconds;
    public readonly int ToSeconds = toSeconds;
    public readonly int RouteId = routeId;
    public readonly NpcPosture Posture = posture;
    public readonly int GossipGroupId = gossipGroupId;

    /// <summary>全天档（起止相同）。<see cref="NpcScheduleCore"/> 判它时优先于任何普通档，
    /// 这样"配了全天 + 又配了白天档"时，白天档赢——<b>具体档永远压过兜底档</b>，与路由的最近匹配同理。</summary>
    public readonly bool IsAllDay => FromSeconds == ToSeconds;

    /// <summary>行是否可用（秒数非负）。越界的秒由 <see cref="NpcScheduleCore"/> 结合周期长度判。</summary>
    public readonly bool IsUsable => FromSeconds >= 0 && ToSeconds >= 0;

    /// <summary>
    /// 行是否落在一个周期内。<b>终点是开区间，所以 <c>ToSeconds == 周期长度</c> 是合法的"做到日终"</b>，
    /// 而 <c>FromSeconds == 周期长度</c> 才是越界（那一秒在周期里不存在）。
    /// 把两端都用同一个 <c>&gt;=</c> 卡掉，会让最后一段作息静默消失——
    /// 症状是"NPC 傍晚准时回屋，第二天却整天站着不动"，数值门照样全绿。
    /// </summary>
    public readonly bool FitsInCycle(int dayCycleSeconds) =>
        dayCycleSeconds > 0 && IsUsable && FromSeconds < dayCycleSeconds && ToSeconds <= dayCycleSeconds;

    public readonly string Describe() =>
        $"sched=[{FromSeconds},{ToSeconds}) route={RouteId} posture={(byte)Posture} gossip={GossipGroupId}";
}

/// <summary>作息未命中的分档。</summary>
public enum NpcScheduleReject : byte
{
    /// <summary>命中。</summary>
    None = 0,

    /// <summary>这个 NPC 没有任何作息行（<b>与"有行但当前时刻不在任何档里"分开</b>：前者是"没配"，后者是配漏了）。</summary>
    NoSlots = 1,

    /// <summary>当前时刻落在两档之间的空隙里（配漏了）。</summary>
    Gap = 2,

    /// <summary>周期长度非正（装配缺陷，永远算不出时段）。</summary>
    BadDayLength = 3,

    /// <summary>存在越界/坏行（秒数超出一个周期或负数）。命中时也可能带这一档，<b>两者不互斥</b>。</summary>
    BadSlot = 4,
}

/// <summary>一次作息求值回执。<b>命中与否都带同一组字段</b>。</summary>
public readonly struct NpcScheduleVerdict(
    bool matched,
    int slotIndex,
    int routeId,
    NpcPosture posture,
    int gossipGroupId,
    int secondsOfDay,
    NpcScheduleReject reason,
    int badSlots)
{
    public readonly bool Matched = matched;

    /// <summary>命中的行下标；未命中为 −1（<b>不给 0</b>，0 是合法下标）。</summary>
    public readonly int SlotIndex = slotIndex;

    /// <summary>生效路线。≤0 表示"不覆盖"。</summary>
    public readonly int RouteId = routeId;

    public readonly NpcPosture Posture = posture;
    public readonly int GossipGroupId = gossipGroupId;

    /// <summary>换算出来的"周期内秒"（诊断与编辑器时间轴游标都读它）。</summary>
    public readonly int SecondsOfDay = secondsOfDay;

    public readonly NpcScheduleReject Reason = reason;

    /// <summary>越界/坏行数。<b>命中时也要带</b>：否则一份"白天档能走、深夜档越界"的表看起来完全正常。</summary>
    public readonly int BadSlots = badSlots;

    public readonly string Describe() =>
        $"sched_hit={(Matched ? 1 : 0)} sched_index={SlotIndex} sched_route={RouteId} " +
        $"sched_posture={(byte)Posture} sched_gossip={GossipGroupId} sched_tod={SecondsOfDay} " +
        $"sched_reason={(byte)Reason} sched_bad={BadSlots}";
}

/// <summary>
/// 作息（日程）的确定性内核（规格 §9.2）。
/// <para>
/// <b>形状可抄、数据未通的那一份先说明白</b>：NarrativePro 的 <c>NPCDefinition.ActivitySchedulePaths</c>
/// （<c>NP\AI\NPCDefinition.cs:63</c>）已有同一概念字段，但它的装载数据是 JSON
/// （<c>NP\Data\NarrativeNPCDatabase.cs:104</c>）且外观那一路直接 new 成空（<c>:309</c>）——
/// 所以这里做的是自己的判据，不复用它的运行时。
/// </para>
/// </summary>
public static class NpcScheduleCore
{
    /// <summary>判据版本。</summary>
    public const int Version = 1;

    /// <summary>默认一天周期秒数（一个"游戏日"比现实日短，具体值属可调数据；这里只提供换算入口，不假装是逆向结论）。</summary>
    public const int DefaultDayCycleSeconds = 1200;

    /// <summary>姿态是否在表里（<see cref="NpcPosture.Unknown"/> 与越界值都算不在）。</summary>
    public static bool IsPostureKnown(NpcPosture posture) => posture >= NpcPosture.Stand && posture <= NpcPosture.Guard;

    /// <summary>世界秒 → 周期内秒（负秒也给出正确位置：向 [0, cycle) 归一，不回绕成反向）。</summary>
    public static int SecondsOfDay(long worldSeconds, int dayCycleSeconds)
    {
        if (dayCycleSeconds <= 0) return -1;
        var m = worldSeconds % dayCycleSeconds;
        if (m < 0) m += dayCycleSeconds;
        return (int)m;
    }

    /// <summary>
    /// 当前时段该走哪条线：命中且档里给了路线就用它，否则落回 <paramref name="fallbackRouteId"/>。
    /// <para><b>为什么单独成函数</b>：宿主要每 tick 问一次"现在该走哪条线"，
    /// 让它自己拼 <see cref="SlotAt"/> + 取字段，就是第二份选择逻辑；这里把"命中→用、没命中→兜底"一次说清，
    /// 且能在不启会话的单测里覆盖跨零点/空档/未配。</para>
    /// </summary>
    /// <param name="slots">作息档（按 seq 排好）。</param>
    /// <param name="worldSeconds">世界秒（宿主把 tick 换算过来）。</param>
    /// <param name="dayCycleSeconds">一天周期秒。</param>
    /// <param name="fallbackRouteId">没有生效档、或生效档没指定路线时用它（通常是刷点/默认路线）。</param>
    public static int RouteAt(IReadOnlyList<NpcScheduleSlot>? slots, long worldSeconds, int dayCycleSeconds, int fallbackRouteId)
    {
        if (slots is null || slots.Count == 0) return fallbackRouteId;

        var verdict = SlotAt(slots, worldSeconds, dayCycleSeconds);
        return verdict.Matched && verdict.RouteId > 0 ? verdict.RouteId : fallbackRouteId;
    }

    /// <summary>单个时段是否覆盖 <paramref name="secondsOfDay"/>。半开区间，跨零点档单独处理。</summary>
    public static bool Covers(in NpcScheduleSlot slot, int secondsOfDay, int dayCycleSeconds)
    {
        if (dayCycleSeconds <= 0) return false;
        if (!slot.IsUsable) return false;
        if (secondsOfDay < 0 || secondsOfDay >= dayCycleSeconds) return false;

        if (slot.IsAllDay) return true;

        if (slot.FromSeconds < slot.ToSeconds)
        {
            return secondsOfDay >= slot.FromSeconds && secondsOfDay < slot.ToSeconds;
        }

        // from > to = 跨零点（例如 22:00→06:00）。
        return secondsOfDay >= slot.FromSeconds || secondsOfDay < slot.ToSeconds;
    }

    /// <summary>
    /// 求当前生效档。
    /// <para><b>选择规则：具体档优先于全天兜底档；同类里按行序取第一个命中</b>（重叠是数据缺陷，
    /// 由 <c>validate</c> 数出来；这里必须给确定性答案而不是"随便一个"，否则两次运行会让 NPC 在不同路线间跳）。</para>
    /// </summary>
    /// <param name="slots">行序即数据序（表里 <c>seq</c> 决定）。</param>
    /// <param name="worldSeconds">世界秒（服务器 tick 换算）。</param>
    /// <param name="dayCycleSeconds">一天周期秒。</param>
    public static NpcScheduleVerdict SlotAt(IReadOnlyList<NpcScheduleSlot>? slots, long worldSeconds, int dayCycleSeconds)
    {
        var tod = SecondsOfDay(worldSeconds, dayCycleSeconds);
        var count = slots?.Count ?? 0;

        if (dayCycleSeconds <= 0)
            return new NpcScheduleVerdict(false, -1, 0, NpcPosture.Stand, 0, tod, NpcScheduleReject.BadDayLength, 0);

        if (count == 0)
            return new NpcScheduleVerdict(false, -1, 0, NpcPosture.Stand, 0, tod, NpcScheduleReject.NoSlots, 0);

        var bad = 0;
        var firstSpecific = -1;
        var firstAllDay = -1;

        for (var i = 0; i < count; i++)
        {
            var s = slots![i];
            if (!s.FitsInCycle(dayCycleSeconds))
            {
                bad++;
                continue;
            }

            if (!Covers(in s, tod, dayCycleSeconds)) continue;

            if (s.IsAllDay)
            {
                if (firstAllDay < 0) firstAllDay = i;
            }
            else if (firstSpecific < 0)
            {
                firstSpecific = i;
            }
        }

        var chosen = firstSpecific >= 0 ? firstSpecific : firstAllDay;
        if (chosen < 0)
            return new NpcScheduleVerdict(false, -1, 0, NpcPosture.Stand, 0, tod, NpcScheduleReject.Gap, bad);

        var hit = slots[chosen];

        // 未登记姿态按站立画（见 NpcPosture.Unknown）：让它"隐身"比让它站错姿势糟得多。
        var posture = IsPostureKnown(hit.Posture) ? hit.Posture : NpcPosture.Stand;

        // 越界坏行要连同命中一起报出去（见 NpcScheduleVerdict.BadSlots 的注释）。
        return new NpcScheduleVerdict(true, chosen, hit.RouteId, posture, hit.GossipGroupId, tod,
            bad > 0 ? NpcScheduleReject.BadSlot : NpcScheduleReject.None, bad);
    }

    /// <summary>
    /// 覆盖普查：<b>返回周期内没有被任何具体档覆盖的秒数</b>，供 <c>validate</c> 判断"作息表有没有洞"。
    /// <para>为什么要有这条：一个有空洞的作息表在运行期表现为"NPC 到点不消失、只是突然站着不动"，
    /// 玩家报上来只会是"这人今天怪怪的"——数值门全绿也抓不到（记忆 [程序化建模：数值门全绿也要看渲染图] 的同族）。</para>
    /// </summary>
    public static int UncoveredSeconds(IReadOnlyList<NpcScheduleSlot>? slots, int dayCycleSeconds)
    {
        if (dayCycleSeconds <= 0) return dayCycleSeconds < 0 ? 0 : dayCycleSeconds;
        if (slots is null || slots.Count == 0) return dayCycleSeconds;

        var covered = new bool[dayCycleSeconds];
        for (var i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            if (!s.FitsInCycle(dayCycleSeconds)) continue;
            if (s.IsAllDay) return 0;

            if (s.FromSeconds < s.ToSeconds)
            {
                for (var t = s.FromSeconds; t < s.ToSeconds; t++) covered[t] = true;
            }
            else
            {
                for (var t = s.FromSeconds; t < dayCycleSeconds; t++) covered[t] = true;
                for (var t = 0; t < s.ToSeconds; t++) covered[t] = true;
            }
        }

        var gap = 0;
        for (var t = 0; t < dayCycleSeconds; t++)
        {
            if (!covered[t]) gap++;
        }

        return gap;
    }
}
