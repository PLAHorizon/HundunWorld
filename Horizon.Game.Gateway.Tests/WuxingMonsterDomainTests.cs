using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Sim;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// "怪 → 五行域"这一列的落点与两处消费（刷怪掉落、击杀好感）。
/// <para>
/// 掉落表是按**域号**装配的（<c>WuxingLootTables.Build</c> 用 1..5 作表 ID），而怪物模板 ID 是
/// 另一个整数空间。默认映射 <see cref="IdentityMonsterLootMapping"/> 把两者当成同一个数，
/// 于是"装配好的 75 条产出"在运行态一条都读不到——且日志毫无痕迹。
/// 本套件把这条链钉在唯一的域号事实源（<see cref="MonsterTemplateCatalog"/> 的 WuxingDomain 列）上。
/// </para>
/// </summary>
[Collection(MonsterContentStaticCollection.Name)]
public class WuxingMonsterDomainTests
{
    // ---------------- 事实源：模板目录里的那一列 ----------------

    [Fact]
    public void Catalog_RegistersOneBeastPerWuxingDomain()
    {
        var rows = MonsterTemplateCatalog.RegisteredTemplates();

        Assert.All(rows, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Name), $"模板 {r.TemplateId} 不得空名");
            // 普通怪必须落在 1..5（要掉域书、要涨好感）；世界 BOSS 允许无域（混沌那一档本就无域）。
            if (r.IsWorldBoss)
            {
                Assert.True(r.WuxingDomain == 0 || (r.WuxingDomain >= 1 && r.WuxingDomain <= 5),
                    $"BOSS 模板 {r.TemplateId} 的域号非法：{r.WuxingDomain}");
            }
            else
            {
                Assert.True(r.WuxingDomain >= 1 && r.WuxingDomain <= 5,
                    $"模板 {r.TemplateId}（{r.Name}）必须落在 1..5 的五行域，实际 {r.WuxingDomain}");
            }
        });

        // 五个域各有一兽（普通怪），保证开发世界刷得出全部五域的书与好感。
        // 世界 BOSS 与它同域（掉的是天阶表），所以这里只按"普通怪"这一档要求覆盖。
        var mobDomains = rows.Where(r => !r.IsWorldBoss).Select(r => r.WuxingDomain).OrderBy(d => d).ToArray();
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, mobDomains);
        Assert.All(rows, r => Assert.NotEqual(0, MonsterTemplateCatalog.LootTableIdOf(r.TemplateId)));
    }

    [Fact]
    public void UnregisteredTemplate_HasNoDomain()
    {
        Assert.Equal(0, MonsterTemplateCatalog.DomainOf(9999));
        // 显示兜底可以给名字（客户端不能空名），但绝不能猜出一个域。
        var fallback = MonsterTemplateCatalog.Get(9999);
        Assert.False(string.IsNullOrWhiteSpace(fallback.Name));
        Assert.Equal(0, fallback.WuxingDomain);
    }

    /// <summary>
    /// 脚本化生成的白名单用的就是 <see cref="MonsterTemplateCatalog.TryGetRegistered"/>
    /// （<c>ScriptAuthorityCore.cs:394</c>），所以这条断言等价于"这些模板能在 PIE 里被 GM 刷出来"——
    /// 世界 BOSS 不在开发刷怪轮换里，验证掉落要走这条口。
    /// </summary>
    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(11)]
    public void WorldBosses_AreWhitelistedForScriptedSpawning(int templateId)
    {
        Assert.True(MonsterTemplateCatalog.TryGetRegistered(templateId, out var info),
            $"BOSS 模板 {templateId} 必须已登记，否则脚本化生成会拒绝它、天阶掉落就无法实机验证");
        Assert.True(info.IsWorldBoss, $"模板 {templateId} 应当是 BOSS 档");
        Assert.False(string.IsNullOrWhiteSpace(info.Name));
    }

    [Fact]
    public void UnknownTemplate_IsNotWhitelisted()
    {
        Assert.False(MonsterTemplateCatalog.TryGetRegistered(9999, out _));
    }

    // ---------------- 消费一：掉落表 ID = 域号 ----------------

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void LootMapping_ResolvesTheDomainColumn_NotTheTemplateId(int templateId)
    {
        var mapping = new WuxingDomainLootMapping();
        Assert.Equal(MonsterTemplateCatalog.LootTableIdOf(templateId), mapping.ResolveLootTableId(templateId));
        // 普通怪走的就是域号本身（1..5），这条把"表 ID = 域号"这个约定钉死
        Assert.Equal(MonsterTemplateCatalog.DomainOf(templateId), mapping.ResolveLootTableId(templateId));
    }

    [Fact]
    public void LootMapping_UnknownTemplate_ResolvesToNoTable()
    {
        Assert.Equal(0, new WuxingDomainLootMapping().ResolveLootTableId(4242));
        Assert.Equal(0, new WuxingDomainLootMapping().ResolveLootTableId(0));
    }

    [Fact]
    public async Task KillOfADomainedBeast_RollsThatDomainTable_AndNothingElse()
    {
        // 每张域表放一件可判别的标记物品：拿到哪件，就说明掷的是哪张表。
        var source = new DomainKeyedLootSource();
        foreach (var domain in new[] { 1, 2, 3, 4, 5 })
        {
            source.Tables[domain] = new LootTableSnapshot
            {
                TableId = domain,
                Entries = new[]
                {
                    new LootEntrySnapshot { ItemId = 800000 + domain, DropRate = 1f, MinCount = 1, MaxCount = 1, Rarity = 1 },
                },
            };
        }

        var provider = new OrleansLootProvider(source, new WuxingDomainLootMapping());
        var mobs = MonsterTemplateCatalog.RegisteredTemplates().Where(t => !t.IsWorldBoss).ToArray();
        Assert.True(await provider.WarmupAsync(mobs.Select(t => t.TemplateId)));
        Assert.Equal(5, provider.CachedTableCount);

        foreach (var row in mobs)
        {
            var drops = provider.Roll(row.TemplateId, killerCharacterId: 2001UL, serverTick: 77);
            var drop = Assert.Single(drops);
            Assert.Equal(800000 + row.WuxingDomain, drop.ItemId);
        }

        // 未登记模板：不掉任何东西。"什么都不掉"与"掉错域"里，只有前者是可接受的失败。
        Assert.Empty(provider.Roll(4242, 2001UL, 77));
    }

    // ---------------- 消费二：击杀 → 分域好感 ----------------

    [Fact]
    public async Task KillAdvancesFavorabilityOfTheBeastsOwnDomain()
    {
        var favor = new FakeFavorabilityWriter();
        await using var sink = new OrleansGameplayEventSink(null, null, favorability: favor);

        sink.Publish(new DsGameplayEvent
        {
            Kind = DsGameplayEventKind.Kill,
            ShardId = 1,
            ServerTick = 5,
            ActorCharacterId = 2001,
            MonsterTemplateId = 1,
        });

        await sink.DrainAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, sink.FavorabilityGrants);

        var call = Assert.Single(favor.Calls);
        // 好感落在业务侧（研读门槛/任务奖励）读的那把键上，而不是背包那把
        Assert.Equal(Horizon.Game.Core.GrainKeys.ForEntity(2001), call.PlayerId);
        Assert.Equal(MonsterTemplateCatalog.DomainOf(1), call.Domain);
        Assert.Equal(OrleansGameplayEventSink.FavorPerKill, call.Delta);
        // targetKey 带模板号：FavorabilityGrain 的"单一来源上限"就是按它计的，
        // 换成固定串会让所有怪共享一个上限，换成实体 ID 会让上限形同虚设。
        Assert.Equal("monster:1", call.TargetKey);
    }

    [Fact]
    public async Task KillOfADomainlessBeast_AccountsNoFavorability()
    {
        var favor = new FakeFavorabilityWriter();
        await using var sink = new OrleansGameplayEventSink(null, null, favorability: favor);

        sink.Publish(new DsGameplayEvent
        {
            Kind = DsGameplayEventKind.Kill,
            ActorCharacterId = 2001,
            MonsterTemplateId = 4242,   // 未登记 → 无域
        });
        await sink.DrainAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(favor.Calls);
        Assert.Equal(0, sink.FavorabilityGrants);
        Assert.Equal(0, sink.FavorabilityFailed);
    }

    [Fact]
    public async Task FavorabilityFailure_IsCounted_AndDoesNotBreakTheKillPath()
    {
        var favor = new FakeFavorabilityWriter { AlwaysFails = true };
        var quests = new FakeQuestWriter();
        await using var sink = new OrleansGameplayEventSink(null, quests, favorability: favor);

        sink.Publish(new DsGameplayEvent
        {
            Kind = DsGameplayEventKind.Kill,
            ActorCharacterId = 2001,
            MonsterTemplateId = 1,
        });
        await sink.DrainAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, sink.FavorabilityFailed);
        Assert.Equal(0, sink.FavorabilityGrants);
        // 好感写不进去，任务进度照常推进——两条账各自独立，不能一荣俱荣一损俱损。
        Assert.Equal(1, sink.QuestObjectiveAdvances);
    }

    [Fact]
    public async Task SinkWithoutFavorabilityWriter_StillProcessesKills()
    {
        var quests = new FakeQuestWriter();
        await using var sink = new OrleansGameplayEventSink(null, quests);

        sink.Publish(new DsGameplayEvent
        {
            Kind = DsGameplayEventKind.Kill,
            ActorCharacterId = 2001,
            MonsterTemplateId = 1,
        });
        await sink.DrainAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, sink.QuestObjectiveAdvances);
        Assert.Equal(0, sink.FavorabilityGrants);
    }

    // ---------------- 假实现 ----------------

    private sealed class DomainKeyedLootSource : ILootTableConfigSource
    {
        public Dictionary<int, LootTableSnapshot> Tables { get; } = new();

        public Task<LootTableSnapshot?> GetLootTableAsync(int lootTableId, CancellationToken cancellationToken = default)
            => Task.FromResult(Tables.TryGetValue(lootTableId, out var t) ? t : null);
    }

    private sealed record FavorCall(Guid PlayerId, int Domain, int Delta, string TargetKey);

    private sealed class FakeFavorabilityWriter : IFavorabilityWriter
    {
        public List<FavorCall> Calls { get; } = new();
        public bool AlwaysFails { get; set; }

        public Task<bool> AddAsync(Guid playerId, int domain, int delta, string targetKey, CancellationToken cancellationToken = default)
        {
            Calls.Add(new FavorCall(playerId, domain, delta, targetKey));
            return Task.FromResult(!AlwaysFails);
        }
    }

    private sealed class FakeQuestWriter : IQuestProgressWriter
    {
        public Task<int> AdvanceKillObjectiveAsync(ulong characterId, int monsterTemplateId, int count, CancellationToken cancellationToken = default)
            => Task.FromResult(count > 0 ? 1 : 0);
    }
}
