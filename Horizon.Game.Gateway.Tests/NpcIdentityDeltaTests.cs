using System.Collections.Generic;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Sim.Npcs;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 身份增量：姿态随作息时段变、服务位图每快照下发（A3/A8 的运行期缺口）。
/// <para>
/// 不测这条的话，症状是"铁匠白天在打铁，太阳落山后还保持着打铁的姿势"——
/// 因为身份只在 Spawn 发一次，而作息换档发生在会话中途。这种错既没有异常，
/// 也没有计数变化能提示，只有对着同一只 NPC 在两个时刻各看一次快照才看得出来。
/// </para>
/// <para>
/// 还要钉住哨兵语义：<c>-1</c> = 未下发，<c>0</c> = 确知"没有任何服务/站立"。
/// 把两者混成一个值，客户端就会给山贼头目长出一个点不开的对话按钮。
/// </para>
/// </summary>
/// <remarks>本类改写进程级 NPC 内容，必须与怪物内容测试串行。</remarks>
[Collection("monster-content")]
public class NpcIdentityDeltaTests
{
    private readonly ITestOutputHelper _log;

    public NpcIdentityDeltaTests(ITestOutputHelper log) => _log = log;

    /// <summary>每秒一拍，>世界秒"与"tick 数"一一对应，断言里不用再换算。</summary>
    private static DsWorldSessionOptions Options() => new()
    {
        ShardId = 0,
        TicksPerSecond = 1,
        SnapshotIntervalTicks = 1,
        EnableAoiFilter = false,
        MaxSessions = 10,
        MaxEntities = 1000,
    };

