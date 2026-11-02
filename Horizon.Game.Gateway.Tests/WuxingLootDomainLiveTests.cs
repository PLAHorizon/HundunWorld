using System;
using System.Collections.Generic;
using System.Linq;
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
/// 真集群证据：怪物模板的 <c>WuxingDomain</c> 一列，能不能在**运行态**把 Silo 装配好的五行域
/// 掉落表接上——也就是需求里那句"通过刷怪获得初级技能书"。
/// <para>
/// 之前这条链的状态是"两端都验过，中间没接"：Silo 侧 <c>WuxingLootTableSeedService</c> 有实机日志
/// （5 张表 / 75 条产出），DS 侧 <c>OrleansLootProvider</c> 有掷骰单测，但生产注册用的是
/// <see cref="IdentityMonsterLootMapping"/>（表 ID = 模板 ID），而表 ID 是 1..5、模板 ID 是另一个
/// 整数空间——击杀掉落恒为空，且没有任何一行日志会为此报错。
/// </para>
/// <para>
/// 环境门控与 <see cref="OrleansGrainLiveIntegrationTests"/> 一致：未设 <c>HORIZON_ORLEANS_REDIS</c>
/// 直接返回（视为跳过），因此不污染 CI。
/// </para>
/// </summary>
[Collection(MonsterContentStaticCollection.Name)]
public class WuxingLootDomainLiveTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public WuxingLootDomainLiveTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    private static string? RedisConnectionString => Environment.GetEnvironmentVariable("HORIZON_ORLEANS_REDIS");

    private static string ClusterId => Environment.GetEnvironmentVariable("HORIZON_ORLEANS_CLUSTER_ID") ?? "dev";
    private static string ServiceId => Environment.GetEnvironmentVariable("HORIZON_ORLEANS_SERVICE_ID") ?? "BaseService";

    private static async Task<(IHost Host, IClusterClient Client)> ConnectAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        var redisOptions = ConfigurationOptions.Parse(RedisConnectionString!);
        builder.UseOrleansClient(client =>
        {
            client.UseRedisClustering(o => o.ConfigurationOptions = redisOptions);
            client.Configure<ClusterOptions>(o =>
            {
                o.ClusterId = ClusterId;
                o.ServiceId = ServiceId;
            });
        });

        var host = builder.Build();
        await host.StartAsync();
        return (host, host.Services.GetRequiredService<IClusterClient>());
    }

    /// <summary>秘籍物品 ID 落在 <c>800000 + 域*10000</c> 这一万号段里。</summary>
    private static int DomainOfBookItem(int itemId) => itemId >= 800000 && itemId < 860000 ? (itemId - 800000) / 10000 : -1;

    [Fact]
    public async Task Live_DomainedBeast_RollsItsOwnDomainTable_AndNeverAnotherDomainsBooks()
    {
        if (string.IsNullOrWhiteSpace(RedisConnectionString)) return;   // 环境门控

        var (host, client) = await ConnectAsync();
        try
        {
            var mapping = new WuxingDomainLootMapping();
            var provider = new OrleansLootProvider(new OrleansLootTableConfigSource(client), mapping);

            // 本测试只管"日常小怪 → 域表"这一档；世界 BOSS → 天阶表那一档见 WuxingBossLootTests。
            var templates = MonsterTemplateCatalog.RegisteredTemplates().Where(t => !t.IsWorldBoss).ToArray();

            // 先按域逐个读回，好在失败时说清"哪几域的表不在集群里"——
            // 这条链的失败模式都是静默的（表缺失 = 不掉东西，而不是报错）。
            var absent = new List<int>();
            var readErrors = new List<string>();
            foreach (var domain in new[] { 1, 2, 3, 4, 5 })
            {
                try
                {
                    var cfg = await client.GetGrain<ILootTableGrain>(domain).GetConfigAsync();
                    if (cfg is null || cfg.Entries is null || cfg.Entries.Length == 0) absent.Add(domain);
                }
                catch (Exception ex)
                {
                    readErrors.Add($"{domain}:{ex.GetType().Name}");
                }
            }

            Assert.True(readErrors.Count == 0, $"读取五行域掉落表失败：{string.Join(", ", readErrors)}");

            // 表缺失时按 Silo 装配的同一套规则重建，但**只在缺的时候写**：
            // 已经装配好的集群绝不会被测试覆盖掉（覆盖会把"DB 里的真值"换成"种子文件里的真值"，
            // 两者一旦分叉，测试就会掩盖问题而不是暴露问题）。
            // 走的是生产用的同一个函数 WuxingLootTableEnsure.EnsureAsync(onlyIfAbsent: true)，
            // 所以这条真集群测试同时也在验 Silo 的自愈路径本身（打的是真实 Grain）。
            // 数据源是随仓种子 011，与 Silo 的 WuxingLootTableSeedService 读的是同一份产出规则。
            var seededDomains = new List<int>();
            if (absent.Count > 0)
            {
                var tables = Horizon.Orleans.Silo.Startup.WuxingLootTables.Build(ParseSeedItemRows());
                var onlyAbsent = tables.Where(kv => absent.Contains(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
                if (onlyAbsent.Count > 0)
                {
                    var result = await Horizon.Orleans.Silo.Startup.WuxingLootTableEnsure.EnsureAsync(
                        onlyAbsent,
                        domain => client.GetGrain<ILootTableGrain>(domain),
                        onlyIfAbsent: true);
                    Assert.True(result.Failed.Count == 0,
                        $"按种子重建掉落表失败：{string.Join(",", result.Failed)}");
                    seededDomains = result.Seeded;
                }
            }

            var stillAbsent = new List<int>();
            foreach (var domain in new[] { 1, 2, 3, 4, 5 })
            {
                var cfg = await client.GetGrain<ILootTableGrain>(domain).GetConfigAsync();
                if (cfg is null || cfg.Entries is null || cfg.Entries.Length == 0) stillAbsent.Add(domain);
            }
            Assert.True(stillAbsent.Count == 0,
                $"集群里缺这些五行域掉落表且按种子重建后仍缺：{string.Join(",", stillAbsent)}");
            if (seededDomains.Count > 0)
            {
                // 缺表本身就是要报出来的事实：Silo 启动装配过、但此刻集群里没有，
                // 说明这份配置没跟着 Grain 激活回收活下来（掉落会静默变成"什么都不掉"）。
                _output.WriteLine(
                    $"注意：{seededDomains.Count} 张五行域掉落表在本次测试里由生产自愈函数补回（域 {string.Join(",", seededDomains)}），" +
                    "说明启动装配未持久化到激活回收之后。");
            }

            // 再跑一遍生产自愈函数，这次针对**全部五个域**：它们此刻都已有数据，
            // 所以正确结果是一张都不写。这一条在真实 Grain 上钉住"缺则补、有不覆盖"的后半句
            // （覆盖掉库里真值的那种自愈，比不自愈更危险，而且同样没有报错）。
            var allTables = Horizon.Orleans.Silo.Startup.WuxingLootTables.Build(ParseSeedItemRows());
            var idempotent = await Horizon.Orleans.Silo.Startup.WuxingLootTableEnsure.EnsureAsync(
                allTables,
                domain => client.GetGrain<ILootTableGrain>(domain),
                onlyIfAbsent: true);
            Assert.Empty(idempotent.Seeded);
            Assert.Empty(idempotent.Failed);
            // 装配出来的每一张表（日常 1..5 与 BOSS 100..105）此刻都已有数据，一张都不许被重写
            Assert.Equal(allTables.Keys.OrderBy(d => d).ToArray(), idempotent.Skipped.OrderBy(d => d).ToArray());
            Assert.Contains(1, idempotent.Skipped);
            Assert.True(idempotent.Skipped.Count >= 5);

            Assert.True(await provider.WarmupAsync(templates.Select(t => t.TemplateId)),
                "五行域掉落表应能从真实 LootTableGrain 预热");
            Assert.Equal(5, provider.CachedTableCount);

            var booksSeenPerDomain = new Dictionary<int, HashSet<int>>();
            foreach (var row in templates)
            {
                var domain = row.WuxingDomain;
                Assert.True(domain >= 1 && domain <= 5, $"模板 {row.TemplateId} 未定域");
                Assert.Equal(domain, mapping.ResolveLootTableId(row.TemplateId));

                var books = new HashSet<int>();
                for (var tick = 1L; tick <= 400L; tick++)
                {
                    // 换掷骰输入（角色/tick）以覆盖概率条目；掷骰本身在 DS 本地，不产生 RPC。
                    foreach (var drop in provider.Roll(row.TemplateId, killerCharacterId: (ulong)(7000 + tick % 50), serverTick: tick))
                    {
                        Assert.True(drop.Count >= 1, "掉落条目至少 1 件");

                        var bookDomain = DomainOfBookItem(drop.ItemId);
                        if (bookDomain > 0)
                        {
                            // 这一条是本测试的主断言：杀掉某域的怪，只能掉出**该域**的书。
                            // "金域信物从火域怪身上掉"没有任何报错，只能靠这种断言显形。
                            Assert.True(domain == bookDomain,
                                $"模板 {row.TemplateId}（域 {domain}）掉出了域 {bookDomain} 的书 {drop.ItemId}");
                            books.Add(drop.ItemId);
                        }

                        if (drop.ItemId >= 910011 && drop.ItemId <= 910015)
                        {
                            Assert.True(910010 + domain == drop.ItemId,
                                $"模板 {row.TemplateId}（域 {domain}）掉出了别域的信物 {drop.ItemId}");
                        }
                    }
                }

                booksSeenPerDomain[domain] = books;
                Assert.True(books.Count > 0, $"域 {domain} 在 400 次击杀里一本初级书都没掉出，说明该表掷不中");
                // §14.2 的第一条产出通路：白阶 1 境那一本必须真能刷出来（而不是"表里有、掉不出"）
                Assert.Contains(800000 + domain * 10000 + 101, books);
            }

            // 五个域互不串门：同一本书不该出现在两个域的产出里。
            var union = booksSeenPerDomain.Values.SelectMany(s => s).ToList();
            Assert.Equal(union.Count, union.Distinct().Count());
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ---------------- 种子反查（仅用于"缺则补填"，不覆盖集群已有数据） ----------------

    /// <summary>
    /// 从随仓种子 011 反查物品行，交给 Silo 装配用的同一个纯函数 <c>WuxingLootTables.Build</c>。
    /// 与数据库读回的差异本身就是信息：若两边分叉，本测试会用种子补上，
    /// 而 Silo 启动装配用的是库里那份——所以只在"表缺失"时才写。
    /// </summary>
    internal static List<Horizon.Model.GameModel.ItemTemplateEntity> ParseSeedItemRows()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Horizon.sln"))) dir = dir.Parent;
        Assert.True(dir != null, "找不到仓库根（Horizon.sln）");
        var sql = System.IO.File.ReadAllText(
            System.IO.Path.Combine(dir!.FullName, "scripts", "sql", "011_seed_wuxing_skill_tree.sql"));

        var rows = new List<Horizon.Model.GameModel.ItemTemplateEntity>();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
            sql, @"INSERT INTO Game_HunduShijie_ItemTemplate \(([^)]*)\) VALUES \(([^;]*)\);"))
        {
            var cols = m.Groups[1].Value.Split(',').Select(c => c.Trim()).ToList();
            var vals = SplitVals(m.Groups[2].Value);
            if (!cols.Contains("item_id") || !cols.Contains("source_detail")) continue;

            rows.Add(new Horizon.Model.GameModel.ItemTemplateEntity
            {
                Id = int.Parse(vals[cols.IndexOf("item_id")]),
                ItemType = int.Parse(vals[cols.IndexOf("item_type")]),
                DropRate = decimal.Parse(vals[cols.IndexOf("drop_rate")], System.Globalization.CultureInfo.InvariantCulture),
                Rarity = byte.Parse(vals[cols.IndexOf("rarity")]),
                SourceType = cols.Contains("source_type") ? int.Parse(vals[cols.IndexOf("source_type")]) : 0,
                SourceDetail = Unquote(vals[cols.IndexOf("source_detail")]),
                ItemName = cols.Contains("item_name") ? Unquote(vals[cols.IndexOf("item_name")]) : string.Empty,
            });
        }

        Assert.True(rows.Count >= 60, $"种子里应能反查到足够多的物品行，实际 {rows.Count}（列序或语句形状变了）");
        return rows;
    }

    /// <summary>按引号与括号深度切 VALUES，避免把 N'金·青锋三式·启锋' 里的逗号当分隔符。</summary>
    internal static List<string> SplitVals(string raw)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var depth = 0;
        var inString = false;

        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (c == '\'')
            {
                if (inString && i + 1 < raw.Length && raw[i + 1] == '\'')
                {
                    current.Append("''");
                    i++;
                    continue;
                }
                inString = !inString;
            }

            if (!inString)
            {
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ',' && depth == 0)
                {
                    parts.Add(current.ToString().Trim());
                    current.Clear();
                    continue;
                }
            }

            current.Append(c);
        }

        if (current.Length > 0) parts.Add(current.ToString().Trim());
        return parts;
    }

    internal static string Unquote(string literal)
    {
        if (literal.StartsWith("N'", StringComparison.OrdinalIgnoreCase) && literal.EndsWith("'"))
            literal = literal.Substring(2, literal.Length - 3);
        return literal.Replace("''", "'");
    }
}
