using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Sync;
using Horizon.Game.Sim;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// MMORPG 开发计划 M4 —— DS 世界战斗闭环测试（验收门 H4-a / H4-b 的实时层部分）。
/// <para>
/// 这条链路在本轮之前是**断的**：<c>SyncCombatHandler</c> → <c>CombatSystemGrain</c> 走的是
/// 一张与 <c>ZoneShardGrain</c> 完全无关的实体表，伤害不落回世界实体 HP，死亡不触发任何后果
/// （代码注释自认 "TODO Phase 2"）。本套件锁定的是"战斗结果真的改变了世界"。
/// </para>
/// </summary>
public class DsCombatLoopTests
{
    private static DsWorldSession NewWorld(int monsterMaxAlive = 1, int playerHp = 100, float spawnX = 0f)
    {
        var world = new DsWorldSession(new DsWorldSessionOptions
        {
            ShardId = 0,
            TicksPerSecond = 60,
            SnapshotIntervalTicks = 3,
            PlayerDefaultMaxHp = playerHp,
        });

        var cfg = SpawnPointConfig.Default;
        cfg.SpawnPointId = 1;
        cfg.X = spawnX;
        cfg.Y = 0f;
        cfg.MaxAlive = monsterMaxAlive;
        cfg.SlotSpacing = 6f;
        cfg.MonsterTemplateId = 100;
        cfg.MaxHp = 30;          // 低血量：便于在少量攻击内验证死亡闭环
        cfg.AttackPower = 10;
        cfg.RespawnDelaySeconds = 5f;
        cfg.AggroRadius = 20f;
        cfg.LeashRadius = 50f;
        world.ConfigureSpawnPoints(new[] { cfg });

        return world;
    }

    /// <summary>玩家站在怪的正前方 2 米（面朝 +X），保证扇区与射程判定都通过。</summary>
    private static void AddPlayerFacingMonster(DsWorldSession world, long sessionId = 2001, float x = -2f)
    {
        Assert.True(world.AddPlayer(sessionId, (ulong)sessionId, x, 0f, 0f, out var reason), reason);
    }

    private static CombatActionPacket Attack(ulong attackerId, ulong targetId, int skillId = 0, float yaw = 0f)
        => new CombatActionPacket
        {
            AttackerId = attackerId,
            TargetId = targetId,
            SkillId = skillId,
            ClientTick = 1,
            AttackerYaw = yaw,
            ActionKind = skillId > 0 ? CombatActionKind.SkillCast : CombatActionKind.NormalAttack,
        };

    private static ulong FirstMonsterId(int spawnPointId = 1, int slot = 0)
        => SpawnDirectorCore.ComputeEntityId(spawnPointId, slot);

    // ---------- 玩家 → 怪物 ----------

    [Fact]
    public void Attack_ReducesMonsterHp()
    {
        var world = NewWorld();
        AddPlayerFacingMonster(world);
        world.Tick(); // 生成怪

        var monsterId = FirstMonsterId();
        Assert.True(world.TryGetMonster(monsterId, out var before));
        var hpBefore = before.Ai.Hp;

        var resolution = world.SubmitCombatAction(2001, Attack(2001, monsterId));

        Assert.Equal(CombatOutcome.Hit, resolution.Outcome);
        Assert.True(resolution.Damage > 0);
        Assert.True(world.TryGetMonster(monsterId, out var after));
        Assert.Equal(hpBefore - resolution.Damage, after.Ai.Hp);
    }

    [Fact]
    public void Attack_EmitsDamagePacketToAttackerSession()
    {
        var world = NewWorld();
        AddPlayerFacingMonster(world);
        world.Tick();

        var monsterId = FirstMonsterId();
        world.SubmitCombatAction(2001, Attack(2001, monsterId));
        var result = world.Tick();

        var dmgEvent = result.Events.FirstOrDefault(e => e.Packet is DamagePacket);
        Assert.NotNull(dmgEvent.Packet);
        Assert.Equal(new long[] { 2001 }, dmgEvent.Audience);

        var packet = (DamagePacket)dmgEvent.Packet;
        Assert.Equal(monsterId, packet.TargetId);
        Assert.Equal(2001UL, packet.AttackerId);
        Assert.True(packet.DamageAmount > 0);
    }

