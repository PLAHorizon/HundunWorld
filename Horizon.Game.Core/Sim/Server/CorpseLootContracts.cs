using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>尸体上的一个掉落槽（跨进程传输用；与 <c>MonsterCorpseLootState.Slot</c> 同构）。</summary>
public sealed class CorpseLootSlotDto
{
    public int ItemId;
    public int Count;
    public int ConditionKind;
    public int QuestId;
    public int Money;
}

/// <summary>一具尸体的拾取列表（DS 死亡那一刻登记给权威侧）。</summary>
public sealed class CorpseLootRegistration
{
    public ulong CorpseEntityId;
    public long ExpireTick;
    public float PickupDistanceMeters;
    public List<CorpseLootSlotDto> Slots { get; } = new();

    /// <summary>
    /// 击杀参与者（谁能取，AC `DIDNT_KILL`）；空 = 不限制。
    /// <para>它与 <see cref="Slots"/> 一样必须完整登记——权威侧只看得到这份名单，
    /// DS 侧那份只是"请求到达时先挡一道"，不是真值。</para>
    /// </summary>
    public List<ulong> Contributors { get; } = new();
}

/// <summary>拾取尝试的结果（与 <c>LootTakeResult</c> 同构，外加"根本没有这具尸体的登记"）。</summary>
public enum CorpseLootTakeStatus
{
    Taken = 1,
    AlreadyTaken = 2,
    Expired = 3,
    TooFar = 4,
    InvalidSlot = 5,
    Empty = 6,

    /// <summary>权威侧没有这具尸体的登记（已随尸体释放 / 从未登记 / 越界重启）。</summary>
    UnknownCorpse = 7,

    /// <summary>不是这次击杀的参与者（AC `DIDNT_KILL`）：东西还在，但轮不到你。</summary>
    NotYours = 8,

    /// <summary>
    /// 这一槽是条件件（AC 的 `needs_quest`）而取的人没持有那个任务：**东西还在**，持有任务的队友仍可来取。
    /// <para>与 <see cref="NotYours"/> 分开：一个是"不是你的击杀"，一个是"任务不对"，
    /// 玩家看到的应是两句话，而不是同一句"取不了"。</para>
    /// </summary>
    ConditionNotMet = 9,
}

/// <summary>一次拾取的回执：状态 + （取到时）那一个槽的内容。</summary>
public sealed class CorpseLootTakeResult
{
    public CorpseLootTakeStatus Status;
    public CorpseLootSlotDto? Slot;

    /// <summary>实际取走的槽号（请求传 -1 = 取下一件时由权威侧回填；其它情况原样带回）。</summary>
    public int SlotIndex = -1;
}

/// <summary>
/// 尸体拾取列表的**权威存储**（2026-10-28 拍板：它住在 Orleans）。
/// <para>做成接口的理由与 <see cref="IExperienceWriter"/> 一族相同：DS 的单测要在**不起集群**的情况下
/// 断言"登记之后击杀不再直接入包、同一槽只可能被取走一次、窗口过期后取不了"，
/// 而直接依赖 Grain 会把这类断言吞成 catch 里的"什么都没发生"。</para>
/// <para>语义三条：① <see cref="RegisterAsync"/> 对同一具尸体重复登记以**最后一次**为准（重生复用实体 id 时必须覆盖）；
/// ② <see cref="RemoveAsync"/> 是幂等的（尸体到期/复活都会调）；③ <see cref="TakeAsync"/> 必须幂等且**只有 Taken 才扣槽**。</para>
/// </summary>
public interface ICorpseLootStore
{
    /// <summary>登记一具尸体的拾取列表；实体 id 复用时覆盖旧登记。</summary>
    Task RegisterAsync(CorpseLootRegistration registration, CancellationToken cancellationToken = default);

    /// <summary>尸体消失/复活时释放登记（幂等）。</summary>
    Task RemoveAsync(ulong corpseEntityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 尝试取走一个槽：窗口、距离、幂等、<b>取的人有没有资格</b>四条判据都由权威侧裁决。
    /// <para><paramref name="takerCharacterId"/> 是必填的：漏传（0）在有限制的尸体上会被拒，
    /// 所以"某条通路忘了带上身份"表现为明确的 NotYours，而不是悄悄放行。</para>
    /// </summary>
    Task<CorpseLootTakeResult> TakeAsync(ulong corpseEntityId, ulong takerCharacterId, int slotIndex, long nowTick,
                                        float distanceMeters, IReadOnlyCollection<int>? heldQuestIds = null,
                                        CancellationToken cancellationToken = default);
}

/// <summary>
/// 进程内实现：给 DS 单测用，**不是生产方式**（生产方式必须是 Grain，见 <c>CorpseLootGrain</c>）。
/// <para>它按 <see cref="MonsterCorpseLootState"/> 的同一套判据回答，所以"DS 侧的拒绝理由"与"权威侧的理由"
/// 不会分叉；两处要分叉的是**谁持锁**，不是判据。</para>
/// </summary>
public sealed class InMemoryCorpseLootStore : ICorpseLootStore
{
    private readonly Dictionary<ulong, Horizon.Game.Sim.Monsters.MonsterCorpseLootState> _states = new();

