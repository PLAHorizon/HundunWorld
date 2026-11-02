using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;
using Horizon.Game.Sim.Npcs;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 端到端（服务器侧）：**随包发布的那份导出文件** `HundunWorld/Data/Npcs/db_content.json`
/// 装进 DS 之后，世界里真的有 5 个 NPC（4 友好 + 1 敌对），名字/头衔/等级/姿态都对得上。
/// <para>
/// 这是"库 → 导出 → 世界"这条链上唯一一份**真实产物**的验收：前面所有用例都是用内存快照搭的，
/// 它们证明不了"导出的文件本身能用"，也证明不了"运营改完库、重新导出之后世界会变"。
/// </para>
/// </summary>
[Collection("monster-content")]
public class NpcShippedContentTests
{
    private readonly ITestOutputHelper _log;

    public NpcShippedContentTests(ITestOutputHelper log) => _log = log;

    private static DsWorldSessionOptions Options() => new()
    {
        ShardId = 0,
        TicksPerSecond = 60,
        SnapshotIntervalTicks = 1,
        EnableAoiFilter = false,
        MaxSessions = 10,
        MaxEntities = 200,
    };

    private static string ShippedContentPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "HundunWorld", "Data", "Npcs", "db_content.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("找不到随包导出的 NPC 内容：HundunWorld/Data/Npcs/db_content.json");
    }

    [Fact]
    public void ShippedExport_SpawnsFiveNpcsWithRealIdentity()
    {
        var path = ShippedContentPath();
        var receipt = NpcContentLoader.LoadFile(path, out var error);
        Assert.NotNull(receipt);
        _log.WriteLine("文件=" + path);
        _log.WriteLine(receipt!.Describe());

        Assert.Equal(0, receipt.ProblemCount);
        // 6 只模板、6 个刷点：400002 已补刷点（先发布后摆点，摆完会随 NPC 一起 publish）。
        Assert.Equal(6, receipt.Templates);
        Assert.Equal(6, receipt.SpawnPoints);
        Assert.Equal(1, receipt.Routes);
        Assert.Equal(1, receipt.LootBindings);

        try
        {
            var world = new DsWorldSession(Options());
            Assert.True(world.AddPlayer(1001, 1001, -20f, 0f, 0f, out var reason), reason);

            var spawned = world.ApplyNpcContentSpawns();
            _log.WriteLine($"spawned={spawned.Spawned} hostile={spawned.SkippedHostile} shard={spawned.SkippedShard} unknown={spawned.SkippedUnknown}");
            Assert.Equal(6, spawned.Spawned);
            Assert.Equal(1, spawned.SkippedHostile);
            Assert.Equal(0, spawned.SkippedShard);
            Assert.Equal(0, spawned.SkippedUnknown);

            var byTemplate = new Dictionary<ulong, NetworkIdentityAuthComponent>();
            var stateOf = new Dictionary<ulong, EntityStateAuthComponent>();

            for (var tick = 0; tick < 10; tick++)
            {
                var result = world.Tick();
                var deltas = result.WorldSnapshot?.Deltas;
                if (deltas == null) continue;
                foreach (var delta in deltas)
                {
                    if (delta.Identity is { } identity) byTemplate[delta.EntityId] = identity;
                    if (delta.State is { } state) stateOf[delta.EntityId] = state;
                }
            }

            // 5 个模板各一只：1/2/3/400001 友好（Npc），500001 敌对（Monster）。
            var seen = new HashSet<int>();
            foreach (var pair in byTemplate)
            {
                var id = pair.Value;
                if (id.TemplateId is not (1 or 2 or 3 or 400001 or 500001)) continue;
                seen.Add(id.TemplateId);

                if (id.TemplateId == 500001)
                {
                    Assert.Equal((int)NetworkEntityType.Monster, id.EntityType);
                    Assert.Equal("山贼头目", id.Nickname);
                    Assert.Equal("山贼", id.Title);
                    // 种子写的 32 级（模板），没配刷点覆盖。
                    Assert.True(stateOf.TryGetValue(pair.Key, out var state));
                    Assert.Equal(32, state.Level);
                }
                else
                {
                    Assert.Equal((int)NetworkEntityType.Npc, id.EntityType);
                    // 服务位随身份下发：铁匠 127（补了任务发布/交付位）/ 守门人 7 / 行商 9 / 货郎 9 / 医者 127。
                    var expectedServices = id.TemplateId switch
                    {
                        1 => 127,
                        2 => 7,
                        3 => 9,
                        400001 => 9,
                        400002 => 127,
                        _ => -1,
                    };
                    Assert.True(stateOf.TryGetValue(pair.Key, out var state));
                    Assert.Equal(expectedServices, state.NpcServiceFlags);
                    _log.WriteLine($"npc {id.TemplateId} name={id.Nickname} title={id.Title} svc={state.NpcServiceFlags} posture={state.NpcStandState}");
                }
            }

            Assert.Equal(5, seen.Count);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    /// <summary>
    /// 随包货架的"商品清单"本身：三家商人各 4 格，买价/收购价/库存都与入库数据一致。
    /// 只断言总条数不够——价格或槽位漂了，玩家看到的仍是另一家店。
    /// </summary>
    [Fact]
    public void ShippedExport_VendorShelvesCarryTheNewGoods()
    {
        var receipt = NpcContentLoader.LoadFile(ShippedContentPath(), out var error);
        Assert.NotNull(receipt);
        Assert.Equal(0, receipt!.ProblemCount);
        Assert.Equal(12, receipt.VendorRows);

        try
        {
            var expected = new Dictionary<int, (int Slot, int Item, long Buy, long Sell, int Stock, int Restock)[]>
            {
                [1] = new[]
                {
                    (0, 700006, 1200L, 300L, 0, 0),
                    (1, 700011, 2400L, 600L, 0, 0),
                    (2, 700032, 600L, 200L, 2, 7200),
                    (3, 910004, 80L, 30L, 0, 0),
                },
                [3] = new[]
                {
                    (0, 91001, 500L, 100L, 3, 3600),
                    (1, 91002, 900L, 180L, 0, 0),
                    (2, 910001, 40L, 15L, 0, 0),
                    (3, 910006, 180L, 60L, 3, 3600),
                },
                [400001] = new[]
                {
                    (0, 700002, 150L, 30L, 0, 0),
                    (1, 50001, 20L, 8L, 0, 0),
                    (2, 50002, 24L, 10L, 0, 0),
                    (3, 700031, 60L, 20L, 0, 0),
                },
            };

            foreach (var (npcId, rows) in expected)
            {
                var actual = NpcContent.VendorRows(npcId);
                Assert.Equal(rows.Length, actual.Count);
                for (var i = 0; i < rows.Length; i++)
                {
                    Assert.Equal(rows[i].Slot, actual[i].Slot);
                    Assert.Equal(rows[i].Item, actual[i].ItemId);
                    Assert.Equal(rows[i].Buy, actual[i].BuyPriceOverride);
                    Assert.Equal(rows[i].Sell, actual[i].SellPriceOverride);
                    Assert.Equal(rows[i].Stock, actual[i].StockMax);
                    Assert.Equal(rows[i].Restock, actual[i].RestockSeconds);
                }
            }

            // 服务端能卖不等于客户端能画：商品必须在客户端物品表里，且标了可交易。
            var clientPath = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(ShippedContentPath())!, "..", "Items", "client_items.json"));
            Assert.True(File.Exists(clientPath), "客户端物品表缺失：" + clientPath);
            using var clientDoc = JsonDocument.Parse(File.ReadAllText(clientPath));
            var catalog = new Dictionary<int, bool>();
            foreach (var item in clientDoc.RootElement.GetProperty("items").EnumerateArray())
                catalog[item.GetProperty("templateId").GetInt32()] = item.GetProperty("canTrade").GetBoolean();

            foreach (var (npcId, _) in expected)
            {
                foreach (var row in NpcContent.VendorRows(npcId))
                {
                    Assert.True(catalog.ContainsKey(row.ItemId), $"物品 {row.ItemId} 不在客户端物品表");
                    Assert.True(catalog[row.ItemId], $"物品 {row.ItemId} 在客户端标为不可交易");
                }
            }
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }
}
