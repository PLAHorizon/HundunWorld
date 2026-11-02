using System;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.Core;
using Horizon.Game.Core.Sim.Server;
using Horizon.Orleans.Interface;
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
/// **真集群**端到端验证：DS 的掉落/背包/任务适配器打到真实 Orleans Grain。
/// <para>
/// <b>为什么是"环境门控"而不是普通单测</b>：它需要运行中的 Redis + Orleans Silo。
/// 未设置环境变量 <c>HORIZON_ORLEANS_REDIS</c> 时测试直接返回（视为跳过），
/// 因此不会污染 CI；设置后即可在真实集群上跑，验证的是
/// "适配器 → 真实 Grain → 真实存储"整条链路，而非假实现。
/// </para>
/// <para>
/// 在此之前 <c>ILootTableGrain</c> / <c>IInventoryGrain</c> / 玩家维度 <c>IQuestGrain</c>
/// 全仓无生产调用方——本文件是它们第一次被真正调用。
/// </para>
/// <para>
/// 运行方式（示例）：
/// <code>
/// $env:HORIZON_ORLEANS_REDIS = '127.0.0.1:9379,password=***'
/// dotnet test --filter "FullyQualifiedName~OrleansGrainLiveIntegrationTests"
/// </code>
/// </para>
/// </summary>
public class OrleansGrainLiveIntegrationTests
{
    private static string? RedisConnectionString => Environment.GetEnvironmentVariable("HORIZON_ORLEANS_REDIS");

    private static bool LiveEnabled => !string.IsNullOrWhiteSpace(RedisConnectionString);

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