    /// <summary>登记次数（回执/判据用：证明"登记真的发生了"，而不是"没抛异常"）。</summary>
    public int RegisterCount { get; private set; }

    /// <summary>释放次数。</summary>
    public int RemoveCount { get; private set; }

    /// <summary>当前还挂着几具尸体（对账用）。</summary>
    public int Count => _states.Count;

    public Task RegisterAsync(CorpseLootRegistration registration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var state = new Horizon.Game.Sim.Monsters.MonsterCorpseLootState
        {
            ExpireTick = registration.ExpireTick,
            PickupDistanceMeters = registration.PickupDistanceMeters,
        };
        for (var i = 0; i < registration.Slots.Count; i++)
        {
            var s = registration.Slots[i];
            state.Slots.Add(new Horizon.Game.Sim.Monsters.MonsterCorpseLootState.Slot
            {
                ItemId = s.ItemId, Count = s.Count, ConditionKind = s.ConditionKind, QuestId = s.QuestId, Money = s.Money,
            });
        }
        for (var i = 0; i < registration.Contributors.Count; i++)
        {
            var id = registration.Contributors[i];
            if (id != 0UL && !state.Contributors.Contains(id)) state.Contributors.Add(id);
        }
        _states[registration.CorpseEntityId] = state;   // 同 id 覆盖（重生复用实体 id）
        RegisterCount++;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(ulong corpseEntityId, CancellationToken cancellationToken = default)
    {
        if (_states.Remove(corpseEntityId)) RemoveCount++;
        return Task.CompletedTask;
    }

    public Task<CorpseLootTakeResult> TakeAsync(ulong corpseEntityId, ulong takerCharacterId, int slotIndex, long nowTick,
                                                float distanceMeters, IReadOnlyCollection<int>? heldQuestIds = null,
                                                CancellationToken cancellationToken = default)
    {
        if (!_states.TryGetValue(corpseEntityId, out var state))
            return Task.FromResult(new CorpseLootTakeResult { Status = CorpseLootTakeStatus.UnknownCorpse });

        Horizon.Game.Sim.Monsters.LootTakeResult outcome;
        Horizon.Game.Sim.Monsters.MonsterCorpseLootState.Slot? slot;
        var actualIndex = slotIndex;
        if (slotIndex < 0)
        {
            outcome = state.TryTakeFirst(nowTick, distanceMeters, takerCharacterId, out slot, out actualIndex, heldQuestIds);
        }
        else
        {
            outcome = state.TryTake(slotIndex, nowTick, distanceMeters, takerCharacterId, out slot, heldQuestIds);
        }
        var result = new CorpseLootTakeResult
        {
            SlotIndex = actualIndex,
            Status = outcome switch
            {
                Horizon.Game.Sim.Monsters.LootTakeResult.Taken => CorpseLootTakeStatus.Taken,
                Horizon.Game.Sim.Monsters.LootTakeResult.AlreadyTaken => CorpseLootTakeStatus.AlreadyTaken,
                Horizon.Game.Sim.Monsters.LootTakeResult.Expired => CorpseLootTakeStatus.Expired,
                Horizon.Game.Sim.Monsters.LootTakeResult.TooFar => CorpseLootTakeStatus.TooFar,
                Horizon.Game.Sim.Monsters.LootTakeResult.InvalidSlot => CorpseLootTakeStatus.InvalidSlot,
                Horizon.Game.Sim.Monsters.LootTakeResult.NotYours => CorpseLootTakeStatus.NotYours,
                Horizon.Game.Sim.Monsters.LootTakeResult.ConditionNotMet => CorpseLootTakeStatus.ConditionNotMet,
                _ => CorpseLootTakeStatus.Empty,
            },
        };
        if (result.Status == CorpseLootTakeStatus.Taken && slot != null)
        {
            result.Slot = new CorpseLootSlotDto
            {
                ItemId = slot.ItemId, Count = slot.Count, ConditionKind = slot.ConditionKind,
                QuestId = slot.QuestId, Money = slot.Money,
            };
        }
        return Task.FromResult(result);
    }
}
