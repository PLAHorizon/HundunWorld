using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;
using Horizon.Game.Sim;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// MMORPG 开发计划 P3 —— DS 权威世界会话测试（验收门 H3-c / H2 的 DS 侧）。
/// <para>
/// 本套件锁定"世界里真的有东西在跑"：玩家握手进世界、刷怪点按配置生成怪、
/// 怪物按 AI 推进并追击玩家、快照把两者一起下发、修正与 InputAck 定向回正确会话。
/// </para>
/// <para>
/// 这些断言对应的历史缺口是 G6（怪物不进世界快照）与 G10（怪物不会动）——
/// 在此之前 <c>MonsterGrain</c> 除自身文件外全仓零引用，世界上根本没有怪物实体。
/// </para>
/// </summary>
public class DsWorldSessionTests
{
    private static DsWorldSessionOptions MakeOptions() => new DsWorldSessionOptions
    {
        ShardId = 0,
        TicksPerSecond = 60,
        SnapshotIntervalTicks = 3,
    };

    private static SpawnPointConfig MakeSpawnPoint(int id = 1, int maxAlive = 2, float x = 0f)
    {
        var cfg = SpawnPointConfig.Default;
        cfg.SpawnPointId = id;
        cfg.ShardId = 0;
        cfg.X = x;
        cfg.Y = 0f;
        cfg.MaxAlive = maxAlive;
        cfg.MonsterTemplateId = 100 + id;
        cfg.RespawnDelaySeconds = 5f;
        cfg.AggroRadius = 15f;
        cfg.LeashRadius = 40f;
        // 注：攻击射程由 MonsterAiConfig.Default 提供（SpawnPointConfig 只暴露玩法/刷怪相关字段）。
        cfg.MoveSpeed = 4f;
        cfg.SlotSpacing = 6f;
        return cfg;
    }

    private static InputPacket MakeInput(ulong characterId, long clientTick, float moveX, float moveY)
        => new InputPacket
        {
            CharacterId = characterId,
            ClientTick = clientTick,
            MoveX = moveX,
            MoveY = moveY,
            MaxSpeed = MovementFormula.DefaultMaxSpeed,
            LookYaw = 0f,
            LookPitch = 0f,
        };

    // ---------- 玩家生命周期 ----------

    [Fact]
    public void AddPlayer_ThenTick_SnapshotContainsPlayer()
    {
        var world = new DsWorldSession(MakeOptions());

        Assert.True(world.AddPlayer(1001, 1001, 5f, 6f, 0f, out var reason), reason);
        Assert.Equal(1, world.PlayerCount);

        var result = TickUntilSnapshotDue(world);

        Assert.NotNull(result.WorldSnapshot);
        var delta = Assert.Single(result.WorldSnapshot!.Deltas);
        Assert.Equal(1001UL, delta.EntityId);
        Assert.Equal(EntityDeltaKind.Spawn, delta.Kind);
        Assert.Equal(DsWorldSession.EntityTypePlayer, delta.Identity!.Value.EntityType);
        // 坐标系约定：wire Transform 为 Flax Y-up —— Y(上下) ← ECS.Z，Z(前后) ← ECS.Y。
        // AddPlayer(ECS: X=5, Y=6, Z=0)。回归防线：漏换轴曾导致远端角色 3D 位置错乱。
        Assert.Equal(5f, delta.Transform!.Value.X, 4);
        Assert.Equal(0f, delta.Transform!.Value.Y, 4);
        Assert.Equal(6f, delta.Transform!.Value.Z, 4);
    }