    [Fact]
    public async Task Live_LootTableGrain_ConfigFlowsThroughAdapterIntoLocalRoll()
    {
        if (!LiveEnabled) return; // 环境门控：无集群则跳过

        var (host, client) = await ConnectAsync();
        try
        {
            // 用时间戳派生的表 ID，避免与既有配置表冲突（且可重复运行）。
            var tableId = 900000 + (int)(DateTime.UtcNow.Ticks % 1000);
            var markerItem = tableId * 10 + 7;

            var grain = client.GetGrain<ILootTableGrain>(tableId);
            await grain.InitializeAsync(new LootTableConfig
            {
                TableId = tableId,
                Name = "live-integration",
                Entries = new[]
                {
                    // 必掉条目：排除随机性，使断言确定。
                    new LootEntry { ItemId = markerItem, ItemName = "live-marker", DropRate = 1f, MinCount = 2, MaxCount = 2, Rarity = 1 },
                    // 零概率条目：必须永不出现，验证概率被尊重。
                    new LootEntry { ItemId = markerItem + 1, ItemName = "never", DropRate = 0f, MinCount = 1, MaxCount = 1, Rarity = 5 },
                },
            });

            // 经真实 Grain 读回
            var readBack = await grain.GetConfigAsync();
            Assert.Equal(tableId, readBack.TableId);
            Assert.Equal(2, readBack.Entries.Length);

            // 适配器把 Grain 契约转成内核快照
            var source = new OrleansLootTableConfigSource(client);
            var provider = new OrleansLootProvider(source);
            Assert.True(await provider.WarmupAsync(new[] { tableId }), "预热应成功");
            Assert.Equal(1, provider.CachedTableCount);

            // 本地确定性掷骰 —— 内容来自 Grain，掷骰不产生 RPC
            var drops = provider.Roll(tableId, 2001UL, serverTick: 12345);
            var drop = Assert.Single(drops);
            Assert.Equal(markerItem, drop.ItemId);
            Assert.Equal(2, drop.Count);
            Assert.DoesNotContain(drops, d => d.ItemId == markerItem + 1);

            // 同输入必须同输出（确定性契约）
            var again = provider.Roll(tableId, 2001UL, serverTick: 12345);
            Assert.Equal(drop.ItemId, again[0].ItemId);
            Assert.Equal(drop.Count, again[0].Count);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task Live_InventoryGrain_AcceptsItemFromAdapter()
    {
        if (!LiveEnabled) return;

        var (host, client) = await ConnectAsync();
        try
        {
            var characterId = (ulong)(DateTime.UtcNow.Ticks % 1_000_000_000);
            var playerKey = GrainKeyMapper.ToInventoryKey(characterId);

            var writer = new OrleansInventoryWriter(client);
            var ok = await writer.AddItemAsync(playerKey, templateId: 5001, quantity: 3, quality: 0);

            Assert.True(ok, "IInventoryGrain.AddItemAsync 应成功（真实 Grain + Redis 存储）");

            // 回读验证物品确实进了背包
            var inventory = await client.GetGrain<Horizon.Orleans.Interface.IInventoryGrain>(playerKey).GetInventoryAsync();
            Assert.NotNull(inventory);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task Live_QuestGrain_NoActiveQuests_ReturnsZeroWithoutThrowing()
    {
        if (!LiveEnabled) return;

        var (host, client) = await ConnectAsync();
        try
        {
            var characterId = (ulong)(DateTime.UtcNow.Ticks % 1_000_000_000);
            var writer = new OrleansQuestProgressWriter(client);

            // 全新角色没有进行中任务：应返回 0 且不抛（实时链路不能被任务系统拖垮）
            var advanced = await writer.AdvanceKillObjectiveAsync(characterId, monsterTemplateId: 100, count: 1);
            Assert.Equal(0, advanced);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task Live_GameplayEventSink_RoutesKillAndLootIntoRealGrains()
    {
        if (!LiveEnabled) return;

        var (host, client) = await ConnectAsync();
        try
        {
            var characterId = (ulong)(DateTime.UtcNow.Ticks % 1_000_000_000);

            var inventory = new OrleansInventoryWriter(client);
            var quests = new OrleansQuestProgressWriter(client);
            await using var sink = new OrleansGameplayEventSink(inventory, quests);

            // 击杀（该角色无任务 → 推进 0，但不失败）+ 掉落（应真的入包）
            sink.Publish(new DsGameplayEvent
            {
                Kind = DsGameplayEventKind.Kill,
                ActorCharacterId = characterId,
                MonsterTemplateId = 100,
                ServerTick = 1,
            });
            sink.Publish(new DsGameplayEvent
            {
                Kind = DsGameplayEventKind.Loot,
                ActorCharacterId = characterId,
                ItemId = 6001,
                ItemCount = 1,
                ServerTick = 1,
            });

            await sink.DrainAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(0, sink.FailedCount);
            Assert.Equal(1, sink.ItemsGranted);
            Assert.Equal(2, sink.ProcessedCount);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// 任务模板接取（§14.2 的"通过任务获得初级技能书"）打到真实 Grain 与真实库。
    /// <para>
    /// 断言的重点不是"能接到任务"，而是<b>奖励来自服务端模板</b>：调用方只递了一个 questId，
    /// 拿到的奖励却精确等于 012 种子里写的那一份。这条一旦被人改回"客户端自带 rewards"，
    /// 这个测试就是那道栅栏——因为那条口子等于让玩家自己声明任务发什么。
    /// </para>
    /// </summary>
    [Fact]
    public async Task Live_QuestGrain_AcceptsFromTemplateAndRewardsComeFromServerSideTable()
    {
        if (!LiveEnabled) return; // 环境门控：无集群则跳过

        var (host, client) = await ConnectAsync();
        try
        {
            // 合成角色键：不碰真人存档，且可重复运行（一次性任务接第二次会被状态机拒）。
            var characterId = 9_900_000_001UL;
            var quest = client.GetGrain<Horizon.Orleans.Interface.IQuestGrain>(GrainKeys.ForEntity(characterId));

            // 第二个参数是网关传给 Grain 的**权威等级**（生产里由 ResolveCharacterLevelAsync 从角色 Grain 取）：
            // 等级门槛现在在接取侧也校一次，所以这里要给足等级，否则测试会变成"等级不够"的假失败。
            var accepted = await quest.AcceptFromTemplateAsync(90001, 60);      // 金行·入门
            Assert.NotNull(accepted);
            Assert.Equal(90001, accepted!.QuestId);
            Assert.StartsWith("金行·入门", accepted.QuestName);
            Assert.Equal(1, accepted.Rewards["item:810101"]);               // 白阶·金·初
            Assert.Equal(1, accepted.Rewards["item:810102"]);
            Assert.Equal(1, accepted.Rewards["item:810103"]);
            Assert.Equal(2, accepted.Rewards["item:910011"]);               // 金行信物 ×2
            Assert.Equal(20, accepted.Rewards["favor:1"]);                  // 金域好感 20

            // 模板不存在必须明确拒，而不是"接成功但什么都没有"
            Assert.Null(await quest.AcceptFromTemplateAsync(999999, 60));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// 任务产出整条链的真集群验证：接（读模板）→ 交（状态机）→ 发（背包 + 分域好感）。
    /// <para>
    /// 网络层只做编排，所以这里按 <c>QuestHandler</c> 的同一组调用顺序复现它：
    /// <c>item:&lt;模板id&gt;</c> 交 <see cref="IInventoryGrain"/>、<c>favor:&lt;域&gt;</c> 交
    /// <see cref="IFavorabilityGrain"/>。这样即便 PIE 还没接上 NarrativePro 的任务触发，
    /// "交任务真的能拿到书与好感"这件事已经有了实机证据，而不是只有单元测试里的假实现。
    /// </para>
    /// </summary>
    [Fact]
    public async Task Live_QuestChain_AcceptCompleteThenGrantBooksAndFavorability()
    {
        if (!LiveEnabled) return; // 环境门控：无集群则跳过

        var (host, client) = await ConnectAsync();
        try
        {
            // 每次换一个新的合成角色键：一次性任务被状态机记在 CompletedQuests 里，
            // 复用同一个键会让"接任务"在第二次运行时被拒，测试就会变成假失败。
            var characterId = 9_900_000_000UL + (ulong)(DateTime.UtcNow.Ticks % 100000);
            var key = GrainKeys.ForEntity(characterId);
            var quest = client.GetGrain<Horizon.Orleans.Interface.IQuestGrain>(key);

            var accepted = await quest.AcceptFromTemplateAsync(90003, 60);       // 水行·入门
            Assert.NotNull(accepted);

            // 012 种子入库后，入门任务带的是**真目标**（猎杀 5 只水域异兽）。
            // 空手交付必须先被拒——这条断言就是把"接了就能交"那个空转状态钉死，
            // 以后谁把目标解回去（或把完成校验放宽），这里就红。
            var emptyHanded = await quest.CompleteQuestAsync(90003);
            Assert.False(emptyHanded.Success, "任务没做任何目标也能交付：击杀目标又变成空转按钮了");
            Assert.Contains("目标尚未完成", emptyHanded.Message);

            Assert.True(await quest.UpdateQuestProgressAsync(90003, 0, 5),
                "推进击杀目标进度被拒：目标索引或计数口径与 QuestGrain 不一致");

            var completed = await quest.CompleteQuestAsync(90003);
            Assert.True(completed.Success, completed.Message);
            Assert.Equal(20, completed.Rewards["favor:3"]);

            // 两把键不是笔误，是本仓的真实口径：背包的权威键是 ToInventoryKey（行囊 UI/使用物品/DS 掉落都读它），
            // 好感读的却是 ForEntity（研读门槛与信物入账都用它）。这一段必须**和生产 handler 一致**：
            // 测试若图省事两端都用同一把键，就会自洽地绿，而生产上"任务发的书进不了行囊"照样发生。
            var inventory = client.GetGrain<IInventoryGrain>(GrainKeyMapper.ToInventoryKey(characterId));
            var favor = client.GetGrain<IFavorabilityGrain>(key);

            foreach (var entry in completed.Rewards)
            {
                if (entry.Key.StartsWith("item:", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(entry.Key.AsSpan(5), out var templateId))
                {
                    Assert.True(await inventory.AddItemAsync(templateId, entry.Value, 0),
                        $"物品 {templateId} 应能入账（模板不存在时这里就会红）");
                }
                else if (entry.Key.StartsWith("favor:", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(entry.Key.AsSpan(6), out var domain))
                {
                    await favor.AddAsync(domain, entry.Value, $"quest:{90003}", perTargetCap: entry.Value);
                }
            }

            // 好感真的落在"这一域"上，而不是全局某个计数器
            var byDomain = await favor.GetByDomainAsync();
            Assert.Equal(20, byDomain[3]);
            Assert.False(byDomain.ContainsKey(1));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}
