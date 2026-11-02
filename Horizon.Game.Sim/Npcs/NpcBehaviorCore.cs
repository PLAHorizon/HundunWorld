using System;
using System.Collections.Generic;

namespace Horizon.Game.Sim.Npcs;

/// <summary>
/// NPC 的 AI 智能档位（规格 §9.1：把"它会做什么"这条玩家可观测的阶梯编码成能力位组合）。
/// <para>
/// <b>为什么用档位 + 位掩码覆盖，而不是九 bool 的逐位覆盖</b>：怪物侧 <c>MonsterAiFlags.Resolve</c>
/// 只有 5 个能力位，写成 5 个 <c>int?</c> 参数还能读；这里 9 个位，写成 9 个可空参数会让每个调用点
/// 变成一列对不齐的实参（本项目已经在测试夹具上被"实参顺序"反复咬过）。
/// 于是改成一对掩码列（强制开 / 强制关），落库是两列整数、编辑上是勾选框。
/// </para>
/// <para>
/// <b>冲突消解口径唯一：强制关赢</b>。同位既被开又被关只可能是配置错误，
/// 让它"关"更安全（少一个行为不会让玩家看到鬼），并且 <see cref="NpcBehaviorCore.Resolve"/>
/// 会把这种冲突如实报成冲突位，由 <c>validate</c> 数出来——不静默选一个。
/// </para>
/// </summary>
public enum NpcAiTier : byte
{
    /// <summary>0 站桩：永远不动、不转头。</summary>
    Static = 0,

    /// <summary>1 会转身面对你。</summary>
    Aware = 1,

    /// <summary>2 会说话（鼠标划过/点击出台词）。</summary>
    Talkative = 2,

    /// <summary>3 会随机踱步并回原位。</summary>
    Wanderer = 3,

    /// <summary>4 会沿固定路线走、到点停顿。</summary>
    Patrol = 4,

    /// <summary>5 会自卫与追击（脱战回位走怪物侧 Evade）。</summary>
    Defensive = 5,

    /// <summary>6 会呼叫增援（守卫一类）。</summary>
    Alertable = 6,

    /// <summary>7 会施法、残血会跑（精英/首领级 NPC）。</summary>
    Elite = 7,

    /// <summary>档位未知/越界时的显式档（<see cref="NpcBehaviorCore.TierOf"/> 拒绝它）。</summary>
    Unknown = 255,
}

/// <summary>能力位的位定义（与 <c>NpcServiceBits</c> 一样，值由本工程定义，不抄 WoW）。</summary>
[Flags]
public enum NpcAiBits : uint
{
    None = 0,

    /// <summary>目标/玩家进入交互距离时转身面对。</summary>
    FacePlayer = 1u << 0,

    /// <summary>会讲 gossip 台词。</summary>
    SpeaksGossip = 1u << 1,

    /// <summary>会随机踱步（对应 WoW <c>MovementType=1</c>）。</summary>
    Wanders = 1u << 2,

    /// <summary>会走 waypoint 路线（<c>MovementType=2</c>）。</summary>
    FollowsPath = 1u << 3,

    /// <summary>被打会还手。</summary>
    DefendsSelf = 1u << 4,

    /// <summary>会追击、并受 leash 约束（脱战回位由怪物侧内核承担）。</summary>
    Pursues = 1u << 5,

    /// <summary>会呼叫附近增援（本位与 <c>DefendsSelf</c> 分开：很多 NPC 只喊不还手）。</summary>
    CallsReinforcement = 1u << 6,

    /// <summary>会施法（复用 <c>MonsterCastingCore</c>）。</summary>
    Casts = 1u << 7,

    /// <summary>残血会跑。</summary>
    FleesAtLowHealth = 1u << 8,
}

/// <summary>展开后的 NPC 能力位（运行期只读位，不再查档——与怪物侧同一条理由：档位是"预设按钮"，覆盖才是真开关）。</summary>
public readonly struct NpcAiFlags(uint bits)
{
    public readonly uint Bits = bits;

    public readonly bool Has(NpcAiBits bit) => (Bits & (uint)bit) != 0;

    /// <summary>逐项回显（编辑器的"档位展开成了什么"面板必须能看见结果，不能只给一个档号）。</summary>
    public readonly string Describe()
    {
        if (Bits == 0) return "ai=none";

        var parts = new List<string>(10);
        foreach (NpcAiBits bit in Enum.GetValues<NpcAiBits>())
        {
            if (bit != NpcAiBits.None && (Bits & (uint)bit) != 0) parts.Add(bit.ToString().ToLowerInvariant());
        }

        return "ai=" + string.Join("|", parts);
    }
}