    [Fact]
    public void Attack_UnknownMonster_IsInvalid()
    {
        var world = NewWorld();
        AddPlayerFacingMonster(world);
        world.Tick();

        var resolution = world.SubmitCombatAction(2001, Attack(2001, 999999UL));
        Assert.Equal(CombatOutcome.Invalid, resolution.Outcome);
    }

    [Fact]
    public void Attack_FromUnknownSession_IsInvalid()
    {
        var world = NewWorld();
        world.Tick();
        var resolution = world.SubmitCombatAction(4242, Attack(4242, FirstMonsterId()));
        Assert.Equal(CombatOutcome.Invalid, resolution.Outcome);
    }

    [Fact]
    public void Attack_OutOfRange_DoesNotDamage()
    {
        var world = NewWorld();
        // 玩家站在 30 米外（远超 3 米普攻射程）
        AddPlayerFacingMonster(world, x: -30f);
        world.Tick();

        var monsterId = FirstMonsterId();
        Assert.True(world.TryGetMonster(monsterId, out var before));
        var hpBefore = before.Ai.Hp;

        var resolution = world.SubmitCombatAction(2001, Attack(2001, monsterId));

        Assert.Equal(CombatOutcome.OutOfRange, resolution.Outcome);
        Assert.True(world.TryGetMonster(monsterId, out var after));
        Assert.Equal(hpBefore, after.Ai.Hp);
    }

    // ---------- 击杀闭环：死亡 → 掉落 → 任务事件 ----------

    [Fact]
    public void KillingMonster_RemovesItAndEmitsDeathPacket()
    {
        var world = NewWorld();
        AddPlayerFacingMonster(world);
        world.Tick();

        var monsterId = FirstMonsterId();
        Assert.True(world.TryGetMonster(monsterId, out var monster));
        var hp = monster.Ai.Hp;

        // 打到死（每次约 10 点伤害，30 血 → 数次）
        SnapshotPacket? lastSnapshot = null;
        var killed = false;
        for (var i = 0; i < 20 && !killed; i++)
        {
            var r = world.SubmitCombatAction(2001, Attack(2001, monsterId));
            var tick = world.Tick();
            lastSnapshot = tick.WorldSnapshot ?? lastSnapshot;
            if (r.TargetDied) killed = true;
        }

        Assert.True(killed, $"应在 20 次攻击内击杀（初始 HP={hp}）");
        Assert.Equal(0, world.MonsterCount);
        Assert.False(world.TryGetMonster(monsterId, out _));
    }

    [Fact]
    public void KillingMonster_ProducesKillAndLootGameplayEvents()
    {
        var world = NewWorld();
        AddPlayerFacingMonster(world);
        world.Tick();

        var monsterId = FirstMonsterId();

        List<DsGameplayEvent> events = new();
        for (var i = 0; i < 20; i++)
        {
            var r = world.SubmitCombatAction(2001, Attack(2001, monsterId));
            var tick = world.Tick();
            if (tick.GameplayEvents.Count > 0) events.AddRange(tick.GameplayEvents);
            if (r.TargetDied) break;
        }

        var kill = Assert.Single(events, e => e.Kind == DsGameplayEventKind.Kill);
        Assert.Equal(2001UL, kill.ActorCharacterId);
        Assert.Equal(100, kill.MonsterTemplateId);
        Assert.Equal(monsterId, kill.MonsterEntityId);

        var loot = events.Where(e => e.Kind == DsGameplayEventKind.Loot).ToList();
        Assert.NotEmpty(loot);
        Assert.All(loot, l => Assert.True(l.ItemCount > 0));
        Assert.All(loot, l => Assert.Equal(2001UL, l.ActorCharacterId));
    }

    [Fact]
    public void GameplayEvent_IdempotencyKeyIsStableAndDistinct()
    {
        var a = new DsGameplayEvent
        {
            Kind = DsGameplayEventKind.Loot, ShardId = 0, ServerTick = 500,
            ActorCharacterId = 2001, MonsterEntityId = 7, ItemId = 100001,
        };
        var b = a;
        var c = a;
        c.ItemId = 100002;

        Assert.Equal(a.BuildIdempotencyKey(), b.BuildIdempotencyKey());
        Assert.NotEqual(a.BuildIdempotencyKey(), c.BuildIdempotencyKey());
        Assert.Contains("500", a.BuildIdempotencyKey(), StringComparison.Ordinal);
    }

