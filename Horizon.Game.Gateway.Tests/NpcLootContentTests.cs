using System.Linq;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Sim.Monsters;
using Horizon.Game.Sim.Npcs;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// NPC 掉落绑定（复用怪物那套结构、owner 列放 npc_id）的装配与取用断言。
/// <para>
/// 缺口背景（设计文档 §12 A11）：敌对 NPC 早先"能打能死却不掉东西"。现在绑定随 NPC 内容一起装配，
/// DS 在怪物内容查不到时退到 NPC 内容。这里钉住三件事：装配计数、按 npc_id 取得到、挂错 npc 会报。
/// </para>
/// </summary>
/// <remarks>本类改写进程级 NPC 内容并读怪物内容，必须与怪物内容测试串行。</remarks>
[Collection("monster-content")]
public class NpcLootContentTests
{
    private readonly ITestOutputHelper _log;

    public NpcLootContentTests(ITestOutputHelper log) => _log = log;

    private static NpcContentSnapshot SnapshotWithHostileNpc()
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 500001,
            Name = "山贼头目",
            ModelKey = "npc_bandit_chief",
            Level = 32,
            Reaction = (byte)NpcStance.Hostile,
        });
        return snapshot;
    }

    private static MonsterLootBindingSnapshotDto Binding(int ownerId, params (int ItemId, int ChanceBps, int Seq)[] entries)
    {
        var dto = new MonsterLootBindingSnapshotDto
        {
            MonsterTemplateId = ownerId,
            LootTableId = 1,
            RollChanceBps = 10000,
            MinCount = 1,
            MaxCount = 1,
            ConditionKind = 0,
            MoneyMin = 5,
            MoneyMax = 25,
        };

        foreach (var e in entries)
        {
            dto.Entries.Add(new MonsterDropSnapshotDto
            {
                ItemId = e.ItemId,
                ChanceBps = e.ChanceBps,
                CountMin = 1,
                CountMax = 2,
                Seq = e.Seq,
                GroupId = 0,
                ConditionKind = -1,
                QuestId = 0,
            });
        }

        return dto;
    }

    [Fact]
    public void LootBindings_AreAssembledAndReadableByNpcId()
    {
        var snapshot = SnapshotWithHostileNpc();
        snapshot.LootBindings.Add(Binding(500001, (750003, 6000, 0), (91001, 1500, 1)));

        var (_, receipt) = NpcContentLoader.Compile(snapshot);
        Assert.Equal(0, receipt.ProblemCount);
        Assert.Equal(1, receipt.LootBindings);

        NpcContent.Install(NpcContentLoader.Compile(snapshot).Bundle);
        try
        {
            var rows = NpcContent.LootBindings(500001);
            Assert.Single(rows);
            Assert.Equal(2, rows[0].Entries.Length);
            // 条目集合（顺序由内核掷骰序决定，这里只比集合；排序后升序即 [91001, 750003]）。
            Assert.Equal(new[] { 91001, 750003 }, rows[0].Entries.Select(e => e.ItemId).OrderBy(x => x).ToArray());
            Assert.Equal(6000, rows[0].Entries.Single(e => e.ItemId == 750003).ChanceBps);

            // 前置：怪物内容里没有这个号 ⇒ DS 的第 1526 行必须退到 NPC 内容（这里断言"确实查不到"）。
            Assert.Empty(MonsterContent.LootBindings(500001));

            // 没配掉落的 NPC 返回空表而不是 null（调用方据此得到"不掉东西"）。
            Assert.Empty(NpcContent.LootBindings(400001));
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void LootBinding_ForUnknownNpc_IsReportedAndNotInstalled()
    {
        var snapshot = SnapshotWithHostileNpc();
        snapshot.LootBindings.Add(Binding(999001, (750003, 10000, 0)));

        var (bundle, receipt) = NpcContentLoader.Compile(snapshot);

        Assert.Contains("loot_binding_unknown_npc:999001", receipt.Problems);
        Assert.False(bundle.LootBindings.ContainsKey(999001));
        Assert.Equal(0, receipt.LootBindings);
        _log.WriteLine(receipt.Describe());
    }
}
