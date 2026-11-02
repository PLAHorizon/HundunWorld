using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Sim;
using Horizon.Game.Sim.Npcs;
using Horizon.NpcCli;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// NPC 编辑器契约测试：目录兼容、草稿契约、装配校验、JSON 绑定。
/// <para>
/// 四条"必须存在"的理由：
/// ① <b>未装配 == 现状</b>：`NpcTemplateCatalog` 改成"先查装配内容"之后，1/2/3 号必须仍然逐字可查——
///    它们是存量任务的发布者，改坏了不会编译失败，只会让旧任务指向空气。
/// ② <b>草稿契约不能漂</b>：编辑器的序列化与 CLI 的反序列化共用同一族 DTO，一旦有人把属性改成公开字段，
///    STJ 会"反序列化成功但全是初值"（记忆 [STJ 默认不绑公开字段]），这条靠往返断言兜住。
/// ③ <b>校验要能真的报出问题</b>：只断言"好草稿通过"时，一个永远返回 0 问题的校验器也会全绿；
///    坏样本必须逐个码对得上。
/// ④ <b>装配与 CLI 校验同一套</b>：两者都走 <c>NpcContentLoader.Compile</c>，这里锁住它的计数口径。
/// </para>
/// </summary>
public class NpcEditorContractTests
{
    private readonly ITestOutputHelper _log;

    public NpcEditorContractTests(ITestOutputHelper log) => _log = log;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Fixture(string name) => Path.Combine(RepoRoot(), "HundunWorld", "Data", "Npcs", name);

    // ────────────────────────── ① 目录：未装配 == 现状 ──────────────────────────

