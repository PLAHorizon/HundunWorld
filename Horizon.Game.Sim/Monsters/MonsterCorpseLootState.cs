using System.Collections.Generic;
using System.Linq;

namespace Horizon.Game.Sim.Monsters;

/// <summary>一次拾取尝试的完整结果（窗口 / 距离 / 幂等 / 越界 都收在一个枚举里）。</summary>
public enum LootTakeResult
{
    /// <summary>取到了。</summary>
    Taken = 1,

    /// <summary>这个槽已经被取走（同一个人重复点、或另一个人先点了）。</summary>
    AlreadyTaken = 2,

    /// <summary>拾取窗口已过（尸体可能还在，但不能再取）。</summary>
    Expired = 3,

    /// <summary>人在拾取距离之外。</summary>
    TooFar = 4,

    /// <summary>槽号越界（&lt;0 或 ≥ 槽数）：调用错，不是"没有东西"。</summary>
    InvalidSlot = 5,

    /// <summary>这具尸体本来就没有可拾取的东西。</summary>
    Empty = 6,

    /// <summary>你不是这次击杀的参与者（AC 的 `DIDNT_KILL`）：东西还在，但轮不到你。</summary>
    NotYours = 7,

    /// <summary>
    /// 这一槽是"只有持有该任务的人能拿"的条件件（AC 的 `needs_quest`），而取的人没有这个任务：
    /// **东西还在**（没被消耗），持有任务的队友仍可来取。
    /// </summary>
    ConditionNotMet = 8,
}

/// <summary>
/// 一具尸体的**权威拾取列表**（2026-10-28 拍板：它归 Orleans，DS 只转发请求）。
/// <para>
/// 这一层是纯数据 + 纯判据：不碰时钟、不碰网络、不碰持久化——Grain 只需要持有它、把 `TakenMask` 存下来。
/// 判据顺序刻意固定：<b>空/越界 → 过期 → 太远 → 已取 → 取走</b>。
/// 反过来（先判已取、再判过期）会让"窗口关了"被报成"你已经拿过了"，玩家会去找不存在的上一个人；
/// 先判太远再判过期会让"站太远"掩盖"窗口已关"，客户端提示会和真实原因不一致。
/// </para>
/// </summary>
public sealed class MonsterCorpseLootState
{
    /// <summary>一个槽：物品（或钱）。<see cref="ItemId"/> = 0 且 <see cref="Money"/> &gt; 0 表示金币槽。</summary>
    public sealed class Slot
    {
        public int ItemId;
        public int Count;
        public int ConditionKind;
        public int QuestId;
        public int Money;

        public bool IsMoney => ItemId <= 0 && Money > 0;
    }

    public List<Slot> Slots { get; } = new();

    /// <summary>
    /// 谁能取（击杀参与者，AC `DIDNT_KILL`）；空 = 不限制（环境击杀 / 历史登记）。
    /// <para>它必须与 <see cref="Slots"/> 一起穿过"死亡 → 登记 → 权威存储"全程：
    /// 只在其中一层有名单，另一层就等于把门敞开（而症状是"别人也能拿走"，没有任何报错）。</para>
    /// </summary>
    public List<ulong> Contributors { get; } = new();

    /// <summary>这个人能不能取这具尸体上的东西（见 <see cref="MonsterCorpseCore.IsAllowedLooter"/>）。</summary>
    public bool IsAllowedTaker(ulong takerCharacterId) =>
        MonsterCorpseCore.IsAllowedLooter(Contributors, takerCharacterId);

    /// <summary>
    /// 哪些槽已经被取走（位图；与 <see cref="MonsterLootPickupCore"/> 同一口径）。
    /// <para>故意写成**字段**：<see cref="TryTake"/> 要把它按 <c>ref</c> 交给判据。若写成属性就得先拷到临时变量再手动写回，
    /// 而“忘了写回”的后果是取了东西但位图不变——下一个人能再取一次，且无任何报错。</para>
    /// </summary>
    public ulong TakenMask;

    /// <summary>拾取窗口截止 tick（0 = 取不了）。</summary>
    public long ExpireTick { get; set; }

    /// <summary>拾取交互距离（米）。</summary>
    public float PickupDistanceMeters { get; set; } = MonsterCorpseCore.DefaultPickupDistanceMeters;