    private static NpcContentSnapshot Snapshot()
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };

        // 铁匠：Gossip|QuestGiver|Vendor|Trainer|CraftTrainer|CraftStation = 121。
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 1, Name = "铸剑师", ModelKey = "npc_smith",
            Level = 60, Reaction = (byte)NpcStance.Friendly,
            AiTier = (byte)NpcAiTier.Static, ServiceFlags = 121,
        });

        // 敌对山贼：服务位确实是 0（不是"未下发"）。
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 500001, Name = "山贼头目", ModelKey = "npc_bandit_chief",
            Level = 32, Reaction = (byte)NpcStance.Hostile,
            AiTier = (byte)NpcAiTier.Elite, ServiceFlags = 0,
        });

        // 121 里的商人/技能/配方位必须有**内容**撑着：装配期会把"开着位却没数据"的位摘掉
        // （见 NpcServiceCore.PruneDataEmpty），本用例要验的是"位图逐快照下发"，不是数据清洗。
        snapshot.VendorRows.Add(new NpcVendorRowDto
        {
            NpcId = 1, Slot = 0, ItemId = 750001, BuyPriceOverride = 10, SellPriceOverride = 5,
        });
        snapshot.TrainerRows.Add(new NpcTrainerRowDto
        {
            NpcId = 1, SkillId = 1001, ServiceKind = (byte)NpcTrainerService.Skill, CostGold = 10, Seq = 0,
        });
        snapshot.TrainerRows.Add(new NpcTrainerRowDto
        {
            NpcId = 1, SkillId = 900001, ServiceKind = (byte)NpcTrainerService.Recipe, CostGold = 800, Seq = 1,
        });

        snapshot.SpawnPoints.Add(new NpcSpawnSnapshotDto
        {
            SpawnPointId = 1, NpcId = 1, X = 0f, Y = 0f, Z = 0f, MaxAlive = 1,
        });
        snapshot.SpawnPoints.Add(new NpcSpawnSnapshotDto
        {
            SpawnPointId = 2, NpcId = 500001, X = 30f, Y = 0f, Z = 0f, MaxAlive = 1,
        });

        // 白天做工、夜里站立：换档点就在会话中途，正好能证明"增量"和"spawn-only"不是一回事。
        snapshot.ScheduleRows.Add(new NpcScheduleRowDto
        {
            NpcId = 1, Seq = 0, FromSeconds = 0, ToSeconds = 600,
            RouteId = 0, Posture = (byte)NpcPosture.Work, GossipGroupId = 1,
        });
        snapshot.ScheduleRows.Add(new NpcScheduleRowDto
        {
            NpcId = 1, Seq = 1, FromSeconds = 600, ToSeconds = 1200,
            RouteId = 0, Posture = (byte)NpcPosture.Stand, GossipGroupId = 1,
        });

        return snapshot;
    }

    [Fact]
    public void PostureFollowsSchedule_ServiceFlagsArePerSnapshot()
    {
        var (bundle, receipt) = NpcContentLoader.Compile(Snapshot());
        Assert.Equal(0, receipt.ProblemCount);
        NpcContent.Install(bundle);

        try
        {
            var world = new DsWorldSession(Options());
            Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out var reason), reason);
            world.ApplyNpcContentSpawns();

            var templateOf = new Dictionary<ulong, int>();
            var standAt10 = new Dictionary<ulong, int>();
            var serviceAt10 = new Dictionary<ulong, int>();
            var standAt700 = new Dictionary<ulong, int>();

            for (var tick = 1; tick <= 700; tick++)
            {
                var result = world.Tick();
                var snapshot = result.WorldSnapshot;
                if (snapshot?.Deltas == null) continue;

                foreach (var delta in snapshot.Deltas)
                {
                    if (delta.Identity is { } identity)
                        templateOf[delta.EntityId] = identity.TemplateId;

                    if (delta.State is not { } state) continue;

                    if (tick == 10)
                    {
                        standAt10[delta.EntityId] = state.NpcStandState;
                        serviceAt10[delta.EntityId] = state.NpcServiceFlags;
                    }
                    else if (tick == 700)
                    {
                        standAt700[delta.EntityId] = state.NpcStandState;
                    }
                }
            }

            ulong smith = 0, bandit = 0;
            foreach (var pair in templateOf)
            {
                if (pair.Value == 1) smith = pair.Key;
                else if (pair.Value == 500001) bandit = pair.Key;
            }

            Assert.True(smith != 0UL && bandit != 0UL, "快照里没同时看到铁匠与山贼（身份没下发？）");

            _log.WriteLine($"smith@10s stand={standAt10.GetValueOrDefault(smith, int.MinValue)} svc={serviceAt10.GetValueOrDefault(smith, int.MinValue)}");
            _log.WriteLine($"smith@700s stand={standAt700.GetValueOrDefault(smith, int.MinValue)}");
            _log.WriteLine($"bandit@10s stand={standAt10.GetValueOrDefault(bandit, int.MinValue)} svc={serviceAt10.GetValueOrDefault(bandit, int.MinValue)}");

            // 白天：做工（2）。这条只可能来自作息档；spawn-only 身份给不出这个数。
            Assert.Equal((int)NpcPosture.Work, standAt10[smith]);
            // 服务位：铁匠 121。
            Assert.Equal(121, serviceAt10[smith]);

            // 换档后：站立（0）。它是"确知的站立"，不是"未下发"（−1）。
            Assert.Equal((int)NpcPosture.Stand, standAt700[smith]);

            // 山贼：没有作息表 → 明确站立；服务位确实是 0（0 与 −1 必须分得开）。
            Assert.Equal((int)NpcPosture.Stand, standAt10[bandit]);
            Assert.Equal(0, serviceAt10[bandit]);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    /// <summary>
    /// NPC 的**头衔/外观键/体型/档位**必须随 Spawn 身份下发。
    /// <para>
    /// 不测这条的话，NPC 会以"无头衔、scale=1、rank=普通"出场——编辑器里配的精英档与体型缩放
    /// 全部静默失效，而名字却是对的：只错一半，最难在屏幕上看出来。
    /// </para>
    /// </summary>
    [Fact]
    public void SpawnIdentity_CarriesNpcTitleModelScaleAndRank()
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 400001,
            Name = "杂货商",
            Title = "货郎",
            ModelKey = "npc_peddler",
            Scale = 1.15f,
            Rank = 1, // 精英
            Level = 30,
            Reaction = (byte)NpcStance.Friendly,
            AiTier = (byte)NpcAiTier.Static,
        });
        snapshot.SpawnPoints.Add(new NpcSpawnSnapshotDto
        {
            SpawnPointId = 1, NpcId = 400001, X = 0f, Y = 0f, Z = 0f, MaxAlive = 1,
        });

        var (bundle, receipt) = NpcContentLoader.Compile(snapshot);
        Assert.Equal(0, receipt.ProblemCount);
        NpcContent.Install(bundle);

        try
        {
            var world = new DsWorldSession(Options());
            Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out var reason), reason);
            world.ApplyNpcContentSpawns();

            Horizon.Game.Message.Sync.Components.NetworkIdentityAuthComponent? identity = null;
            for (var tick = 0; tick < 3 && identity == null; tick++)
            {
                var result = world.Tick();
                var delta = result.WorldSnapshot?.Deltas;
                if (delta == null) continue;
                foreach (var d in delta)
                {
                    if (d.Kind == Horizon.Game.Message.Sync.EntityDeltaKind.Spawn && d.Identity != null)
                        identity = d.Identity;
                }
            }

            Assert.True(identity.HasValue, "没看到 NPC 的 Spawn 身份包");
            var id = identity!.Value;
            _log.WriteLine($"name={id.Nickname} title={id.Title} model={id.ModelKey} scale={id.ModelScale} rank={id.Rank}");

            Assert.Equal("杂货商", id.Nickname);
            Assert.Equal("货郎", id.Title);
            Assert.Equal("npc_peddler", id.ModelKey);
            Assert.Equal(1.15f, id.ModelScale, 3);
            Assert.Equal(1, id.Rank);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }
}
