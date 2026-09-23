using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core.Sim.Server;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// MMORPG 开发计划 M4.3/M4.4 收尾 —— Orleans 接线测试。
/// <para>
/// 在本轮之前 <c>ILootTableGrain</c> / <c>IInventoryGrain</c> / 玩家维度 <c>IQuestGrain</c>
/// **全仓没有任何生产调用方**。本套件锁定的是"这三个 Grain 真的被调用了，且参数正确"。
/// </para>
/// <para>
/// 全部使用假实现（不依赖 Orleans 集群），因为被测对象是**编排与转换逻辑**：
/// 缓存、确定性掷骰、队列路由、键换算、失败隔离。真实 Grain 的调用由
/// <c>OrleansDsGrainAdapters</c> 承担，属薄适配层。
/// </para>
/// </summary>
public class OrleansGameplayWiringTests
{
    // ---------------- 假实现 ----------------

    private sealed class FakeLootSource : ILootTableConfigSource
    {
        public Dictionary<int, LootTableSnapshot> Tables { get; } = new();
        public int CallCount;
        public bool ThrowOnCall;

        public Task<LootTableSnapshot?> GetLootTableAsync(int lootTableId, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (ThrowOnCall) throw new InvalidOperationException("grain unavailable");
            return Task.FromResult(Tables.TryGetValue(lootTableId, out var t) ? t : null);
        }
    }

    private sealed class FakeInventory : IInventoryWriter
    {
        public List<(Guid PlayerId, int TemplateId, int Quantity, int Quality)> Calls { get; } = new();
        public bool Result { get; set; } = true;

