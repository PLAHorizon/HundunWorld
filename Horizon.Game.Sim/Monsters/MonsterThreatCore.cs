using System;

namespace Horizon.Game.Sim.Monsters;

/// <summary>
/// 威胁表条目（一个参与者一条记录）。
/// </summary>
/// <remarks>
/// <b>为什么是"按 EntityId 升序的数组"而不是 <c>Dictionary&lt;ulong,long&gt;</c></b>：
/// <c>Horizon.Game.Sim</c> 的确定性纪律要求枚举顺序固定（见 <c>MonsterAiCore</c> 的类注释），
/// 而 Dictionary 的枚举顺序由哈希与插入历史决定。旧版 <c>MonsterGrain._threatTable</c> 正是用了
/// Dictionary，且它全仓零引用（<c>DsWorldSessionTests.cs:20</c> 记录过），无法与 DS 对拍。
/// </remarks>
public struct MonsterThreatEntry
{
    /// <summary>威胁来源实体 ID（玩家/召唤物），升序唯一。</summary>
    public ulong EntityId;

    /// <summary>累计威胁值（整数，避免浮点累积漂移）。</summary>
    public long Threat;

    /// <summary>最后一次贡献威胁的服务器 tick（过期修剪用）。</summary>
    public long LastUpdateTick;
}

/// <summary>
/// 确定性威胁表内核（逆向规格 §4）—— 把 <c>MonsterAiCore</c> 的单槽 <c>ThreatTargetId</c>
/// 升级为"每人一条记录 + 滞回切换"，从而能表达 OT 与嘲讽。
/// <para>
/// <b>玩家视觉上的对应物</b>：一只怪被多人打时目标会换人、治疗者会意外拉走怪、
/// 嘲讽必定立刻抢到手且几秒内抢不走。这三条观测在单槽模型里<b>一条都表达不出来</b>。
/// </para>
/// </summary>
public static class MonsterThreatCore
{
    /// <summary>版本（进金轨迹锚点）。</summary>
    public const int Version = 1;

    /// <summary>伤害转威胁的百分比（WoW：1 伤害 = 1 威胁）。</summary>
    public const int DamageThreatPercent = 100;

    /// <summary>治疗转威胁的百分比（WoW 观测约 0.5，"奶妈 OT"的来源）。</summary>
    public const int HealingThreatPercent = 50;

    /// <summary>
    /// "聪明怪"的切换滞回：挑战者必须超过当前目标的 <c>Num/Den</c> 倍才换目标。
    /// <para>WoW 经典服对近战坦克是 1.3（"要超出 T 的 30% 才 OT"），对其他人是 1.0。
    /// 用定点整数（13/10）而不是 float 1.3f，是为了让"乘完再比"在所有平台上按位一致。</para>
    /// </summary>
    public const long SmartThreatMultNum = 13;
    public const long SmartThreatMultDen = 10;

    /// <summary>嘲讽把挑战者的威胁抬到"当前最高 + 该增量"，并锁定若干秒。</summary>
    public const long TauntMargin = 1;

    /// <summary>表容量上限（超出者丢弃最小威胁项，防 OOM 与恶意堆表）。</summary>
    public const int MaxEntries = 30;