    [Fact]
    public void Catalog_WithoutInstall_IsByteIdenticalToHardcodedTable()
    {
        NpcContent.ResetToHardcoded();
        try
        {
            // 三行存量：名字/头衔/模型键/五行域逐字（改动这里等于改存档语义与好感记账）。
            var expected = new (int Id, string Name, string Title, string Key, int Domain)[]
            {
                (1, "铸剑师", "铁匠", "npc_smith", 1),
                (2, "守门人", "城门", "npc_gatekeeper", 5),
                (3, "游方商队", "行商", "npc_merchant", 2),
            };

            foreach (var e in expected)
            {
                Assert.True(NpcTemplateCatalog.TryGetRegistered(e.Id, out var info), $"未登记 {e.Id}");
                Assert.Equal(e.Name, info.Name);
                Assert.Equal(e.Title, info.Title);
                Assert.Equal(e.Key, info.ModelKey);
                Assert.Equal(e.Domain, info.WuxingDomain);
                Assert.Equal(e.Domain, NpcTemplateCatalog.DomainOf(e.Id));
            }

            // 未登记：授权返回 false，显示走确定性兜底（绝不用于放行）。
            Assert.False(NpcTemplateCatalog.TryGetRegistered(9999, out _));
            Assert.Equal(0, NpcTemplateCatalog.DomainOf(9999));
            var fallback = NpcTemplateCatalog.Get(9999);
            Assert.Equal("路人#9999", fallback.Name);
            Assert.Equal("npc_9999", fallback.ModelKey);

            Assert.Equal(3, NpcTemplateCatalog.RegisteredTemplates().Length);
            Assert.True(NpcTemplateCatalog.TryFindByName("npc_smith", out var byKey));
            Assert.Equal(1, byKey.TemplateId);
            Assert.True(NpcTemplateCatalog.TryFindByName("铸剑师", out var byName));
            Assert.Equal(1, byName.TemplateId);
            Assert.False(NpcTemplateCatalog.TryFindByName("不存在的人", out _));
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void Catalog_AfterInstall_ReadsInstalledAndKeepsLegacyResolvable()
    {
        NpcContent.ResetToHardcoded();
        try
        {
            var bundle = new NpcContentBundle { ContentRevision = 7 };
            bundle.ByNpcId[400001] = new NpcRuntimeConfig
            {
                NpcId = 400001, Name = "杂货商", Title = "货郎", ModelKey = "npc_peddler", WuxingDomain = 2,
                Stance = NpcStance.Friendly, ServiceFlags = (uint)NpcServiceBits.Vendor, Tier = NpcAiTier.Wanderer,
                LevelBase = 30, AttackableOverride = -1,
            };

            Assert.Equal(0, NpcContent.Install(bundle));

            // 装配值优先
            Assert.True(NpcTemplateCatalog.TryGetRegistered(400001, out var info));
            Assert.Equal("杂货商", info.Name);
            Assert.Equal(2, NpcTemplateCatalog.DomainOf(400001));

            // 装配包里没有 1/2/3，但**存量三行必须仍然查得到**（否则旧任务发布者指向空气）
            Assert.True(NpcTemplateCatalog.TryGetRegistered(1, out var legacy));
            Assert.Equal("铸剑师", legacy.Name);

            // 并集 + 稳定排序
            var all = NpcTemplateCatalog.RegisteredTemplates();
            Assert.Equal(4, all.Length);
            Assert.Equal(new[] { 1, 2, 3, 400001 }, all.Select(x => x.TemplateId).ToArray());
            Assert.Equal(7, NpcContent.Revision);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    // ────────────────────────── ② 草稿契约与 ③ 校验 ──────────────────────────

    [Fact]
    public void Draft_GoodFixture_CompilesCleanWithExpectedCounts()
    {
        var json = File.ReadAllText(Fixture("dev_draft_peddler.json"));
        var draft = NpcDraft.FromJson(json);
        Assert.NotNull(draft);

        var (bundle, receipt) = NpcContentLoader.Compile(draft!.ToSnapshot());

        Assert.Equal(0, receipt.ProblemCount);
        Assert.Equal(1, receipt.Templates);
        Assert.Equal(1, receipt.SpawnPoints);
        Assert.Equal(1, receipt.Routes);
        Assert.Equal(2, receipt.VendorRows);
        Assert.Equal(2, receipt.TrainerRows);
        Assert.Equal(1, receipt.Recipes);
        Assert.Equal(4, receipt.GossipRows);
        Assert.Equal(2, receipt.QuestLinks);
        Assert.Equal(2, receipt.ScheduleRows);

        // 展开后的运行期配置：档位、位图、立场都要真的落到 bundle 里（不是只回执好看）
        Assert.True(bundle.ByNpcId.TryGetValue(400001, out var cfg));
        Assert.Equal(NpcStance.Friendly, cfg!.Stance);
        Assert.Equal((uint)NpcServiceBits.Vendor, cfg.ServiceFlags & (uint)NpcServiceBits.Vendor);
        Assert.True(cfg.Flags.Has(NpcAiBits.Wanders));
        Assert.True(cfg.Flags.Has(NpcAiBits.FacePlayer));
    }

    [Fact]
    public void Draft_BrokenFixture_ReportsEveryDistinctCode()
    {
        var json = File.ReadAllText(Fixture("dev_draft_broken.json"));
        var draft = NpcDraft.FromJson(json);
        Assert.NotNull(draft);

        var (_, receipt) = NpcContentLoader.Compile(draft!.ToSnapshot());

        var expected = new[]
        {
            "template_bad_reaction:400002:9",
            "ai_tier_unknown:400002:99",
            "vendor_stock_never_restocks:400002:91001",
            "trainer_bad_row:400002:0",
            "recipe_bad_product:900002",
            "recipe_no_materials:900002",
            "recipe_bad_curve:900002",
            "gossip_action_bit_off:400002:1",
            "questlink_bad_quest:400002:0",
            "schedule_gap:400002:900s",
        };

        Assert.Equal(expected.Length, receipt.ProblemCount);
        foreach (var code in expected)
        {
            Assert.Contains(code, receipt.Problems);
        }

        _log.WriteLine(receipt.Describe());
    }

    [Fact]
    public void Draft_EmptyOrMalformedInput_FailsWithNamedReason()
    {
        Assert.Null(NpcDraft.FromJson(""));
        Assert.Null(NpcDraft.FromJson("not json"));

        // 合法 JSON 但缺主体：反序列化成功、Template 为 null（这一条由调用方按 draft_template_missing 拒）。
        var empty = NpcDraft.FromJson("{}");
        Assert.NotNull(empty);
        Assert.Null(empty!.Template);
    }

    // ────────────────────────── ④ JSON 绑定（防"公开字段"退化） ──────────────────────────

    [Fact]
    public void Snapshot_JsonRoundTrip_PreservesEveryKindOfValue()
    {
        var snap = new NpcContentSnapshot { ContentRevision = 42 };
        snap.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 500001, Name = "山贼头目", Title = "山贼", ModelKey = "npc_bandit_chief",
            Scale = 1.1f, Level = 32, Reaction = 2, ServiceFlags = 0, AiTier = 7,
            AttackableOverride = -1, AlertRadiusMeters = 18, Rank = 1,
        });
        snap.VendorRows.Add(new NpcVendorRowDto { NpcId = 500001, Slot = 3, ItemId = 750001, BuyPriceOverride = 1200, StockMax = 2, RestockSeconds = 60 });
        snap.Problems.Add("sample_problem");

        var json = snap.ToJson();
        var back = NpcContentSnapshot.FromJson(json);

        Assert.NotNull(back);
        Assert.Equal(42, back!.ContentRevision);
        Assert.Single(back.Templates);
        Assert.Equal(500001, back.Templates[0].NpcId);
        Assert.Equal("山贼头目", back.Templates[0].Name);
        Assert.Equal(1.1f, back.Templates[0].Scale);
        Assert.Equal((byte)7, back.Templates[0].AiTier);
        Assert.Single(back.VendorRows);
        Assert.Equal(3, back.VendorRows[0].Slot);
        Assert.Equal(1200, back.VendorRows[0].BuyPriceOverride);
        Assert.Single(back.Problems);

        // 骆驼峰命名（编辑器按这个键读）。键名漂了不会报错，只会让编辑器读到 null。
        Assert.Contains("\"npcId\"", json, StringComparison.Ordinal);
        Assert.Contains("\"buyPriceOverride\"", json, StringComparison.Ordinal);
    }

    // ────────────────────────── ⑤ 对话菜单的 0 语义 ──────────────────────────
    [Fact]
    public void Gossip_ZeroLevelBoundsMeanUnlimitedAndHiddenCountsAreReported()
    {
        var open = new NpcGossipOption(0, 1, 0, NpcGossipAction.Talk, 0, 0, 0);
        Assert.True(NpcGossipCore.LevelVisible(in open, 1));
        Assert.True(NpcGossipCore.LevelVisible(in open, 300));

        var senior = new NpcGossipOption(1, 1, 0, NpcGossipAction.OpenVendor, 0, 30, 60);
        Assert.False(NpcGossipCore.LevelVisible(in senior, 29));
        Assert.True(NpcGossipCore.LevelVisible(in senior, 30));
        Assert.True(NpcGossipCore.LevelVisible(in senior, 60));
        Assert.False(NpcGossipCore.LevelVisible(in senior, 61));

        // 被等级挡掉的条数必须回出去（"这组一条都没有"与"全被挡掉"要能分辨）
        var group = NpcGossipCore.GroupFor(new[] { open, senior }, 1, 10, out var hidden);
        Assert.Single(group);
        Assert.Equal(1, hidden);

        // 动作 ↔ 服务位成对校验：没开 Vendor 位却挂"打开商店"就是坏数据
        Assert.False(NpcGossipCore.ActionServiceAvailable(in senior, 0));
        Assert.True(NpcGossipCore.ActionServiceAvailable(in senior, (uint)NpcServiceBits.Vendor));
        Assert.Equal(NpcServiceBits.None, NpcGossipCore.RequiredService(NpcGossipAction.Talk));
    }

    /// <summary>
    /// 草稿里的掉落绑定：归属必须被改写成**草稿主体的 NPC**，且装配回执数得出来。
    /// <para>不做归一的话，编辑器里复制一份草稿改个 npcId，掉落会静默挂在旧 NPC 上。</para>
    /// </summary>
    [Fact]
    public void Draft_LootBindingsAreOwnedByDraftNpc()
    {
        var draft = new NpcDraft
        {
            Template = new NpcTemplateSnapshot
            {
                NpcId = 500001, Name = "山贼头目", ModelKey = "npc_bandit_chief",
                Level = 32, Reaction = (byte)NpcStance.Hostile,
            },
        };

        draft.LootBindings.Add(new MonsterLootBindingSnapshotDto
        {
            // 故意写错 owner：草稿保存时必须被改写成本条的 npcId。
            MonsterTemplateId = 999,
            LootTableId = 1,
            RollChanceBps = 10000,
            MinCount = 1,
            MaxCount = 1,
            ConditionKind = 0,
            MoneyMin = 5,
            MoneyMax = 25,
            Entries =
            {
                new MonsterDropSnapshotDto { ItemId = 750003, ChanceBps = 6000, CountMin = 1, CountMax = 2, Seq = 0, ConditionKind = -1 },
            },
        });

        var snapshot = draft.ToSnapshot();
        Assert.Equal(500001, snapshot.LootBindings[0].MonsterTemplateId);

        var (bundle, receipt) = NpcContentLoader.Compile(snapshot);
        Assert.Equal(0, receipt.ProblemCount);
        Assert.Equal(1, receipt.LootBindings);
        Assert.True(bundle.LootBindings.ContainsKey(500001));
        Assert.False(bundle.LootBindings.ContainsKey(999));
    }

    /// <summary>
    /// 插件的草稿模型与 CLI 的快照 DTO 是两套源码（跨脚本域互相引用不到），
    /// 掉落这一族的字段名必须逐字一致，且插件序列化必须是 camelCase——
    /// 少一处或大小写策略变了，症状是"编辑器里配了掉落、保存后库里没有"，且两边都不报错。
    /// </summary>
    [Fact]
    public void PluginDraftModel_UsesSameLootKeysAsCli()
    {
        var pluginPath = Path.Combine(RepoRoot(), "HundunWorld", "Plugins", "NpcEditor",
            "Source", "NpcEditor", "NpcDraftModel.cs");
        // 掉落那一族 DTO 定义在怪物快照文件里（NPC 复用它）；NPC 快照只多一个引用列表。
        var cliDtoPath = Path.Combine(RepoRoot(), "Horizon.Game.Core", "Sim", "Server", "MonsterContentSnapshot.cs");
        var npcSnapshotPath = Path.Combine(RepoRoot(), "Horizon.Game.Core", "Sim", "Server", "NpcContentSnapshot.cs");

        Assert.True(File.Exists(pluginPath), "插件草稿模型不见了：" + pluginPath);
        Assert.True(File.Exists(cliDtoPath), "CLI 侧快照 DTO 不见了：" + cliDtoPath);

        var plugin = File.ReadAllText(pluginPath);
        var cli = File.ReadAllText(cliDtoPath) + File.ReadAllText(npcSnapshotPath);

        // 绑定级
        foreach (var key in new[]
                 {
                     "LootBindings", "MonsterTemplateId", "LootTableId", "RollChanceBps",
                     "MinCount", "MaxCount", "ConditionKind", "QuestId", "MoneyMin", "MoneyMax", "Entries",
                 })
        {
            Assert.Contains(key, plugin, StringComparison.Ordinal);
            Assert.Contains(key, cli, StringComparison.Ordinal);
        }

        // 条目级
        foreach (var key in new[]
                 {
                     "ItemId", "ChanceBps", "CountMin", "CountMax", "Seq", "GroupId",
                 })
        {
            Assert.Contains(key, plugin, StringComparison.Ordinal);
            Assert.Contains(key, cli, StringComparison.Ordinal);
        }

        // 线上键是 camelCase：策略变了就全对不上。
        Assert.Contains("JsonNamingPolicy.CamelCase", plugin, StringComparison.Ordinal);
    }

    // ────────────────────────── ⑥ 客户端立场表：两侧键名与取值 ──────────────────────────

    /// <summary>
    /// 表键名是**跨脚本域协议**：写侧（<c>NpcVisualsExport</c>）与读侧（Flax 的 <c>NpcStanceCatalog</c>）
    /// 互相引用不到，改名只能靠两侧同时改。这里按字面量逐键断言，漂一个就红。
    /// </summary>
    [Fact]
    public void NpcVisuals_BothSidesUseTheSameKeys()
    {
        var clientPath = Path.Combine(RepoRoot(), "HundunWorld", "Source", "Game", "Selection", "NpcStanceCatalog.cs");
        Assert.True(File.Exists(clientPath), "客户端立场目录不见了：" + clientPath);
        var clientSource = File.ReadAllText(clientPath);

        // 写侧声明的协议键：npcs / npcId / modelKey / stance / services（name/title 只为人 review，不进协议）
        // + 逐只指认的预制体 prefab（空串 = 没配）
        // + 训练师窗口展示行 trainerRows/skillId/kind/gold/itemId/itemCount/seq/label（配方行的展示名）。
        Assert.Equal(
            new[] { "npcs", "npcId", "modelKey", "stance", "services", "prefab",
                    "trainerRows", "skillId", "kind", "gold", "itemId", "itemCount", "seq", "label",
                    "gossipOptions", "arg", "minLevel", "maxLevel", "text",
                    "gossipGroupId", "group" },
            NpcVisualsExport.ClientKeys);

        foreach (var key in NpcVisualsExport.ClientKeys)
        {
            // 读侧必须真的按这个名字取值（TryGetProperty("key") / GetProperty）。少一个键就是静默空表。
            Assert.Contains("\"" + key + "\"", clientSource, StringComparison.Ordinal);
        }

        // 表文件路径也是协议的一部分。
        Assert.Contains("Data/Npcs/client_visuals.json", clientSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// 立场编号必须与内核枚举一一对应：客户端不引用 Sim 程序集，靠"导出口原样写出"对齐。
    /// 若有人在 <see cref="NpcStance"/> 中间插一项，这里会先红，而不是线上全员换色。
    /// </summary>
    [Fact]
    public void NpcVisuals_StanceNumbersMatchKernelEnum()
    {
        Assert.Equal(0, (int)NpcStance.Friendly);
        Assert.Equal(1, (int)NpcStance.Neutral);
        Assert.Equal(2, (int)NpcStance.Hostile);

        Assert.Equal(0, NpcVisualsExport.StanceValue(NpcStance.Friendly));
        Assert.Equal(1, NpcVisualsExport.StanceValue(NpcStance.Neutral));
        Assert.Equal(2, NpcVisualsExport.StanceValue(NpcStance.Hostile));
        Assert.Equal(0, NpcVisualsExport.StanceValue(NpcStance.Unknown)); // 坏数据按友好，不变成红名
    }

    /// <summary>
    /// 草稿必须收齐"这条 NPC 会走的所有路线"——刷点引用的**和作息档引用的**。
    /// <para>
    /// 只看刷点会漏掉"只在某个时段才走这条线"的路线：那样的 NPC 在编辑器里打开后路线是空的，
    /// 保存一次就把作息切路线的数据补丢了（而运行期表现是"到点不巡街"）。
    /// </para>
    /// </summary>
    [Fact]
    public void Draft_IncludesRouteReferencedOnlyBySchedule()
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
        snapshot.Templates.Add(new NpcTemplateSnapshot { NpcId = 400001, Name = "行商", Level = 30, Reaction = 0 });
        snapshot.SpawnPoints.Add(new NpcSpawnSnapshotDto { SpawnPointId = 1, NpcId = 400001, RouteId = 0 });
        snapshot.Routes.Add(new NpcRouteSnapshot
        {
            PathId = 7,
            Name = "只在白天走的线",
            Points = { new NpcRoutePointSnapshot { Seq = 0, X = 0, Y = 0 }, new NpcRoutePointSnapshot { Seq = 1, X = 5, Y = 0 } },
        });
        snapshot.ScheduleRows.Add(new NpcScheduleRowDto
        {
            NpcId = 400001, Seq = 0, FromSeconds = 0, ToSeconds = 600, RouteId = 7,
        });

        var draft = NpcDraft.FromSnapshotForNpc(snapshot, 400001);

        Assert.Single(draft.Routes);
        Assert.Equal(7, draft.Routes[0].PathId);
        Assert.Equal(2, draft.Routes[0].Points.Count);
    }

    /// <summary>
    /// 保存不带版本号 = 每个草稿都是 0，而库里是 1 —— "加载→保存"永远报版本冲突，
    /// 且回执像是别人先改了（实际谁都没动）。这条钉住"草稿必须带库里那份版本"。
    /// </summary>
    [Fact]
    public void DraftCarriesContentVersionFromStore()
    {
        var store = Path.Combine(RepoRoot(), "Horizon.NpcCli", "NpcStore.cs");
        var source = File.ReadAllText(store);
        Assert.Contains("draft.ContentVersion = GetContentVersion(npcId)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 发布态要蔓延到**路线**：刷点/作息引用的路线没发布，导出时整条路线缺失，
    /// 装配器随即报 spawn_unknown_route ——"发布了 NPC，世界里却没有他"，而每张表看起来都对。
    /// </summary>
    [Fact]
    public void PublishStateCascadesToReferencedRoutes()
    {
        var store = Path.Combine(RepoRoot(), "Horizon.NpcCli", "NpcStore.cs");
        var source = File.ReadAllText(store);
        Assert.Contains("publishedRoutes", source, StringComparison.Ordinal);
        Assert.Contains("NpcWaypointPaths.Where(x => routeIds.Contains(x.Id))", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 插件草稿模型必须带路线字段，且 camelCase 名称与 CLI 的 DTO 对得上——
    /// 插件序列化时漏掉 `routes` 等于"编辑器保存一次就把巡街线删掉"。
    /// </summary>
    [Fact]
    public void PluginDraftModel_RoutesRoundTripThroughCliDraft()
    {
        var pluginPath = Path.Combine(RepoRoot(), "HundunWorld", "Plugins", "NpcEditor",
            "Source", "NpcEditor", "NpcDraftModel.cs");
        var plugin = File.ReadAllText(pluginPath);
        foreach (var key in new[] { "Routes", "PathId", "Name", "Mode", "PauseAtPointSeconds", "UseNavMesh", "Points", "Seq", "PauseSeconds" })
            Assert.Contains(key, plugin, StringComparison.Ordinal);

        // 真按插件的 camelCase 形状喂给 CLI，看路线有没有被认出来。
        const string json = """
        {
          "contentVersion": 3,
          "template": { "npcId": 400001, "name": "货郎", "level": 30, "reaction": 0 },
          "spawnPoints": [ { "spawnPointId": 0, "npcId": 400001, "x": 8.0, "y": -2.0, "z": 0.0, "routeId": 7, "maxAlive": 1, "respawnSeconds": 45.0 } ],
          "routes": [ { "pathId": 7, "name": "巡街", "mode": 1, "pauseAtPointSeconds": 2.0, "useNavMesh": false,
                        "points": [ { "seq": 0, "x": 1.0, "y": 2.0, "z": 0.0, "pauseSeconds": 0.5 },
                                    { "seq": 1, "x": 3.0, "y": 4.0, "z": 0.0, "pauseSeconds": 0.0 } ] } ]
        }
        """;

        var draft = NpcDraft.FromJson(json);
        Assert.NotNull(draft);
        Assert.Single(draft!.Routes);
        Assert.Equal(7, draft.Routes[0].PathId);
        Assert.False(draft.Routes[0].UseNavMesh);
        Assert.Equal(2, draft.Routes[0].Points.Count);
        Assert.Equal(0.5f, draft.Routes[0].Points[0].PauseSeconds, 3);

        // 校验也得认：刷点引用的路线在草稿里 → 不应报 spawn_unknown_route。
        var (_, receipt) = NpcContentLoader.Compile(draft.ToSnapshot());
        Assert.DoesNotContain(receipt.Problems, p => p.StartsWith("spawn_unknown_route", StringComparison.Ordinal));
    }

    /// <summary>
    /// 刷点保存要**保住编号**：spawn_point_id 是 IDENTITY，删光重插会让每次保存都换号，
    /// 往返对账永远有一格对不上；号段/日志/对账表都按它指认"哪一点"。
    /// </summary>
    [Fact]
    public void SpawnPointSave_PreservesExplicitIds()
    {
        var store = Path.Combine(RepoRoot(), "Horizon.NpcCli", "NpcStore.cs");
        var source = File.ReadAllText(store);
        Assert.Contains("SET IDENTITY_INSERT [Game_HunduShijie_NpcSpawnPoint] ON", source, StringComparison.Ordinal);
        Assert.Contains("Id = spawnIds[si]", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 插件草稿模型必须带配方字段（此前整族缺失：编辑器保存一次就把配方内容丢掉），
    /// 且 camelCase 名称要能被 CLI 的草稿认出来。
    /// </summary>
    [Fact]
    public void PluginDraftModel_RecipesRoundTripThroughCliDraft()
    {
        var plugin = File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Plugins", "NpcEditor",
            "Source", "NpcEditor", "NpcDraftModel.cs"));
        foreach (var key in new[] { "Recipes", "RecipeId", "ProductItemId", "CostGold", "StationNpcId", "SkillReq", "Materials", "ItemId", "Count", "QualityGapCaps", "QualityChanceBps" })
            Assert.Contains(key, plugin, StringComparison.Ordinal);

        const string json = """
        {
          "contentVersion": 1,
          "template": { "npcId": 400001, "name": "铁匠", "level": 30, "reaction": 0 },
          "recipes": [ { "recipeId": 900001, "name": "锻造·测试", "productItemId": 91002, "costGold": 300,
                         "stationNpcId": 400001, "skillReq": 10,
                         "materials": [ { "itemId": 700011, "count": 2 } ],
                         "qualityGapCaps": [5, 10, 15, 60], "qualityChanceBps": [10000, 6000, 3000, 1000] } ]
        }
        """;

        var draft = NpcDraft.FromJson(json);
        Assert.NotNull(draft);
        Assert.Single(draft!.Recipes);
        Assert.Equal(91002, draft.Recipes[0].ProductItemId);
        Assert.Single(draft.Recipes[0].Materials);
        Assert.Equal(700011, draft.Recipes[0].Materials[0].ItemId);
        Assert.Equal(new[] { 10000, 6000, 3000, 1000 }, draft.Recipes[0].QualityChanceBps.ToArray());
    }

    /// <summary>
    /// AI 覆盖（强制开/强制关）必须从草稿一路走到内核：
    /// 强制关要能压过档位预设，同位既开又关必须报 `ai_force_conflict`（不是按某个顺序静默选一个）。
    /// </summary>
    [Fact]
    public void Draft_AiOverridesReachKernel_AndConflictsAreReported()
    {
        // 1) 强制打开 Static 档没有的"走路线"位：展开结果必须带上它。
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 400001, Name = "会走路线的站桩货郎", Level = 30, Reaction = 0,
            AiTier = (byte)NpcAiTier.Static,
            AiBitsForceOn = 1u << 3,   // FollowsPath
        });

        var (bundle, receipt) = NpcContentLoader.Compile(snapshot);
        Assert.Equal(0, receipt.ProblemCount);
        Assert.True(bundle.ByNpcId[400001].Flags.Has(Horizon.Game.Sim.Npcs.NpcAiBits.FollowsPath));

        // 2) 同位既开又关：冲突必须报出来。
        snapshot.Templates[0].AiBitsForceOff = 1u << 3;
        var (_, conflictReceipt) = NpcContentLoader.Compile(snapshot);
        Assert.Contains(conflictReceipt.Problems, p => p.StartsWith("ai_force_conflict:400001", StringComparison.Ordinal));

        // 3) 插件侧的位值表要与内核一致（编辑器跨脚本域，只有这份常量对得上）。
        var plugin = File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Plugins", "NpcEditor",
            "Source", "NpcEditor", "NpcDraftModel.cs"));
        Assert.Contains("public const uint FollowsPath = 1u << 3;", plugin, StringComparison.Ordinal);
        Assert.Contains("public const uint CallsReinforcement = 1u << 6;", plugin, StringComparison.Ordinal);
        Assert.Equal(1u << 3, (uint)Horizon.Game.Sim.Npcs.NpcAiBits.FollowsPath);
        Assert.Equal(1u << 6, (uint)Horizon.Game.Sim.Npcs.NpcAiBits.CallsReinforcement);
    }

    /// <summary>
    /// 编辑器输入侧的互斥：勾一边清另一边。规则产出的两列喂给内核后不允许再有 <c>ai_force_conflict</c>，
    /// 且不许顺手清掉没被碰的位（"改成否"不该把另一边点亮）。
    /// </summary>
    [Fact]
    public void EditorAiOverrideRule_ProducesNoKernelConflict()
    {
        // 两列都是"是"时：勾哪边就只留哪边。
        Assert.Equal((true, false), NpcEditor.NpcAiOverrideEditing.Exclusive(true, true, onSideChanged: true));
        Assert.Equal((false, true), NpcEditor.NpcAiOverrideEditing.Exclusive(true, true, onSideChanged: false));

        // 改成"否"不动另一边；另一边本来是"是"也保持。
        Assert.Equal((false, false), NpcEditor.NpcAiOverrideEditing.Exclusive(false, false, onSideChanged: true));
        Assert.Equal((false, false), NpcEditor.NpcAiOverrideEditing.Exclusive(false, false, onSideChanged: false));
        Assert.Equal((true, false), NpcEditor.NpcAiOverrideEditing.Exclusive(true, false, onSideChanged: true));
        Assert.Equal((false, true), NpcEditor.NpcAiOverrideEditing.Exclusive(false, true, onSideChanged: false));

        // 不变量：4 种输入 × 2 个改动侧，产出永远不可能两边同时为"是"。
        foreach (var on in new[] { false, true })
        foreach (var off in new[] { false, true })
        foreach (var onSideChanged in new[] { true, false })
        {
            var (nextOn, nextOff) = NpcEditor.NpcAiOverrideEditing.Exclusive(on, off, onSideChanged);
            Assert.False(nextOn && nextOff, $"on={on} off={off} changedOn={onSideChanged} 仍两边为是");
        }

        // 端到端：规则产出直接当两列掩码，内核既不该报冲突，也不该丢掉被勾的位。
        const uint followsPath = (uint)NpcAiBits.FollowsPath;
        var (ruleOn, ruleOff) = NpcEditor.NpcAiOverrideEditing.Exclusive(true, true, onSideChanged: true);
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 400001, Name = "互斥后仍会走路线", Level = 30, Reaction = 0,
            AiTier = (byte)NpcAiTier.Static,
            AiBitsForceOn = ruleOn ? followsPath : 0u,
            AiBitsForceOff = ruleOff ? followsPath : 0u,
        });

        var (bundle, receipt) = NpcContentLoader.Compile(snapshot);
        Assert.DoesNotContain(receipt.Problems, p => p.StartsWith("ai_force_conflict", StringComparison.Ordinal));
        Assert.True(bundle.ByNpcId[400001].Flags.Has(NpcAiBits.FollowsPath));
    }

    /// <summary>导出的表：可解析、按编号排序、立场合法、且与提交进版本库的那份逐字段一致。</summary>
    [Fact]
    public void NpcVisuals_ExportedTableIsStableAndValid()
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
        snapshot.Templates.Add(new NpcTemplateSnapshot { NpcId = 5, ModelKey = "npc_b", Reaction = 2, Name = "乙" });
        snapshot.Templates.Add(new NpcTemplateSnapshot { NpcId = 2, ModelKey = "npc_a", Reaction = 0, Name = "甲" });
        snapshot.Templates.Add(new NpcTemplateSnapshot { NpcId = 9, ModelKey = "", Reaction = 1, Name = "无模型键" }); // 应被跳过

        var first = NpcVisualsExport.Build(snapshot, out var count);
        var second = NpcVisualsExport.Build(snapshot, out _);

        Assert.Equal(2, count);
        Assert.Equal(first, second); // 同输入同输出（导出幂等）
        Assert.True(first.IndexOf("npc_a", StringComparison.Ordinal) < first.IndexOf("npc_b", StringComparison.Ordinal),
            "表必须按编号排序，否则版本库里全是无意义 diff");

        using var doc = JsonDocument.Parse(first);
        var npcs = doc.RootElement.GetProperty("npcs");
        Assert.Equal(2, npcs.GetArrayLength());
        foreach (var row in npcs.EnumerateArray())
        {
            var stance = row.GetProperty("stance").GetInt32();
            Assert.InRange(stance, 0, 2);
            Assert.False(string.IsNullOrEmpty(row.GetProperty("modelKey").GetString()));
        }

        // 提交进版本库的那份表：字段与条数都要对得上。
        // 022 入库后由 export-visuals 从库导出 5 条（1/2/3 + 400001 商人样板 + 500001 敌对样板）。
        var committed = Path.Combine(RepoRoot(), "HundunWorld", "Data", "Npcs", "client_visuals.json");
        Assert.True(File.Exists(committed), "随包发布的立场表缺失：" + committed);
        using var committedDoc = JsonDocument.Parse(File.ReadAllText(committed));
        var committedRows = committedDoc.RootElement.GetProperty("npcs");
        // 6 只：1/2/3 + 400001 商人 + 400002 医者（已发布但还没摆刷点）+ 500001 敌对。
        Assert.Equal(6, committedRows.GetArrayLength());
        var byId = new Dictionary<int, int>();
        var stanceById = new Dictionary<int, int>();
        foreach (var row in committedRows.EnumerateArray())
        {
            var id = row.GetProperty("npcId").GetInt32();
            byId[id] = row.GetProperty("services").GetInt32();
            stanceById[id] = row.GetProperty("stance").GetInt32();
        }

        // 服务位与 022 种子同值：铁匠 121（含商人）、守门人 7（无商人）、行商 9（含商人）、
        // 货郎 9、山贼 0（**0 不是"未下发"**，客户端据此不会给它长对话按钮）。
        Assert.Equal(127, byId[1]);   // 1 号挂着发布/交付 10001，位图必须含 QuestGiver|QuestTurnIn

        // 医者声明 127，但货架/教学活动为空，导出按既有剪枝只剩 71（svc_pruned=3）。
        Assert.Equal(71, byId[400002]);
        Assert.Equal(7, byId[2]);
        Assert.Equal(9, byId[3]);
        Assert.Equal(9, byId[400001]);
        Assert.Equal(0, byId[500001]);

        // 立场：1/2/3/400001 友好，500001 敌对（红名）。
        Assert.Equal(0, stanceById[1]);
        Assert.Equal(0, stanceById[2]);
        Assert.Equal(0, stanceById[3]);
        Assert.Equal(0, stanceById[400001]);
        Assert.Equal(2, stanceById[500001]);

        // 现有五只都还没指认美术：prefab 必须是空串（键在、值空），客户端据此回退占位人形。
        foreach (var row in committedRows.EnumerateArray())
            Assert.Equal(string.Empty, row.GetProperty("prefab").GetString());

        // 训练师窗口的展示行：铁匠教 1001(200 金)/1002(500 金)/配方 900001(800 金)，按 seq 排。
        var smith = committedRows.EnumerateArray().Single(r => r.GetProperty("npcId").GetInt32() == 1);
        var taught = smith.GetProperty("trainerRows").EnumerateArray().ToArray();
        Assert.Equal(3, taught.Length);
        Assert.Equal(new[] { 1001, 1002, 900001 }, taught.Select(t => t.GetProperty("skillId").GetInt32()).ToArray());
        Assert.Equal(new[] { 200, 500, 800 }, taught.Select(t => t.GetProperty("gold").GetInt32()).ToArray());
        Assert.Equal(new[] { 1, 1, 2 }, taught.Select(t => t.GetProperty("kind").GetInt32()).ToArray());

        // 配方行的展示名来自配方表：客户端没有配方目录，只有编号时菜单里只能显示"配方#900001"。
        // 技能行不带名字（技能名由客户端技能表提供），所以 label 必须是空串而不是 null。
        Assert.Equal("锻造·玄铁重铠", taught[2].GetProperty("label").GetString());
        Assert.Equal(string.Empty, taught[0].GetProperty("label").GetString());

        // 对话菜单：铁匠 0(文本)/1(商店)/3(制作)，行商 0/1，任务 NPC 与敌对 NPC 无菜单。
        int[] GossipKinds(int npcId)
        {
            var row = committedRows.EnumerateArray().Single(r => r.GetProperty("npcId").GetInt32() == npcId);
            return row.GetProperty("gossipOptions").EnumerateArray()
                .Select(o => o.GetProperty("kind").GetInt32()).ToArray();
        }

        // 1 号：闲聊/商店/制作 + 交付（2026-11-01 补 TurnInQuest，否则"交付"没有入口）。
        Assert.Equal(new[] { 0, 1, 3, 4 }, GossipKinds(1));
        Assert.Equal(new[] { 0, 1 }, GossipKinds(3));
        // 2 号守门人：闲聊 + 交付（它是 90006 的任务链参与者，2026-11-01 补的行）。
        Assert.Equal(new[] { 0, 4 }, GossipKinds(2));
        Assert.Empty(GossipKinds(500001));
    }

