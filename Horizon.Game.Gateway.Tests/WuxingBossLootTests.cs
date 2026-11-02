using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Sim;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using StackExchange.Redis;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 世界 BOSS 那一档产出：天阶三式与混沌终式到底掉不掉得出来。
/// <para>
/// <b>这一档原来是没有的</b>：生成器把天阶书写成 <c>via:["worldBoss"]</c>，
/// 而 <c>WuxingLootTables.Build</c> 只认 <c>via</c> 含 <c>loot</c> 的行——于是天阶 15 本与混沌 1 本
/// 不进任何掉落表，全仓也没有任何一处消费 <c>worldBoss</c> 这个标记。
/// 后果不是"少一路产出"：道阶演化的前置是<b>天阶三境练满</b>，所以整条"超越天阶"的路
/// 在实现里是走不通的，而且一路都没有报错（BOSS 身上什么都不掉，看起来像运气差）。
/// </para>
/// </summary>
[Collection(MonsterContentStaticCollection.Name)]
public class WuxingBossLootTests
{
    // ---------------- 表 ID 口径：两端必须是同一个数 ----------------

    [Fact]
    public void WorldBossTableBase_IsTheSameNumberOnBothSides()
    {
        // Silo 装配用的表 ID 与 DS 解析用的表 ID 各写了一次这个数（Silo 不便为此新增到 Sim 的工程引用）。
        // 分叉的表现：BOSS 掉不出天阶、道阶又练不满，两边日志都正常。
        Assert.Equal(MonsterTemplateCatalog.WorldBossTableBase, Horizon.Orleans.Silo.Startup.WuxingLootTables.WorldBossTableBase);
    }

    [Theory]
    [InlineData(6, 101)]    // 锐金夜叉：金域 BOSS
    [InlineData(8, 103)]    // 玄溟螭：水域 BOSS
    [InlineData(10, 105)]   // 厚岩魁：土域 BOSS
    [InlineData(11, 100)]   // 混沌遗蜕：无域，走总表
    [InlineData(1, 3)]      // 墨麒麟：普通怪仍是域表
    [InlineData(4242, 0)]   // 未登记：无表
    public void Mapping_RoutesBossesToTheBossTierAndMobsToTheDomainTier(int templateId, int expectedTableId)
    {
        Assert.Equal(expectedTableId, MonsterTemplateCatalog.LootTableIdOf(templateId));
        Assert.Equal(expectedTableId, new WuxingDomainLootMapping().ResolveLootTableId(templateId));
    }

    [Fact]
    public void Catalog_HasOneBossPerDomainPlusAChaosBoss_AndMobsStaySeparate()
    {
        var rows = MonsterTemplateCatalog.RegisteredTemplates();
        var bosses = rows.Where(r => r.IsWorldBoss).ToArray();
        var mobs = rows.Where(r => !r.IsWorldBoss).ToArray();

        Assert.Equal(5, bosses.Count(b => b.WuxingDomain >= 1 && b.WuxingDomain <= 5));  // 五行各一只
        Assert.Single(bosses, b => b.WuxingDomain == 0);                                 // 混沌那一只
        Assert.All(mobs, m => Assert.False(m.IsWorldBoss));

        // BOSS 不许掉日常书，日常怪不许掉天阶：两档表 ID 必须彻底分开
        Assert.All(mobs, m => Assert.True(MonsterTemplateCatalog.LootTableIdOf(m.TemplateId) is >= 1 and <= 5));
        Assert.All(bosses, b => Assert.True(MonsterTemplateCatalog.LootTableIdOf(b.TemplateId) >= 100));
    }

    // ---------------- 装配口径：worldBoss 那一档真的进了表 ----------------

