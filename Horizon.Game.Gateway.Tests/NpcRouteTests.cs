using System;
using System.Collections.Generic;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Sync;
using Horizon.Game.Sim.Npcs;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 内容 NPC 的 waypoint 路线必须真的被读到。
/// <para>
/// 判据取"走得比踱步圈远"：线路第二点在 30 米外，而踱步半径只有 6 米——
/// 只要 NPC 越过 10 米，就只能是沿线路走的，不可能是绕出生点踱步。
/// （窗口按机制常量算：Idle→Patrol 3 秒 + 30 米 / 1.6 m/s ≈ 19 秒，这里给 25 秒余量。）
/// </para>
/// </summary>
/// <remarks>本类改写进程级 NPC 内容，必须与怪物内容测试串行。</remarks>
[Collection("monster-content")]
public class NpcRouteTests
{
    private readonly ITestOutputHelper _log;

    public NpcRouteTests(ITestOutputHelper log) => _log = log;

    private static DsWorldSessionOptions Options() => new()
    {
        ShardId = 0,
        TicksPerSecond = 60,
        SnapshotIntervalTicks = 1,
        EnableAoiFilter = false,
        MaxSessions = 10,
        MaxEntities = 1000,
    };

    [Fact]
    public void NpcFollowsWaypointRouteBeyondWanderRadius()
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };

        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 400001, Name = "巡街货郎", ModelKey = "npc_patrol",
            Level = 30, Reaction = (byte)NpcStance.Friendly,
            AiTier = (byte)NpcAiTier.Patrol, SpeedWalk = 1.6f,
        });

        snapshot.SpawnPoints.Add(new NpcSpawnSnapshotDto
        {
            SpawnPointId = 1, NpcId = 400001, X = 0f, Y = 0f, Z = 0f, MaxAlive = 1, RouteId = 7,
        });

        var route = new NpcRouteSnapshot { PathId = 7, Name = "巡街", Mode = 1, PauseAtPointSeconds = 0f, UseNavMesh = false };
        route.Points.Add(new NpcRoutePointSnapshot { Seq = 0, X = 0f, Y = 0f, Z = 0f });
        route.Points.Add(new NpcRoutePointSnapshot { Seq = 1, X = 30f, Y = 0f, Z = 0f });
        snapshot.Routes.Add(route);

        var (bundle, receipt) = NpcContentLoader.Compile(snapshot);
        Assert.Equal(0, receipt.ProblemCount);
        NpcContent.Install(bundle);

        try
        {
            var world = new DsWorldSession(Options());
            Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out var reason), reason);

            var spawned = world.ApplyNpcContentSpawns();
            Assert.Equal(1, spawned.Spawned);

            ulong npcEntity = 0;
            var maxX = 0f;

            // 25 秒：Idle 3 秒 + 30 米路程 / 1.6 m/s ≈ 19 秒，留余量。
            for (var tick = 0; tick < 1500; tick++)
            {
                var result = world.Tick();
                var snap = result.WorldSnapshot;
                if (snap?.Deltas == null) continue;

                foreach (var delta in snap.Deltas)
                {
                    if (delta.Kind == EntityDeltaKind.Despawn) continue;
                    if (delta.Kind == EntityDeltaKind.Spawn && delta.Identity is { } identity && identity.TemplateId == 400001)
                        npcEntity = delta.EntityId;

                    if (npcEntity != 0 && delta.EntityId == npcEntity && delta.Transform is { } transform)
                    {
                        if (transform.X > maxX) maxX = transform.X;
                    }
                }
            }

            _log.WriteLine($"npc_entity={npcEntity} max_x={maxX:0.###}m");

            Assert.NotEqual(0UL, npcEntity);
            Assert.True(maxX > 10f,
                $"NPC 最远只走到 {maxX:0.###}m（踱步半径 6m）——线路没有被读到，或线路解析失败");
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }
}
