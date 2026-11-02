using System.Collections.Generic;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Sync;
using Horizon.Game.Sim;
using Horizon.Game.Sim.Npcs;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 敌对 NPC 的**刷新时间**必须由内容表决定（编辑器里那一列 <c>respawn_seconds</c>）。
/// <para>
/// 不测这条的话，症状是"运营把 30 秒改成 5 分钟，游戏里还是 30 秒"——两边都不报错，
/// 而这一列在编辑器里有输入框、在种子脚本里有值，看起来"已经接好了"。
/// </para>
/// </summary>
[Collection("monster-content")]
public class NpcRespawnTests
{
    private readonly ITestOutputHelper _log;

    public NpcRespawnTests(ITestOutputHelper log) => _log = log;

    private const int TicksPerSecond = 10;

    private static DsWorldSessionOptions Options() => new()
    {
        ShardId = 0,
        TicksPerSecond = TicksPerSecond,
        SnapshotIntervalTicks = 1,
        EnableAoiFilter = false,
        MaxSessions = 10,
        MaxEntities = 100,
    };

    private static NpcContentSnapshot Snapshot(float respawnSeconds)
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 500001,
            Name = "山贼头目",
            Title = "山贼",
            ModelKey = "npc_bandit_chief",
            Level = 1,
            Reaction = (byte)NpcStance.Hostile,
            AiTier = (byte)NpcAiTier.Elite,
            // 本用例的主题是"重生计时"，不是逃跑：精英档自带 FleesAtLowHealth，
            // 逃跑又按奔跑速度跑（§11.64），会跑出攻击距离导致 400 拍打不死。显式关掉这一位。
            AiBitsForceOff = (uint)NpcAiBits.FleesAtLowHealth,
        });
        snapshot.SpawnPoints.Add(new NpcSpawnSnapshotDto
        {
            SpawnPointId = 1,
            NpcId = 500001,
            X = 2f, Y = 0f, Z = 0f,
            MaxAlive = 1,
            RespawnSeconds = respawnSeconds,
        });
        return snapshot;
    }

    private static CombatActionPacket Attack(ulong attackerId, ulong targetId) => new()
    {
        AttackerId = attackerId,
        TargetId = targetId,
        SkillId = 0,
        ClientTick = 1,
        AttackerYaw = 0f,
        ActionKind = CombatActionKind.NormalAttack,
    };

    [Fact]
    public void HostileNpc_RespawnsAfterConfiguredSeconds()
    {
        const float respawnSeconds = 2f;

        var (bundle, receipt) = NpcContentLoader.Compile(Snapshot(respawnSeconds));
        Assert.Equal(0, receipt.ProblemCount);
        NpcContent.Install(bundle);

        try
        {
            var world = new DsWorldSession(Options());
            Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out var reason), reason);

            var spawn = world.ApplyNpcContentSpawns();
            Assert.Equal(1, spawn.Spawned);
            Assert.Equal(1, spawn.SkippedHostile);

            ulong monsterId = 0UL;
            long deathTick = -1;
            long respawnTick = -1;

            for (var tick = 1; tick <= 400; tick++)
            {
                if (monsterId == 0UL || deathTick < 0)
                {
                    var attack = world.SubmitCombatAction(1001, Attack(1001, monsterId == 0UL ? 0UL : monsterId));
                    // 首拍还不知道实体 id：先只推进世界，从快照里认出它。
                    if (monsterId != 0UL && attack.Outcome != CombatOutcome.Hit)
                    {
                        // 打不中也继续推进；掷骰依赖 tick，下一拍可能就中了
                    }
                }

                var result = world.Tick();
                foreach (var evt in result.Events)
                {
                    if (evt.Packet is not DeathPacket death) continue;
                    if (monsterId != 0UL && death.EntityId == monsterId) deathTick = world.ServerTick;
                }

                var deltas = result.WorldSnapshot?.Deltas;
                if (deltas == null) continue;
                foreach (var delta in deltas)
                {
                    if (delta.Kind != EntityDeltaKind.Spawn || delta.Identity == null) continue;
                    if (delta.Identity.Value.TemplateId != 500001) continue;

                    if (monsterId == 0UL)
                    {
                        monsterId = delta.EntityId;
                        _log.WriteLine($"首次生成 EntityId={monsterId} tick={world.ServerTick}");
                    }
                    else if (deathTick >= 0 && respawnTick < 0)
                    {
                        respawnTick = world.ServerTick;
                        _log.WriteLine($"重生 tick={respawnTick}（死亡 tick={deathTick}）");
                    }
                }

                if (respawnTick >= 0) break;
            }

            Assert.True(monsterId != 0UL, "快照里没认出敌对 NPC");
            Assert.True(deathTick >= 0, "打了 400 拍也没把它打死（血量/攻击参数变了？）");
            Assert.True(respawnTick >= 0, $"死亡后 40 秒都没有重生事件（配置是 {respawnSeconds} 秒）");

            var delayTicks = respawnTick - deathTick;
            var expected = (long)(respawnSeconds * TicksPerSecond);
            _log.WriteLine($"delayTicks={delayTicks} expected≈{expected}");

            // 下界证明"没有提前刷"（配置被忽略时会变成别的节奏），上界证明"真的刷了"。
            Assert.True(delayTicks >= expected - 2,
                $"重生比配置更快：{delayTicks} 拍 < {expected} 拍——respawn_seconds 可能没接进生成点配置");
            Assert.True(delayTicks <= expected + 3 * TicksPerSecond,
                $"重生比配置慢太多：{delayTicks} 拍 vs {expected} 拍");
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    /// <summary>
    /// 反向对照：<c>respawn_seconds = 0</c> 表示"用全局默认曲线"（默认 15 秒），
    /// 绝不能跟"配了 2 秒"得到同一个节奏——否则上一条用例可能只是默认值恰好接近 2 秒，
    /// 根本没有证明那一列被读。
    /// </summary>
    [Fact]
    public void ZeroRespawnSeconds_FallsBackToGlobalDefault()
    {
        var (bundle, receipt) = NpcContentLoader.Compile(Snapshot(0f));
        Assert.Equal(0, receipt.ProblemCount);
        NpcContent.Install(bundle);

        try
        {
            var world = new DsWorldSession(Options());
            Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out var reason), reason);
            world.ApplyNpcContentSpawns();

            ulong monsterId = 0UL;
            long deathTick = -1;
            long respawnTick = -1;

            for (var tick = 1; tick <= 900; tick++)
            {
                if (monsterId != 0UL && deathTick < 0)
                    world.SubmitCombatAction(1001, Attack(1001, monsterId));

                var result = world.Tick();
                foreach (var evt in result.Events)
                {
                    if (evt.Packet is DeathPacket death && monsterId != 0UL && death.EntityId == monsterId)
                        deathTick = world.ServerTick;
                }

                var deltas = result.WorldSnapshot?.Deltas;
                if (deltas == null) continue;
                foreach (var delta in deltas)
                {
                    if (delta.Kind != EntityDeltaKind.Spawn || delta.Identity == null) continue;
                    if (delta.Identity.Value.TemplateId != 500001) continue;

                    if (monsterId == 0UL) monsterId = delta.EntityId;
                    else if (deathTick >= 0 && respawnTick < 0) respawnTick = world.ServerTick;
                }

                if (respawnTick >= 0) break;
            }

            Assert.True(deathTick >= 0 && respawnTick >= 0, "默认节奏下没有完成「死亡→重生」");
            var delayTicks = respawnTick - deathTick;
            _log.WriteLine($"默认节奏 delayTicks={delayTicks}");

            // 默认 15 秒 = 150 拍；给足下界，只证明"不是 2 秒那档"。
            Assert.True(delayTicks >= 100,
                $"respawn_seconds=0 却 {delayTicks} 拍就重生——0 被当成了具体秒数，而不是「用默认曲线」");
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }
}
