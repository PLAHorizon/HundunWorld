using System.Collections.Generic;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Sim;
using Horizon.Game.Sim.Npcs;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 敌对 NPC 的 **AI 档位必须影响战斗行为**：会还手/会追的档才主动索敌。
/// <para>
/// 不测这条的话，"AI 智能程度"在敌对 NPC 身上完全无表现——站桩山贼与精英山贼一模一样，
/// 编辑器里选哪一档都只改了一个没人读的数字。
/// </para>
/// </summary>
[Collection("monster-content")]
public class NpcAiTierRuntimeTests
{
    private readonly ITestOutputHelper _log;

    public NpcAiTierRuntimeTests(ITestOutputHelper log) => _log = log;

    private static DsWorldSessionOptions Options() => new()
    {
        ShardId = 0,
        TicksPerSecond = 20,
        SnapshotIntervalTicks = 1,
        EnableAoiFilter = false,
        MaxSessions = 10,
        MaxEntities = 100,
    };

    private static NpcContentSnapshot Snapshot()
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };

        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 500001,
            Name = "木桩山贼",
            Level = 1,
            Reaction = (byte)NpcStance.Hostile,
            AiTier = (byte)NpcAiTier.Static,   // 不还手、不追
            AlertRadiusMeters = 18,
        });

        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 500002,
            Name = "悍匪头目",
            Level = 1,
            Reaction = (byte)NpcStance.Hostile,
            AiTier = (byte)NpcAiTier.Elite,    // 还手 + 追击
            AlertRadiusMeters = 18,
        });

        snapshot.SpawnPoints.Add(new NpcSpawnSnapshotDto
        {
            SpawnPointId = 1, NpcId = 500001, X = 8f, Y = 0f, Z = 0f, MaxAlive = 1,
        });
        snapshot.SpawnPoints.Add(new NpcSpawnSnapshotDto
        {
            SpawnPointId = 2, NpcId = 500002, X = 0f, Y = 8f, Z = 0f, MaxAlive = 1,
        });

        return snapshot;
    }

    [Fact]
    public void OnlyAggressiveTiers_AcquirePlayerTarget()
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
            Assert.Equal(2, spawned.SkippedHostile);

            ulong passive = 0UL, aggressive = 0UL;
            var byTemplate = new Dictionary<ulong, int>();

            for (var tick = 0; tick < 200; tick++)
            {
                var result = world.Tick();
                var deltas = result.WorldSnapshot?.Deltas;
                if (deltas == null) continue;
                foreach (var delta in deltas)
                {
                    if (delta.Identity is { } identity)
                        byTemplate[delta.EntityId] = identity.TemplateId;
                }
            }

            foreach (var pair in byTemplate)
            {
                if (pair.Value == 500001) passive = pair.Key;
                else if (pair.Value == 500002) aggressive = pair.Key;
            }

            Assert.True(passive != 0UL && aggressive != 0UL, "快照里没同时认出两只敌对 NPC");
            Assert.True(world.TryGetMonster(passive, out var passiveMonster));
            Assert.True(world.TryGetMonster(aggressive, out var aggressiveMonster));

            _log.WriteLine($"passive state={passiveMonster.Ai.State} threat={passiveMonster.Ai.ThreatTargetId}");
            _log.WriteLine($"aggressive state={aggressiveMonster.Ai.State} threat={aggressiveMonster.Ai.ThreatTargetId}");

            Assert.Equal(0UL, passiveMonster.Ai.ThreatTargetId);
            Assert.Equal(1001UL, aggressiveMonster.Ai.ThreatTargetId);

            // 站桩档也不该"瞎晃"：会踱步是 Wanders 位的事，不是怪物点的默认 6 米。
            var passiveDrift = System.MathF.Sqrt(
                (passiveMonster.Ai.X - 8f) * (passiveMonster.Ai.X - 8f)
                + passiveMonster.Ai.Y * passiveMonster.Ai.Y);
            _log.WriteLine($"passive_drift={passiveDrift:0.###}m");
            Assert.True(passiveDrift < 0.2f, $"站桩档的敌对 NPC 不该走动：{passiveDrift:0.###}m");
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    /// <summary>
    /// `FacePlayer` 位：会看人的 NPC 转向玩家，站桩档不动朝向。
    /// <para>
    /// 只断言 <c>Yaw</c>、不断言位置：转身与走动是两件事——友善 NPC 一靠近就挪位置，
    /// 是我们最不希望看到的"热情"。
    /// </para>
    /// </summary>
    [Fact]
    public void FacePlayerTier_TurnsTowardPlayer_StaticDoesNot()
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 400001, Name = "会看人的货郎", Level = 30,
            Reaction = (byte)NpcStance.Friendly,
            AiTier = (byte)NpcAiTier.Aware,        // FacePlayer
        });
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 400002, Name = "不看人的门卫", Level = 30,
            Reaction = (byte)NpcStance.Friendly,
            AiTier = (byte)NpcAiTier.Static,       // 无位
        });
        snapshot.SpawnPoints.Add(new NpcSpawnSnapshotDto
        {
            SpawnPointId = 1, NpcId = 400001, X = 0f, Y = 0f, Z = 0f, MaxAlive = 1, Yaw = 0f,
        });
        snapshot.SpawnPoints.Add(new NpcSpawnSnapshotDto
        {
            SpawnPointId = 2, NpcId = 400002, X = 0f, Y = 10f, Z = 0f, MaxAlive = 1, Yaw = 0f,
        });

        var (bundle, receipt) = NpcContentLoader.Compile(snapshot);
        Assert.Equal(0, receipt.ProblemCount);
        NpcContent.Install(bundle);

        try
        {
            var world = new DsWorldSession(Options());
            // 玩家站在两只 NPC 的北侧：会看人的那只应转向 +Y（yaw ≈ π/2）。
            Assert.True(world.AddPlayer(1001, 1001, 0f, 6f, 0f, out var reason), reason);
            world.ApplyNpcContentSpawns();

            var templateOf = new Dictionary<ulong, int>();
            var yawOf = new Dictionary<ulong, float>();
            var posOf = new Dictionary<ulong, (float X, float Y)>();

            for (var tick = 0; tick < 30; tick++)
            {
                var result = world.Tick();
                var deltas = result.WorldSnapshot?.Deltas;
                if (deltas == null) continue;
                foreach (var delta in deltas)
                {
                    if (delta.Identity is { } identity) templateOf[delta.EntityId] = identity.TemplateId;
                    if (delta.Transform is { } t)
                    {
                        yawOf[delta.EntityId] = t.Yaw;
                        posOf[delta.EntityId] = (t.X, t.Y);
                    }
                }
            }

            ulong aware = 0UL, staticNpc = 0UL;
            foreach (var pair in templateOf)
            {
                if (pair.Value == 400001) aware = pair.Key;
                else if (pair.Value == 400002) staticNpc = pair.Key;
            }

            Assert.True(aware != 0UL && staticNpc != 0UL, "快照里没同时认出两只 NPC");
            Assert.True(yawOf.TryGetValue(aware, out var awareYaw));
            Assert.True(yawOf.TryGetValue(staticNpc, out var staticYaw));

            _log.WriteLine($"aware_yaw={awareYaw:0.###} static_yaw={staticYaw:0.###}");
            Assert.Equal(System.MathF.PI / 2f, awareYaw, 2);   // 面向 +Y
            Assert.Equal(0f, staticYaw, 2);

            // 转身不等于挪窝：两人都该留在出生点附近。
            Assert.True(posOf.TryGetValue(aware, out var awarePos));
            Assert.True(System.MathF.Abs(awarePos.X) < 0.05f && System.MathF.Abs(awarePos.Y) < 0.05f,
                $"会看人的 NPC 不该因此走动：({awarePos.X:0.###},{awarePos.Y:0.###})");
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }
}