    /// <summary>
    /// 配方行的 <c>label</c> 必须由导出器**算出来**，不能只靠"随包表里恰好有一份"。
    /// <para>
    /// 随包表是产物：导出代码回归（查表被删/条件写反）时没人会重新导出它，旧表仍带着正确名字，
    /// 对提交产物的断言照样绿。这里用合成快照直接跑 <see cref="NpcVisualsExport.Build"/>，
    /// 钉住"配方 id → 配方名"这条查表；技能行必须留空串（客户端按技能表取名）。
    /// </para>
    /// </summary>
    [Fact]
    public void NpcVisuals_RecipeRowLabelComesFromRecipeTable()
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
        snapshot.Templates.Add(new NpcTemplateSnapshot { NpcId = 7, ModelKey = "npc_test", Reaction = 0, Name = "丙" });
        snapshot.Recipes.Add(new NpcRecipeSnapshotDto { RecipeId = 900123, Name = "测试·回春丹" });
        snapshot.TrainerRows.Add(new NpcTrainerRowDto
        {
            NpcId = 7, SkillId = 900123, ServiceKind = 2, CostGold = 5, Seq = 0,
        });
        snapshot.TrainerRows.Add(new NpcTrainerRowDto
        {
            NpcId = 7, SkillId = 1001, ServiceKind = 1, CostGold = 7, Seq = 1,
        });

