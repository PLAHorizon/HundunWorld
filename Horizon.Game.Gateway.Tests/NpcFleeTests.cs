using System.Collections.Generic;
using System.Linq;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Sync;
using Horizon.Game.Sim;
using Horizon.Game.Sim.Npcs;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// NPC 的"残血逃跑"（<see cref="NpcAiBits.FleesAtLowHealth"/>）。
/// <para>
/// 判据在内核（阈值 25%、活着、上限已知），行为在 DS：进入追击/攻击、血量 ≤ 阈值时
/// **朝远离当前威胁的方向跑且本 tick 不出手**。以前这一位只在编辑器里可选、运行期没有任何消费者，
/// 玩家看到的是"精英山贼和站桩山贼一样，血到底也照样跟你对砍"。
/// </para>
/// <para>
/// 同夹具里放一只**不带这一位**的敌对 NPC 作正对照：它同样残血、同样在战斗，但不跑——
/// 否则"跑了"可能只是"残血本来就乱走"。
/// </para>
/// </summary>
[Collection("monster-content")]
public class NpcFleeTests
{
    private readonly ITestOutputHelper _log;

    public NpcFleeTests(ITestOutputHelper log) => _log = log;

    private static DsWorldSessionOptions Options() => new()
    {
        ShardId = 0,
        TicksPerSecond = 10,
        SnapshotIntervalTicks = 1,
        EnableAoiFilter = false,
        MaxSessions = 10,
        MaxEntities = 100,
    };

    // ────────────────────────────── 内核判据 ──────────────────────────────

    [Fact]
    public void 阈值边界_等于阈值也逃_多一滴就不逃()
    {
        Assert.True(NpcFleeCore.IsLowHealth(25, 100));
        Assert.False(NpcFleeCore.IsLowHealth(26, 100));

        // 小数字也要按比例判：1/4 = 25% 算残血，1/3 = 33% 不算——
        // "1 滴血"本身不是判据（满血的小怪也是 1 滴血）。
        Assert.True(NpcFleeCore.IsLowHealth(1, 4));
        Assert.False(NpcFleeCore.IsLowHealth(1, 3));
        Assert.False(NpcFleeCore.IsLowHealth(4, 4));
    }

    [Fact]
    public void 尸体与未知上限不逃()
    {
        Assert.False(NpcFleeCore.IsLowHealth(0, 100));
        Assert.False(NpcFleeCore.IsLowHealth(-5, 100));
        Assert.False(NpcFleeCore.IsLowHealth(10, 0));
    }

    [Fact]
    public void 大血量不因乘法溢出被判成残血()
    {
        // int 相乘会溢出成负数 ⇒ 满血被当成残血。判据用 long 乘就是为了这条。
        Assert.False(NpcFleeCore.IsLowHealth(int.MaxValue, int.MaxValue));
        Assert.True(NpcFleeCore.IsLowHealth(1, int.MaxValue));
    }

    [Fact]
    public void 能力位关掉就不逃()
    {
        Assert.False(NpcFleeCore.ShouldFlee(false, 1, 100));
        Assert.True(NpcFleeCore.ShouldFlee(true, 1, 100));
    }

    [Fact]
    public void 阈值常量就是25()
    {
        // 改这个数字等于改玩家看到的手感：要么先改文档与验收，要么别动。
        Assert.Equal(25, NpcFleeCore.HpPercentThreshold);
    }

    // ────────────────────────────── DS 行为 ──────────────────────────────