    /// <summary>
    /// 记录一次威胁贡献。就地维护"升序 + 唯一"。
    /// </summary>
    /// <param name="table">表缓冲（容量至少 <see cref="MaxEntries"/>）。</param>
    /// <param name="count">当前条目数（引用，函数内可能 +1）。</param>
    /// <param name="entityId">贡献者。</param>
    /// <param name="delta">威胁增量（可为 0；负数被当作 0 处理，威胁不做减法）。</param>
    /// <param name="serverTick">当前 tick。</param>
    /// <returns>该条目在表中的下标；表已满且无处可并时返回 -1。</returns>
    public static int AddThreat(Span<MonsterThreatEntry> table, ref int count, ulong entityId, long delta, long serverTick)
    {
        if (entityId == 0UL) return -1;
        if (delta < 0L) delta = 0L;

        // 已存在：累加并刷新时刻。
        for (var i = 0; i < count; i++)
        {
            if (table[i].EntityId != entityId) continue;
            table[i].Threat += delta;
            table[i].LastUpdateTick = serverTick;
            return i;
        }

        if (count >= MaxEntries)
        {
            // 满了：挤掉威胁最小者（同威胁时挤掉 EntityId 最大者，保证确定性）。
            var worst = 0;
            for (var i = 1; i < count; i++)
            {
                if (table[i].Threat < table[worst].Threat ||
                    (table[i].Threat == table[worst].Threat && table[i].EntityId > table[worst].EntityId))
                {
                    worst = i;
                }
            }
            if (delta <= table[worst].Threat) return -1;
            table[worst] = default;
            count--;
            // 移除后顺序仍升序（少一个元素不影响），继续走插入分支。
        }

        // 插入到升序正确位置：从后往前挪。
        var pos = count;
        while (pos > 0 && table[pos - 1].EntityId > entityId)
        {
            table[pos] = table[pos - 1];
            pos--;
        }
        table[pos] = new MonsterThreatEntry { EntityId = entityId, Threat = delta, LastUpdateTick = serverTick };
        count++;
        return pos;
    }

    /// <summary>把伤害转成威胁增量（按伤害类型系数，近战/技能同值；后续可接技能自带仇恨）。</summary>
    public static long ThreatFromDamage(long damage)
    {
        if (damage <= 0L) return 0L;
        return damage * DamageThreatPercent / 100L;
    }

    /// <summary>把治疗转成威胁增量（逆向得到的 0.5 系数——"我明明没打它为什么追我"的答案）。</summary>
    public static long ThreatFromHealing(long healing)
    {
        if (healing <= 0L) return 0L;
        return healing * HealingThreatPercent / 100L;
    }

    /// <summary>
    /// 嘲讽：把 <paramref name="entityId"/> 的威胁抬到当前最高 + <see cref="TauntMargin"/>。
    /// </summary>
    /// <remarks>
    /// 这是"设为下限"而不是"加一个大数"。加固定值会留下"叠两层 buff 就永久抢不走"的副作用，
    /// 而 WoW 的嘲讽表现是"立刻第一 + 持续几秒"，几秒后普通 OT 仍可能抢回。
    /// </remarks>
    public static int Taunt(Span<MonsterThreatEntry> table, ref int count, ulong entityId, long serverTick)
    {
        var highest = 0L;
        for (var i = 0; i < count; i++)
        {
            if (table[i].EntityId == entityId) continue;
            if (table[i].Threat > highest) highest = table[i].Threat;
        }
        return AddThreatSet(table, ref count, entityId, highest + TauntMargin, serverTick);
    }

    /// <summary>直接设定某条目的威胁值（用于嘲讽与"减仇恨技能"）。</summary>
    public static int SetThreat(Span<MonsterThreatEntry> table, ref int count, ulong entityId, long threat, long serverTick)
        => AddThreatSet(table, ref count, entityId, threat, serverTick);

    private static int AddThreatSet(Span<MonsterThreatEntry> table, ref int count, ulong entityId, long value, long serverTick)
    {
        if (entityId == 0UL) return -1;
        if (value < 0L) value = 0L;
        for (var i = 0; i < count; i++)
        {
            if (table[i].EntityId != entityId) continue;
            table[i].Threat = value;
            table[i].LastUpdateTick = serverTick;
            return i;
        }
        if (count >= MaxEntries) return -1;
        var pos = count;
        while (pos > 0 && table[pos - 1].EntityId > entityId)
        {
            table[pos] = table[pos - 1];
            pos--;
        }
        table[pos] = new MonsterThreatEntry { EntityId = entityId, Threat = value, LastUpdateTick = serverTick };
        count++;
        return pos;
    }

