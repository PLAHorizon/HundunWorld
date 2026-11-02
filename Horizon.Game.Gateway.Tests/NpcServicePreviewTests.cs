using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Sim.Npcs;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 服务入口预览（编辑器"玩家会看到什么"）：把编译后的内容包与玩家态喂进
/// <see cref="NpcServiceCore"/>，逐条给出可见性与不可见理由。
/// <para>
/// 以前 <c>Resolve</c> 只有数据摘位在用，编辑器无法回答"我勾了商人，玩家为什么看不到"；
/// 这里同时钉住"内容为空 → 摘位 + 理由"和"任务关系 → 任务位可见"两条。
/// </para>
/// </summary>
public class NpcServicePreviewTests
{
    private static NpcContentSnapshot Snapshot(bool withRows)
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 400030, ModelKey = "npc_preview", Reaction = (byte)NpcStance.Friendly, Name = "预览商",
            ServiceFlags = (uint)(NpcServiceBits.Vendor | NpcServiceBits.Trainer | NpcServiceBits.CraftTrainer
                                  | NpcServiceBits.Gossip | NpcServiceBits.QuestGiver),
        });

        if (withRows)
        {
            snapshot.VendorRows.Add(new NpcVendorRowDto
            {
                NpcId = 400030, Slot = 0, ItemId = 750001, BuyPriceOverride = 10, SellPriceOverride = 5,
            });
            snapshot.TrainerRows.Add(new NpcTrainerRowDto
            {
                NpcId = 400030, SkillId = 1001, ServiceKind = (byte)NpcTrainerService.Skill, CostGold = 10, Seq = 0,
            });
            snapshot.TrainerRows.Add(new NpcTrainerRowDto
            {
                NpcId = 400030, SkillId = 900001, ServiceKind = (byte)NpcTrainerService.Recipe, CostGold = 800, Seq = 1,
            });
        }

        return snapshot;
    }

    [Fact]
    public void 内容为空时入口不可见_并给出理由()
    {
        var (bundle, receipt) = NpcContentLoader.Compile(Snapshot(withRows: false));
        Assert.Equal(0, receipt.ProblemCount);

        var preview = NpcServicePreviewCore.Build(bundle, 400030, playerLevel: 10, questCounts: default);

        Assert.Equal((uint)(NpcServiceBits.Gossip | NpcServiceBits.QuestGiver), preview.EffectiveFlags);

        bool Visible(NpcServiceBits bit)
        {
            foreach (var e in preview.Entries) if (e.Bit == bit) return e.Visible;
            return false;
        }

        NpcServiceHiddenReason ReasonOf(NpcServiceBits bit)
        {
            foreach (var e in preview.Entries) if (e.Bit == bit) return e.Reason;
            return NpcServiceHiddenReason.Visible;
        }

        Assert.False(Visible(NpcServiceBits.Vendor));
        Assert.Equal(NpcServiceHiddenReason.NoVendorStock, ReasonOf(NpcServiceBits.Vendor));
        Assert.Equal(NpcServiceHiddenReason.NoTrainerRows, ReasonOf(NpcServiceBits.Trainer));
        Assert.Equal(NpcServiceHiddenReason.NoRecipeRows, ReasonOf(NpcServiceBits.CraftTrainer));
        Assert.Equal(NpcServiceHiddenReason.NoQuestForThisNpc, ReasonOf(NpcServiceBits.QuestGiver));
        Assert.True(Visible(NpcServiceBits.Gossip));
        Assert.Equal(1, preview.VisibleCount);
    }

    [Fact]
    public void 有内容有任务关系时入口全可见()
    {
        var (bundle, receipt) = NpcContentLoader.Compile(Snapshot(withRows: true));
        Assert.Equal(0, receipt.ProblemCount);

        var quests = new NpcQuestCounts(turnInReady: 1, offerAvailable: 0, inProgress: 0, lockedOffer: 0);
        var preview = NpcServicePreviewCore.Build(bundle, 400030, playerLevel: 10, questCounts: quests);

        Assert.Equal((uint)(NpcServiceBits.Vendor | NpcServiceBits.Trainer | NpcServiceBits.CraftTrainer
                            | NpcServiceBits.Gossip | NpcServiceBits.QuestGiver), preview.EffectiveFlags);
        Assert.Equal(5, preview.VisibleCount);
    }

    [Fact]
    public void 目录里没有这位时不猜()
    {
        var (bundle, _) = NpcContentLoader.Compile(Snapshot(withRows: true));
        var preview = NpcServicePreviewCore.Build(bundle, 499999, playerLevel: 1, questCounts: default);

        Assert.Null(preview.Entries);
        Assert.Equal(0, preview.VisibleCount);
    }
}
