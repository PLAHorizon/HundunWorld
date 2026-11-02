using System.Threading.Tasks;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Gateway.Services;
using Orleans.TestingHost;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 尸体拾取走**真 Orleans 运行时**（TestCluster：编解码 / 消息路由 / 调度全都真跑）。
/// <para>
/// 此前的证据只到两段：进程内 <see cref="InMemoryCorpseLootStore"/>（判据同源）与上行三帧的字节往返。
/// 两段各自绿证明不了 `CorpseLootRegistrationWire.Contributors`（[Id(4)]）在 Orleans 编解码里真的过得去——
/// 漏一个 Id 或漏标 [GenerateSerializer] 的症状是"名单到对岸变成空数组"，
/// 而空数组的语义恰好是**不限制**：权限门静默失效、没有任何报错，只有"别人也能拿走"。
/// </para>
/// </summary>
public class CorpseLootClusterTests : IAsyncLifetime
{
    private TestCluster? _cluster;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<ZoneShardTestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<ZoneShardTestClientConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster != null)
        {
            await _cluster.StopAllSilosAsync();
            _cluster.Dispose();
        }
    }

    private static CorpseLootRegistration Registration(ulong corpseId, ulong[]? contributors = null, long expireTick = 1200)
    {
        var registration = new CorpseLootRegistration
        {
            CorpseEntityId = corpseId,
            ExpireTick = expireTick,
            PickupDistanceMeters = 5.5f,
        };
        registration.Slots.Add(new CorpseLootSlotDto { ItemId = 555, Count = 2 });
        registration.Slots.Add(new CorpseLootSlotDto { ItemId = 0, Count = 1, Money = 10 });
        if (contributors != null)
        {
            for (var i = 0; i < contributors.Length; i++) registration.Contributors.Add(contributors[i]);
        }
        return registration;
    }

    [Fact]
    public async Task 真Orleans下参与名单与槽位一起过线而名单外的人取不到()
    {
        var client = _cluster!.Client;
        var store = new CorpseLootGrainStore(client);
        var corpseId = 910001UL;
        await store.RegisterAsync(Registration(corpseId, new[] { 2001UL }));

        // 名单外：NotYours，且不带槽内容（"名单到对岸变空数组"会让这条变成 Taken）
        var stranger = await store.TakeAsync(corpseId, 2002UL, 0, 700, 0f);
        Assert.Equal(CorpseLootTakeStatus.NotYours, stranger.Status);
        Assert.Null(stranger.Slot);

        // 名单内：取得到第一件（槽内容跨 Grain 边界回来）
        var looter = await store.TakeAsync(corpseId, 2001UL, 0, 700, 0f);
        Assert.Equal(CorpseLootTakeStatus.Taken, looter.Status);
        Assert.NotNull(looter.Slot);
        Assert.Equal(555, looter.Slot!.ItemId);
        Assert.Equal(2, looter.Slot.Count);

        // 同一槽再取：AlreadyTaken；换槽号 -1 取下一个（金币），回执带实际槽号
        Assert.Equal(CorpseLootTakeStatus.AlreadyTaken, (await store.TakeAsync(corpseId, 2001UL, 0, 700, 0f)).Status);
        var money = await store.TakeAsync(corpseId, 2001UL, -1, 700, 0f);
        Assert.Equal(CorpseLootTakeStatus.Taken, money.Status);
        Assert.Equal(1, money.SlotIndex);
        Assert.Equal(10, money.Slot!.Money);
        Assert.Equal(CorpseLootTakeStatus.AlreadyTaken, (await store.TakeAsync(corpseId, 2001UL, -1, 700, 0f)).Status);

        // 没登记过的尸体：UnknownCorpse（与"取空"分开）
        Assert.Equal(CorpseLootTakeStatus.UnknownCorpse, (await store.TakeAsync(910002UL, 2001UL, 0, 700, 0f)).Status);
    }

    [Fact]
    public async Task 真Orleans下上行三帧与作废都落到同一具Grain上()
    {
        var client = _cluster!.Client;
        var store = new CorpseLootGrainStore(client);
        var handler = new CorpseLootUplinkHandler(store);
        var corpseId = 910010UL;

        // DS 的登记帧（线上形态）→ 网关处理器 → Grain
        var registerFrame = new DsCorpseLootRegister
        {
            CorpseEntityId = corpseId,
            ExpireTick = 1200,
            PickupDistanceMeters = 5.5f,
            Slots = new[]
            {
                new CorpseLootSlotWire { ItemId = 777, Count = 1 },
            },
            Contributors = new[] { 2001UL },
        };
        Assert.True(await handler.HandleRegisterAsync(registerFrame));
        Assert.Equal(1, handler.RegisteredCount);

        // 名单外的人经转发帧问权威：回执必须**同一请求号**且是 NotYours
        var strangerReply = await handler.HandleForwardAsync(new DsCorpseLootForward
        {
            SessionId = 2002, CorpseEntityId = corpseId, SlotIndex = 0, RequestId = 41,
            ServerTick = 700, TakerCharacterId = 2002UL,
        });
        Assert.Equal(41, strangerReply.RequestId);
        Assert.Equal(2002, strangerReply.SessionId);
        Assert.Equal((byte)CorpseLootTakeStatus.NotYours, strangerReply.Status);
        Assert.Equal(0, strangerReply.ItemId);   // 没取到就绝不能带物品

        var okReply = await handler.HandleForwardAsync(new DsCorpseLootForward
        {
            SessionId = 2001, CorpseEntityId = corpseId, SlotIndex = 0, RequestId = 42,
            ServerTick = 700, TakerCharacterId = 2001UL,
        });
        Assert.Equal(42, okReply.RequestId);
        Assert.Equal((byte)CorpseLootTakeStatus.Taken, okReply.Status);
        Assert.Equal(777, okReply.ItemId);

        // 作废帧：之后同一个 Grain 一律 UnknownCorpse
        await handler.HandleGoneAsync(new DsCorpseLootGone { CorpseEntityId = corpseId });
        Assert.Equal(1, handler.RemovedCount);
        Assert.Equal(CorpseLootTakeStatus.UnknownCorpse, (await store.TakeAsync(corpseId, 2001UL, 0, 700, 0f)).Status);
    }

    /// <summary>最小任务读取端：只回答"这个人在进行中的任务 id"。</summary>
    private sealed class HeldQuests : IQuestStateReader
    {
        private readonly int[] _ids;
        public HeldQuests(params int[] ids) => _ids = ids;
        public Task<QuestHoldState> QueryHoldAsync(ulong characterId, int questId, System.Threading.CancellationToken cancellationToken = default)
            => Task.FromResult(System.Array.IndexOf(_ids, questId) >= 0 ? QuestHoldState.Held : QuestHoldState.NotHeld);
        public Task<System.Collections.Generic.IReadOnlyCollection<int>?> ActiveQuestIdsAsync(
            ulong characterId, System.Threading.CancellationToken cancellationToken = default)
            => Task.FromResult<System.Collections.Generic.IReadOnlyCollection<int>?>(_ids);
    }

    private static CorpseLootRegistration ConditionalRegistration(ulong corpseId)
    {
        var reg = new CorpseLootRegistration
        {
            CorpseEntityId = corpseId,
            ExpireTick = 1200,
            PickupDistanceMeters = 5.5f,
        };
        reg.Slots.Add(new CorpseLootSlotDto { ItemId = 700, Count = 1 });
        reg.Slots.Add(new CorpseLootSlotDto { ItemId = 701, Count = 1, ConditionKind = 1, QuestId = 90007 });
        reg.Slots.Add(new CorpseLootSlotDto { ItemId = 0, Count = 1, Money = 10 });
        reg.Contributors.Add(2001UL);
        return reg;
    }

    [Fact]
    public async Task 真Orleans下击杀金币真的进钱包()
    {
        // "掉落物品及掉落几率"里金币那一半的最后一跳：KillMoney 事件 → OrleansCurrencyWriter →
        // ICurrencyGrain.AddAsync。此前只有受 HORIZON_ORLEANS_REDIS 门控的 Live 用例覆盖；
        // 钱包 Grain 只依赖持久状态（无 EF），所以能在 TestCluster 里真跑，并**按余额回读**判成功
        // （回执说 Ok 但余额没动，属于"加进了一个没人读的 grain"那一类）。
        var client = _cluster!.Client;
        const ulong characterId = 9_900_000_701UL;
        var wallet = client.GetGrain<Horizon.Orleans.Interface.ICurrencyGrain>((long)characterId);
        var before = await wallet.GetBalanceAsync(Horizon.Game.Sim.Economy.WalletCurrency.Gold);

        await using (var sink = new OrleansGameplayEventSink(null, null, currency: new OrleansCurrencyWriter(client)))
        {
            sink.Publish(new DsGameplayEvent
            {
                Kind = DsGameplayEventKind.KillMoney,
                ActorCharacterId = characterId,
                Money = 37,
                ServerTick = 1,
            });
            await sink.DrainAsync(TimeSpan.FromSeconds(5));
        }

        var after = await wallet.GetBalanceAsync(Horizon.Game.Sim.Economy.WalletCurrency.Gold);
        Assert.Equal(before + 37, after);
    }

    [Fact]
    public async Task 真Orleans下条件槽只发给持有该任务的人且不消耗()
    {
        // needs_quest 的最后一条缝：槽的 (ConditionKind, QuestId) 必须穿过 Orleans 编解码、
        // 由权威侧判、并且**判不过时不消耗**——否则同队真正持有任务的人晚来一步就永远拿不到。
        var client = _cluster!.Client;
        var store = new CorpseLootGrainStore(client);
        var corpseId = 910201UL;
        await store.RegisterAsync(ConditionalRegistration(corpseId));

        var denied = await store.TakeAsync(corpseId, 2001UL, 1, 700, 0f, heldQuestIds: null);
        Assert.Equal(CorpseLootTakeStatus.ConditionNotMet, denied.Status);
        Assert.Null(denied.Slot);

        var taken = await store.TakeAsync(corpseId, 2001UL, 1, 700, 0f, heldQuestIds: new[] { 90007 });
        Assert.Equal(CorpseLootTakeStatus.Taken, taken.Status);
        Assert.Equal(701, taken.Slot!.ItemId);
    }

    [Fact]
    public async Task 网关转发取走时把持有任务一起带给权威侧()
    {
        // 网关这一跳是唯一能拿到任务状态的地方（DS 没有，Grain 不该有）。
        // 少了它，权威侧只能按"判不了"处理：东西还在，但持有任务的人也拿不到。
        var store = new InMemoryCorpseLootStore();
        var corpseId = 910301UL;
        await store.RegisterAsync(ConditionalRegistration(corpseId));

        var withReader = new CorpseLootUplinkHandler(store, new HeldQuests(90007));
        var granted = await withReader.HandleForwardAsync(new DsCorpseLootForward
        {
            SessionId = 2001, CorpseEntityId = corpseId, SlotIndex = 1, RequestId = 51,
            ServerTick = 700, TakerCharacterId = 2001UL,
        });
        Assert.Equal((byte)CorpseLootTakeStatus.Taken, granted.Status);
        Assert.Equal(701, granted.ItemId);

        // 没有读取端：同一槽（先重置一份新登记）必须拒绝，且回执不带物品
        var store2 = new InMemoryCorpseLootStore();
        await store2.RegisterAsync(ConditionalRegistration(corpseId));
        var noReader = new CorpseLootUplinkHandler(store2);
        var denied = await noReader.HandleForwardAsync(new DsCorpseLootForward
        {
            SessionId = 2001, CorpseEntityId = corpseId, SlotIndex = 1, RequestId = 52,
            ServerTick = 700, TakerCharacterId = 2001UL,
        });
        Assert.Equal((byte)CorpseLootTakeStatus.ConditionNotMet, denied.Status);
        Assert.Equal(0, denied.ItemId);
        // 槽还在：换一个"持任务"的读取端（同一份存储）仍能取走
        var reader2 = new CorpseLootUplinkHandler(store2, new HeldQuests(90007));
        var second = await reader2.HandleForwardAsync(new DsCorpseLootForward
        {
            SessionId = 2001, CorpseEntityId = corpseId, SlotIndex = 1, RequestId = 53,
            ServerTick = 700, TakerCharacterId = 2001UL,
        });
        Assert.Equal((byte)CorpseLootTakeStatus.Taken, second.Status);
        Assert.Equal(701, second.ItemId);
    }
}