    [Fact]
    public void Build_PutsTierFiveBooksIntoBossTablesAndNeverIntoMobTables()
    {
        var rows = new List<Horizon.Model.GameModel.ItemTemplateEntity>
        {
            Row(810501, 6, 0.01, 5, """{"via":["worldBoss"],"dropRate":0.01,"domain":1}"""),
            Row(830503, 6, 0.01, 5, """{"via":["worldBoss"],"dropRate":0.01,"domain":3}"""),
            Row(890001, 6, 0.005, 5, """{"via":["worldBoss"],"dropRate":0.005,"domain":0}"""),
            Row(810101, 6, 0.08, 1, """{"via":["loot","quest"],"domain":1}"""),
            Row(870101, 6, 0d, 5, """{"via":["evolve"],"domain":1}"""),      // 道阶：不许拾取
        };

        var tables = Horizon.Orleans.Silo.Startup.WuxingLootTables.Build(rows);

        Assert.Equal(new[] { 1, 100, 101, 103 }, tables.Keys.ToArray());
        Assert.Equal(new[] { 810501 }, tables[101].Entries.Select(e => e.ItemId).ToArray());
        Assert.Equal(new[] { 830503 }, tables[103].Entries.Select(e => e.ItemId).ToArray());
        Assert.Equal(new[] { 890001 }, tables[100].Entries.Select(e => e.ItemId).ToArray());

        // 日常表里只有白阶那一本：天阶一旦混进去，"一张书就是学问"这条设计就被小怪冲垮
        Assert.Equal(new[] { 810101 }, tables[1].Entries.Select(e => e.ItemId).ToArray());

        // 天阶 rarity 5：一次只掉一张，且走保底（否则 1% 的产出会长期颗粒无收）
        var tierFive = tables[101].Entries.Single();
        Assert.Equal(1, tierFive.MaxCount);
        Assert.True(tierFive.IsPityEligible);
    }

    [Fact]
    public void AWorldBossKill_RollsOnlyItsOwnDomainsTierFiveBooks()
    {
        // 每张表放一件可判别的标记物品：拿到哪件，就说明掷的是哪张表。
        var source = new TableKeyedLootSource();
        foreach (var domain in new[] { 1, 2, 3, 4, 5 })
        {
            source.Tables[100 + domain] = new LootTableSnapshot
            {
                TableId = 100 + domain,
                Entries = new[]
                {
                    new LootEntrySnapshot { ItemId = 800000 + domain * 10000 + 501, DropRate = 1f, MinCount = 1, MaxCount = 1, Rarity = 5 },
                },
            };
        }
        source.Tables[100] = new LootTableSnapshot
        {
            TableId = 100,
            Entries = new[] { new LootEntrySnapshot { ItemId = 890001, DropRate = 1f, MinCount = 1, MaxCount = 1, Rarity = 5 } },
        };
        // 日常档也给标记物品：这样才能断"普通怪掷的是日常表"，而不是仅仅断"它掷不出东西"
        foreach (var domain in new[] { 1, 2, 3, 4, 5 })
        {
            source.Tables[domain] = new LootTableSnapshot
            {
                TableId = domain,
                Entries = new[]
                {
                    new LootEntrySnapshot { ItemId = 800000 + domain * 10000 + 101, DropRate = 1f, MinCount = 1, MaxCount = 2, Rarity = 1 },
                },
            };
        }

        var provider = new OrleansLootProvider(source, new WuxingDomainLootMapping());
        var allTemplates = MonsterTemplateCatalog.RegisteredTemplates().Select(t => t.TemplateId);
        Assert.True(provider.WarmupAsync(allTemplates).Result);
        Assert.Equal(11, provider.CachedTableCount);

        foreach (var row in MonsterTemplateCatalog.RegisteredTemplates().Where(t => t.IsWorldBoss))
        {
            var drop = Assert.Single(provider.Roll(row.TemplateId, 2001UL, serverTick: 9));
            var expected = row.WuxingDomain == 0 ? 890001 : 800000 + row.WuxingDomain * 10000 + 501;
            Assert.True(drop.ItemId == expected,
                $"BOSS 模板 {row.TemplateId}（域 {row.WuxingDomain}）应只掉 {expected}，实际 {drop.ItemId}");
        }

        // 普通怪绝不该掷到 BOSS 表：那等于让小怪掉出天阶
        foreach (var mob in MonsterTemplateCatalog.RegisteredTemplates().Where(t => !t.IsWorldBoss))
        {
            var mobDrop = Assert.Single(provider.Roll(mob.TemplateId, 2001UL, 9));
            Assert.Equal(800000 + mob.WuxingDomain * 10000 + 101, mobDrop.ItemId);
        }
    }

    // ---------------- 真集群：天阶/混沌确实从 BOSS 身上掉得出来 ----------------

    private static string? RedisConnectionString => Environment.GetEnvironmentVariable("HORIZON_ORLEANS_REDIS");

