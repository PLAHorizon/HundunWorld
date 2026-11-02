using System;
using System.Collections.Generic;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Sync;
using Horizon.Game.Sim.Npcs;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 友好 NPC 的"会踱步"必须真的动起来（A3 的运行期缺口）。
/// <para>
/// 判据用**快照里的世界坐标**：装配两个 NPC（一个 `Wanders` 档、一个站桩档），跑若干拍后
/// 会走的那只必须离出生点有可观位移，站桩那只必须几乎不动。
/// 只看"生成计数"证明不了这件事——参数被静默丢弃时计数照样是 2。
/// </para>
/// </summary>
/// <remarks>本类改写进程级 NPC 内容，必须与怪物内容测试串行。</remarks>
[Collection("monster-content")]
public class NpcIdleWanderTests
{
    private readonly ITestOutputHelper _log;

    public NpcIdleWanderTests(ITestOutputHelper log) => _log = log;

    private static DsWorldSessionOptions Options() => new()
    {
        ShardId = 0,
        TicksPerSecond = 60,
        SnapshotIntervalTicks = 1,
        EnableAoiFilter = false,
        MaxSessions = 10,
        MaxEntities = 1000,
    };

    private static NpcContentSnapshot Snapshot()
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };

        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 400001, Name = "会走的货郎", ModelKey = "npc_walker",
            Level = 30, Reaction = (byte)NpcStance.Friendly,
            AiTier = (byte)NpcAiTier.Wanderer, SpeedWalk = 1.6f,
        });

        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 400002, Name = "站桩的门卫", ModelKey = "npc_static",
            Level = 30, Reaction = (byte)NpcStance.Friendly,
            AiTier = (byte)NpcAiTier.Static, SpeedWalk = 1.6f,
        });

        snapshot.SpawnPoints.Add(new NpcSpawnSnapshotDto
        {
            SpawnPointId = 1, NpcId = 400001, X = 0f, Y = 0f, Z = 0f, MaxAlive = 1,
        });
        snapshot.SpawnPoints.Add(new NpcSpawnSnapshotDto
        {
            SpawnPointId = 2, NpcId = 400002, X = 40f, Y = 0f, Z = 0f, MaxAlive = 1,
        });

        return snapshot;
    }

    [Fact]
    public void WandererMoves_StaticNpcStays()
    {
        var (bundle, receipt) = NpcContentLoader.Compile(Snapshot());
        Assert.Equal(0, receipt.ProblemCount);
        NpcContent.Install(bundle);

        try
        {
            var world = new DsWorldSession(Options());
            Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out var reason), reason);

            var spawned = world.ApplyNpcContentSpawns();
            Assert.Equal(2, spawned.Spawned);
            Assert.Equal(0, spawned.SkippedHostile);

            var templateOf = new Dictionary<ulong, int>();
            var lastXy = new Dictionary<ulong, (float X, float Y)>();

            // 五秒（300 拍）：AI 的 Idle→Patrol 需要 3 秒，之前只跑 2 秒会测成"从没起身"。
            for (var tick = 0; tick < 300; tick++)
            {
                var result = world.Tick();
                var snapshot = result.WorldSnapshot;
                if (snapshot?.Deltas == null) continue;

                foreach (var delta in snapshot.Deltas)
                {
                    if (delta.Kind == EntityDeltaKind.Despawn) continue;

                    if (delta.Kind == EntityDeltaKind.Spawn && delta.Identity is { } identity)
                        templateOf[delta.EntityId] = identity.TemplateId;

                    if (delta.Transform is { } transform)
                        lastXy[delta.EntityId] = (transform.X, transform.Y);
                }
            }

            Assert.True(templateOf.Count >= 2, $"快照里没看到两个 NPC 的身份（只见到 {templateOf.Count} 个实体）");

            var wandererMoved = 0f;
            var staticMoved = 0f;
            var sawWanderer = false;
            var sawStatic = false;

            foreach (var pair in templateOf)
            {
                if (!lastXy.TryGetValue(pair.Key, out var pos)) continue;

                if (pair.Value == 400001)
                {
                    sawWanderer = true;
                    wandererMoved = MathF.Sqrt(pos.X * pos.X + pos.Y * pos.Y); // 出生点在原点
                }
                else if (pair.Value == 400002)
                {
                    sawStatic = true;
                    staticMoved = MathF.Sqrt((pos.X - 40f) * (pos.X - 40f) + pos.Y * pos.Y);
                }
            }

            Assert.True(sawWanderer && sawStatic, "快照里没同时看到会走与站桩的两个 NPC");
            _log.WriteLine($"wanderer_moved={wandererMoved:0.###}m static_moved={staticMoved:0.###}m");

            Assert.True(wandererMoved > 0.3f, $"会踱步的 NPC 五秒内几乎没动：{wandererMoved:0.###}m——生成参数可能被静默丢弃");
            Assert.True(staticMoved < 0.05f, $"站桩 NPC 不该移动：{staticMoved:0.###}m");
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }
}