        var json = NpcVisualsExport.Build(snapshot, out var count);
        Assert.Equal(1, count);

        using var doc = JsonDocument.Parse(json);
        var rows = doc.RootElement.GetProperty("npcs")[0].GetProperty("trainerRows").EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal("测试·回春丹", rows[0].GetProperty("label").GetString());
        Assert.Equal(string.Empty, rows[1].GetProperty("label").GetString());
    }

    /// <summary>
    /// 台词正文（023 的 <c>text</c>）：**配了才写出**。
    /// <para>
    /// 只在非空时写键有两个理由：① 随包五只都还没配台词，产物应与上一版逐字节相同（同源门不用重导）；
    /// ② 客户端读不到键时必须退回"按动作种类合成"，所以"空串也写一个空键"没有任何信息量。
    /// 这条同时钉住"写侧真的把 <see cref="NpcGossipRowDto.Text"/> 读进了 JSON"——
    /// 只看客户端有没有解析，接错源（比如永远写空串）也会绿。
    /// </para>
    /// </summary>
    [Fact]
    public void GossipText_IsExportedOnlyWhenAuthored()
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
        snapshot.Templates.Add(new NpcTemplateSnapshot { NpcId = 7, ModelKey = "npc_test", Reaction = 0, Name = "丙" });
        snapshot.GossipRows.Add(new NpcGossipRowDto
        {
            NpcId = 7, GroupId = 1, Seq = 0, ActionKind = 0, Text = "客官慢走，炉火还热着。",
        });
        snapshot.GossipRows.Add(new NpcGossipRowDto
        {
            NpcId = 7, GroupId = 1, Seq = 1, ActionKind = 1, Text = "",
        });

        var json = NpcVisualsExport.Build(snapshot, out var count);
        Assert.Equal(1, count);

        using var doc = JsonDocument.Parse(json);
        var options = doc.RootElement.GetProperty("npcs")[0].GetProperty("gossipOptions")
            .EnumerateArray().ToArray();
        Assert.Equal(2, options.Length);

        var authored = options.Single(o => o.GetProperty("seq").GetInt32() == 0);
        Assert.Equal("客官慢走，炉火还热着。", authored.GetProperty("text").GetString());

        var synthesized = options.Single(o => o.GetProperty("seq").GetInt32() == 1);
        Assert.False(synthesized.TryGetProperty("text", out _),
            "空台词不该写出键：客户端按种类合成，写空键只会让产物与旧版产生无意义 diff");
    }

    /// <summary>
    /// 台词组必须**两端都导出**：NPC 行的 <c>gossipGroupId</c>（默认组）与每个选项的 <c>group</c>（0=通用）。
    /// 只导一边时客户端没法筛，两套台词会同时出现——这正是模板 <c>gossip_group_id</c> 之前零读者的表现。
    /// </summary>
    [Fact]
    public void GossipGroup_IsExportedAndFiltersByGroup()
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 400007, ModelKey = "npc_test", Reaction = 0, Name = "丙",
            ServiceFlags = (uint)NpcServiceBits.Gossip, GossipGroupId = 2,
        });
        snapshot.GossipRows.Add(new NpcGossipRowDto { NpcId = 400007, GroupId = 2, Seq = 0, ActionKind = 0 });
        snapshot.GossipRows.Add(new NpcGossipRowDto { NpcId = 400007, GroupId = 9, Seq = 1, ActionKind = 1 });
        snapshot.GossipRows.Add(new NpcGossipRowDto { NpcId = 400007, GroupId = 0, Seq = 2, ActionKind = 3 });

        var json = NpcVisualsExport.Build(snapshot, out var count);
        Assert.Equal(1, count);

        using var doc = JsonDocument.Parse(json);
        var row = doc.RootElement.GetProperty("npcs")[0];
        Assert.Equal(2, row.GetProperty("gossipGroupId").GetInt32());

        var groups = row.GetProperty("gossipOptions").EnumerateArray()
            .Select(o => o.GetProperty("group").GetInt32()).ToArray();
        Assert.Equal(new[] { 0, 2, 9 }, groups.OrderBy(g => g).ToArray());

        // 客户端按同一口径筛：活跃组 2 只留组 2 与通用 0。
        var kept = HundunWorld.Game.Selection.NpcGossipMenu.IndicesToShow(groups, 2)
            .Select(i => groups[i]).OrderBy(g => g).ToArray();
        Assert.Equal(new[] { 0, 2 }, kept);
    }

    /// <summary>
    /// 台词超过库列宽度（200）必须在**装配校验**就报出来，而不是等到写库时截断/抛错。
    /// 两侧边界都测：200 通过、201 报 <c>gossip_text_too_long</c>。
    /// </summary>
    [Fact]
    public void GossipText_OverColumnWidthIsRejectedAtCompile()
    {
        static NpcContentSnapshot Build(string text)
        {
            var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
            snapshot.Templates.Add(new NpcTemplateSnapshot
            {
                // 7 落在号段外（装配期会报 npc_id_out_of_segment），这里只测宽度闸门，用服务段号。
                NpcId = 400007, ModelKey = "npc_test", Reaction = 0, Name = "丙",
                // 仅文本项（kind=0）也要有对话位，否则装配期会报 gossip_action_bit_off。
                ServiceFlags = (uint)NpcServiceBits.Gossip,
            });
            snapshot.GossipRows.Add(new NpcGossipRowDto
            {
                NpcId = 400007, GroupId = 1, Seq = 0, ActionKind = 0, Text = text,
            });
            return snapshot;
        }

        var atLimit = NpcContentLoader.Compile(Build(new string('字', NpcContentSnapshot.GossipTextMaxLength)));
        Assert.True(atLimit.Receipt.ProblemCount == 0,
            "200 字应当放行，实际：" + string.Join(",", atLimit.Receipt.Problems));

        var over = NpcContentLoader.Compile(Build(new string('字', NpcContentSnapshot.GossipTextMaxLength + 1)));
        Assert.Contains("gossip_text_too_long:400007:0", over.Receipt.Problems);
    }

    /// <summary>
    /// 「新建」是唯一能**发号**的入口：CLI 只认 service/hostile 两段（1–3 保留），插件两个按钮各调一次
    /// <c>allocate-id</c>。任何一侧改名/挪走，编辑器就只能手填号——而手填撞号在保存链路里只表现为覆盖
    /// （乐观锁管的是版本，不管"这个号本来就有人"）。
    /// </summary>
    [Fact]
    public void NewNpcFlow_AllocatesIdInsideSegment()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.NpcCli", "Program.cs"));
        Assert.Contains("\"allocate-id\" => RunAllocateId(rest)", program, StringComparison.Ordinal);
        Assert.Contains("\"service\" => NpcIdSegment.Service", program, StringComparison.Ordinal);
        Assert.Contains("\"hostile\" => NpcIdSegment.Hostile", program, StringComparison.Ordinal);
        Assert.Contains("allocate-id --segment service|hostile", program, StringComparison.Ordinal);

        var window = File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Plugins", "NpcEditor",
            "Source", "NpcEditorEditor", "NpcEditorWindow.cs"));
        Assert.Contains("新建服务", window, StringComparison.Ordinal);
        Assert.Contains("新建敌对", window, StringComparison.Ordinal);
        Assert.Contains("\"allocate-id --segment \"", window, StringComparison.Ordinal);
    }

    /// <summary>
    /// 「新建」摆出来的空白草稿必须**过服务端同一套校验**（服务型：友好 + 档 2；敌对型：红名 + 档 5，号在敌对段）。
    /// <para>
    /// 插件只写"号 / 立场 / 档位"三格，其余全是模型默认值——默认值与装载器判据一旦漂掉
    /// （号段闸门、档位范围、立场与号段一致性），症状就是"新建出来的 NPC 一保存就被拒"，
    /// 而界面上只能看到一条回执，看不出是哪一格不合规。
    /// </para>
    /// </summary>
    [Fact]
    public void NewNpcBlankDraft_PassesServerValidation()
    {
        foreach (var (id, reaction, tier) in new[] { (400002, (byte)0, (byte)2), (500002, (byte)2, (byte)5) })
        {
            var draft = new NpcDraft
            {
                Template = new NpcTemplateSnapshot
                {
                    NpcId = id,
                    Name = "新建测试 " + id,
                    Reaction = reaction,
                    AiTier = tier,
                },
            };

            var (_, receipt) = NpcContentLoader.Compile(draft.ToSnapshot());
            Assert.True(receipt.ProblemCount == 0,
                $"新建空草稿 {id} 被拒：" + string.Join(",", receipt.Problems));
        }
    }

    /// <summary>
    /// 逐只指认的美术必须真的进表：带 <c>prefab_guid</c> 的模板导出后这一只的 `prefab` 就是它；
    /// 没配的是空串（客户端据此回退占位人形，而不是去猜别人的 prefab）。
    /// </summary>
    [Fact]
    public void NpcVisuals_PrefabGuidFlowsToClientTable()
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 7, ModelKey = "npc_smith", Reaction = 0,
            PrefabGuid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
        });
        snapshot.Templates.Add(new NpcTemplateSnapshot { NpcId = 8, ModelKey = "npc_other", Reaction = 1 });
        // 新建 NPC 默认没有模型键：只要配了预制体 GUID，这一行也必须进表（键只做分组，不是身份）。
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 9, ModelKey = "", Reaction = 0, PrefabGuid = "bbbbbbbb-cccc-dddd-eeee-ffffffffffff",
        });
        // 键和预制体都没有的行才真的没有可认的东西。
        snapshot.Templates.Add(new NpcTemplateSnapshot { NpcId = 10, ModelKey = "", Reaction = 0 });

        var json = NpcVisualsExport.Build(snapshot, out var count);
        Assert.Equal(3, count);

        using var doc = JsonDocument.Parse(json);
        var rows = doc.RootElement.GetProperty("npcs").EnumerateArray().ToArray();
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", rows[0].GetProperty("prefab").GetString());
        Assert.Equal(string.Empty, rows[1].GetProperty("prefab").GetString());
        Assert.Equal(9, rows[2].GetProperty("npcId").GetInt32());
        Assert.Equal(string.Empty, rows[2].GetProperty("modelKey").GetString());
        Assert.Equal("bbbbbbbb-cccc-dddd-eeee-ffffffffffff", rows[2].GetProperty("prefab").GetString());
    }

    /// <summary>
    /// 随包的两份产物必须**同源**：客户端立场表 == 用随包内容快照重新导出的结果（逐字节，只有换行符归一）。
    /// <para>
    /// 为什么要有这一条：`client_visuals.json` 是代码的产物、`db_content.json` 是内容的事实。
    /// 只断言"表里有几条、字段对不对"时，<b>导出器改了格式却没重导</b>照样全绿——
    /// 玩家那边读到的是旧格式（键对得上、值已经过期）。这条把"改导出器 ⇒ 必须重导"变成红灯。
    /// </para>
    /// </summary>
    [Fact]
    public void ClientVisuals_IsExactExportOfShippedContent()
    {
        var contentPath = Path.Combine(RepoRoot(), "HundunWorld", "Data", "Npcs", "db_content.json");
        var tablePath = Path.Combine(RepoRoot(), "HundunWorld", "Data", "Npcs", "client_visuals.json");
        Assert.True(File.Exists(contentPath), "随包内容缺失：" + contentPath);
        Assert.True(File.Exists(tablePath), "随包立场表缺失：" + tablePath);

        var snapshot = JsonSerializer.Deserialize<NpcContentSnapshot>(
            File.ReadAllText(contentPath), NpcSnapshotJson.Options);
        Assert.NotNull(snapshot);

        var fresh = NpcVisualsExport.Build(snapshot!, out var count);
        var committed = File.ReadAllText(tablePath);

        // 行尾归一：两份产物用谁的行尾不该成为红的原因（内容一致才算一致）。
        Assert.Equal(fresh.Replace("\r\n", "\n"), committed.Replace("\r\n", "\n"));
        Assert.Equal(6, count);
    }

    /// <summary>
    /// 插件保存时**不能整表替换**刷点/路线/配方：一个 NPC 可以有多条，而面板只显示第一条。
    /// 老写法 <c>_draft.SpawnPoints = new List&lt;...&gt; { 一条 }</c> 会把没显示的行静默删掉
    /// （保存成功、库里少行，玩家看到的是"另一个营地的人没了"）。
    /// <para>
    /// 这里钉两件事：保存路径走合并入口；老写法不许回来。合并语义本身（替换第 index 条 /
    /// 按 PathId、RecipeId 命中 / 0 追加 / null 容器补齐）在 <c>NpcDraftModel</c> 里，
    /// 由插件域的三模块编译 + 独立 harness 覆盖（测试工程引用不到 Flax 插件域）。
    /// </para>
    /// </summary>
    [Fact]
    public void PluginMergesMultiRowContentInsteadOfReplacing()
    {
        var model = File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Plugins", "NpcEditor",
            "Source", "NpcEditor", "NpcDraftModel.cs"));
        Assert.Contains("public void ReplaceSpawn(int index, NpcSpawnPointModel spawn)", model, StringComparison.Ordinal);
        Assert.Contains("public void UpsertRoute(NpcRouteModel route)", model, StringComparison.Ordinal);
        Assert.Contains("public void UpsertRecipe(NpcRecipeModel recipe)", model, StringComparison.Ordinal);
        // 按**面板行号**就地替换：新建路线/配方在落库前业务号为 0，只认业务号会每次提交都追加一条。
        Assert.Contains("public void UpsertRouteAt(int index, NpcRouteModel route)", model, StringComparison.Ordinal);
        Assert.Contains("public void UpsertRecipeAt(int index, NpcRecipeModel recipe)", model, StringComparison.Ordinal);
        Assert.Contains("public static void AppendHidden<T>(", model, StringComparison.Ordinal);

        var window = File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Plugins", "NpcEditor",
            "Source", "NpcEditorEditor", "NpcEditorWindow.cs"));
        Assert.Contains("_draft.ReplaceSpawn(hadSpawn ? spawnIndex : -1", window, StringComparison.Ordinal);
        Assert.Contains("NpcRowIndex.Step(", window, StringComparison.Ordinal);
        Assert.Contains("NpcRowIndex.Clamp(", window, StringComparison.Ordinal);
        Assert.Contains("_draft.UpsertRouteAt(routeIndex", window, StringComparison.Ordinal);
        Assert.Contains("_draft.UpsertRecipeAt(recipeIndex", window, StringComparison.Ordinal);

        Assert.DoesNotMatch(new Regex(@"_draft\.SpawnPoints\s*=\s*new List<NpcSpawnPointModel>\s*\{\s*new"), window);
        Assert.DoesNotMatch(new Regex(@"_draft\.Routes\s*=\s*new List<NpcRouteModel>\s*\{\s*new"), window);
        Assert.DoesNotMatch(new Regex(@"_draft\.Recipes\s*=\s*new List<NpcRecipeModel>\s*\{\s*new"), window);

        // 其余多行面板同样是"面板行数 < 库行数 + 整组替换"：不许再回到"只按可见行 new 一个列表"的写法。
        // （本地变量再赋回去是允许的；这里挡的是直接 new List 覆盖。）
        foreach (var (field, type) in new[]
                 {
                     ("VendorRows", "NpcVendorRowModel"),
                     ("TrainerRows", "NpcTrainerRowModel"),
                     ("GossipRows", "NpcGossipRowModel"),
                     ("ScheduleRows", "NpcScheduleRowModel"),
                     ("QuestLinks", "NpcQuestLinkModel"),
                     ("SkillMounts", "NpcSkillMountModel"),
                 })
        {
            Assert.DoesNotMatch(new Regex(@"_draft\." + field + @"\s*=\s*new List<" + type + ">"), window);
        }

        // 每一处都必须真的走"保尾"合并（少一处就等于那个面板又会被整组替换掉）。
        Assert.Contains("NpcRowListEditing.AppendHidden(vendorRows", window, StringComparison.Ordinal);
        Assert.Contains("NpcRowListEditing.AppendHidden(trainerRows", window, StringComparison.Ordinal);
        Assert.Contains("NpcRowListEditing.AppendHidden(gossipRows", window, StringComparison.Ordinal);
        Assert.Contains("NpcRowListEditing.AppendHidden(scheduleRows", window, StringComparison.Ordinal);
        Assert.Contains("NpcRowListEditing.AppendHidden(questLinks", window, StringComparison.Ordinal);
        Assert.Contains("NpcRowListEditing.AppendHidden(points", window, StringComparison.Ordinal);
        Assert.Contains("NpcRowListEditing.AppendHidden(materials", window, StringComparison.Ordinal);
        Assert.Contains("NpcSkillMountEditing.MergeSkillMounts(", window, StringComparison.Ordinal);
        Assert.Contains("NpcLootEditing.AppendHiddenEntries(", window, StringComparison.Ordinal);
        Assert.Contains("NpcLootEditing.MergeBindings(", window, StringComparison.Ordinal);

        // 任务链的描述/目标两行：文本由 CLI 拼（QuestChainText 一份规则），插件只打印。
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.NpcCli", "Program.cs"));
        Assert.Contains("QuestWireMapper", program, StringComparison.Ordinal);
        Assert.Contains("QuestChainText.ObjectiveLine", program, StringComparison.Ordinal);
        Assert.Contains("描述：", window, StringComparison.Ordinal);
        Assert.Contains("目标：", window, StringComparison.Ordinal);
    }

    /// <summary>
    /// 逐件掉落条件必须在**三条入口**上都活着：读库 → 草稿（`NpcStore`）、草稿 → 库（`Upsert`）、
    /// 插件保存（`CommitLootRows`）。
    /// <para>
    /// 以前读侧写死 <c>-1/0</c>、插件写侧也写死 <c>-1</c>：库里配的"任务持有者才掉 / 同组互斥"
    /// 打开编辑器保存一次就变"人人可掉 / 逐件独立"，而草稿对账两边都是默认值，往返测试看不出来。
    /// 怪物侧早就有同一条守卫（`MonsterEditorContractTests` 的 entryRead 断言），NPC 侧补上。
    /// </para>
    /// </summary>
    [Fact]
    public void NpcLootEntryConditionsSurviveAllThreePaths()
    {
        var store = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.NpcCli", "NpcStore.cs"));
        Assert.Contains("ConditionKind = e.ConditionKind", store, StringComparison.Ordinal);
        Assert.Contains("QuestId = e.QuestId", store, StringComparison.Ordinal);
        Assert.DoesNotContain("条目级条件暂未建表", store);

        var window = File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Plugins", "NpcEditor",
            "Source", "NpcEditorEditor", "NpcEditorWindow.cs"));
        // 绑定级：条件/任务 id/人数区间按旧值作底座
        Assert.Contains("ConditionKind = previous?.ConditionKind ?? 0", window, StringComparison.Ordinal);
        Assert.Contains("MinCount = previous?.MinCount ?? 1", window, StringComparison.Ordinal);
        // 条目级：组号按物品号从旧行带回；条件/任务 id 现在面板里能改（下拉 + 文本框）
        Assert.Contains("GroupId = keep?.GroupId ?? 0", window, StringComparison.Ordinal);
        Assert.Contains("NpcLootConditionUi.ValueAt(_lootCondition[i].SelectedIndex)", window, StringComparison.Ordinal);
        Assert.Contains("NpcLootConditionUi.IndexOf(", window, StringComparison.Ordinal);
        // 绑定级：同两格现在也能改（没有"继承"档，索引即库值）。
        Assert.Contains("NpcLootConditionUi.BindingValueAt(_lootBindingCondition.SelectedIndex)", window, StringComparison.Ordinal);
        Assert.Contains("NpcLootConditionUi.BindingIndexOf(", window, StringComparison.Ordinal);

        // 写侧仍然按草稿值落库（读回 -1 时必须原样写回 -1，不许被夹成 0）。
        Assert.Contains("ConditionKind = e.ConditionKind,", store, StringComparison.Ordinal);
    }

    /// <summary>
    /// 路线试算的接线与**同一把尺子**：CLI 动词 + 插件按钮必须存在；
    /// 试算的"短段"阈值必须与巡逻内核的"到点距离"同值——两把尺子不同值时，
    /// 试算说"这一段太短"而世界里其实走得好好的（或反过来），运营只会不再信这个面板。
    /// </summary>
    [Fact]
    public void RoutePreview_WiredToCliAndPlugin_SharesArriveRuler()
    {
        Assert.Equal(Horizon.Game.Sim.Monsters.MonsterPatrolCore.ArriveDistance, NpcRoutePreviewCore.ShortLegMetres);

        var program = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.NpcCli", "Program.cs"));
        Assert.Contains("\"preview-route\" => RunPreviewRoute(rest)", program, StringComparison.Ordinal);
        Assert.Contains("preview-route - [--route-index <n>] [--speed <m/s>]", program, StringComparison.Ordinal);

        var window = File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Plugins", "NpcEditor",
            "Source", "NpcEditorEditor", "NpcEditorWindow.cs"));
        Assert.Contains("\"preview-route - --route-index \"", window, StringComparison.Ordinal);
        Assert.Contains("路线试算", window, StringComparison.Ordinal);
    }

    /// <summary>
    /// 客户端 NPC 外观分流的接线守卫（A4）：预制体按"编号优先、模型键兜底"解析；命中后不再套人形兜底；
    /// NPC 也吃逐只缩放；坏 GUID 回退占位而不是隐形；Actor 命名分得清 NPC。
    /// <para>
    /// 这些代码在 Flax 脚本域，测试工程引用不到 → 只能按源码钉。漂了的表现是
    /// "库里配了 GUID、世界里仍是占位人形"，而没有任何报错。
    /// </para>
    /// </summary>
    [Fact]
    public void ClientNpcVisualRouting_IsWired()
    {
        var client = File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Source", "Game", "FlaxActorSyncSystem.cs"));
        Assert.Contains("NpcStanceCatalog.TryGetPrefab(identity.TemplateId, identity.ModelKey", client, StringComparison.Ordinal);
        Assert.Contains("SelectableKind.Npc => $\"Npc_{entityId}\"", client, StringComparison.Ordinal);
        Assert.Contains("!useMonsterPrefab && !useNpcPrefab", client, StringComparison.Ordinal);
        Assert.Contains("kind == SelectableKind.Monster || kind == SelectableKind.Npc", client, StringComparison.Ordinal);
        Assert.Contains("NPC {entityId} 的预制体加载失败", client, StringComparison.Ordinal);

        var catalog = File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Source", "Game", "Selection", "NpcStanceCatalog.cs"));
        Assert.Contains("\"prefab\"", catalog, StringComparison.Ordinal);
        Assert.Contains("TryGetPrefab(int npcId, string modelKey, out string prefabGuid)", catalog, StringComparison.Ordinal);
    }

    /// <summary>
    /// 面板行数必须装得下**随包内容里单只 NPC 的最大行数**。
    /// <para>
    /// 面板是固定行数逐格重建、保存是整组替换：行数一旦小于内容，第 N+1 行在面板上根本看不见，
    /// 作者连"改它"的机会都没有（本轮已让它不再被删，但改成"能看见"才是真解）。
    /// 判据取**实测**：从 `db_content.json` 按 npcId 分组数最大值，不拿流水线回执的合计当单只行数。
    /// </para>
    /// </summary>
    [Fact]
    public void PanelRowCountsCoverShippedPerNpcMaximum()
    {
        var window = File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Plugins", "NpcEditor",
            "Source", "NpcEditorEditor", "NpcEditorWindow.cs"));

        int Constant(string name)
        {
            var m = Regex.Match(window, @"const int " + name + @" = (\d+);");
            Assert.True(m.Success, "找不到常量 " + name + "——行数改成算出来的就得同步改这条判据");
            return int.Parse(m.Groups[1].Value);
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld",
            "Data", "Npcs", "db_content.json")));
        var root = doc.RootElement;

        int MaxPerNpc(string arrayName)
        {
            if (!root.TryGetProperty(arrayName, out var rows)) return 0;
            var counts = new Dictionary<int, int>();
            foreach (var row in rows.EnumerateArray())
            {
                if (!row.TryGetProperty("npcId", out var idProp)) continue;
                var id = idProp.GetInt32();
                counts[id] = counts.TryGetValue(id, out var c) ? c + 1 : 1;
            }

            return counts.Count == 0 ? 0 : counts.Values.Max();
        }

        int MaxChildArray(string arrayName, string childName)
        {
            Assert.True(root.TryGetProperty(arrayName, out var rows), $"随包内容缺 {arrayName}——这条判据会退化成恒真");
            var max = 0;
            foreach (var row in rows.EnumerateArray())
            {
                if (!row.TryGetProperty(childName, out var items)) continue;
                if (items.GetArrayLength() > max) max = items.GetArrayLength();
            }

            Assert.True(max > 0, $"{arrayName}.{childName} 量出来是 0——夹具退化了，判据证明不了任何事");
            return max;
        }

        var vendorNeed = MaxPerNpc("vendorRows");
        var trainerNeed = MaxPerNpc("trainerRows");
        var gossipNeed = MaxPerNpc("gossipRows");
        Assert.True(vendorNeed > 0, "随包内容没量到任何货架行——退化了");
        Assert.True(trainerNeed > 0, "随包内容没量到任何教学行——退化了");
        Assert.True(gossipNeed > 0, "随包内容没量到任何台词行——退化了");

        var need = new (string Constant, int Required, string Source)[]
        {
            ("VendorRows", vendorNeed, "vendorRows/单只"),
            ("TrainerRows", trainerNeed, "trainerRows/单只"),
            ("GossipRows", gossipNeed, "gossipRows/单只"),
            ("QuestRows", MaxPerNpc("questLinks"), "questLinks/单只"),
            ("RoutePoints", MaxChildArray("routes", "points"), "routes.points/单条"),
            ("RecipeMaterialRows", MaxChildArray("recipes", "materials"), "recipes.materials/单张"),
        };

        foreach (var (name, required, source) in need)
        {
            var have = Constant(name);
            Assert.True(have >= required,
                $"{name}={have} 装不下随包最大 {required} 行（{source}）：多出来的行在面板上看不见、也改不了");
        }
    }

    /// <summary>
    /// 作息提交必须**沿用载入的台词组**：这一列面板没露，写死 0 会把"白天/夜里两套菜单"静默清掉。
    /// （2026-10-27 真库实测过一次：随包 1 在库里变成了 0。）
    /// </summary>
    [Fact]
    public void ScheduleCommitKeepsGossipGroup()
    {
        var window = File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Plugins", "NpcEditor",
            "Source", "NpcEditorEditor", "NpcEditorWindow.cs"));
        Assert.Contains("GossipGroupId = priorSchedule?.GossipGroupId ?? 0", window, StringComparison.Ordinal);
        Assert.DoesNotContain("GossipGroupId = 0,", window);   // 硬编码清空的老写法不许回来
    }

    /// <summary>
    /// 任务编辑器的 NPC 下拉与"发布者是否存在"也必须读库：它跑的是 `Horizon.QuestCli catalog`，
    /// 旧实现取 `NpcTemplateCatalog.RegisteredTemplates()`（只有 1/2/3）⇒ 新 NPC 在下拉里消失、校验报 Missing。
    /// </summary>
    [Fact]
    public void QuestEditorCatalogReadsNpcsFromStore()
    {
        var questStore = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.QuestCli", "QuestStore.cs"));
        Assert.Contains("public List<NpcTemplateEntity> AllNpcTemplates()", questStore, StringComparison.Ordinal);
        Assert.Contains("NpcIds().Contains(templateId)", questStore, StringComparison.Ordinal);

        var questCli = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.QuestCli", "Program.cs"));
        Assert.Contains("store!.AllNpcTemplates()", questCli, StringComparison.Ordinal);
    }

    /// <summary>
    /// `catalog` 必须以**库**为数据源（含草稿）。CLI 每次都是新进程、从不装配内容，
    /// 若退回 `NpcTemplateCatalog.RegisteredTemplates()`（硬编码 1/2/3 + 装配内容），
    /// 新发布的 NPC 在 NPC 编辑器与任务编辑器里都会"看得到编号、读不到数据"。
    /// </summary>
    [Fact]
    public void CatalogReadsFromStore()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.NpcCli", "Program.cs"));
        Assert.Contains("store!.ListTemplates()", program, StringComparison.Ordinal);
        Assert.Contains("静态表兜底", program, StringComparison.Ordinal);

        var store = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.NpcCli", "NpcStore.cs"));
        Assert.Contains("public List<NpcCatalogRow> ListTemplates()", store, StringComparison.Ordinal);
    }

    /// <summary>
    /// 保存**保持**发布态（2026-10-27 拍板）：编辑已上线 NPC 再保存，不该把它静默下线。
    /// 新建模板/新建路线仍是草稿，发布依旧是显式动作。
    /// </summary>
    [Fact]
    public void SaveKeepsPublishState()
    {
        var store = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.NpcCli", "NpcStore.cs"));
        Assert.DoesNotContain("now.PublishState = 0;", store);
        // 只允许"新建路线"那一处从草稿开始；更新分支不许再把已有路线打回草稿。
        var resets = Regex.Matches(store, @"path\.PublishState = 0;").Count;
        Assert.Equal(1, resets);
    }

    /// <summary>
    /// 掉落绑定与怪物**共用整数 id 空间**：只有敌对 NPC 才可能掉落。
    /// 少了这层过滤，友好 NPC（铁匠/守门人/行商）会把同 id 的怪物种子掉落当成自己的，
    /// 而保存友好 NPC 又会按同一 MonsterId 删掉那些怪物行——两个方向都要守。
    /// </summary>
    [Fact]
    public void NpcLootOnlyForHostileNpcs()
    {
        var store = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.NpcCli", "NpcStore.cs"));
        Assert.Contains("if (!hostileIds.Contains(b.MonsterId)) continue;", store, StringComparison.Ordinal);
        Assert.Contains("var canWriteLoot = t.Reaction ==", store, StringComparison.Ordinal);
        Assert.Contains("canWriteLoot ? draft.LootBindings", store, StringComparison.Ordinal);
    }

    /// <summary>
    /// `024` 的三份清单必须同构：建表列 / `NpcStore` 写入列 / 回读列，且 DTO 的每个字段都能落到列上。
    /// <para>
    /// 表还没执行，列名一旦对不上只有**执行那天**才会炸（而且是运行时 SQL 错），离线闸门是唯一能提前发现的地方。
    /// 反向也要守：`mount_id` 是 IDENTITY，出现在写入列里就是"不能对自增列显式赋值"。
    /// </para>
    /// </summary>
    [Fact]
    public void SkillMountSchemaInsertAndReadListsAgree()
    {
        static string ToSnake(string name)
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]) && i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(name[i]));
            }

            return sb.ToString();
        }

        var sql = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "sql", "024_npc_skill_mount.sql"));
        var createStart = sql.IndexOf("CREATE TABLE [Game_HunduShijie_NpcSkillMount]", StringComparison.Ordinal);
        Assert.True(createStart >= 0, "024 里没有建表语句");
        var createEnd = sql.IndexOf("CONSTRAINT", createStart, StringComparison.Ordinal);
        Assert.True(createEnd > createStart, "建表语句没有 CONSTRAINT 段，解析会漏列");
        var createBlock = sql.Substring(createStart, createEnd - createStart);
        // 列名有 CamelCase 的基类列（IsValid/GameId/AreaId/ServerId/GameUserId），正则不能只认小写。
        var sqlColumns = Regex.Matches(createBlock, @"\[([A-Za-z_]+)\]\s+(?:bit|int|float|tinyint|uniqueidentifier)")
            .Select(m => m.Groups[1].Value).ToList();
        Assert.True(sqlColumns.Count >= 26, $"建表列只解析到 {sqlColumns.Count} 列——解析退化了，判据证明不了任何事");

        var store = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.NpcCli", "NpcStore.cs"));
        var insertStart = store.IndexOf("INSERT INTO [{table}] (", StringComparison.Ordinal);
        Assert.True(insertStart >= 0, "NpcStore 里找不到技能挂载的 INSERT");
        var insertEnd = store.IndexOf("VALUES (", insertStart, StringComparison.Ordinal);
        Assert.True(insertEnd > insertStart, "INSERT 没有 VALUES 段");
        var insertColumns = Regex.Matches(store.Substring(insertStart, insertEnd - insertStart), @"\[([A-Za-z_]+)\]")
            .Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(25, insertColumns.Count);

        // 锚在技能挂载独有的列上：`[npc_id] AS NpcId` 会先命中台词读取块（同文件里也有 npc_id）。
        var selectStart = store.IndexOf("[skill_id] AS SkillId", StringComparison.Ordinal);
        Assert.True(selectStart >= 0, "NpcStore 里找不到技能挂载的 SELECT");
        var selectEnd = store.IndexOf("FROM [Game_HunduShijie_NpcSkillMount]", selectStart, StringComparison.Ordinal);
        Assert.True(selectEnd > selectStart, "SELECT 没有 FROM 段");
        var readColumns = Regex.Matches(store.Substring(selectStart, selectEnd - selectStart), @"\[([A-Za-z_]+)\] AS")
            .Select(m => m.Groups[1].Value).ToList();
        // 起点切在 skill_id 上，npc_id 在它前面一格。
        readColumns.Insert(0, "npc_id");
        Assert.Equal(20, readColumns.Count);

        var dtoColumns = typeof(NpcSkillMountDto).GetProperties().Select(p => ToSnake(p.Name)).ToList();
        Assert.Equal(20, dtoColumns.Count);

        foreach (var column in dtoColumns)
        {
            Assert.Contains(column, sqlColumns);
            Assert.Contains(column, insertColumns);
            Assert.Contains(column, readColumns);
        }

        Assert.DoesNotContain("mount_id", insertColumns);   // IDENTITY：显式赋值会直接报错
        Assert.DoesNotContain("mount_id", dtoColumns);
        Assert.DoesNotContain("mount_id", readColumns);     // 行身份对内容无用，读回只会多个没人用的字段

        // 整组替换的"清空"语义：**先删后判空**。
        // 反过来（`rows.Count == 0` 先 return）会让"作者清空全部挂载后保存"变成 no-op，
        // 库里旧行永远留着——与刷点删除那次同族，只有真库往返才看得见。
        var methodStart = store.IndexOf("private int WriteSkillMounts", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "NpcStore 里找不到 WriteSkillMounts");
        var methodEnd = store.IndexOf("/// <summary>", methodStart, StringComparison.Ordinal);
        Assert.True(methodEnd > methodStart, "WriteSkillMounts 没有结束锚点");
        var body = store.Substring(methodStart, methodEnd - methodStart);
        var deleteAt = body.IndexOf("DELETE FROM [{table}] WHERE [npc_id]", StringComparison.Ordinal);
        var emptyAt = body.IndexOf("rows.Count == 0", StringComparison.Ordinal);
        Assert.True(deleteAt >= 0, "WriteSkillMounts 里找不到整组删除");
        Assert.True(emptyAt >= 0, "WriteSkillMounts 里找不到空列表分支");
        Assert.True(deleteAt < emptyAt, "空列表分支排在整组删除之前——清空挂载保存后库里旧行不会被删");
    }

    /// <summary>
    /// `023` 同样未执行：列名/列宽/守卫/写入截断必须同源，否则"能保存但读回被拒"。
    /// <para>
    /// 判据不是"常量等于 200"，而是 **SQL 写多少列宽、代码就许多少字符**：把 SQL 改成 100
    /// 而代码仍许 200，执行后每次保存都会因为超长被截断（或整包失败），而离线测试全绿。
    /// </para>
    /// </summary>
    [Fact]
    public void GossipTextSchemaMatchesGuardAndLimit()
    {
        var sql = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "sql", "023_npc_gossip_text.sql"));
        var add = Regex.Match(sql, @"ADD \[(\w+)\] nvarchar\((\d+)\) NOT NULL");
        Assert.True(add.Success, "023 里没解析到 ADD [text] nvarchar(N) —— 解析退化，判据证明不了任何事");
        var column = add.Groups[1].Value;
        var width = int.Parse(add.Groups[2].Value);
        Assert.Equal("text", column);
        Assert.True(width > 0, "列宽是 0？");
        Assert.Contains("IF COL_LENGTH(N'Game_HunduShijie_NpcGossipOption', N'text') IS NULL", sql, StringComparison.Ordinal);

        var store = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.NpcCli", "NpcStore.cs"));
        Assert.Contains("COL_LENGTH(N'Game_HunduShijie_NpcGossipOption', N'text')", store, StringComparison.Ordinal);
        Assert.Contains("public const int NpcGossipTextMaxLength = NpcContentSnapshot.GossipTextMaxLength;",
            store, StringComparison.Ordinal);   // 只许别名，不许第二份字面量
        Assert.Contains("[seq] AS Seq, [text] AS Text", store, StringComparison.Ordinal);
        Assert.Contains("SET [text] = {0}", store, StringComparison.Ordinal);   // 保存是"删光重插"，台词要跟着走

        var limit = NpcContentSnapshot.GossipTextMaxLength;
        Assert.Equal(width, limit);
        Assert.Equal(limit, NpcStore.NpcGossipTextMaxLength);
    }
}
