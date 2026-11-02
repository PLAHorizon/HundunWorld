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
/// 敌对 NPC 被击杀后，**NPC 内容自带的掉落**必须真的掷出来并进入跨层事件（物品 + 金币）。
/// <para>
/// 不测这条的话，症状是"山贼打死了，尸体上什么都没有"——绑定表里明明配了行，
/// 而 DS 只在怪物内容里查掉落。022 种子给 500001 配的正是这份绑定（D4：扩 owner 不另立表）。
/// </para>
/// </summary>
[Collection("monster-content")]
public class NpcKillLootTests
{
    private readonly ITestOutputHelper _log;

    public NpcKillLootTests(ITestOutputHelper log) => _log = log;

    private static DsWorldSessionOptions Options() => new()
    {
        ShardId = 0,
        TicksPerSecond = 10,
        SnapshotIntervalTicks = 1,
        EnableAoiFilter = false,
        MaxSessions = 10,
        MaxEntities = 100,
    };

    private static NpcContentSnapshot Snapshot(int killCreditNpcId = 0)
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 500001,
            Name = "山贼头目",
            Level = 1,
            Reaction = (byte)NpcStance.Hostile,
            AiTier = (byte)NpcAiTier.Static,
            KillCreditNpcId = killCreditNpcId,
        });
        snapshot.SpawnPoints.Add(new NpcSpawnSnapshotDto
        {
            SpawnPointId = 1, NpcId = 500001, X = 2f, Y = 0f, Z = 0f, MaxAlive = 1,
        });

        // 与 022 种子同形：绑定必触，掉一件青竹 + 5~25 铜。
        var binding = new MonsterLootBindingSnapshotDto
        {
            MonsterTemplateId = 500001,
            LootTableId = 1,
            RollChanceBps = 10000,
            MinCount = 1,
            MaxCount = 1,
            ConditionKind = 0,
            MoneyMin = 5,
            MoneyMax = 25,
        };
        binding.Entries.Add(new MonsterDropSnapshotDto
        {
            ItemId = 700002,
            ChanceBps = 10000,   // 必掉（测试要能断言"在"，不能靠 60% 碰运气）
            CountMin = 1,
            CountMax = 2,
            Seq = 0,
            ConditionKind = -1,
        });
        snapshot.LootBindings.Add(binding);

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

    /// <summary>打死一只敌对 NPC，返回它的事件流与运行时实体 id。credit 由夹具模板决定。</summary>
    private static (ulong MonsterId, List<DsGameplayEvent> Events) RunKill(int killCreditNpcId)
    {
        var (bundle, receipt) = NpcContentLoader.Compile(Snapshot(killCreditNpcId));
        Assert.Equal(0, receipt.ProblemCount);
        NpcContent.Install(bundle);

        var world = new DsWorldSession(Options());
        Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out var reason), reason);
        world.ApplyNpcContentSpawns();

        ulong monsterId = 0UL;
        var events = new List<DsGameplayEvent>();

        for (var tick = 1; tick <= 400; tick++)
        {
            if (monsterId != 0UL) world.SubmitCombatAction(1001, Attack(1001, monsterId));
            var result = world.Tick();
            events.AddRange(result.GameplayEvents);

            var deltas = result.WorldSnapshot?.Deltas;
            if (deltas != null)
            {
                foreach (var delta in deltas)
                {
                    if (monsterId == 0UL
                        && delta.Kind == EntityDeltaKind.Spawn
                        && delta.Identity is { } identity
                        && identity.TemplateId == 500001)
                    {
                        monsterId = delta.EntityId;
                    }
                }
            }

            if (events.Any(e => e.Kind == DsGameplayEventKind.Loot)) break;
        }

        return (monsterId, events);
    }

    [Fact]
    public void HostileNpcKill_EmitsLootAndMoneyEvents()
    {
        NpcContent.ResetToHardcoded();
        try
        {
            var (monsterId, events) = RunKill(killCreditNpcId: 0);
            Assert.True(monsterId != 0UL, "快照里没认出敌对 NPC");

            var kill = events.Where(e => e.Kind == DsGameplayEventKind.Kill).ToArray();
            var loot = events.Where(e => e.Kind == DsGameplayEventKind.Loot).ToArray();
            var money = events.Where(e => e.Kind == DsGameplayEventKind.KillMoney).ToArray();

            _log.WriteLine($"kill={kill.Length} loot={loot.Length} money={money.Length} " +
                           $"items=[{string.Join(",", loot.Select(l => l.ItemId))}]");

            Assert.NotEmpty(kill);
            Assert.Equal(500001, kill[0].MonsterTemplateId);

            Assert.NotEmpty(loot);
            Assert.Contains(loot, l => l.ItemId == 700002 && l.ItemCount >= 1);

            Assert.NotEmpty(money);
            Assert.InRange(money[0].Money, 5, 25);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    /// <summary>
    /// 击杀归属转移（`kill_credit_npc_id`，WoW KillCredit1/2）：任务"击杀 N 只 X"与五行域好感
    /// 都读 `Kill` 事件的模板号，所以它必须换成 credit 目标；**掉落与金币仍属于尸体**，
    /// 不能被一起改走（否则打死甲会掉乙的货）。
    /// </summary>
    [Fact]
    public void KillCredit_RemapsKillIdentityOnly()
    {
        NpcContent.ResetToHardcoded();
        try
        {
            var (monsterId, events) = RunKill(killCreditNpcId: 500002);
            Assert.True(monsterId != 0UL, "快照里没认出敌对 NPC");

            var kill = events.Where(e => e.Kind == DsGameplayEventKind.Kill).ToArray();
            var loot = events.Where(e => e.Kind == DsGameplayEventKind.Loot).ToArray();
            var money = events.Where(e => e.Kind == DsGameplayEventKind.KillMoney).ToArray();

            _log.WriteLine($"kill_id={(kill.Length > 0 ? kill[0].MonsterTemplateId : -1)} " +
                           $"loot_ids=[{string.Join(",", loot.Select(l => l.MonsterTemplateId))}] " +
                           $"money_ids=[{string.Join(",", money.Select(m => m.MonsterTemplateId))}]");

            Assert.NotEmpty(kill);
            Assert.Equal(500002, kill[0].MonsterTemplateId);

            Assert.NotEmpty(loot);
            Assert.All(loot, l => Assert.Equal(500001, l.MonsterTemplateId));
            Assert.NotEmpty(money);
            Assert.All(money, m => Assert.Equal(500001, m.MonsterTemplateId));
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }
}