    /// <summary>
    /// 从一次击杀的掷骰结果建列表：先普通掉落、再条件掉落、最后金币（如果有）。
    /// <para>顺序即槽号——它会被写进持久层，**以后加东西只能往尾部追加**，否则旧存档的槽号会整体平移。</para>
    /// </summary>
    public static MonsterCorpseLootState FromRoll(in MonsterLootRollResult roll, long expireTick, float pickupDistanceMeters,
                                                 IReadOnlyList<ulong>? contributors = null)
    {
        var state = new MonsterCorpseLootState
        {
            ExpireTick = expireTick,
            PickupDistanceMeters = pickupDistanceMeters > 0f
                ? pickupDistanceMeters
                : MonsterCorpseCore.DefaultPickupDistanceMeters,
        };
        CopyContributors(state.Contributors, contributors);
        for (var i = 0; i < roll.Drops.Count; i++)
        {
            var d = roll.Drops[i];
            state.Slots.Add(new Slot { ItemId = d.ItemId, Count = d.Count, ConditionKind = d.ConditionKind, QuestId = d.QuestId });
        }
        for (var i = 0; i < roll.ConditionalDrops.Count; i++)
        {
            var d = roll.ConditionalDrops[i];
            state.Slots.Add(new Slot { ItemId = d.ItemId, Count = d.Count, ConditionKind = d.ConditionKind, QuestId = d.QuestId });
        }
        if (roll.Money > 0) state.Slots.Add(new Slot { ItemId = 0, Count = 1, Money = roll.Money });
        return state;
    }

    /// <summary>从**只读的**输入建列表（Grain 反序列化/对账用；<paramref name="slots"/> 逐条复制）。</summary>
    public static MonsterCorpseLootState FromSlots(
        IReadOnlyList<Slot> slots, ulong takenMask, long expireTick, float pickupDistanceMeters,
        IReadOnlyList<ulong>? contributors = null)
    {
        var state = new MonsterCorpseLootState
        {
            TakenMask = takenMask,
            ExpireTick = expireTick,
            PickupDistanceMeters = pickupDistanceMeters > 0f
                ? pickupDistanceMeters
                : MonsterCorpseCore.DefaultPickupDistanceMeters,
        };
        CopyContributors(state.Contributors, contributors);
        for (var i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            state.Slots.Add(new Slot
            {
                ItemId = s.ItemId, Count = s.Count, ConditionKind = s.ConditionKind, QuestId = s.QuestId, Money = s.Money,
            });
        }
        return state;
    }

    /// <summary>
    /// 权威判据：这一刻 <paramref name="takerCharacterId"/> 能不能取第 <paramref name="slotIndex"/> 个槽。
    /// <b>成功才会改 <see cref="TakenMask"/></b>，失败一律保持原样。
    /// </summary>
    /// <remarks>
    /// 权限排在"空/越界"之后、"过期/太远"之前：后面两条是对**这具尸体能不能拿**的判断，
    /// 对一个根本没有资格的人回答"太远了"，会让他以为走近一点就能拿——那是假话。
    /// </remarks>
    public LootTakeResult TryTake(int slotIndex, long nowTick, float distanceMeters, ulong takerCharacterId, out Slot? taken,
                                  IReadOnlyCollection<int>? heldQuestIds = null)
    {
        taken = null;
        if (Slots.Count == 0) return LootTakeResult.Empty;
        if (slotIndex < 0 || slotIndex >= Slots.Count || slotIndex >= MonsterLootPickupCore.MaxSlots)
            return LootTakeResult.InvalidSlot;
        if (!IsAllowedTaker(takerCharacterId)) return LootTakeResult.NotYours;

        var window = MonsterCorpseCore.CanPickUp(nowTick, ExpireTick, distanceMeters, PickupDistanceMeters);
        if (window == MonsterCorpseCore.PickupDenial.Expired) return LootTakeResult.Expired;
        if (window == MonsterCorpseCore.PickupDenial.TooFar) return LootTakeResult.TooFar;
        // 条件件：只有持有该任务的人能取。位置刻意在这里——
        // 放在窗口/距离之后（站太远先说"太远"，别让玩家以为是任务问题），
        // 放在真正取走之前（不满足就不能消耗这一槽，持有任务的队友还能来取）。
        if (ConditionBlocked(Slots[slotIndex], heldQuestIds)) return LootTakeResult.ConditionNotMet;

        var outcome = MonsterLootPickupCore.TryTake(ref TakenMask, slotIndex);
        if (outcome == MonsterLootPickupCore.PickupOutcome.AlreadyTaken) return LootTakeResult.AlreadyTaken;
        // 越界已经在上面判过；这里只剩 Taken
        taken = Slots[slotIndex];
        return LootTakeResult.Taken;
    }

