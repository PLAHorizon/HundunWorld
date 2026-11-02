using System;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Sync;
using Horizon.Game.Sim;
using Horizon.Game.Sim.Factions;
using Horizon.Game.Sim.Npcs;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// D13 阵营敌对矩阵：<b>正派 ↔ 邪派 互敌，其余不敌对</b>。
/// <para>
/// 不测这条的话，阵营列没有任何判据，"未入阵营"（0）会被当成一个可参战的阵营，
/// 于是所有新角色开局就能互相打——而这是最不该靠"看起来没问题"来保证的一条。
/// </para>
/// </summary>
public class FactionCoreTests
{
    private readonly ITestOutputHelper _log;

    public FactionCoreTests(ITestOutputHelper log) => _log = log;

    [Theory]
    [InlineData(1, 2, true)]    // 正 → 邪：敌
    [InlineData(2, 1, true)]    // 邪 → 正：敌（对称）
    [InlineData(1, 1, false)]   // 同阵营
    [InlineData(2, 2, false)]
    [InlineData(0, 1, false)]   // 未入阵营不参战
    [InlineData(1, 0, false)]
    [InlineData(0, 0, false)]
    [InlineData(7, 1, false)]   // 越界脏数据：拒绝，不静默并入 0
    [InlineData(-1, 2, false)]
    public void Matrix_OnlyRighteousVsWickedIsHostile(int attacker, int defender, bool expected)
    {
        var verdict = FactionCore.Evaluate(attacker, defender);
        _log.WriteLine($"{attacker}->{defender}: {verdict.Describe()}");

        Assert.Equal(expected, verdict.Hostile);
    }

    [Fact]
    public void RejectReasons_AreDistinguishable()
    {
        Assert.Equal(FactionReject.None, FactionCore.Evaluate(1, 2).Reason);
        Assert.Equal(FactionReject.SameAlignment, FactionCore.Evaluate(2, 2).Reason);
        Assert.Equal(FactionReject.UnalignedInvolved, FactionCore.Evaluate(0, 2).Reason);
        Assert.Equal(FactionReject.UnknownFaction, FactionCore.Evaluate(9, 2).Reason);

        // 越界值在回执里必须显示成"未入阵营"这一确定值，而理由说明是数据问题——
        // 只留理由不留值会让诊断丢掉"原本是 9"这条线索。
        var unknown = FactionCore.Evaluate(9, 2);
        Assert.Equal(FactionAlignment.Unaligned, unknown.Attacker);
        Assert.Contains("faction_a=0", unknown.Describe());
        Assert.Contains("faction_reject=3", unknown.Describe());
    }

    [Fact]
    public void PvpKillSource_RequiresHostileFaction()
    {
        var hostile = NpcKillCore.SourceOfPvp(1, 2, out var v1);
        Assert.Equal(NpcKillSource.PvpPlayer, hostile);
        Assert.True(v1.Hostile);

        var same = NpcKillCore.SourceOfPvp(1, 1, out var v2);
        Assert.Equal(NpcKillSource.Unknown, same);
        Assert.Equal(FactionReject.SameAlignment, v2.Reason);

        // Unknown 来源 = 0 经验（拒绝理由取自 NpcKillCore 的分派，不是这里另算）。
        var single = NpcKillCore.Evaluate(
            NpcKillSource.Unknown,
            Horizon.Game.Sim.Monsters.MonsterXpConfig.Default,
            60, 60);
        Assert.Equal(0, single.Xp);
        Assert.Equal(NpcKillXpReject.SourceGrantsNone, single.Reason);
    }
}

/// <summary>
/// 阵营矩阵在**真实战斗入口**上被查询：玩家互殴的合法性只由它判。
/// <para>
/// 这条测的是"接上了没有"，不是矩阵本身（矩阵另有表驱动用例）。只测矩阵的话，
/// 它可能是个谁也没调用的纯函数，而玩家实际打人时仍然走老的 Invalid 分支。
/// </para>
/// </summary>
[Collection("monster-content")]
public class DsPvpFactionGateTests
{
    private static DsWorldSession NewWorld() => new(new DsWorldSessionOptions
    {
        ShardId = 0,
        TicksPerSecond = 60,
        SnapshotIntervalTicks = 1,
        EnableAoiFilter = false,
        MaxSessions = 10,
        MaxEntities = 100,
    });

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
    public void HostileFaction_TakesDamageAndDies()
    {
        var world = NewWorld();
        Assert.True(world.AddPlayer(2001, 2001, 0f, 0f, 0f, out var r1, level: 60, faction: 1), r1); // 正派
        Assert.True(world.AddPlayer(2002, 2002, 2f, 0f, 0f, out var r2, level: 60, faction: 2), r2); // 邪派

        // 打若干下直到打死（玩家血量是每个会话的默认值，一刀未必死）。
        var died = false;
        var damageSeen = false;
        for (var i = 0; i < 400 && !died; i++)
        {
            // 每轮都要推进 tick：战斗掷骰是 (攻击者, 目标, tick, salt) 的纯函数，
            // 不推进的话同一输入每次都给出同一个结果——正好撞上闪避时，400 次全闪。
            var hit = world.SubmitCombatAction(2001, Attack(2001, 2002));
            if (hit.Outcome == CombatOutcome.Hit) damageSeen = true;

            var result = world.Tick();
            foreach (var evt in result.Events)
            {
                if (evt.Packet is DeathPacket death && death.EntityId == 2002) died = true;
            }
        }

        Assert.True(damageSeen, "敌对阵营互殴没有造成任何伤害");
        Assert.True(died, "打到死都没看到受击者的死亡事件");
        Assert.True(world.PvpDamageDealtCount > 0);
        Assert.Equal(0, world.PvpRejectedFactionCount); // 正例：敌对阵营一次都没被拒（证明这个计数器真的会停在 0）
        Assert.True(world.PvpKillXpGranted > 0, "PvP 击杀没有产生荣誉经验（灰名断供之外的正常等级差应当 >0）");
    }

    [Fact]
    public void SameOrUnalignedFaction_CannotFight()
    {
        var world = NewWorld();
        Assert.True(world.AddPlayer(2001, 2001, 0f, 0f, 0f, out var r1, faction: 0), r1);
        Assert.True(world.AddPlayer(2002, 2002, 2f, 0f, 0f, out var r2, faction: 0), r2);

        var same = world.SubmitCombatAction(2001, Attack(2001, 2002));
        Assert.Equal(CombatOutcome.Invalid, same.Outcome);
        Assert.Equal(1, world.PvpRejectedFactionCount); // 只有这一枪被阵营矩阵拒

        // 同阵营也不打。
        var world2 = NewWorld();
        Assert.True(world2.AddPlayer(3001, 3001, 0f, 0f, 0f, out var r3, faction: 2), r3);
        Assert.True(world2.AddPlayer(3002, 3002, 2f, 0f, 0f, out var r4, faction: 2), r4);
        Assert.Equal(CombatOutcome.Invalid, world2.SubmitCombatAction(3001, Attack(3001, 3002)).Outcome);
    }
}