/// <summary>一次档位展开的回执：展开结果 + 冲突位（既被强制开又被强制关）。</summary>
public readonly struct NpcAiResolveVerdict(NpcAiFlags flags, uint conflictBits, bool tierKnown)
{
    public readonly NpcAiFlags Flags = flags;

    /// <summary>冲突位。<b>非 0 是配置缺陷</b>，必须由 <c>validate</c> 数出来并报错，而不是按某个顺序静默选一个。</summary>
    public readonly uint ConflictBits = conflictBits;

    /// <summary>档位号是否在表里。越界档位不猜预设值——回 <see cref="NpcAiBits.None"/> 预设并如实报 false。</summary>
    public readonly bool TierKnown = tierKnown;

    public readonly string Describe() =>
        $"{Flags.Describe()} conflict={(byte)ConflictBits} tier_known={(TierKnown ? 1 : 0)}";
}

/// <summary>移动方式（与 WoW <c>creature_template.MovementType</c> 同形：<c>0=Idle 1=Random 2=Waypoint</c>）。</summary>
/// <remarks>
/// <b>它与 <see cref="NpcAiBits.Wanders"/> / <see cref="NpcAiBits.FollowsPath"/> 是同一件事的两种写法</b>，
/// 所以这里刻意做成"从能力位派生"而不是第二份配置：两个真相必然漂。
/// 表里只存能力位；<see cref="NpcBehaviorCore.MovementModeOf"/> 是唯一的读法。
/// </remarks>
public enum NpcMovementMode : byte
{
    /// <summary>站桩。</summary>
    Stationary = 0,

    /// <summary>随机踱步。</summary>
    RandomWander = 1,

    /// <summary>走路线。</summary>
    WaypointPath = 2,
}

/// <summary>增援候选（调用方从会话实体表里取来的最小事实集）。</summary>
/// <param name="EntityId">实体 id，用于全序破平局（同距离时按 id 小者优先，保证可重放）。</param>
/// <param name="Distance">到求援者的距离（米）。<b>负数按不可选处理</b>：距离来自跨端换轴后的量，
/// 出现负值说明上游算错了，宁可少叫一个人。</param>
/// <param name="CanRespond">这个候选当前能不能应召（已阵亡/已在响应别人的，由调用方判）。</param>
public readonly struct NpcReinforcementCandidate(int entityId, double distance, bool canRespond)
{
    public readonly int EntityId = entityId;
    public readonly double Distance = distance;
    public readonly bool CanRespond = canRespond;
}

/// <summary>增援选取的回执。<b>找不到也要回同一套字段</b>（选中清单、半径、候选总数、拒绝档）。</summary>
public readonly struct NpcReinforcementVerdict(
    int chosenEntityId,
    IReadOnlyList<int> pickedEntityIds,
    int scannedCount,
    int radiusMeters,
    NpcAlertReject reason)
{
    /// <summary>第一个被叫来的人（<see cref="PickedEntityIds"/> 为空时是 −1，<b>不给 0</b>——0 会撞成合法 id 的另一例）。</summary>
    public readonly int ChosenEntityId = chosenEntityId;

    /// <summary>
    /// 被叫来的完整名单，<b>顺序就是应召顺序</b>（距离升序、同距离按实体 id 升序）。
    /// 只回"第一个人"的话，全序这条承诺就没法被断言——而全序正是重放与对账的前提。
    /// </summary>
    public readonly IReadOnlyList<int> PickedEntityIds = pickedEntityIds;

    /// <summary>被叫来的人数。</summary>
    public readonly int ChosenCount => PickedEntityIds?.Count ?? 0;

    /// <summary>扫过的候选总数。<b>扫描型判据必须报条数</b>，否则"安静地找到 0 条"与"没扫"同形。</summary>
    public readonly int ScannedCount = scannedCount;

    public readonly int RadiusMeters = radiusMeters;

    public readonly NpcAlertReject Reason = reason;

    public readonly string Describe() =>
        $"alert_picked={ChosenCount} alert_first={ChosenEntityId} alert_scanned={ScannedCount} " +
        $"alert_radius={RadiusMeters} alert_reason={(byte)Reason}";
}

/// <summary>增援失败的分档。</summary>
public enum NpcAlertReject : byte
{
    /// <summary>成功叫到人。</summary>
    None = 0,

    /// <summary>候选列表为空（附近根本没人）。</summary>
    NoCandidates = 1,