    [Fact]
    public async Task Live_WorldBossDropsItsOwnDomainTierFiveBooks_ClosingTheDaoJieUpstream()
    {
        if (string.IsNullOrWhiteSpace(RedisConnectionString)) return;   // 环境门控

        var builder = Host.CreateApplicationBuilder();
        var redisOptions = ConfigurationOptions.Parse(RedisConnectionString!);
        builder.UseOrleansClient(client =>
        {
            client.UseRedisClustering(o => o.ConfigurationOptions = redisOptions);
            client.Configure<ClusterOptions>(o => { o.ClusterId = "dev"; o.ServiceId = "BaseService"; });
        });

        var host = builder.Build();
        await host.StartAsync();
        try
        {
            var client = host.Services.GetRequiredService<IClusterClient>();

            // 与启动装配同一个纯函数、同一份种子；只在缺的时候写（已有数据绝不覆盖）。
            var tables = Horizon.Orleans.Silo.Startup.WuxingLootTables.Build(WuxingLootDomainLiveTests.ParseSeedItemRows());
            var result = await Horizon.Orleans.Silo.Startup.WuxingLootTableEnsure.EnsureAsync(
                tables, d => client.GetGrain<ILootTableGrain>(d), onlyIfAbsent: true);
            Assert.True(result.Failed.Count == 0, $"BOSS 档装配失败：{string.Join(",", result.Failed)}");
            Assert.Contains(100, tables.Keys);
            foreach (var domain in new[] { 1, 2, 3, 4, 5 }) Assert.Contains(100 + domain, tables.Keys);

            var provider = new OrleansLootProvider(new OrleansLootTableConfigSource(client), new WuxingDomainLootMapping());
            var bosses = MonsterTemplateCatalog.RegisteredTemplates().Where(t => t.IsWorldBoss).Select(t => t.TemplateId).ToArray();
            Assert.True(await provider.WarmupAsync(bosses), "六张 BOSS 档表应能从真实 Grain 预热");

            var tierFiveSeen = new HashSet<int>();
            foreach (var row in MonsterTemplateCatalog.RegisteredTemplates().Where(t => t.IsWorldBoss))
            {
                var books = new List<int>();
                // 4000 次：天阶 1% / 混沌 0.5% 的概率条目要的是"迟早掉得出"，
                // 600 次会有百分之几的样本掉不到某一本，那就是纯抖动而不是规则问题。
                for (var tick = 1L; tick <= 4000L; tick++)
                {
                    foreach (var drop in provider.Roll(row.TemplateId, (ulong)(9000 + tick % 30), tick))
                    {
                        // 只许掉天阶号段（8d05xx）或混沌 890001：BOSS 身上掉出日常书说明表 ID 串了档
                        var isTierFive = drop.ItemId >= 800100 && drop.ItemId <= 860000
                            && (drop.ItemId - 800000) % 10000 >= 501 && (drop.ItemId - 800000) % 10000 <= 503;
                        Assert.True(isTierFive || drop.ItemId == 890001,
                            $"BOSS 模板 {row.TemplateId} 掉出了非天阶产出 {drop.ItemId}");

                        if (row.WuxingDomain >= 1 && isTierFive)
                        {
                            Assert.True((drop.ItemId - 800000) / 10000 == row.WuxingDomain,
                                $"BOSS 模板 {row.TemplateId}（域 {row.WuxingDomain}）掉出了域 {(drop.ItemId - 800000) / 10000} 的天阶书 {drop.ItemId}");
                        }
                        books.Add(drop.ItemId);
                        tierFiveSeen.Add(drop.ItemId);
                    }
                }

                Assert.True(books.Count > 0, $"BOSS 模板 {row.TemplateId} 在 4000 次击杀里什么都没掉");
            }

            // 天阶 15 本 + 混沌 1 本，一本都不能缺席：道阶演化要的是"该域三境练满"
            var expectedTierFive = new List<int>();
            for (var d = 1; d <= 5; d++)
                for (var realm = 1; realm <= 3; realm++)
                    expectedTierFive.Add(800000 + d * 10000 + 500 + realm);

            Assert.Contains(890001, tierFiveSeen);
            Assert.Empty(expectedTierFive.Except(tierFiveSeen));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private sealed class TableKeyedLootSource : ILootTableConfigSource
    {
        public Dictionary<int, LootTableSnapshot> Tables { get; } = new();

        public Task<LootTableSnapshot?> GetLootTableAsync(int lootTableId, CancellationToken cancellationToken = default)
            => Task.FromResult(Tables.TryGetValue(lootTableId, out var t) ? t : null);
    }

    private static Horizon.Model.GameModel.ItemTemplateEntity Row(
        int id, int itemType, double dropRate, int rarity, string sourceDetail)
        => new()
        {
            Id = id,
            ItemType = itemType,
            DropRate = (decimal)dropRate,
            Rarity = (byte)rarity,
            SourceDetail = sourceDetail,
            SourceType = 2,
            ItemName = id.ToString(),
        };
}
