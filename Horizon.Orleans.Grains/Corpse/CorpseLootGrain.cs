using System.Threading.Tasks;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Sim.Monsters;
using Horizon.Orleans.Interface.Corpse;
// Core 里也有一份线上形态（上行协议用），名字撞了：这里明确用 Grain 边界那一份。
using WireSlot = Horizon.Orleans.Interface.Corpse.CorpseLootSlotWire;
using Orleans;

namespace Horizon.Orleans.Grains.Corpse
{
    /// <summary>
    /// 尸体拾取列表的权威持有者（键 = 尸体实体 id）。
    /// <para>状态是 <see cref="MonsterCorpseLootState"/>——Sim 层的纯状态机：窗口/距离/幂等三条判据都在那里，
    /// Grain 只负责"同一具尸体的请求串行化"与生命周期。**故意不做持久化**：尸体只活几十秒，
    /// 它到期时 DS 会发 Gone；持久化只会让"重启后还挂着一具早该没了的尸体"这种账更难清。</para>
    /// </summary>
    public sealed class CorpseLootGrain : Grain, ICorpseLootGrain
    {
        private MonsterCorpseLootState? _state;

        /// <inheritdoc />
        public Task RegisterAsync(CorpseLootRegistrationWire registration)
        {
            var state = new MonsterCorpseLootState
            {
                ExpireTick = registration.ExpireTick,
                PickupDistanceMeters = registration.PickupDistanceMeters,
            };
            for (var i = 0; i < registration.Slots.Length; i++)
            {
                var s = registration.Slots[i];
                state.Slots.Add(new MonsterCorpseLootState.Slot
                {
                    ItemId = s.ItemId,
                    Count = s.Count,
                    ConditionKind = s.ConditionKind,
                    QuestId = s.QuestId,
                    Money = s.Money,
                });
            }
            // 击杀参与者（谁能取）：与槽位同批落到状态里，判据由 Sim 层同一处回答。
            for (var i = 0; i < registration.Contributors.Length; i++)
            {
                var id = registration.Contributors[i];
                if (id != 0UL && !state.Contributors.Contains(id)) state.Contributors.Add(id);
            }
            _state = state;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RemoveAsync()
        {
            _state = null;
            DeactivateOnIdle();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<CorpseLootTakeReply> TakeAsync(ulong takerCharacterId, int slotIndex, long nowTick, float distanceMeters,
                                            int[]? heldQuestIds = null)
        {
            if (_state == null)
                return Task.FromResult(new CorpseLootTakeReply { Status = (byte)CorpseLootTakeStatus.UnknownCorpse });

            // slotIndex < 0 = "取下一件"（AC 右键连取）：槽号由这一侧回执带回，客户端不必自己跟踪。
            LootTakeResult outcome;
            MonsterCorpseLootState.Slot? slot;
            int actualIndex;
            if (slotIndex < 0)
            {
                outcome = _state.TryTakeFirst(nowTick, distanceMeters, takerCharacterId, out slot, out actualIndex, heldQuestIds);
            }
            else
            {
                outcome = _state.TryTake(slotIndex, nowTick, distanceMeters, takerCharacterId, out slot, heldQuestIds);
                actualIndex = slotIndex;
            }
            var reply = new CorpseLootTakeReply
            {
                SlotIndex = actualIndex,
                Status = outcome switch
                {
                    LootTakeResult.Taken => (byte)CorpseLootTakeStatus.Taken,
                    LootTakeResult.AlreadyTaken => (byte)CorpseLootTakeStatus.AlreadyTaken,
                    LootTakeResult.Expired => (byte)CorpseLootTakeStatus.Expired,
                    LootTakeResult.TooFar => (byte)CorpseLootTakeStatus.TooFar,
                    LootTakeResult.InvalidSlot => (byte)CorpseLootTakeStatus.InvalidSlot,
                    LootTakeResult.NotYours => (byte)CorpseLootTakeStatus.NotYours,
                    LootTakeResult.ConditionNotMet => (byte)CorpseLootTakeStatus.ConditionNotMet,
                    _ => (byte)CorpseLootTakeStatus.Empty,
                },
            };
            if (outcome == LootTakeResult.Taken && slot != null)
            {
                reply.Slot = new WireSlot
                {
                    ItemId = slot.ItemId,
                    Count = slot.Count,
                    ConditionKind = slot.ConditionKind,
                    QuestId = slot.QuestId,
                    Money = slot.Money,
                };
            }
            return Task.FromResult(reply);
        }
    }
}
