using System;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core.Sim.Server;
using Horizon.Orleans.Interface.Corpse;
using Orleans;
// Core 的上行协议里也有同名类型：这里明确用 Grain 边界那一份。
using WireSlot = Horizon.Orleans.Interface.Corpse.CorpseLootSlotWire;

namespace Horizon.Game.Gateway.Services;

/// <summary>
/// 把 <see cref="ICorpseLootStore"/> 落到 Orleans（2026-10-28 拍板：列表权威在 Grain）。
/// <para>键 = 尸体实体 id（<c>ulong</c> 原样按位转 <c>long</c>：Grain 键只要求稳定，不要求可读；
/// 换成字符串哈希会让"同一个实体 id 在不同进程算出不同键"成为可能）。</para>
/// </summary>
public sealed class CorpseLootGrainStore : ICorpseLootStore
{
    private readonly IClusterClient _cluster;

    public CorpseLootGrainStore(IClusterClient cluster)
        => _cluster = cluster ?? throw new ArgumentNullException(nameof(cluster));

    private ICorpseLootGrain GrainOf(ulong corpseEntityId) => _cluster.GetGrain<ICorpseLootGrain>(unchecked((long)corpseEntityId));

    /// <inheritdoc />
    public async Task RegisterAsync(CorpseLootRegistration registration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var wire = new CorpseLootRegistrationWire
        {
            CorpseEntityId = registration.CorpseEntityId,
            ExpireTick = registration.ExpireTick,
            PickupDistanceMeters = registration.PickupDistanceMeters,
            Contributors = registration.Contributors.Count > 0
                ? registration.Contributors.ToArray()
                : System.Array.Empty<ulong>(),
        };
        var slots = new WireSlot[registration.Slots.Count];
        for (var i = 0; i < slots.Length; i++)
        {
            var s = registration.Slots[i];
            slots[i] = new WireSlot
            {
                ItemId = s.ItemId, Count = s.Count, ConditionKind = s.ConditionKind, QuestId = s.QuestId, Money = s.Money,
            };
        }
        wire.Slots = slots;
        await GrainOf(registration.CorpseEntityId).RegisterAsync(wire).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task RemoveAsync(ulong corpseEntityId, CancellationToken cancellationToken = default)
        => GrainOf(corpseEntityId).RemoveAsync();

    /// <inheritdoc />
    public async Task<CorpseLootTakeResult> TakeAsync(ulong corpseEntityId, ulong takerCharacterId, int slotIndex, long nowTick,
                                                      float distanceMeters, IReadOnlyCollection<int>? heldQuestIds = null,
                                                      CancellationToken cancellationToken = default)
    {
        var reply = await GrainOf(corpseEntityId)
            .TakeAsync(takerCharacterId, slotIndex, nowTick, distanceMeters, heldQuestIds is null ? null : System.Linq.Enumerable.ToArray(heldQuestIds))
            .ConfigureAwait(false);
        var result = new CorpseLootTakeResult { Status = (CorpseLootTakeStatus)reply.Status, SlotIndex = reply.SlotIndex };
        if (result.Status == CorpseLootTakeStatus.Taken && reply.Slot is not null)
        {
            result.Slot = new CorpseLootSlotDto
            {
                ItemId = reply.Slot.ItemId,
                Count = reply.Slot.Count,
                ConditionKind = reply.Slot.ConditionKind,
                QuestId = reply.Slot.QuestId,
                Money = reply.Slot.Money,
            };
        }
        return result;
    }
}
