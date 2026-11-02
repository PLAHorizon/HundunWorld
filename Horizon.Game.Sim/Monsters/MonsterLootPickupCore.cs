namespace Horizon.Game.Sim.Monsters;

/// <summary>
/// 尸体拾取的**权威侧原语**：一具尸体上的若干掉落槽，每个槽只能被取走一次。
/// <para>
/// 2026-10-28 拍板：拾取列表权威放 Orleans、窗口与距离可配。这一层只回答一件事——
/// "这个槽现在还能不能取"，并把"已取走"记在一个 <see cref="ulong"/> 位图里。
/// 为什么把它做成纯函数而不是直接写进 Grain：**取出幂等**是全部四人同时拾取争端的核心，
/// 它必须能离线逐格对拍；Grain 那边只负责持锁与持久化，判据留在这一层（同 <see cref="MonsterLootCore"/> 的分工）。
/// </para>
/// <para>
/// 槽上限 <see cref="MaxSlots"/> = 64（位图宽度）。超限不是"取不到"，是**配置/调用错**：
/// 返回 <see cref="PickupOutcome.InvalidSlot"/> 并保持位图不变，让调用方大声失败，
/// 而不是静默当成"这具尸体什么都没掉"。
/// </para>
/// </summary>
public static class MonsterLootPickupCore
{
    /// <summary>一具尸体最多能被逐槽取走的件数（位图宽度；内容校验器应保证不超过它）。</summary>
    public const int MaxSlots = 64;

    /// <summary>一次拾取尝试的三种结果。</summary>
    public enum PickupOutcome
    {
        /// <summary>取到了：位图上这一格从 0 变 1。</summary>
        Taken = 1,

        /// <summary>别人（或自己上一手）已经取走了：位图不变。</summary>
        AlreadyTaken = 2,

        /// <summary>槽号越界（&lt;0 或 &gt;=<see cref="MaxSlots"/>）或整张位图不合法：位图不变。</summary>
        InvalidSlot = 3,
    }

    /// <summary>这个槽是不是已经被取走了。</summary>
    public static bool IsTaken(ulong takenMask, int slotIndex)
        => slotIndex >= 0 && slotIndex < MaxSlots && (takenMask & (1UL << slotIndex)) != 0UL;

    /// <summary>
    /// 尝试取走一个槽。<b>成功才会改位图</b>——重复取同一槽、槽号越界都保持原样返回。
    /// <para>
    /// 判据顺序有意固定：先判越界（调用错），再判是否已取（竞争），最后才置位。
    /// 反过来的话，越界槽会被读成"已取"或"取成功"，玩家看到的是"点一下掉一件空气"。
    /// </para>
    /// </summary>
    public static PickupOutcome TryTake(ref ulong takenMask, int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= MaxSlots) return PickupOutcome.InvalidSlot;
        var bit = 1UL << slotIndex;
        if ((takenMask & bit) != 0UL) return PickupOutcome.AlreadyTaken;
        takenMask |= bit;
        return PickupOutcome.Taken;
    }

    /// <summary>这具尸体上还剩几个槽没被取走（<paramref name="totalSlots"/> 由内容侧给出）。</summary>
    public static int RemainingCount(ulong takenMask, int totalSlots)
    {
        if (totalSlots <= 0) return 0;
        if (totalSlots > MaxSlots) totalSlots = MaxSlots;
        var remaining = 0;
        for (var i = 0; i < totalSlots; i++)
            if ((takenMask & (1UL << i)) == 0UL) remaining++;
        return remaining;
    }

    /// <summary>这具尸体是不是已经被取空（全部槽都取走）。空尸体（<paramref name="totalSlots"/> &lt;= 0）也算取空。</summary>
    public static bool IsEmpty(ulong takenMask, int totalSlots)
        => RemainingCount(takenMask, totalSlots) == 0;
}
