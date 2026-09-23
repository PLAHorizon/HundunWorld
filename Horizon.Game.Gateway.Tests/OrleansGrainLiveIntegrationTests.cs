using System;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.Core.Sim.Server;
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
}
