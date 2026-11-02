using System.Collections.Generic;
using System.Linq;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Sync;
using Horizon.Game.Sim.Npcs;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 刷点表的三列以前"写了没人读"：<c>level_override</c>（等级覆盖）与
/// <c>facing_mode</c>（0 固定 / 1 随机 / 2 面向玩家）在运行期完全没有消费者。
/// <para>
/// 症状是运营把某个点改成"面向玩家出生"或"这一只固定 42 级"，游戏里毫无变化，
/// 而编辑器/库里那一格看起来明明配好了。
/// </para>
/// </summary>
[Collection("monster-content")]
public class NpcSpawnExtrasTests
{
    private readonly ITestOutputHelper _log;

    public NpcSpawnExtrasTests(ITestOutputHelper log) => _log = log;

    private static DsWorldSessionOptions Options() => new()
    {
        ShardId = 0,
        TicksPerSecond = 20,
        SnapshotIntervalTicks = 1,
        EnableAoiFilter = false,
        MaxSessions = 10,
        MaxEntities = 100,
    };

    private static NpcContentSnapshot Snapshot(
        int npcId, byte stance, byte facingMode, float yaw, int levelOverride,
        int levelVariance = 0, int spawnPointId = 1)
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = npcId,
            Name = "测试 NPC",
            Level = 30,
            LevelVariance = levelVariance,
            Reaction = stance,
            AiTier = (byte)NpcAiTier.Static,   // 没有 FacePlayer 位，朝向只由刷点决定
        });
        snapshot.SpawnPoints.Add(new NpcSpawnSnapshotDto
        {
            SpawnPointId = spawnPointId, NpcId = npcId,
            X = 0f, Y = 0f, Z = 0f,
            Yaw = yaw,
            FacingMode = facingMode,
            LevelOverride = levelOverride,
            MaxAlive = 1,
        });
        return snapshot;
    }

    private static (int Level, float Yaw) SpawnAndRead(NpcContentSnapshot snapshot, float playerY = 5f)
    {
        var (bundle, receipt) = NpcContentLoader.Compile(snapshot);
        Assert.Equal(0, receipt.ProblemCount);
        NpcContent.Install(bundle);

        var world = new DsWorldSession(Options());
        Assert.True(world.AddPlayer(1001, 1001, 0f, playerY, 0f, out var reason), reason);
        world.ApplyNpcContentSpawns();

        int level = 0;
        float yaw = float.NaN;
        for (var tick = 0; tick < 5; tick++)
        {
            var result = world.Tick();
            var deltas = result.WorldSnapshot?.Deltas;
            if (deltas == null) continue;
            foreach (var delta in deltas)
            {
                if (delta.Identity == null) continue;
                if (delta.State is { } state) level = state.Level;
                if (delta.Transform is { } transform) yaw = transform.Yaw;
            }
        }

        return (level, yaw);
    }

    [Fact]
    public void LevelOverride_IsApplied()
    {
        try
        {
            var (level, _) = SpawnAndRead(Snapshot(400001, (byte)NpcStance.Friendly, 0, 0f, 42));
            _log.WriteLine("level=" + level);
            Assert.Equal(42, level);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void FacingMode0_UsesConfiguredYaw()
    {
        try
        {
            var (_, yaw) = SpawnAndRead(Snapshot(400001, (byte)NpcStance.Friendly, 0, 1.0f, 0));
            _log.WriteLine("yaw=" + yaw);
            Assert.Equal(1.0f, yaw, 2);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void FacingMode2_FacesNearestPlayer()
    {
        try
        {
            // 玩家在正北（+Y）：朝向应为 π/2。
            var (_, yaw) = SpawnAndRead(Snapshot(400001, (byte)NpcStance.Friendly, 2, 0f, 0));
            _log.WriteLine("yaw=" + yaw);
            Assert.Equal(System.MathF.PI / 2f, yaw, 2);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void FacingMode1_IsDeterministicAcrossSessions()
    {
        float first;
        try
        {
            (_, first) = SpawnAndRead(Snapshot(400001, (byte)NpcStance.Friendly, 1, 0f, 0));
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }

        float second;
        try
        {
            (_, second) = SpawnAndRead(Snapshot(400001, (byte)NpcStance.Friendly, 1, 0f, 0));
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }

        _log.WriteLine($"first={first:0.###} second={second:0.###}");
        Assert.Equal(first, second, 4);
        Assert.InRange(first, 0f, System.MathF.PI * 2f);
    }

    /// <summary>
    /// 等级浮动（<c>level_variance</c>）：基准 30 ± 5，同一刷点必须稳定、不同刷点应出现不同值，
    /// 且全体落在闭区间内。以前这一列零读者：编辑器里配了"浮动"，游戏里永远是基准级。
    /// </summary>
    [Fact]
    public void LevelVariance_RollsWithinRangeAndVariesBySpawnPoint()
    {
        var (bundle, receipt) = NpcContentLoader.Compile(
            Snapshot(400021, (byte)NpcStance.Friendly, 0, 0f, 0, levelVariance: 5));
        Assert.Equal(0, receipt.ProblemCount);
        Assert.True(bundle.ByNpcId.TryGetValue(400021, out var cfg));

        int Roll(int spawnPointId) => new NpcSpawnSnapshot(
            spawnPointId, 0, 0, 0f, 0f, 0f, 0f, 400021, 0, 1, 0f, 0, 0).ResolveLevel(cfg!);

        var values = new HashSet<int>();
        for (var id = 1; id <= 40; id++)
        {
            var value = Roll(id);
            values.Add(value);
            Assert.InRange(value, 25, 35);
        }

        _log.WriteLine($"distinct={values.Count} values=[{string.Join(",", values.OrderBy(v => v))}]");
        Assert.True(values.Count >= 2, "40 个刷点掷出同一个等级＝没有真的掷（或种子写死）");
        Assert.Equal(Roll(7), Roll(7));   // 同一点复活/重生成必须同值
    }

    [Fact]
    public void LevelVariance_IsAppliedToSpawnedNpc()
    {
        try
        {
            // 选一个掷出来**不等于基准**的刷点，否则"应用了浮动"与"没应用"在读数上同形。
            var (probe, _) = NpcContentLoader.Compile(
                Snapshot(400022, (byte)NpcStance.Friendly, 0, 0f, 0, levelVariance: 5));
            var cfg = probe.ByNpcId[400022];
            var spawnPointId = 1;
            for (; spawnPointId <= 40; spawnPointId++)
            {
                var value = new NpcSpawnSnapshot(spawnPointId, 0, 0, 0f, 0f, 0f, 0f, 400022, 0, 1, 0f, 0, 0)
                    .ResolveLevel(cfg);
                if (value != cfg.LevelBase) break;
            }

            Assert.True(spawnPointId <= 40, "40 个刷点都没掷出非基准值，夹具失效");
            var expected = new NpcSpawnSnapshot(spawnPointId, 0, 0, 0f, 0f, 0f, 0f, 400022, 0, 1, 0f, 0, 0)
                .ResolveLevel(cfg);

            var (level, _) = SpawnAndRead(
                Snapshot(400022, (byte)NpcStance.Friendly, 0, 0f, 0, levelVariance: 5, spawnPointId: spawnPointId));

            _log.WriteLine($"spawn={spawnPointId} expected={expected} actual={level}");
            Assert.Equal(expected, level);
            Assert.NotEqual(cfg.LevelBase, level);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void LevelVarianceZero_UsesBase()
    {
        try
        {
            var (level, _) = SpawnAndRead(Snapshot(400023, (byte)NpcStance.Friendly, 0, 0f, 0, levelVariance: 0));
            _log.WriteLine("level=" + level);
            Assert.Equal(30, level);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }
}