    /// <summary>
    /// DS 通道此前 spawn 只带 HP，昵称与等级完全缺失，远程玩家头顶只能显示
    /// Player_&lt;id&gt; 与 Lv.1。档案由网关在握手时权威注入，回归防线：三字段必须随 Spawn 出现。
    /// </summary>
    [Fact]
    public void AddPlayer_WithProfile_SpawnCarriesNicknameTitleAndLevel()
    {
        var world = new DsWorldSession(MakeOptions());

        Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out var reason,
            nickname: "叶惊鸿", level: 37, title: "内门弟子"), reason);

        var result = TickUntilSnapshotDue(world);
        var delta = Assert.Single(result.WorldSnapshot!.Deltas);

        Assert.Equal("叶惊鸿", delta.Identity!.Value.Nickname);
        Assert.Equal("内门弟子", delta.Identity!.Value.Title);
        Assert.Equal(37, delta.State!.Value.Level);
    }

    /// <summary>
    /// v13 运行期档案变更：改名/换称号必须在**不重发 Spawn** 的前提下让 AOI 内其他客户端看到。
    /// 同时要求只在变化的那一帧下发一次，之后回到 null（否则每帧多带两个字符串，白烧带宽）。
    /// </summary>
    [Fact]
    public void PlayerProfileChanged_EmitsProfileOnceThenStops()
    {
        var world = new DsWorldSession(MakeOptions());
        world.AddPlayer(1001, 1001, 0f, 0f, 0f, out _, nickname: "旧名", level: 5, title: "杂役");

        // Spawn 帧：档案走 Identity，Profile 应为 null（不重复下发）。
        var spawn = TickUntilSnapshotDue(world);
        Assert.Null(spawn.WorldSnapshot!.Deltas[0].Profile);
        Assert.Equal("旧名", spawn.WorldSnapshot!.Deltas[0].Identity!.Value.Nickname);

        // 无变化的帧：仍为 null。
        var steady = TickUntilSnapshotDue(world);
        Assert.Null(steady.WorldSnapshot!.Deltas[0].Profile);

        // 变更帧：只带真正变化的字段。
        Assert.True(world.TryGetPlayer(1001, out var renamed));
        renamed.Nickname = "新名";
        var changed = TickUntilSnapshotDue(world);
        var profile = changed.WorldSnapshot!.Deltas[0].Profile;
        Assert.NotNull(profile);
        Assert.Equal("新名", profile!.Value.Nickname);
        Assert.Null(profile!.Value.Title);

        // 收敛：变化只下发一次。
        var settled = TickUntilSnapshotDue(world);
        Assert.Null(settled.WorldSnapshot!.Deltas[0].Profile);
    }

    /// <summary>
    /// 死亡状态位由 DS 权威血量派生，必须实时下发——否则远程客户端看不到别人倒下。
    /// </summary>
    [Fact]
    public void PlayerDeath_CarriesDeadStateBit()
    {
        var world = new DsWorldSession(MakeOptions());
        world.AddPlayer(1001, 1001, 0f, 0f, 0f, out _);

        var alive = TickUntilSnapshotDue(world);
        Assert.Equal(0u, alive.WorldSnapshot!.Deltas[0].State!.Value.StateBits);

        Assert.True(world.TryGetPlayer(1001, out var victim));
        victim.Hp = 0;
        var dead = TickUntilSnapshotDue(world);
        Assert.True((dead.WorldSnapshot!.Deltas[0].State!.Value.StateBits & EntityStateBits.Dead) != 0u);
    }

    [Fact]
    public void AddPlayer_SecondSnapshot_UsesUpdateKindNotSpawn()
    {
        var world = new DsWorldSession(MakeOptions());
        world.AddPlayer(1001, 1001, 0f, 0f, 0f, out _);

        var first = TickUntilSnapshotDue(world);
        Assert.Equal(EntityDeltaKind.Spawn, first.WorldSnapshot!.Deltas[0].Kind);

        var second = TickUntilSnapshotDue(world);
        Assert.Equal(EntityDeltaKind.Update, second.WorldSnapshot!.Deltas[0].Kind);
        // 非 Spawn 帧不重复携带 Identity（省带宽，且客户端不该重建实体）。
        Assert.Null(second.WorldSnapshot!.Deltas[0].Identity);
    }

    [Fact]
    public void AddPlayer_IsIdempotentForSameSessionAndCharacter()
    {
        var world = new DsWorldSession(MakeOptions());
        Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out _));
        Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out _));
        Assert.Equal(1, world.PlayerCount);
    }

    [Theory]
    [InlineData(0L, 1001UL, "bad_session_id")]
    [InlineData(1001L, 0UL, "bad_character_id")]
    public void AddPlayer_InvalidIdentifiers_AreRejected(long sessionId, ulong characterId, string expectedReason)
    {
        var world = new DsWorldSession(MakeOptions());
        Assert.False(world.AddPlayer(sessionId, characterId, 0f, 0f, 0f, out var reason));
        Assert.Equal(expectedReason, reason);
    }

    [Fact]
    public void AddPlayer_CharacterIdBeyondLowWord_IsRejectedToPreventMonsterIdCollision()
    {
        // 怪物实体 ID 高 32 位非 0；角色 ID 超出低 32 位会与怪物 ID 空间重叠 → 必须显式拒绝，
        // 否则会出现"玩家和怪争夺同一个实体 ID"的静默串体。
        var world = new DsWorldSession(MakeOptions());
        var huge = (1UL << 32) | 5UL;

        Assert.False(world.AddPlayer(1001, huge, 0f, 0f, 0f, out var reason));
        Assert.Equal("character_id_out_of_range", reason);
    }

    [Fact]
    public void RemovePlayer_DropsFromWorldAndSnapshot()
    {
        var world = new DsWorldSession(MakeOptions());
        world.AddPlayer(1001, 1001, 0f, 0f, 0f, out _);
        TickUntilSnapshotDue(world);

        Assert.True(world.RemovePlayer(1001));
        Assert.Equal(0, world.PlayerCount);

        var result = world.Tick();
        // 无玩家时不再产出快照（没有受众）。
        Assert.Null(result.WorldSnapshot);
        Assert.Empty(result.AudienceSessionIds);
    }

    [Fact]
    public void RemovePlayer_NextTick_BroadcastsDespawnToRemainingSessions()
    {
        var world = new DsWorldSession(MakeOptions());
        world.AddPlayer(1001, 1001, 0f, 0f, 0f, out _);
        world.AddPlayer(1002, 1002, 1f, 0f, 0f, out _);
        TickUntilSnapshotDue(world);

        Assert.True(world.RemovePlayer(1001));

        // 修复 BUG（离线角色永久残留）：会话结束后的第一个 tick 必须全广播 Despawn。
        var result = world.Tick();
        Assert.NotNull(result.DespawnBroadcast);
        var delta = Assert.Single(result.DespawnBroadcast!.Deltas);
        Assert.Equal(1001UL, delta.EntityId);
        Assert.Equal(EntityDeltaKind.Despawn, delta.Kind);
        Assert.Equal(new[] { 1002L }, result.DespawnBroadcastAudience);

        // 已广播：下一 tick 不得重复下发（避免长期重发）。
        Assert.Null(world.Tick().DespawnBroadcast);
    }

    [Fact]
    public void SubmitInput_UnknownSession_IsIgnored()
    {
        var world = new DsWorldSession(MakeOptions());
        Assert.False(world.SubmitInput(9999, MakeInput(9999, 1, 1f, 0f)));
    }

    // ---------- 移动：与 SimWorld 同源 ----------

    [Fact]
    public void SubmitInput_MovesPlayerAlongInputDirection()
    {
        var world = new DsWorldSession(MakeOptions());
        world.AddPlayer(1001, 1001, 0f, 0f, 0f, out _);

        float? firstX = null;
        float lastX = 0f;

        for (var i = 0; i < 30; i++)
        {
            world.SubmitInput(1001, MakeInput(1001, i + 1, 1f, 0f));
            var r = world.Tick();

            // 只在快照 tick 上采样（其余 tick 没有下发数据，本就无双端可观测的坐标）。
            var delta = r.WorldSnapshot?.Deltas.FirstOrDefault();
            if (delta?.Transform is { } t)
            {
                firstX ??= t.X;
                lastX = t.X;
            }
        }

        Assert.NotNull(firstX);
        Assert.True(lastX > firstX!.Value, $"玩家应沿 +X 移动，实际 {firstX} → {lastX}");
    }

    [Fact]
    public void SubmitInput_ProducesInputAckForTheRightSession()
    {
        var world = new DsWorldSession(MakeOptions());
        world.AddPlayer(1001, 1001, 0f, 0f, 0f, out _);

        world.SubmitInput(1001, MakeInput(1001, 42, 1f, 0f));
        var result = world.Tick();

        var ack = Assert.Single(result.InputAcks);
        Assert.Equal(1001L, ack.SessionId);
        Assert.True(ack.LastProcessedClientTick >= 42);
    }

    // ---------- 刷怪（G6） ----------

    [Fact]
    public void SpawnPoints_ProduceMonstersOnFirstTick()
    {
        var world = new DsWorldSession(MakeOptions());
        world.ConfigureSpawnPoints(new[] { MakeSpawnPoint(maxAlive: 3) });

        var result = world.Tick();

        Assert.Equal(3, result.SpawnedMonsters.Count);
        Assert.Equal(3, world.MonsterCount);
    }

    [Fact]
    public void SpawnPoints_MonstersUseDeterministicEntityIds()
    {
        var world = new DsWorldSession(MakeOptions());
        world.ConfigureSpawnPoints(new[] { MakeSpawnPoint(id: 7, maxAlive: 2) });

        var result = world.Tick();

        Assert.Contains(SpawnDirectorCore.ComputeEntityId(7, 0), result.SpawnedMonsters);
        Assert.Contains(SpawnDirectorCore.ComputeEntityId(7, 1), result.SpawnedMonsters);
    }

    [Fact]
    public void SpawnPoints_ZeroOrNegativeId_IsIgnored()
    {
        var world = new DsWorldSession(MakeOptions());
        var bad = MakeSpawnPoint();
        bad.SpawnPointId = 0; // 0 与"无实体"哨兵冲突

        world.ConfigureSpawnPoints(new[] { bad });
        var result = world.Tick();

        Assert.Empty(result.SpawnedMonsters);
    }

    [Fact]
    public void Monsters_AppearInWorldSnapshotAlongsidePlayers()
    {
        var world = new DsWorldSession(MakeOptions());
        world.ConfigureSpawnPoints(new[] { MakeSpawnPoint(maxAlive: 2, x: 10f) });
        world.AddPlayer(1001, 1001, 0f, 0f, 0f, out _);

        var result = TickUntilSnapshotDue(world);

        var deltas = result.WorldSnapshot!.Deltas;
        Assert.Equal(3, deltas.Length); // 1 玩家 + 2 怪
        Assert.Single(deltas, d => d.Identity!.Value.EntityType == DsWorldSession.EntityTypePlayer);
        Assert.Equal(2, deltas.Count(d => d.Identity!.Value.EntityType == DsWorldSession.EntityTypeMonster));
    }

    [Fact]
    public void SnapshotAudience_IsAllOnlineSessions()
    {
        var world = new DsWorldSession(MakeOptions());
        world.AddPlayer(1001, 1001, 0f, 0f, 0f, out _);
        world.AddPlayer(1002, 1002, 5f, 0f, 0f, out _);

        // 首个快照节拍：两会话均为晚加入待补发 → 共享快照受众为空（改走专属强制 Spawn，见 AoiTests）。
        var joinTick = TickUntilSnapshotDue(world);
        Assert.Empty(joinTick.AudienceSessionIds);
        Assert.Equal(2, joinTick.InitialSpawnSnapshots!.Count);

        // 稳定态快照节拍：受众 = 全部在线会话。
        var steady = TickUntilSnapshotDue(world);
        Assert.Equal(new long[] { 1001, 1002 }, steady.AudienceSessionIds);
    }

    // ---------- 怪物 AI 在世界里真的会动（G10） ----------

    [Fact]
    public void Monster_ChasesNearbyPlayer()
    {
        var world = new DsWorldSession(MakeOptions());
        // 刷怪点在原点，玩家放在仇恨半径内（15m 内）。
        world.ConfigureSpawnPoints(new[] { MakeSpawnPoint(maxAlive: 1, x: 0f) });
        world.AddPlayer(1001, 1001, 8f, 0f, 0f, out _);

        world.Tick(); // 生成怪
        var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);
        Assert.True(world.TryGetMonster(monsterId, out var before));
        var startX = before.Ai.X;

        for (var i = 0; i < 60; i++) world.Tick();

        Assert.True(world.TryGetMonster(monsterId, out var after));
        Assert.True(after.Ai.X > startX, $"怪物应朝 +X 的玩家移动，实际 {startX} → {after.Ai.X}");
        Assert.True(after.Ai.State is MonsterAiState.Chase or MonsterAiState.Attack,
            $"怪物应处于追击/攻击状态，实际 {after.Ai.State}");
    }

    [Fact]
    public void Monster_WithoutPlayers_StaysNearSpawn()
    {
        var world = new DsWorldSession(MakeOptions());
        world.ConfigureSpawnPoints(new[] { MakeSpawnPoint(maxAlive: 1, x: 0f) });
        world.Tick();

        var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);
        for (var i = 0; i < 300; i++) world.Tick();

        Assert.True(world.TryGetMonster(monsterId, out var monster));
        var dist = MathF.Sqrt(monster.Ai.X * monster.Ai.X + monster.Ai.Y * monster.Ai.Y);
        // 无目标时只做巡逻，不应远离出生点（含游荡半径余量）。
        Assert.True(dist <= MonsterAiConfig.Default.WanderRadius + 1f, $"怪物离出生点过远：{dist}");
    }

    // ---------- 死亡与重生 ----------

    [Fact]
    public void MarkMonsterDead_RemovesFromWorldAndSchedulesRespawn()
    {
        var world = new DsWorldSession(MakeOptions());
        world.ConfigureSpawnPoints(new[] { MakeSpawnPoint(maxAlive: 1) });
        world.Tick();

        var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);
        Assert.True(world.MarkMonsterDead(monsterId, world.ServerTick));
        Assert.Equal(0, world.MonsterCount);

        // 重生延迟 5s = 300 tick；到期前不应重生。
        for (var i = 0; i < 200; i++) world.Tick();
        Assert.Equal(0, world.MonsterCount);

        // 到期后应以**同一实体 ID** 重生（保持客户端实体身份稳定）。
        for (var i = 0; i < 200; i++) world.Tick();
        Assert.Equal(1, world.MonsterCount);
        Assert.True(world.TryGetMonster(monsterId, out _));
    }

    [Fact]
    public void MarkMonsterDead_BroadcastsDespawn_SoClientsDoNotKeepAGhost()
    {
        var world = new DsWorldSession(MakeOptions());
        world.ConfigureSpawnPoints(new[] { MakeSpawnPoint(maxAlive: 1) });
        Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out var r), r);
        world.Tick();

        var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);
        Assert.True(world.MarkMonsterDead(monsterId, world.ServerTick));

        // 死亡必须告知客户端：实体此后不再出现在任何快照里，只"停止更新"等于让客户端留一只
        // 冻在原地的鬼怪，而且"选中目标只在目标真消失时才丢"这条语义也无从触发。
        var tick = world.Tick();
        Assert.NotNull(tick.DespawnBroadcast);
        Assert.Contains(tick.DespawnBroadcast!.Deltas,
            d => d.EntityId == monsterId && d.Kind == EntityDeltaKind.Despawn);
        Assert.Contains(1001L, tick.DespawnBroadcastAudience);

        // 与玩家离线同一条路径：一次性登记，不得每 tick 重发。
        Assert.Null(world.Tick().DespawnBroadcast);
    }

    [Fact]
    public void MarkMonsterDead_UnknownEntity_ReturnsFalse()
    {
        var world = new DsWorldSession(MakeOptions());
        Assert.False(world.MarkMonsterDead(12345, 0));
    }

    // ---------- 确定性 ----------

    [Fact]
    public void TwoWorlds_WithSameInputSequence_ProduceIdenticalMonsterPositions()
    {
        static string Run()
        {
            var world = new DsWorldSession(MakeOptions());
            world.ConfigureSpawnPoints(new[] { MakeSpawnPoint(id: 1, maxAlive: 3), MakeSpawnPoint(id: 2, maxAlive: 2, x: 50f) });
            world.AddPlayer(1001, 1001, 12f, 3f, 0f, out _);

            var sb = new System.Text.StringBuilder();
            for (var tick = 0; tick < 400; tick++)
            {
                if (tick % 4 == 0)
                {
                    world.SubmitInput(1001, MakeInput(1001, tick + 1, 1f, 0.25f));
                }
                world.Tick();

                foreach (var id in new[] { SpawnDirectorCore.ComputeEntityId(1, 0), SpawnDirectorCore.ComputeEntityId(2, 1) })
                {
                    if (world.TryGetMonster(id, out var m))
                    {
                        sb.Append(BitConverter.SingleToInt32Bits(m.Ai.X)).Append('|')
                          .Append(BitConverter.SingleToInt32Bits(m.Ai.Y)).Append('|')
                          .Append((byte)m.Ai.State).Append(';');
                    }
                }
            }
            return sb.ToString();
        }

        var a = Run();
        var b = Run();
        Assert.Equal(a, b);
        Assert.True(a.Length > 100, "轨迹不应为空（反假绿）");
    }

    // ---------- 辅助 ----------

    private static DsTickResult TickUntilSnapshotDue(DsWorldSession world, int maxTicks = 10)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            var r = world.Tick();
            if (r.SnapshotDue) return r;
        }
        throw new InvalidOperationException("在给定 tick 预算内未到达快照节拍。");
    }
}