    /// <summary>半径非正（数据缺陷：<c>alert_radius=0</c> 的守卫永远叫不来人）。</summary>
    BadRadius = 2,

    /// <summary>有人在半径内，但全都不能应召（都死了/都在响应别人）。</summary>
    NoneAvailable = 3,
}

/// <summary>
/// NPC 行为档位的确定性内核（规格 §9.1、§3.2 的"呼叫增援"）。
/// <para>
/// <b>与怪物侧的分工</b>：威胁表/追击/脱战/施法/走图<b>全部复用</b> <c>MonsterThreatCore</c>、
/// <c>MonsterCastingCore</c>、<c>MonsterPathCore</c>、<c>MonsterPatrolCore</c>（设计文档 §4 底部的复用清单）。
/// 本类只管两件怪物侧没有的：① NPC 专有能力位（转身/说话/踱步/呼叫增援）的档位展开；
/// ② "谁能应召"的选取规则。写第二套 AI 分派只会得到两份必然漂的行为表。
/// </para>
/// </summary>
public static class NpcBehaviorCore
{
    /// <summary>判据版本。</summary>
    public const int Version = 1;

    /// <summary>呼叫增援的默认半径（米）。与怪物侧 <c>ThreatRadius</c> 同量级，独立取常量是因为语义不同。</summary>
    public const int DefaultAlertRadiusMeters = 20;

    /// <summary>一次应召最多叫几个人（观测：WoW 守卫通常 1–2 个跑来）。</summary>
    public const int MaxReinforcementsPerAlert = 2;

    /// <summary>档位预设。逐行对应规格 §9.1 的观测阶梯（高一档 = 低档全部 + 新增那一项）。</summary>
    public static uint ForTier(NpcAiTier tier) => tier switch
    {
        NpcAiTier.Static => (uint)NpcAiBits.None,
        NpcAiTier.Aware => (uint)NpcAiBits.FacePlayer,
        NpcAiTier.Talkative => (uint)(NpcAiBits.FacePlayer | NpcAiBits.SpeaksGossip),
        NpcAiTier.Wanderer => (uint)(NpcAiBits.FacePlayer | NpcAiBits.SpeaksGossip | NpcAiBits.Wanders),
        NpcAiTier.Patrol => (uint)(NpcAiBits.FacePlayer | NpcAiBits.SpeaksGossip | NpcAiBits.Wanders | NpcAiBits.FollowsPath),
        NpcAiTier.Defensive => (uint)(NpcAiBits.FacePlayer | NpcAiBits.SpeaksGossip | NpcAiBits.Wanders |
                              NpcAiBits.FollowsPath | NpcAiBits.DefendsSelf | NpcAiBits.Pursues),
        NpcAiTier.Alertable => (uint)(NpcAiBits.FacePlayer | NpcAiBits.SpeaksGossip | NpcAiBits.Wanders |
                               NpcAiBits.FollowsPath | NpcAiBits.DefendsSelf | NpcAiBits.Pursues |
                               NpcAiBits.CallsReinforcement),
        NpcAiTier.Elite => (uint)(NpcAiBits.FacePlayer | NpcAiBits.SpeaksGossip | NpcAiBits.Wanders |
                           NpcAiBits.FollowsPath | NpcAiBits.DefendsSelf | NpcAiBits.Pursues |
                           NpcAiBits.CallsReinforcement | NpcAiBits.Casts | NpcAiBits.FleesAtLowHealth),
        _ => (uint)NpcAiBits.None,
    };

    /// <summary>档位是否已知（越界档位不当成 0 档处理，只如实报 false）。</summary>
    public static bool IsTierKnown(NpcAiTier tier) => tier is >= NpcAiTier.Static and <= NpcAiTier.Elite;

    /// <summary>
    /// 反查档位（编辑器回显"这份位图等于第几档"）。
    /// <para><b>只在位图与某个预设逐位相等时返回那个档</b>，否则回 <see cref="NpcAiTier.Unknown"/>：
    /// "取最接近的档"会让 UI 显示一个玩家没配过的档位号，那是骗人。</para>
    /// </summary>
    public static NpcAiTier TierOf(uint bits)
    {
        for (var t = (int)NpcAiTier.Static; t <= (int)NpcAiTier.Elite; t++)
        {
            var tier = (NpcAiTier)t;
            if (IsTierKnown(tier) && ForTier(tier) == bits) return tier;
        }

        return NpcAiTier.Unknown;
    }

