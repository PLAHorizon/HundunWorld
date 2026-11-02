using System;
using System.Collections.Generic;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Sync;
using Horizon.Game.Sim.Npcs;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 追击使用 **`speed_run`**：同档、同距离的两只敌对 NPC，跑速 8 的那只接近玩家的速度必须明显更快。
/// <para>
/// 以前 `speed_run` 只服务逃跑，追击仍按走速（`speed_walk`）——玩家看到的是"山贼冲刺和散步一样快"。
/// 缺省 0 = 退回走速，所以怪物模板与金轨迹逐字不变；只有内容 NPC 的刷点会填这一格。
/// </para>
/// </summary>
[Collection("monster-content")]
public class NpcChaseRunSpeedTests
{
    private readonly ITestOutputHelper _log;

    public NpcChaseRunSpeedTests(ITestOutputHelper log) => _log = log;

    private static DsWorldSessionOptions Options() => new()
    {
        ShardId = 0,
        TicksPerSecond = 10,
        SnapshotIntervalTicks = 1,
        EnableAoiFilter = false,
        MaxSessions = 10,
        MaxEntities = 100,
    };

    private static NpcContentSnapshot Snapshot() => new()
    {
        ContentRevision = 1,
        Templates =
        {
            new NpcTemplateSnapshot
            {
                NpcId = 500008, Name = "疾行山贼", Level = 1,
                Reaction = (byte)NpcStance.Hostile, AiTier = (byte)NpcAiTier.Elite,
                SpeedWalk = 1.6f, SpeedRun = 8f,
            },
            new NpcTemplateSnapshot
            {
                NpcId = 500009, Name = "慢跑山贼", Level = 1,
                Reaction = (byte)NpcStance.Hostile, AiTier = (byte)NpcAiTier.Elite,
                // 装配器会把 <=0 归一成默认 4，所以夹具直接用"跑速=走速"当对照（显式的慢跑）。
                SpeedWalk = 1.6f, SpeedRun = 1.6f,
            },
        },
        SpawnPoints =
        {
            new NpcSpawnSnapshotDto { SpawnPointId = 1, NpcId = 500008, X = 9f, Y = 0f, Z = 0f, MaxAlive = 1 },
            new NpcSpawnSnapshotDto { SpawnPointId = 2, NpcId = 500009, X = -9f, Y = 0f, Z = 0f, MaxAlive = 1 },
        },
    };

    private static double DistanceToOrigin(in Horizon.Game.Sim.MonsterAiSelf ai)
        => Math.Sqrt(ai.X * ai.X + ai.Y * ai.Y);

    [Fact]
    public void 追击用跑速_跑得快的接近更明显()
    {
        var (bundle, receipt) = NpcContentLoader.Compile(Snapshot());
        Assert.Equal(0, receipt.ProblemCount);
        NpcContent.Install(bundle);

        try
        {
            var world = new DsWorldSession(Options());
            Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out var reason), reason);
            world.ApplyNpcContentSpawns();

            var ids = new Dictionary<int, ulong>();
            for (var tick = 1; tick <= 10 && ids.Count < 2; tick++)
            {
                var result = world.Tick();
                foreach (var delta in result.WorldSnapshot?.Deltas ?? Array.Empty<EntityDelta>())
                {
                    if (delta.Kind != EntityDeltaKind.Spawn || delta.Identity is not { } identity) continue;
                    if (identity.TemplateId is 500008 or 500009) ids[identity.TemplateId] = delta.EntityId;
                }
            }

            Assert.True(ids.ContainsKey(500008) && ids.ContainsKey(500009), "两只敌对 NPC 没有都生成");
            for (var tick = 0; tick < 5; tick++) world.Tick();

            Assert.True(world.TryGetMonster(ids[500008], out var fast));
            Assert.True(world.TryGetMonster(ids[500009], out var slow));
            Assert.Contains(fast.Ai.State, new[] { Horizon.Game.Sim.MonsterAiState.Chase, Horizon.Game.Sim.MonsterAiState.Attack });
            Assert.Contains(slow.Ai.State, new[] { Horizon.Game.Sim.MonsterAiState.Chase, Horizon.Game.Sim.MonsterAiState.Attack });

            var fastStart = DistanceToOrigin(fast.Ai);
            var slowStart = DistanceToOrigin(slow.Ai);

            for (var tick = 0; tick < 5; tick++) world.Tick();

            world.TryGetMonster(ids[500008], out fast);
            world.TryGetMonster(ids[500009], out slow);
            var fastClosed = fastStart - DistanceToOrigin(fast.Ai);
            var slowClosed = slowStart - DistanceToOrigin(slow.Ai);

            _log.WriteLine($"fast closed={fastClosed:F2} slow closed={slowClosed:F2} " +
                           $"fastState={fast.Ai.State} slowState={slow.Ai.State}");

            Assert.True(slowClosed > 0.2, $"慢跑的那只没有接近玩家：{slowClosed:F2}");
            Assert.True(fastClosed > slowClosed * 2.0,
                $"跑速 8 的接近速度没有明显快于走速：fast={fastClosed:F2} slow={slowClosed:F2}");
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 内核跑速判据_零退回走速_不能动仍不能动()
    {
        var cfg = new Horizon.Game.Sim.MonsterAiConfig { MoveSpeed = 1.6f, RunSpeed = 0f };
        Assert.Equal(1.6f, Horizon.Game.Sim.MonsterAiConfig.ChaseSpeed(in cfg));

        cfg.RunSpeed = 8f;
        Assert.Equal(8f, Horizon.Game.Sim.MonsterAiConfig.ChaseSpeed(in cfg));

        // 站桩档（MoveSpeed=0）不会因为多了跑速而开始追人。
        cfg.MoveSpeed = 0f;
        Assert.Equal(0f, Horizon.Game.Sim.MonsterAiConfig.ChaseSpeed(in cfg));
    }
}
