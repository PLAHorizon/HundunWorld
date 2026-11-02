using System;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>
/// 网关侧的上行三帧处理器：登记 / 作废 / 转发取走。
/// <para>放在 Core 而不是网关里，是为了让"转发真的问了权威、回执真的带着请求号"这件事能在**不起集群**的情况下被单测钉住
/// （与 <see cref="IExperienceWriter"/> 一族同一条理由）。网关只负责解帧、调用它、把回执帧写回连接。</para>
/// </summary>
public sealed class CorpseLootUplinkHandler
{
    private readonly ICorpseLootStore _store;

    /// <summary>
    /// 任务状态读取端（条件件判据）。null = 这一端不提供 ⇒ 条件槽一律判"不满足"（东西还在，不消耗），
    /// 绝不"判不了就放行"——那会把任务件发给不该拿的人。
    /// </summary>
    private readonly IQuestStateReader? _questStates;

    public CorpseLootUplinkHandler(ICorpseLootStore store, IQuestStateReader? questStates = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _questStates = questStates;
    }

    /// <summary>登记成功的帧数。</summary>
    public long RegisteredCount { get; private set; }

    /// <summary>作废帧数。</summary>
    public long RemovedCount { get; private set; }

    /// <summary>转发给权威侧的取走请求数。</summary>
    public long ForwardedCount { get; private set; }

    /// <summary>登记失败数（权威侧不可用/异常）。</summary>
    public long RegisterFailures { get; private set; }

    /// <summary>登记一具尸体的拾取列表。</summary>
    public async Task<bool> HandleRegisterAsync(DsCorpseLootRegister register, CancellationToken cancellationToken = default)
    {
        if (register is null || register.Slots is null || register.Slots.Length == 0) return false;
        var registration = new CorpseLootRegistration
        {
            CorpseEntityId = register.CorpseEntityId,
            ExpireTick = register.ExpireTick,
            PickupDistanceMeters = register.PickupDistanceMeters,
        };
        for (var i = 0; i < register.Slots.Length; i++)
        {
            var s = register.Slots[i];
            registration.Slots.Add(new CorpseLootSlotDto
            {
                ItemId = s.ItemId, Count = s.Count, ConditionKind = s.ConditionKind, QuestId = s.QuestId, Money = s.Money,
            });
        }
        if (register.Contributors is { Length: > 0 })
        {
            for (var i = 0; i < register.Contributors.Length; i++)
            {
                var id = register.Contributors[i];
                if (id == 0UL || registration.Contributors.Contains(id)) continue;
                registration.Contributors.Add(id);
            }
        }
        try
        {
            await _store.RegisterAsync(registration, cancellationToken).ConfigureAwait(false);
            RegisteredCount++;
            return true;
        }
        catch
        {
            // 权威侧不可用：计数而不是静默——否则"登记没成功"与"这具尸体本来就没东西"在日志里同形。
            RegisterFailures++;
            return false;
        }
    }

    /// <summary>作废一具尸体（幂等）。</summary>
    public async Task HandleGoneAsync(DsCorpseLootGone gone, CancellationToken cancellationToken = default)
    {
        if (gone is null) return;
        try
        {
            await _store.RemoveAsync(gone.CorpseEntityId, cancellationToken).ConfigureAwait(false);
            RemovedCount++;
        }
        catch
        {
            // 作废失败最坏是多留一份 60 秒的账，不值得让它把上行连接打断；计数留给对账。
            RegisterFailures++;
        }
    }

    /// <summary>
    /// 转发一次取走请求，回执**必须带原请求号**——不然 DS 那边配不上对，玩家点了没反应。
    /// </summary>
    public async Task<DsCorpseLootResult> HandleForwardAsync(DsCorpseLootForward forward, CancellationToken cancellationToken = default)
    {
        if (forward is null) return new DsCorpseLootResult { Status = (byte)CorpseLootTakeStatus.InvalidSlot };
        ForwardedCount++;
        // 距离传 **0**：距离由 DS 那一侧按权威坐标量过（它才是唯一知道玩家位置的地方），
        // 这里再量一次既没有坐标也没有意义；权威侧只剩窗口与幂等两条要判。
        // 0 在 CanPickUp 里是"贴着站"（含边界）⇒ 不会被误判太远。传 float.MaxValue 会当场被判 TooFar
        // （初始版本就这么写，实测红在这里）。

        // 条件件：把取的人**进行中**的任务一次性带下去，由权威侧按槽自己的 (ConditionKind, QuestId) 判。
        // 逐槽再 RPC 一次不划算；null（没有读取端/问不出来）时权威侧按"不满足"处理，不消耗槽。
        int[]? heldQuestIds = null;
        if (_questStates != null)
        {
            try
            {
                var ids = await _questStates.ActiveQuestIdsAsync(forward.TakerCharacterId, cancellationToken).ConfigureAwait(false);
                if (ids is { Count: > 0 })
                {
                    heldQuestIds = new int[ids.Count];
                    var n = 0;
                    foreach (var id in ids) heldQuestIds[n++] = id;
                }
            }
            catch
            {
                heldQuestIds = null;
            }
        }

        var result = await _store
            .TakeAsync(forward.CorpseEntityId, forward.TakerCharacterId, forward.SlotIndex, forward.ServerTick, 0f,
                       heldQuestIds, cancellationToken)
            .ConfigureAwait(false);
        var reply = new DsCorpseLootResult
        {
            RequestId = forward.RequestId,
            SessionId = forward.SessionId,
            SlotIndex = result.SlotIndex >= 0 ? result.SlotIndex : forward.SlotIndex,
            Status = (byte)result.Status,
        };
        if (result.Status == CorpseLootTakeStatus.Taken && result.Slot != null)
        {
            reply.ItemId = result.Slot.ItemId;
            reply.Count = result.Slot.Count;
            reply.ConditionKind = result.Slot.ConditionKind;
            reply.QuestId = result.Slot.QuestId;
            reply.Money = result.Slot.Money;
        }
        return reply;
    }
}