    /// <summary>
    /// 这一槽对这个人是否"条件不满足"。判据只有一处：槽自己带的 (ConditionKind, QuestId)。
    /// <para><paramref name="heldQuestIds"/> 为 null = 调用方不知道任务状态 ⇒ 一律按不满足处理。
    /// 宁可"取不了"也不把任务件发给不该拿的人（与业务层"判不了→不给"同一口径）；
    /// 代价是：没有任务读取端的部署里条件件拿不到——那一档会由 <see cref="ConditionNotMet"/> 显形，不是静默丢。</para>
    /// </summary>
    private static bool ConditionBlocked(in Slot slot, IReadOnlyCollection<int>? heldQuestIds)
        => slot.ConditionKind == MonsterLootCore.ConditionQuestHolder
           && (heldQuestIds is null || !heldQuestIds.Contains(slot.QuestId));

    /// <summary>
    /// 取走**第一个还没被取走**的槽（AC 的右键连取就是这个语义）。
    /// <para>为什么需要它：客户端一键拾取不该自己跟踪槽号——那会把"槽序=内容侧顺序"这条知识
    /// 复制到客户端，而槽序恰恰是只能往尾部追加的持久化契约。槽号由权威侧回执带回来。</para>
    /// </summary>
    public LootTakeResult TryTakeFirst(long nowTick, float distanceMeters, ulong takerCharacterId, out Slot? taken, out int takenIndex,
                                       IReadOnlyCollection<int>? heldQuestIds = null)
    {
        taken = null;
        takenIndex = -1;
        if (Slots.Count == 0) return LootTakeResult.Empty;
        if (!IsAllowedTaker(takerCharacterId)) return LootTakeResult.NotYours;
        var window = MonsterCorpseCore.CanPickUp(nowTick, ExpireTick, distanceMeters, PickupDistanceMeters);
        if (window == MonsterCorpseCore.PickupDenial.Expired) return LootTakeResult.Expired;
        if (window == MonsterCorpseCore.PickupDenial.TooFar) return LootTakeResult.TooFar;
        var blockedByCondition = false;
        for (var i = 0; i < Slots.Count && i < MonsterLootPickupCore.MaxSlots; i++)
        {
            if (MonsterLootPickupCore.IsTaken(TakenMask, i)) continue;
            // 条件件跳过而不是拒整次：后面可能还有普通件/金币可以先拿；
            // 但一个都没拿到时要如实说"条件不满足"，不能说成"已经被取走了"（那是另一件事）。
            if (ConditionBlocked(Slots[i], heldQuestIds)) { blockedByCondition = true; continue; }
            var take = MonsterLootPickupCore.TryTake(ref TakenMask, i);
            if (take != MonsterLootPickupCore.PickupOutcome.Taken) continue;
            taken = Slots[i];
            takenIndex = i;
            return LootTakeResult.Taken;
        }
        // 一个都没拿到：还剩下的是"条件不满足"还是"本来都取走了"——对玩家是两句话，必须分开。
        return blockedByCondition ? LootTakeResult.ConditionNotMet : LootTakeResult.AlreadyTaken;
    }

    /// <summary>还剩几个槽（已取走的不算）。</summary>
    public int Remaining => MonsterLootPickupCore.RemainingCount(TakenMask, Slots.Count);

    /// <summary>是不是已经取空。</summary>
    public bool IsEmpty => MonsterLootPickupCore.IsEmpty(TakenMask, Slots.Count);

    private static void CopyContributors(List<ulong> target, IReadOnlyList<ulong>? source)
    {
        if (source == null) return;
        for (var i = 0; i < source.Count; i++)
        {
            var id = source[i];
            if (id == 0UL) continue;
            if (target.Contains(id)) continue;
            target.Add(id);
        }
    }
}
