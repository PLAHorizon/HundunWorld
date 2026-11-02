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
/// NPC 求援（<see cref="NpcAiBits.CallsReinforcement"/>）：被打的敌对 NPC 会叫醒**警戒半径内**的同族。
/// <para>
/// 这一位此前对 NPC 只有装载期校验（勾了位必须配警戒半径），运行期没有任何读者——
/// 玩家看到的是"打了哨兵，旁边的同伙站着看"。本用例钉住三件事：近处同族被叫醒并真的开始追、
/// 半径外的同族不动、叫醒数量计入回执计数（诊断"位没生效"要比"看起来没人来"更早）。
/// </para>
/// </summary>
[Collection("monster-content")]
public class NpcReinforcementTests
{
    private readonly ITestOutputHelper _log;

    public NpcReinforcementTests(ITestOutputHelper log) => _log = log;

    private static DsWorldSessionOptions Options() => new()
    {
        ShardId = 0,
        TicksPerSecond = 10,
        SnapshotIntervalTicks = 1,
        EnableAoiFilter = false,
        MaxSessions = 10,
        MaxEntities = 100,
    };

    private static NpcContentSnapshot Snapshot()
    {
        // 档位 6（Alertable）含 CallsReinforcement；警戒半径默认 20 米。
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 500001, Name = "哨兵", Level = 1,
            Reaction = (byte)NpcStance.Hostile, AiTier = (byte)NpcAiTier.Alertable,
        });
        snapshot.SpawnPoints.Add(new NpcSpawnSnapshotDto { SpawnPointId = 1, NpcId = 500001, X = 2f, Y = 0f, Z = 0f });
        snapshot.SpawnPoints.Add(new NpcSpawnSnapshotDto { SpawnPointId = 2, NpcId = 500001, X = 21f, Y = 0f, Z = 0f });
        // 21 米：在自己仇恨半径（20）之外，但在被攻击者的警戒半径（20）之内——只有被叫醒才会动。
        snapshot.SpawnPoints.Add(new NpcSpawnSnapshotDto { SpawnPointId = 3, NpcId = 500001, X = 50f, Y = 0f, Z = 0f });
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
    public void 被打的NPC叫醒半径内的同族_半径外不动()
    {
        var (bundle, receipt) = NpcContentLoader.Compile(Snapshot());
        Assert.Equal(0, receipt.ProblemCount);
        NpcContent.Install(bundle);

        try
        {
            var world = new DsWorldSession(Options());
            Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out var reason), reason);
            world.ApplyNpcContentSpawns();

            var ids = new List<ulong>();
            for (var tick = 1; tick <= 10 && ids.Count < 3; tick++)
            {
                var result = world.Tick();
                foreach (var delta in result.WorldSnapshot?.Deltas ?? System.Array.Empty<EntityDelta>())
                {
                    if (delta.Kind == EntityDeltaKind.Spawn && delta.Identity is { } identity
                        && identity.TemplateId == 500001 && !ids.Contains(delta.EntityId))
                    {
                        ids.Add(delta.EntityId);
                    }
                }
            }

            Assert.Equal(3, ids.Count);

            // 按**出生点**把三只分类（SpawnX 不会动；拿当前 X 分类会被游荡/追击搅乱）。
            var bySpawnX = new Dictionary<float, ulong>();
            foreach (var id in ids)
            {
                Assert.True(world.TryGetMonster(id, out var m), "生成后立刻查不到实体：" + id);
                bySpawnX[m.Ai.SpawnX] = id;
            }

            Assert.True(bySpawnX.Keys.Any(x => x < 3f) && bySpawnX.Keys.Any(x => x > 20f && x < 22f) && bySpawnX.Keys.Any(x => x > 40f),
                "三只没有按预期出生点生成：" + string.Join(",", bySpawnX.Keys));

            var victimId = bySpawnX.Single(kv => kv.Key < 3f).Value;
            var nearId = bySpawnX.Single(kv => kv.Key > 20f && kv.Key < 22f).Value;
            var farId = bySpawnX.Single(kv => kv.Key > 40f).Value;
            Assert.True(world.TryGetPlayer(1001, out var player) && player != null);
            Assert.True(world.TryGetMonster(victimId, out var victimBefore));
            var victimHpBefore = victimBefore.Ai.Hp;

            // 打哨兵：**打中一下就停**（再打下去会把它打死，尸体一清就查不到实体了）。
            var woken = false;
            var landed = false;
            for (var tick = 0; tick < 40 && !woken; tick++)
            {
                world.SubmitCombatAction(1001, Attack(player.EntityId, victimId));
                world.Tick();
                world.TryGetMonster(victimId, out var now);
                landed |= now != null && now.Ai.Hp < victimHpBefore;
                woken = world.NpcReinforcementWokenCount > 0;
                if (landed && tick >= 2) break;   // 已经打中：再给两拍让被叫醒的同族决策
            }

            Assert.True(landed, "40 拍里一次都没打中（夹具前提不成立）");

            // 被叫醒是"写入目标"这一拍完成的，位移从下一拍才开始：再走三拍再断言"真的往玩家那边挪了"。
            for (var extra = 0; extra < 3; extra++) world.Tick();

            Assert.True(world.TryGetMonster(nearId, out var near), "近处同族不见了");
            Assert.True(world.TryGetMonster(farId, out var far), "远处同族不见了");
            _log.WriteLine($"near x={near.Ai.X:F2} target={near.Ai.ThreatTargetId} state={near.Ai.State}; " +
                           $"far x={far.Ai.X:F2} target={far.Ai.ThreatTargetId} state={far.Ai.State}; " +
                           $"woken={world.NpcReinforcementWokenCount}");

            Assert.True(woken, "打了勾选求援的哨兵，却没有同族被叫醒");
            Assert.Equal(player.EntityId, near.Ai.ThreatTargetId);
            Assert.Contains(near.Ai.State, new[] { MonsterAiState.Chase, MonsterAiState.Attack });
            Assert.True(near.Ai.X < 21f, $"被叫醒的同族没有往玩家那边动：x={near.Ai.X:F2}");

            // 半径外（48m）的同族不该被叫醒。
            Assert.Equal(0UL, far.Ai.ThreatTargetId);
            Assert.NotEqual(MonsterAiState.Chase, far.Ai.State);
            Assert.Equal(1, world.NpcReinforcementWokenCount);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }
}