    /// <summary>档位 + 强制开/强制关展开。<b>同位冲突时强制关赢，并把冲突位报出去</b>（类注释里的口径）。</summary>
    public static NpcAiResolveVerdict Resolve(NpcAiTier tier, uint forceOn, uint forceOff)
    {
        var known = IsTierKnown(tier);
        var preset = known ? (uint)ForTier(tier) : 0u;

        // 只认已定义的位：未定义的位不参与冲突计算，交给 UnknownBits 单独报（两件事不混在一起判）。
        var defined = DefinedBits();
        var on = forceOn & defined;
        var off = forceOff & defined;
        var conflict = on & off;

        var bits = (preset | on) & ~off;
        return new NpcAiResolveVerdict(new NpcAiFlags(bits), conflict, known);
    }

    /// <summary>已定义的位全集（<see cref="UnknownBits"/> 与 <see cref="Resolve"/> 共用，避免两处各写一遍）。</summary>
    public static uint DefinedBits()
    {
        uint known = 0;
        foreach (NpcAiBits bit in Enum.GetValues<NpcAiBits>())
        {
            if (bit != NpcAiBits.None) known |= (uint)bit;
        }

        return known;
    }

    /// <summary>未识别的位（编辑器与 CLI 都要显式拒绝死位，见 <c>NpcServiceCore.UnknownBits</c> 同款理由）。</summary>
    public static uint UnknownBits(uint bits) => bits & ~DefinedBits();

    /// <summary>从能力位派生移动方式（唯一的读法，见 <see cref="NpcMovementMode"/> 的注释）。</summary>
    public static NpcMovementMode MovementModeOf(in NpcAiFlags flags)
    {
        // 路线优先：会走路线的 NPC 不需要再随机踱步（两者同时开是配置噪声，取确定性的那个）。
        if (flags.Has(NpcAiBits.FollowsPath)) return NpcMovementMode.WaypointPath;
        if (flags.Has(NpcAiBits.Wanders)) return NpcMovementMode.RandomWander;
        return NpcMovementMode.Stationary;
    }

    /// <summary>
    /// 增援选取：半径内、可应召、按 (距离升序, 实体 id 升序) 取前 N 个。
    /// <para><b>全序是刻意的</b>：距离并列时若不破平局，同一份现场两次运行会叫来不同的人，
    /// 重放与对账就都没法做（怪物侧 A* 那条"全序堆"的先例同源）。</para>
    /// </summary>
    public static NpcReinforcementVerdict PickReinforcements(
        IReadOnlyList<NpcReinforcementCandidate> candidates,
        int radiusMeters,
        int maxCount = MaxReinforcementsPerAlert)
    {
        var scanned = candidates?.Count ?? 0;
        if (radiusMeters <= 0)
            return new NpcReinforcementVerdict(-1, Array.Empty<int>(), scanned, radiusMeters, NpcAlertReject.BadRadius);

        // 到这里 candidates 一定非空（空集合与 null 同形：都叫不到人），取一个本地引用让后面的解引用有据可依。
        var list = candidates!;
        if (scanned == 0)
            return new NpcReinforcementVerdict(-1, Array.Empty<int>(), scanned, radiusMeters, NpcAlertReject.NoCandidates);

        var inRange = 0;
        var picked = new List<(double Distance, int EntityId)>(Math.Max(1, maxCount));

        for (var i = 0; i < scanned; i++)
        {
            var c = list[i];
            if (c.Distance < 0 || c.Distance > radiusMeters) continue;
            inRange++;
            if (!c.CanRespond) continue;
            picked.Add((c.Distance, c.EntityId));
        }

        if (picked.Count == 0)
        {
            // 半径内有人但都不能应召 → NoneAvailable；一个人都没有 → NoCandidates。两档分开报。
            return new NpcReinforcementVerdict(-1, Array.Empty<int>(), scanned, radiusMeters,
                inRange > 0 ? NpcAlertReject.NoneAvailable : NpcAlertReject.NoCandidates);
        }

        picked.Sort(static (a, b) =>
        {
            var byDistance = a.Distance.CompareTo(b.Distance);
            return byDistance != 0 ? byDistance : a.EntityId.CompareTo(b.EntityId);
        });

        var take = maxCount < 0 ? 0 : Math.Min(maxCount, picked.Count);
        var ids = new List<int>(take);
        for (var i = 0; i < take; i++) ids.Add(picked[i].EntityId);

        return new NpcReinforcementVerdict(take > 0 ? ids[0] : -1, ids, scanned, radiusMeters, NpcAlertReject.None);
    }
}
