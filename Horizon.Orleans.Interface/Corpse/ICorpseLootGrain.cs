using System.Threading.Tasks;
using Orleans;

namespace Horizon.Orleans.Interface.Corpse
{
    /// <summary>尸体上的一个掉落槽（Grain 边界的线上形态）。</summary>
    [GenerateSerializer]
    public sealed class CorpseLootSlotWire
    {
        [Id(0)] public int ItemId { get; set; }
        [Id(1)] public int Count { get; set; }
        [Id(2)] public int ConditionKind { get; set; }
        [Id(3)] public int QuestId { get; set; }
        [Id(4)] public int Money { get; set; }
    }

    /// <summary>登记一具尸体的拾取列表。</summary>
    [GenerateSerializer]
    public sealed class CorpseLootRegistrationWire
    {
        [Id(0)] public ulong CorpseEntityId { get; set; }
        [Id(1)] public long ExpireTick { get; set; }
        [Id(2)] public float PickupDistanceMeters { get; set; }
        [Id(3)] public CorpseLootSlotWire[] Slots { get; set; } = System.Array.Empty<CorpseLootSlotWire>();

        /// <summary>击杀参与者（谁能取）；空 = 不限制。尾部追加，老 DS 不发这一格时语义不变。</summary>
        [Id(4)] public ulong[] Contributors { get; set; } = System.Array.Empty<ulong>();
    }

    /// <summary>一次拾取的回执（状态值沿用 DS 侧 <c>CorpseLootTakeStatus</c> 的字节）。</summary>
    [GenerateSerializer]
    public sealed class CorpseLootTakeReply
    {
        [Id(0)] public byte Status { get; set; }
        [Id(1)] public CorpseLootSlotWire? Slot { get; set; }

        /// <summary>实际取走的槽号（请求传 -1 = 取下一件时，客户端要靠它知道取到了哪一格）。</summary>
        [Id(2)] public int SlotIndex { get; set; }
    }

    /// <summary>
    /// 一具尸体的拾取列表（2026-10-28 拍板：列表权威住在 Orleans）。
    /// <para>按尸体实体 id 分键；尸体只活 60 秒左右，所以状态可以是纯内存——但**判据不在 Grain 里**：
    /// 窗口/距离/幂等三条都在 <c>MonsterCorpseLootState</c>（Sim 层纯函数，已被单测逐格钉住），
    /// Grain 只负责持锁与生命周期。这样"两边判得不一样"这件事在结构上就不可能发生。</para>
    /// </summary>
    public interface ICorpseLootGrain : IGrainWithIntegerKey
    {
        /// <summary>登记/覆盖（尸体实体 id 复用时以最后一次为准）。</summary>
        Task RegisterAsync(CorpseLootRegistrationWire registration);

        /// <summary>释放（幂等）；释放后本激活自行去激活。</summary>
        Task RemoveAsync();

        /// <summary>尝试取走一个槽；判据与 DS 同源（含"是不是击杀参与者"）。</summary>
        /// <param name="heldQuestIds">
        /// 取的人**当前进行中**的任务 id（条件件判据）。null = 调用方不知道 ⇒ 条件槽一律不发放
        /// （AC 的 `needs_quest` 语义：判不了就当不满足），普通槽不受影响。
        /// </param>
        Task<CorpseLootTakeReply> TakeAsync(ulong takerCharacterId, int slotIndex, long nowTick, float distanceMeters,
                                            int[]? heldQuestIds = null);
    }
}
