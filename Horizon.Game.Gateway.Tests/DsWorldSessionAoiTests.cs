using System;
using System.Linq;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Sync;
using Horizon.Game.Sim;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

public class DsWorldSessionAoiTests
{
    private static DsWorldSessionOptions MakeAoiOptions(
        bool enableAoi = true,
        int chunkSize = 16,
        int radiusChunks = 28,
        int maxSessions = 5000) => new()
    {
        ShardId = 0,
        TicksPerSecond = 60,
        SnapshotIntervalTicks = 1,
        EnableAoiFilter = enableAoi,
        AoiChunkSize = chunkSize,
        AoiRadiusChunks = radiusChunks,
        MaxSessions = maxSessions,
    };

    [Fact]
    public void AoiDisabled_Tick_ReturnsWorldSnapshot_AndNullSnapshotGroups()
    {
        var world = new DsWorldSession(MakeAoiOptions(enableAoi: false));
        Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out var r1), r1);
        Assert.True(world.AddPlayer(1002, 1002, 5000f, 5000f, 0f, out var r2), r2);

        // 第 1 个快照节拍：两个会话均为"晚加入待补发"——共享快照受众为空，
        // 各自收到专属强制 Spawn 快照（所有实体 Kind=Spawn + Identity）。
        var joinTick = world.Tick();
        Assert.NotNull(joinTick.WorldSnapshot);
        Assert.Null(joinTick.SnapshotGroups);
        Assert.Empty(joinTick.AudienceSessionIds);
        Assert.Equal(2, joinTick.WorldSnapshot!.Deltas.Length);
        Assert.NotNull(joinTick.InitialSpawnSnapshots);
        Assert.Equal(new long[] { 1001L, 1002L },
            joinTick.InitialSpawnSnapshots!.Select(s => s.SessionId).ToArray());
        foreach (var initial in joinTick.InitialSpawnSnapshots)
        {
            Assert.Equal(2, initial.Snapshot.Deltas.Length);
            Assert.All(initial.Snapshot.Deltas, d => Assert.Equal(EntityDeltaKind.Spawn, d.Kind));
            Assert.All(initial.Snapshot.Deltas, d => Assert.NotNull(d.Identity));
        }

        // 第 2 个快照节拍：补发完成，回落到共享快照（受众 = 全部会话）。
        var steadyTick = world.Tick();
        Assert.Equal(new long[] { 1001, 1002 }, steadyTick.AudienceSessionIds);
        Assert.Null(steadyTick.InitialSpawnSnapshots);
        Assert.Equal(2, steadyTick.WorldSnapshot!.Deltas.Length);
    }

    [Fact]
    public void AoiEnabled_TwoPlayersFarApart_ProducesTwoGroups_EachWithSingleAudience()
    {
        var world = new DsWorldSession(MakeAoiOptions(enableAoi: true));
        Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out var r1), r1);
        Assert.True(world.AddPlayer(1002, 1002, 10000f, 10000f, 0f, out var r2), r2);

        // 第 1 个快照节拍（晚加入补发）：两会话都走专属快照（相距 10km 互不可见，各见 1 个实体）。
        var joinTick = world.Tick();
        Assert.Null(joinTick.WorldSnapshot);
        Assert.NotNull(joinTick.SnapshotGroups);
        Assert.Empty(joinTick.SnapshotGroups!);
        Assert.NotNull(joinTick.InitialSpawnSnapshots);
        Assert.Equal(new long[] { 1001L, 1002L },
            joinTick.InitialSpawnSnapshots!.Select(s => s.SessionId).ToArray());
        Assert.All(joinTick.InitialSpawnSnapshots, s => Assert.Single(s.Snapshot.Deltas));

        // 第 2 个快照节拍：回落分桶路径——2 组、每组单受众。
        var result = world.Tick();
        Assert.Null(result.WorldSnapshot);
        Assert.NotNull(result.SnapshotGroups);
        Assert.Equal(2, result.SnapshotGroups!.Count);
        foreach (var group in result.SnapshotGroups)
        {
            Assert.Single(group.AudienceSessionIds);
            Assert.Single(group.Snapshot.Deltas);
        }
        Assert.Equal(1001L, result.SnapshotGroups[0].AudienceSessionIds[0]);
        Assert.Equal(1002L, result.SnapshotGroups[1].AudienceSessionIds[0]);
        Assert.Null(result.InitialSpawnSnapshots);
    }

    [Fact]
    public void AoiEnabled_TwoPlayersCloseTogether_ProducesOneGroupWithBothAudiences()
    {
        var world = new DsWorldSession(MakeAoiOptions(enableAoi: true));
        Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out var r1), r1);
        Assert.True(world.AddPlayer(1002, 1002, 10f, 10f, 0f, out var r2), r2);

        // 第 1 个快照节拍（晚加入补发）：两会话各收到含双方实体的强制 Spawn 快照。
        var joinTick = world.Tick();
        Assert.NotNull(joinTick.SnapshotGroups);
        Assert.Empty(joinTick.SnapshotGroups!);
        Assert.NotNull(joinTick.InitialSpawnSnapshots);
        Assert.Equal(2, joinTick.InitialSpawnSnapshots!.Count);
        Assert.All(joinTick.InitialSpawnSnapshots, s => Assert.Equal(2, s.Snapshot.Deltas.Length));

        // 第 2 个快照节拍：回落分桶路径——单组、双受众、2 个实体。
        var result = world.Tick();
        Assert.Null(result.WorldSnapshot);
        Assert.NotNull(result.SnapshotGroups);
        Assert.Single(result.SnapshotGroups!);
        var group = result.SnapshotGroups[0];
        Assert.Equal(2, group.AudienceSessionIds.Length);
        Assert.Contains(1001L, group.AudienceSessionIds);
        Assert.Contains(1002L, group.AudienceSessionIds);
        Assert.Equal(2, group.Snapshot.Deltas.Length);
    }

    [Fact]
    public void AoiEnabled_DeterministicOutput_SameInputProducesIdenticalGroups()
    {
        static string Run()
        {
            var world = new DsWorldSession(MakeAoiOptions(enableAoi: true));
            world.AddPlayer(1001, 1001, 0f, 0f, 0f, out _);
            world.AddPlayer(1002, 1002, 100f, 0f, 0f, out _);
            world.AddPlayer(1003, 1003, 5000f, 5000f, 0f, out _);
            var sb = new System.Text.StringBuilder();
            for (var tick = 0; tick < 10; tick++)
            {
                var result = world.Tick();
                if (result.SnapshotGroups is null) continue;
                sb.Append($"T{result.ServerTick}:G{result.SnapshotGroups.Count}[");
                foreach (var g in result.SnapshotGroups)
                {
                    sb.Append('(');
                    sb.Append(string.Join(",", g.AudienceSessionIds));
                    sb.Append("):D");
                    sb.Append(string.Join(",", g.Snapshot.Deltas.Select(d => d.EntityId)));
                    sb.Append(';');
                }
                sb.Append(']');
            }
            return sb.ToString();
        }
        var a = Run();
        var b = Run();
        Assert.Equal(a, b);
        Assert.True(a.Length > 50);
    }

    [Fact]
    public void AddPlayer_ExceedsMaxSessions_ReturnsFalseWithShardFullReason()
    {
        var world = new DsWorldSession(MakeAoiOptions(enableAoi: true, maxSessions: 2));
        Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out _));
        Assert.True(world.AddPlayer(1002, 1002, 10f, 0f, 0f, out _));
        Assert.False(world.AddPlayer(1003, 1003, 20f, 0f, 0f, out var reason));
        Assert.Equal("shard_full", reason);
        Assert.Equal(2, world.PlayerCount);
        Assert.Equal(1, world.RejectedHandshakeCount);
    }

    [Fact]
    public void AoiEnabled_MonstersNearPlayer_AppearInPlayerGroup()
    {
        var world = new DsWorldSession(MakeAoiOptions(enableAoi: true));
        var cfg = SpawnPointConfig.Default;
        cfg.SpawnPointId = 1;
        cfg.ShardId = 0;
        cfg.X = 5f;
        cfg.Y = 0f;
        cfg.MaxAlive = 2;
        cfg.MonsterTemplateId = 101;
        cfg.RespawnDelaySeconds = 30f;
        cfg.AggroRadius = 15f;
        cfg.LeashRadius = 40f;
        cfg.MoveSpeed = 4f;
        cfg.SlotSpacing = 6f;
        world.ConfigureSpawnPoints(new[] { cfg });
        world.AddPlayer(1001, 1001, 0f, 0f, 0f, out _);

        // 第 1 个快照节拍：唯一会话晚加入 → 专属补发快照（自己 + 刷出的 2 只怪，全部 Spawn）。
        var joinTick = world.Tick();
        Assert.NotNull(joinTick.SnapshotGroups);
        Assert.Empty(joinTick.SnapshotGroups!);
        var initial = Assert.Single(joinTick.InitialSpawnSnapshots!);
        Assert.Equal(1001L, initial.SessionId);
        Assert.Equal(3, initial.Snapshot.Deltas.Length);
        Assert.All(initial.Snapshot.Deltas, d => Assert.Equal(EntityDeltaKind.Spawn, d.Kind));

        // 第 2 个快照节拍：回落分桶路径——单组、受众 [1001]、3 个实体（含怪）。
        var result = world.Tick();
        Assert.NotNull(result.SnapshotGroups);
        var group = Assert.Single(result.SnapshotGroups!);
        Assert.Equal(3, group.Snapshot.Deltas.Length);
        Assert.Single(group.AudienceSessionIds);
        Assert.Equal(1001L, group.AudienceSessionIds[0]);
    }

    [Fact]
    public void LateJoiner_ReceivesForcedSpawn_ForPreExistingEntities()
    {
        // 回归：多客户端互相不可见的根因——晚加入会话拿不到已存在实体的 Spawn（实体级全局标志）。
        // 修复后：晚加入会话在下一快照节拍收到专属全量强制 Spawn 快照（含已存在玩家的 Identity）。
        var world = new DsWorldSession(MakeAoiOptions(enableAoi: true));
        Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out _));

        // 让 1001 先“稳定”：两次 tick 使首包补发完成。
        _ = world.Tick();
        _ = world.Tick();

        // 晚加入 1002（与 1001 相距 10m，互相可见）。
        Assert.True(world.AddPlayer(1002, 1002, 10f, 0f, 0f, out _));
        var joinTick = world.Tick();

        // 晚加入者收到专属强制 Spawn：必须包含已存在的玩家 1001（Spawn + Identity）。
        Assert.NotNull(joinTick.InitialSpawnSnapshots);
        var spawnFor1002 = Assert.Single(joinTick.InitialSpawnSnapshots!);
        Assert.Equal(1002L, spawnFor1002.SessionId);
        var deltas = spawnFor1002.Snapshot.Deltas;
        Assert.Equal(2, deltas.Length); // 自己 + 已存在的 1001
        var preExisting = Assert.Single(deltas, d => d.EntityId == 1001UL);
        Assert.Equal(EntityDeltaKind.Spawn, preExisting.Kind);
        Assert.NotNull(preExisting.Identity);

        // 早先的 1001 从分组共享路径收到 1002 的首次 Spawn（互相可见的另一个方向）。
        Assert.NotNull(joinTick.SnapshotGroups);
        var groupFor1001 = Assert.Single(joinTick.SnapshotGroups!, g => g.AudienceSessionIds.Contains(1001L));
        var newcomerDelta = Assert.Single(groupFor1001.Snapshot.Deltas, d => d.EntityId == 1002UL);
        Assert.Equal(EntityDeltaKind.Spawn, newcomerDelta.Kind);
    }

    [Fact]
    public void Reconnect_Resume_ReschedulesInitialSpawn()
    {
        // 回归：重连（同一会话重复握手 / 重连恢复请求）后客户端已重建 ECS 世界，必须重新补发强制 Spawn。
        var world = new DsWorldSession(MakeAoiOptions(enableAoi: false));
        Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out _));
        _ = world.Tick(); // 首次补发完成

        var steady = world.Tick();
        Assert.Null(steady.InitialSpawnSnapshots); // 稳定态无补发

        // 同一会话重复握手（幂等重连）→ 重新登记。
        Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out _));
        var afterRehandshake = world.Tick();
        var spawn = Assert.Single(afterRehandshake.InitialSpawnSnapshots!);
        Assert.Equal(1001L, spawn.SessionId);
        Assert.All(spawn.Snapshot.Deltas, d => Assert.Equal(EntityDeltaKind.Spawn, d.Kind));

        // 重连恢复请求（ScheduleInitialSpawn）同样触发补发。
        Assert.True(world.ScheduleInitialSpawn(1001));
        var afterResume = world.Tick();
        Assert.Single(afterResume.InitialSpawnSnapshots!, s => s.SessionId == 1001L);

        // 未知会话不登记。
        Assert.False(world.ScheduleInitialSpawn(9999));
    }
}