    private static NpcContentSnapshot Snapshot()
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };

        // 精英档（含 FleesAtLowHealth）：跑到 +2 处，离玩家 2 米。
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 500001, Name = "逃跑山贼", Level = 1,
            Reaction = (byte)NpcStance.Hostile, AiTier = (byte)NpcAiTier.Elite,
        });
        snapshot.SpawnPoints.Add(new NpcSpawnSnapshotDto
        {
            SpawnPointId = 1, NpcId = 500001, X = 2f, Y = 0f, Z = 0f, MaxAlive = 1,
        });

        // 自卫档（无 FleesAtLowHealth）：同样残血、同样在战斗，但不跑——正对照。
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 500003, Name = "硬扛山贼", Level = 1,
            Reaction = (byte)NpcStance.Hostile, AiTier = (byte)NpcAiTier.Defensive,
        });
        snapshot.SpawnPoints.Add(new NpcSpawnSnapshotDto
        {
            SpawnPointId = 2, NpcId = 500003, X = -2f, Y = 0f, Z = 0f, MaxAlive = 1,
        });

        return snapshot;
    }

    [Fact]
    public void 残血精英远离玩家_同夹具的硬扛怪不跑()
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
                    if (identity.TemplateId is 500001 or 500003) ids[identity.TemplateId] = delta.EntityId;
                }
            }

            Assert.True(ids.ContainsKey(500001) && ids.ContainsKey(500003), "两只敌对 NPC 没有都生成");

            // 先确认两只都真的进入战斗（否则"没跑"可能只是"根本没打起来"）。
            for (var tick = 0; tick < 5; tick++) world.Tick();
            Assert.True(world.TryGetMonster(ids[500001], out var flee) && flee.Ai.Hp > 0);
            Assert.True(world.TryGetMonster(ids[500003], out var tank) && tank.Ai.Hp > 0);
            _log.WriteLine($"flee state={flee.Ai.State} hp={flee.Ai.Hp}; tank state={tank.Ai.State} hp={tank.Ai.Hp}");
            Assert.Contains(flee.Ai.State, new[] { MonsterAiState.Chase, MonsterAiState.Attack });
            Assert.Contains(tank.Ai.State, new[] { MonsterAiState.Chase, MonsterAiState.Attack });

            // 两只都打到 10%（上限 100）：一个该跑，一个该继续打。
            var fleeSelf = flee.Ai;
            fleeSelf.Hp = 10;
            flee.Ai = fleeSelf;
            var tankSelf = tank.Ai;
            tankSelf.Hp = 10;
            tank.Ai = tankSelf;

            var fleeStartX = flee.Ai.X;
            var tankStartX = tank.Ai.X;

            for (var tick = 0; tick < 15; tick++) world.Tick();

            world.TryGetMonster(ids[500001], out flee);
            world.TryGetMonster(ids[500003], out tank);
            _log.WriteLine($"after: flee x={flee.Ai.X:F2} (start {fleeStartX:F2}) hp={flee.Ai.Hp}; " +
                           $"tank x={tank.Ai.X:F2} (start {tankStartX:F2}) hp={tank.Ai.Hp}; " +
                           $"fleeCount={world.NpcFleeStartedCount}");

            // 逃跑：朝远离玩家（+X）的方向挪出去，且进过一次"逃跑"。
            Assert.True(flee.Ai.X > fleeStartX + 0.3f,
                $"残血精英没有远离玩家：x {fleeStartX:F2} → {flee.Ai.X:F2}");
            Assert.Equal(1, world.NpcFleeStartedCount);

            // 正对照：同血量、同战斗，但不该被"逃跑"改写——它留在原地附近继续打。
            Assert.True(tank.Ai.X > tankStartX - 1.0f,
                $"没有逃跑能力的怪也被推走了：x {tankStartX:F2} → {tank.Ai.X:F2}");
            Assert.True(world.TryGetPlayer(1001, out var player) && player.Hp < player.MaxHp,
                "硬扛怪应该在这 15 拍里继续造成伤害（玩家血量没掉）");
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    /// <summary>
    /// 逃跑速度必须读 **`speed_run`**：同档、同残血、同时刻的两只，跑速 8 的那只位移应约为 4 的 1.5 倍以上。
    /// <para>以前 `SpeedRun` 全仓零读者——编辑器里配的跑速对逃跑毫无影响，逃跑永远按行走速度走。</para>
    /// </summary>
    [Fact]
    public void 逃跑使用奔跑速度()
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 500001, Name = "慢跑", Level = 1,
            Reaction = (byte)NpcStance.Hostile, AiTier = (byte)NpcAiTier.Elite,
            SpeedWalk = 1.6f, SpeedRun = 4f,
        });
        snapshot.SpawnPoints.Add(new NpcSpawnSnapshotDto
        {
            SpawnPointId = 1, NpcId = 500001, X = 2f, Y = 0f, Z = 0f, MaxAlive = 1,
        });
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 500007, Name = "快跑", Level = 1,
            Reaction = (byte)NpcStance.Hostile, AiTier = (byte)NpcAiTier.Elite,
            SpeedWalk = 1.6f, SpeedRun = 8f,
        });
        snapshot.SpawnPoints.Add(new NpcSpawnSnapshotDto
        {
            SpawnPointId = 2, NpcId = 500007, X = -2f, Y = 0f, Z = 0f, MaxAlive = 1,
        });

        var (bundle, receipt) = NpcContentLoader.Compile(snapshot);
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
                    if (identity.TemplateId is 500001 or 500007) ids[identity.TemplateId] = delta.EntityId;
                }
            }

            Assert.True(ids.ContainsKey(500001) && ids.ContainsKey(500007), "两只逃跑 NPC 没有都生成");
            for (var tick = 0; tick < 5; tick++) world.Tick();

            Assert.True(world.TryGetMonster(ids[500001], out var slow));
            Assert.True(world.TryGetMonster(ids[500007], out var fast));
            Assert.Contains(slow.Ai.State, new[] { MonsterAiState.Chase, MonsterAiState.Attack });
            Assert.Contains(fast.Ai.State, new[] { MonsterAiState.Chase, MonsterAiState.Attack });

            var slowSelf = slow.Ai;
            slowSelf.Hp = 10;
            slow.Ai = slowSelf;
            var fastSelf = fast.Ai;
            fastSelf.Hp = 10;
            fast.Ai = fastSelf;

            var slowX = slow.Ai.X;
            var fastX = fast.Ai.X;

            for (var tick = 0; tick < 15; tick++) world.Tick();

            world.TryGetMonster(ids[500001], out slow);
            world.TryGetMonster(ids[500007], out fast);
            var slowMoved = System.MathF.Abs(slow.Ai.X - slowX);
            var fastMoved = System.MathF.Abs(fast.Ai.X - fastX);
            _log.WriteLine($"slow moved={slowMoved:F2} fast moved={fastMoved:F2}");

            Assert.True(slowMoved > 0.3f, $"慢跑那只没动：{slowMoved:F2}");
            Assert.True(fastMoved > slowMoved * 1.5f,
                $"跑速 8 的位移没有明显大于跑速 4：slow={slowMoved:F2} fast={fastMoved:F2}");
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }
}
