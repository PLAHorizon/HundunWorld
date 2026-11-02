using System.Collections.Generic;

namespace Horizon.Game.Sim.Npcs;

/// <summary>NPC 编号所处的号段。</summary>
public enum NpcIdSegment
{
    /// <summary>既有契约号：1/2/3（铸剑师/守门人/游方商队）。<b>不得改号</b>——任务表的 giver_template_id 写着它们。</summary>
    Legacy = 1,

    /// <summary>服务型 NPC：400000–499999（商人/训练师/制作/任务）。</summary>
    Service = 2,

    /// <summary>敌对型 NPC：500000–599999（可被击杀，给经验与掉落）。</summary>
    Hostile = 3,

    /// <summary>不在任何已登记号段内。加载时拒绝——见 <see cref="NpcIdSegmentCore.IsAllowed"/>。</summary>
    OutOfRange = 0,
}

/// <summary>
/// NPC 号段判据（A1 的"号段即身份"）。
/// <para>
/// <b>为什么要拒绝而不是放行</b>：号段决定"这只是一只 NPC 还是可以被击杀的敌人"之外的很多东西
/// （掉落 owner 号段、任务反查、编辑器分组）。把 150000 登记成 NPC 时，如果放行，
/// 它会一切正常地跑起来，直到某天有人按号段做统计/分组才发现它落在一个谁都不认的洞里。
/// </para>
/// <para>加新号段时只改这里 + <see cref="IsAllowed"/>，别在装配器或编辑器里另写一份范围判断。</para>
/// </summary>
public static class NpcIdSegmentCore
{
    /// <summary>判据版本：改号段边界时递增。</summary>
    public const int Version = 1;

    public const int LegacyMin = 1;
    public const int LegacyMax = 3;
    public const int ServiceMin = 400_000;
    public const int ServiceMax = 499_999;
    public const int HostileMin = 500_000;
    public const int HostileMax = 599_999;

    /// <summary>号段判定。</summary>
    public static NpcIdSegment SegmentOf(int npcId)
    {
        if (npcId >= LegacyMin && npcId <= LegacyMax) return NpcIdSegment.Legacy;
        if (npcId >= ServiceMin && npcId <= ServiceMax) return NpcIdSegment.Service;
        if (npcId >= HostileMin && npcId <= HostileMax) return NpcIdSegment.Hostile;
        return NpcIdSegment.OutOfRange;
    }

    /// <summary>编号是否落在已登记号段内。</summary>
    public static bool IsAllowed(int npcId) => SegmentOf(npcId) != NpcIdSegment.OutOfRange;

    /// <summary>回执用的号段名（诊断/回执里念出来，别让人去翻表）。</summary>
    public static string NameOf(NpcIdSegment segment) => segment switch
    {
        NpcIdSegment.Legacy => "legacy",
        NpcIdSegment.Service => "service",
        NpcIdSegment.Hostile => "hostile",
        _ => "out_of_range",
    };

    /// <summary>号段边界；<see cref="NpcIdSegment.OutOfRange"/> 回 (0,0)。</summary>
    public static (int Min, int Max) RangeOf(NpcIdSegment segment) => segment switch
    {
        NpcIdSegment.Legacy => (LegacyMin, LegacyMax),
        NpcIdSegment.Service => (ServiceMin, ServiceMax),
        NpcIdSegment.Hostile => (HostileMin, HostileMax),
        _ => (0, 0),
    };

    /// <summary>
    /// 在号段里分配一个新编号：取该号段**已用最大值 + 1**，**不复用空号**。
    /// <para>
    /// 为什么不回收再发：编号会写进任务 giver/turn-in、掉落 owner、日志和玩家截图。
    /// 退役一只 NPC 后把这个号发给另一个人，等于让历史记录里的数字指向另一只——
    /// WoW 的 creature entry 也从不二次分配。用尽回 0，调用方必须当失败处理（0 不是合法编号）。
    /// </para>
    /// <para>号段越界/未知一律回 0：宁可不发号，也不能发出一个装载器会拒的号。</para>
    /// </summary>
    public static int NextFree(IEnumerable<int> usedIds, NpcIdSegment segment)
    {
        var (min, max) = RangeOf(segment);
        if (min == 0) return 0;

        var next = min;
        if (usedIds != null)
        {
            foreach (var id in usedIds)
            {
                // 别的号段的行与本号段无关；只看落在 [min,max] 的行。
                if (id < min || id > max) continue;
                if (id >= next) next = id + 1;
            }
        }

        return next <= max ? next : 0;
    }
}