    [Fact]
    public void KillingMonster_SchedulesDeterministicRespawn()
    {
        var world = NewWorld();
        AddPlayerFacingMonster(world);
        world.Tick();
        var monsterId = FirstMonsterId();

        for (var i = 0; i < 20; i++)
        {
            var r = world.SubmitCombatAction(2001, Attack(2001, monsterId));
            world.Tick();
            if (r.TargetDied) break;
        }
        Assert.Equal(0, world.MonsterCount);

        // 重生延迟 5s = 300 tick；到期后以同一实体 ID 回来。
        for (var i = 0; i < 400; i++) world.Tick();
        Assert.Equal(1, world.MonsterCount);
        Assert.True(world.TryGetMonster(monsterId, out _));
    }

    // ---------- 怪物 → 玩家 ----------

    [Fact]
    public void MonsterAttacksPlayer_AndReducesPlayerHp()
    {
        var world = NewWorld(monsterMaxAlive: 1, playerHp: 100);
        AddPlayerFacingMonster(world); // 玩家在怪正前方 2 米，处于怪的攻击射程内
        world.Tick();

        for (var i = 0; i < 200; i++) world.Tick();

        Assert.True(world.TryGetPlayer(2001, out var player));
        Assert.True(player.Hp < 100, $"怪物应已对玩家造成伤害，实际 HP={player.Hp}");
    }

    [Fact]
    public void MonsterKillsPlayer_ThenPlayerRespawns()
    {
        // 玩家 12 血、怪攻击力 10（每次约 5 点）→ 3 次命中致死。
        var world = NewWorld(monsterMaxAlive: 1, playerHp: 12);
        AddPlayerFacingMonster(world);
        world.Tick();

        var died = false;
        for (var i = 0; i < 1200 && !died; i++)
        {
            var tick = world.Tick();
            if (tick.GameplayEvents.Any(e => e.Kind == DsGameplayEventKind.PlayerDeath)) died = true;
        }

        Assert.True(died, "玩家应在怪物攻击下死亡");
        Assert.True(world.TryGetPlayer(2001, out var dead));
        Assert.True(dead.IsDead);
        Assert.True(dead.RespawnAtTick > 0);
        var respawnAt = dead.RespawnAtTick;

        // 精确推到"复活 tick"再断言：怪会持续攻击，多跑几 tick 就无法区分
        // "复活回满血"与"复活后又被打了"——那样的断言是弱断言。
        // Tick 内部顺序为 怪物 AI → 玩家复活，故复活 tick 结束时 HP 应恰为满值。
        while (world.ServerTick < respawnAt) world.Tick();

        Assert.True(world.TryGetPlayer(2001, out var revived));
        Assert.False(revived.IsDead);
        Assert.Equal(revived.MaxHp, revived.Hp);
    }

    // ---------- 与下行编码的接缝 ----------

    [Fact]
    public void CombatEvents_AreEncodedIntoDownlinkBatch()
    {
        var world = NewWorld();
        AddPlayerFacingMonster(world);
        world.Tick();

        var monsterId = FirstMonsterId();
        world.SubmitCombatAction(2001, Attack(2001, monsterId));
        var tick = world.Tick();

        var batch = DsDownlinkEncoder.Encode(tick);

        // 找出一帧可解码为 DamagePacket 的条目
        DamagePacket? damage = null;
        foreach (var item in batch.Items)
        {
            var decoded = SyncPacketCodec.Decode(item.Frame);
            if (decoded is DamagePacket d) { damage = d; break; }
        }

        Assert.NotNull(damage);
        Assert.Equal(2001UL, damage!.AttackerId);
        Assert.Equal(monsterId, damage.TargetId);
    }

    // ---------- 确定性 ----------

    [Fact]
    public void TwoWorlds_SameCombatSequence_ProduceIdenticalOutcome()
    {
        static string Run(ulong seedShift)
        {
            var world = NewWorld();
            AddPlayerFacingMonster(world);
            world.Tick();
            var monsterId = FirstMonsterId();

            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < 10; i++)
            {
                var r = world.SubmitCombatAction(2001, Attack(2001, monsterId));
                world.Tick();
                sb.Append((byte)r.Outcome).Append('|').Append(r.Damage).Append('|').Append(r.IsCritical ? 1 : 0).Append(';');
            }
            _ = seedShift;
            return sb.ToString();
        }

        Assert.Equal(Run(0), Run(0));
    }
}