    /// <summary>
    /// 选出当前目标：<b>滞回</b>——只有当挑战者威胁 &gt; 当前目标威胁 × <c>SmartNum/SmartDen</c> 时才换。
    /// </summary>
    /// <param name="table">升序威胁表。</param>
    /// <param name="count">条目数。</param>
    /// <param name="currentEntityId">当前锁定目标（0 = 无）。</param>
    /// <param name="multNum">滞回分子（13 = 需要超出 30%）。</param>
    /// <param name="multDen">滞回分母（10）。</param>
    /// <param name="absentIsDead">表内不存在的 EntityId 视为可剔除（调用方负责先 <see cref="Prune"/>）。</param>
    /// <returns>应攻击的目标；表空返回 0。</returns>
    /// <remarks>
    /// <paramref name="absentIsDead"/> 保留是为了让签名与后续扩展（"目标死亡立即转火"）对得上；
    /// 当前实现里表已由 <see cref="Prune"/> 清洗，故不产生额外行为。
    /// </remarks>
    public static ulong SelectCurrent(
        in ReadOnlySpan<MonsterThreatEntry> table,
        int count,
        ulong currentEntityId,
        long multNum = SmartThreatMultNum,
        long multDen = SmartThreatMultDen,
        bool absentIsDead = true)
    {
        if (count <= 0) return 0UL;

        var bestId = 0UL;
        var bestThreat = long.MinValue;
        var currentThreat = long.MinValue;
        var hasCurrent = false;

        for (var i = 0; i < count; i++)
        {
            var e = table[i];
            if (e.EntityId == bestId && e.Threat == bestThreat && bestId != 0UL) continue; // 防御：重复条目
            if (e.Threat > bestThreat || (e.Threat == bestThreat && e.EntityId < bestId))
            {
                bestThreat = e.Threat;
                bestId = e.EntityId;
            }
            if (e.EntityId == currentEntityId)
            {
                currentThreat = e.Threat;
                hasCurrent = true;
            }
        }

        if (!hasCurrent || currentEntityId == 0UL) return bestId;
        if (bestId == currentEntityId) return currentEntityId;

        // 滞回判据（定点）：挑战者要超过 current × num/den。用乘法避开除法截断的方向性歧义。
        // 两边同乘 den：challenge × den > current × num  →  换目标。
        if (multDen <= 0L) multDen = 10L;
        if (multNum <= 0L) multNum = 10L;
        var passes = bestThreat * multDen > currentThreat * multNum;
        return passes ? bestId : currentEntityId;
    }

    /// <summary>最高威胁者（不带滞回，嘲讽与诊断用）。</summary>
    public static ulong Highest(in ReadOnlySpan<MonsterThreatEntry> table, int count)
    {
        var bestId = 0UL;
        var best = long.MinValue;
        for (var i = 0; i < count; i++)
        {
            if (table[i].Threat > best || (table[i].Threat == best && table[i].EntityId < bestId))
            {
                best = table[i].Threat;
                bestId = table[i].EntityId;
            }
        }
        return bestId;
    }

    /// <summary>
    /// 修剪：移除"过期"或"不再可见"的条目。就地压缩，保持升序。
    /// </summary>
    /// <param name="table">威胁表。</param>
    /// <param name="count">条目数（引用，返回新值）。</param>
    /// <param name="serverTick">当前 tick。</param>
    /// <param name="expireTicks">超过该 tick 数没有新贡献即移除（0 = 不按时效修剪）。</param>
    /// <param name="aliveProbe">存在性探针（返回 false 的条目移除；null = 不探活）。</param>
    /// <returns>被移除的条目数。</returns>
    public static int Prune(
        Span<MonsterThreatEntry> table,
        ref int count,
        long serverTick,
        long expireTicks,
        Func<ulong, bool>? aliveProbe = null)
    {
        if (count <= 0) return 0;
        var w = 0;
        for (var i = 0; i < count; i++)
        {
            var keep = true;
            if (expireTicks > 0L && serverTick - table[i].LastUpdateTick > expireTicks) keep = false;
            if (keep && aliveProbe != null && !aliveProbe(table[i].EntityId)) keep = false;
            if (!keep) continue;
            if (w != i) table[w] = table[i];
            w++;
        }
        var removed = count - w;
        count = w;
        return removed;
    }

    /// <summary>清空（脱战/Evade 时必须调用——"回位后血条瞬间满且不再理你"的那一半）。</summary>
    public static void Clear(Span<MonsterThreatEntry> table, ref int count)
    {
        count = 0;
        table.Clear();
    }

    /// <summary>表是否已空（宿主据此决定进 Idle 还是 Evade）。</summary>
    public static bool IsEmpty(int count) => count <= 0;
}