        public Task<bool> AddItemAsync(Guid playerId, int templateId, int quantity, int quality, CancellationToken cancellationToken = default)
        {
            Calls.Add((playerId, templateId, quantity, quality));
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeQuestWriter : IQuestProgressWriter
    {
        public List<(ulong CharacterId, int TemplateId, int Count)> Calls { get; } = new();
        public int ReturnValue { get; set; } = 1;

        public Task<int> AdvanceKillObjectiveAsync(ulong characterId, int monsterTemplateId, int count, CancellationToken cancellationToken = default)
        {
            Calls.Add((characterId, monsterTemplateId, count));
            return Task.FromResult(ReturnValue);
        }
    }

    private static LootTableSnapshot Table(int id, params LootEntrySnapshot[] entries)
        => new LootTableSnapshot { TableId = id, Entries = entries };

    private static LootEntrySnapshot Entry(int itemId, float rate, int min = 1, int max = 1)
        => new LootEntrySnapshot { ItemId = itemId, DropRate = rate, MinCount = min, MaxCount = max, Rarity = 1 };

    // ---------------- 掉落：配置加载与缓存 ----------------

    [Fact]
    public async Task Warmup_LoadsTableIntoCache()
    {
        var source = new FakeLootSource();
        source.Tables[100] = Table(100, Entry(5001, 1f));

        var provider = new OrleansLootProvider(source);

        Assert.True(await provider.WarmupAsync(new[] { 100 }));
        Assert.Equal(1, provider.CachedTableCount);
        Assert.Equal(1, source.CallCount);
    }

    [Fact]
    public async Task Warmup_MissingTable_IsCountedAsFailure()
    {
        var source = new FakeLootSource();
        var provider = new OrleansLootProvider(source);

        Assert.False(await provider.WarmupAsync(new[] { 999 }));
        Assert.Equal(0, provider.CachedTableCount);
    }

    [Fact]
    public async Task Warmup_GrainThrows_DoesNotPropagate()
    {
        // 业务层不可用不能杀死 DS 宿主——预热失败只应体现为"无掉落"。
        var source = new FakeLootSource { ThrowOnCall = true };
        var provider = new OrleansLootProvider(source);

        Assert.False(await provider.WarmupAsync(new[] { 100 }));
    }

    [Fact]
    public async Task Warmup_SecondCallForSameTable_DoesNotRefetch()
    {
        var source = new FakeLootSource();
        source.Tables[100] = Table(100, Entry(5001, 1f));
        var provider = new OrleansLootProvider(source);

        await provider.WarmupAsync(new[] { 100 });
        await provider.WarmupAsync(new[] { 100 });

        Assert.Equal(1, source.CallCount);
    }

    // ---------------- 掉落：本地确定性掷骰 ----------------

    [Fact]
    public async Task Roll_GuaranteedEntry_AlwaysDrops()
    {
        var source = new FakeLootSource();
        source.Tables[100] = Table(100, Entry(5001, 1f));
        var provider = new OrleansLootProvider(source);
        await provider.WarmupAsync(new[] { 100 });

        for (var tick = 1; tick <= 50; tick++)
        {
            var drops = provider.Roll(100, 2001UL, tick);
            Assert.Single(drops);
            Assert.Equal(5001, drops[0].ItemId);
        }
    }

    [Fact]
    public async Task Roll_ZeroRateEntry_NeverDrops()
    {
        var source = new FakeLootSource();
        source.Tables[100] = Table(100, Entry(5001, 0f));
        var provider = new OrleansLootProvider(source);
        await provider.WarmupAsync(new[] { 100 });

        for (var tick = 1; tick <= 50; tick++)
        {
            Assert.Empty(provider.Roll(100, 2001UL, tick));
        }
    }

    [Fact]
    public async Task Roll_IsDeterministic_ForSameInputs()
    {
        var source = new FakeLootSource();
        source.Tables[100] = Table(100, Entry(5001, 0.5f), Entry(5002, 0.5f), Entry(5003, 1f, 1, 5));
        var provider = new OrleansLootProvider(source);
        await provider.WarmupAsync(new[] { 100 });

        var first = provider.Roll(100, 2001UL, 777);
        for (var i = 0; i < 50; i++)
        {
            var again = provider.Roll(100, 2001UL, 777);
            Assert.Equal(first.Count, again.Count);
            for (var j = 0; j < first.Count; j++)
            {
                Assert.Equal(first[j].ItemId, again[j].ItemId);
                Assert.Equal(first[j].Count, again[j].Count);
            }
        }
    }

    [Fact]
    public async Task Roll_CountStaysWithinConfiguredBounds()
    {
        var source = new FakeLootSource();
        source.Tables[100] = Table(100, Entry(5001, 1f, min: 2, max: 4));
        var provider = new OrleansLootProvider(source);
        await provider.WarmupAsync(new[] { 100 });

        var seen = new HashSet<int>();
        for (var tick = 1; tick <= 200; tick++)
        {
            var drops = provider.Roll(100, 2001UL, tick);
            Assert.Single(drops);
            Assert.InRange(drops[0].Count, 2, 4);
            seen.Add(drops[0].Count);
        }

        // 反退化：数量应当真的在区间内变化，而不是恒定取 min。
        Assert.True(seen.Count > 1, "掉落数量应随掷骰变化");
    }

    [Fact]
    public async Task Roll_TableNotWarmed_ReturnsEmptyAndCountsMiss()
    {
        // fail-closed：宁可不掉，也不要静默编造掉落（那会让运营数据与配置脱节且难以发现）。
        var source = new FakeLootSource();
        var provider = new OrleansLootProvider(source);

        Assert.Empty(provider.Roll(100, 2001UL, 1));
        Assert.Equal(1, provider.CacheMissCount);
        Assert.Equal(0, provider.TotalRolls);
    }

    [Fact]
    public async Task Roll_UsesConfiguredMapping_NotIdentity()
    {
        // 怪物模板 7 → 掉落表 100（映射可替换，生产应读配置表）。
        var source = new FakeLootSource();
        source.Tables[100] = Table(100, Entry(5001, 1f));

        var provider = new OrleansLootProvider(source, new FixedMapping(7, 100));
        Assert.True(await provider.WarmupAsync(new[] { 7 }));

        var drops = provider.Roll(7, 2001UL, 1);
        Assert.Single(drops);
    }

    [Fact]
    public void Roll_InvalidInputs_AreNoOps()
    {
        var provider = new OrleansLootProvider(new FakeLootSource());
        Assert.Empty(provider.Roll(0, 2001UL, 1));
        Assert.Empty(provider.Roll(100, 0UL, 1));
    }

    private sealed class FixedMapping : IMonsterLootMapping
    {
        private readonly int _template, _table;
        public FixedMapping(int template, int table) { _template = template; _table = table; }
        public int ResolveLootTableId(int monsterTemplateId) => monsterTemplateId == _template ? _table : 0;
    }

    // ---------------- 跨层事件：击杀 → 任务 ----------------

    [Fact]
    public async Task KillEvent_AdvancesQuestObjective()
    {
        var quests = new FakeQuestWriter();
        await using var sink = new OrleansGameplayEventSink(null, quests);

        sink.Publish(new DsGameplayEvent
        {
            Kind = DsGameplayEventKind.Kill,
            ActorCharacterId = 2001,
            MonsterTemplateId = 100,
            ServerTick = 42,
        });

        await sink.DrainAsync(TimeSpan.FromSeconds(3));

        var call = Assert.Single(quests.Calls);
        Assert.Equal(2001UL, call.CharacterId);
        Assert.Equal(100, call.TemplateId);
        Assert.Equal(1, call.Count);
        Assert.Equal(1, sink.QuestObjectiveAdvances);
    }

    // ---------------- 跨层事件：掉落 → 背包 ----------------

    [Fact]
    public async Task LootEvent_AddsItemToInventoryGrain()
    {
        var inv = new FakeInventory();
        await using var sink = new OrleansGameplayEventSink(inv, null);

        sink.Publish(new DsGameplayEvent
        {
            Kind = DsGameplayEventKind.Loot,
            ActorCharacterId = 2001,
            ItemId = 5001,
            ItemCount = 3,
        });

        await sink.DrainAsync(TimeSpan.FromSeconds(3));

        var call = Assert.Single(inv.Calls);
        Assert.Equal(GrainKeyMapper.ToInventoryKey(2001), call.PlayerId);
        Assert.Equal(5001, call.TemplateId);
        Assert.Equal(3, call.Quantity);
        Assert.Equal(3, sink.ItemsGranted);
    }

    [Fact]
    public async Task LootEvent_InventoryFailure_IsCountedNotThrown()
    {
        var inv = new FakeInventory { Result = false };
        await using var sink = new OrleansGameplayEventSink(inv, null);

        sink.Publish(new DsGameplayEvent { Kind = DsGameplayEventKind.Loot, ActorCharacterId = 1, ItemId = 5, ItemCount = 1 });
        await sink.DrainAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(1, sink.FailedCount);
        Assert.Equal(0, sink.ItemsGranted);
    }

    [Fact]
    public async Task NullDependencies_DoNotThrow_AndAreCounted()
    {
        // 业务层未接线时，DS 仍须能跑完整战斗闭环（否则"先上 DS、后接业务"的灰度顺序不成立）。
        await using var sink = new OrleansGameplayEventSink(null, null);

        sink.Publish(new DsGameplayEvent { Kind = DsGameplayEventKind.Kill, ActorCharacterId = 1, MonsterTemplateId = 2 });
        sink.Publish(new DsGameplayEvent { Kind = DsGameplayEventKind.Loot, ActorCharacterId = 1, ItemId = 5, ItemCount = 1 });

        await sink.DrainAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(2, sink.ProcessedCount);
        Assert.Equal(0, sink.FailedCount);
    }

    [Fact]
    public async Task PlayerDeathEvent_IsCountedNotRouted()
    {
        var quests = new FakeQuestWriter();
        var inv = new FakeInventory();
        await using var sink = new OrleansGameplayEventSink(inv, quests);

        sink.Publish(new DsGameplayEvent { Kind = DsGameplayEventKind.PlayerDeath, ActorCharacterId = 1 });
        await sink.DrainAsync(TimeSpan.FromSeconds(3));

        // 死亡惩罚属业务策略，当前无对应 Grain 接口——显式计数而非静默丢弃。
        Assert.Equal(1, sink.ProcessedCount);
        Assert.Empty(quests.Calls);
        Assert.Empty(inv.Calls);
    }

    [Fact]
    public async Task ManyEvents_AreAllProcessed()
    {
        var inv = new FakeInventory();
        await using var sink = new OrleansGameplayEventSink(inv, null);

        for (var i = 1; i <= 200; i++)
        {
            sink.Publish(new DsGameplayEvent { Kind = DsGameplayEventKind.Loot, ActorCharacterId = (ulong)i, ItemId = 5001, ItemCount = 1 });
        }

        await sink.DrainAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(200, inv.Calls.Count);
        Assert.Equal(0, sink.FailedCount);
    }

    // ---------------- 键换算 ----------------

    [Theory]
    [InlineData(1UL)]
    [InlineData(2001UL)]
    [InlineData(0xFFFFFFFFUL)]
    [InlineData(0x0123456789ABCDEFUL)]
    public void GrainKeyMapper_RoundTrips(ulong characterId)
    {
        var key = GrainKeyMapper.ToInventoryKey(characterId);
        Assert.Equal(characterId, GrainKeyMapper.ToCharacterId(key));
    }

    [Fact]
    public void GrainKeyMapper_IsInjective()
    {
        var a = GrainKeyMapper.ToInventoryKey(1);
        var b = GrainKeyMapper.ToInventoryKey(2);
        Assert.NotEqual(a, b);
    }

    // ---------------- 任务目标匹配器 ----------------

    [Theory]
    [InlineData("kill", "击杀 5 只狼", true)]
    [InlineData("KillMonster", "", true)]
    [InlineData("击杀", "杀 3 只熊", true)]
    [InlineData("collect", "采集 5 个草药", false)]
    [InlineData("talk", "与村长对话", false)]
    [InlineData("", "", false)]
    public void KillObjectiveMatcher_RecognizesKillObjectives(string type, string desc, bool expected)
    {
        var m = new KillObjectiveTypeMatcher();
        Assert.Equal(expected, m.ShouldAdvance(type, desc, 100));
    }

    [Fact]
    public void KillObjectiveMatcher_ZeroTemplate_IsRejected()
    {
        Assert.False(new KillObjectiveTypeMatcher().ShouldAdvance("kill", "击杀", 0));
    }
}
