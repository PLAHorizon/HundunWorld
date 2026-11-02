using System.Linq;
using System.Text.Json;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Sim.Npcs;
using Horizon.NpcCli;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// "服务位开着、但内容为空"必须在**装配与导出同一处判据**下被摘掉。
/// <para>
/// 症状来源：位图同时驱动客户端的右键菜单与服务端的提交校验。位开着而货架/教学行为空时，
/// 玩家看到"看看货架"，点下去服务端回 <c>not_a_vendor</c>——点了没反应，而日志里全是正常拒绝。
/// <see cref="NpcServiceCore.PruneDataEmpty"/> 是唯一判据；这里钉住它自身的边界，
/// 以及**装配**（运行期身份）与**导出**（客户端表）确实都调了它。
/// </para>
/// </summary>
public class NpcServiceAvailabilityTests
{
    private const uint AllDataBits =
        (uint)(NpcServiceBits.Vendor | NpcServiceBits.Trainer | NpcServiceBits.CraftTrainer);
    private const uint KeepBits =
        (uint)(NpcServiceBits.QuestGiver | NpcServiceBits.CraftStation | NpcServiceBits.Gossip);

    [Fact]
    public void 内容为空的三个位被摘掉_任务设施对话不动()
    {
        var pruned = NpcServiceCore.PruneDataEmpty(AllDataBits | KeepBits, 0, 0, 0);
        Assert.Equal(KeepBits, pruned);
    }

    /// <summary>
    /// 货架经济字段的结构闸门（不查物品表）：负库存/负补货、负价格、两头都是正价却"卖得比买得贵"。
    /// </summary>
    [Fact]
    public void 货架经济字段的结构问题要报出来()
    {
        var s = new NpcContentSnapshot { ContentRevision = 1 };
        s.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 400011, Name = "商人", Level = 1, Reaction = (byte)NpcStance.Friendly,
            ServiceFlags = (uint)NpcServiceBits.Vendor,
        });
        s.VendorRows.Add(new NpcVendorRowDto { NpcId = 400011, Slot = 0, ItemId = 700001, BuyPriceOverride = 10, SellPriceOverride = 20 });
        s.VendorRows.Add(new NpcVendorRowDto { NpcId = 400011, Slot = 1, ItemId = 700002, StockMax = -5, RestockSeconds = -1 });
        s.VendorRows.Add(new NpcVendorRowDto { NpcId = 400011, Slot = 2, ItemId = 700003, BuyPriceOverride = -1 });

        var (_, receipt) = NpcContentLoader.Compile(s);

        Assert.Contains("vendor_sell_above_buy:400011:700001:20>10", receipt.Problems);
        Assert.Contains("vendor_bad_stock:400011:700002:-5", receipt.Problems);
        Assert.Contains("vendor_bad_restock:400011:700002:-1", receipt.Problems);
        Assert.Contains("vendor_bad_price:400011:700003:-1:0", receipt.Problems);
    }

    [Fact]
    public void 任务链接缺少对应服务位要报出来()
    {
        static NpcContentSnapshot Snapshot(uint flags, int role)
        {
            var s = new NpcContentSnapshot { ContentRevision = 1 };
            s.Templates.Add(new NpcTemplateSnapshot
            {
                NpcId = 400010, Name = "甲", Level = 1, Reaction = (byte)NpcStance.Friendly,
                ServiceFlags = flags,
            });
            s.QuestLinks.Add(new NpcQuestLinkDto { NpcId = 400010, QuestId = 90010, Role = (byte)role, OrderSeq = 0 });
            return s;
        }

        var (_, giverMissing) = NpcContentLoader.Compile(Snapshot((uint)NpcServiceBits.QuestTurnIn, role: 1));
        Assert.Contains("questlink_giver_without_service:400010:90010", giverMissing.Problems);

        var (_, turnInMissing) = NpcContentLoader.Compile(Snapshot((uint)NpcServiceBits.QuestGiver, role: 2));
        Assert.Contains("questlink_turnin_without_service:400010:90010", turnInMissing.Problems);

        // 正对照：位给齐了就不许再报（否则上面两条只是"永远报"）。
        var (_, ok) = NpcContentLoader.Compile(
            Snapshot((uint)(NpcServiceBits.QuestGiver | NpcServiceBits.QuestTurnIn), role: 1));
        Assert.DoesNotContain(ok.Problems, p => p.StartsWith("questlink_giver_without_service"));
        Assert.DoesNotContain(ok.Problems, p => p.StartsWith("questlink_turnin_without_service"));
    }

    [Fact]
    public void 有数据就保留()
    {
        var pruned = NpcServiceCore.PruneDataEmpty(AllDataBits | KeepBits, 1, 1, 1);
        Assert.Equal(AllDataBits | KeepBits, pruned);

        // 只要各有一位就够：0 与负数是"空"，正数是"有"。
        Assert.Equal((uint)NpcServiceBits.Vendor, NpcServiceCore.PruneDataEmpty((uint)NpcServiceBits.Vendor, 1, 0, 0));
        Assert.Equal(0u, NpcServiceCore.PruneDataEmpty((uint)NpcServiceBits.Vendor, -1, 0, 0));
    }

    private static NpcContentSnapshot Snapshot(bool withVendorRow, bool withSkillRow)
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 400010, ModelKey = "npc_a", Reaction = (byte)NpcStance.Friendly, Name = "甲",
            ServiceFlags = (uint)(NpcServiceBits.Vendor | NpcServiceBits.Trainer | NpcServiceBits.Gossip),
        });
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 400011, ModelKey = "npc_b", Reaction = (byte)NpcStance.Friendly, Name = "乙",
            ServiceFlags = (uint)(NpcServiceBits.Vendor | NpcServiceBits.Trainer | NpcServiceBits.Gossip),
        });

        if (withVendorRow)
        {
            snapshot.VendorRows.Add(new NpcVendorRowDto
            {
                NpcId = 400011, Slot = 0, ItemId = 750001, BuyPriceOverride = 10, SellPriceOverride = 5,
            });
        }

        if (withSkillRow)
        {
            snapshot.TrainerRows.Add(new NpcTrainerRowDto
            {
                NpcId = 400011, SkillId = 1001, ServiceKind = (byte)NpcTrainerService.Skill, CostGold = 10, Seq = 0,
            });
        }

        return snapshot;
    }

    [Fact]
    public void 装配时按每只NPC的数据摘位并记账()
    {
        var (bundle, receipt) = NpcContentLoader.Compile(Snapshot(withVendorRow: true, withSkillRow: true));

        // 甲：两个位都没数据 → 只剩对话；乙：货架与技能行都有 → 位全留。
        var a = bundle.ByNpcId[400010];
        var b = bundle.ByNpcId[400011];
        Assert.Equal((uint)NpcServiceBits.Gossip, a.ServiceFlags);
        Assert.Equal((uint)(NpcServiceBits.Vendor | NpcServiceBits.Trainer | NpcServiceBits.Gossip), b.ServiceFlags);

        Assert.Equal(2, receipt.PrunedServiceBits); // 甲被摘了两位
        Assert.Contains(receipt.PrunedServices, s => s.StartsWith("service_bit_pruned:400010:0x", System.StringComparison.Ordinal));
        Assert.DoesNotContain(receipt.PrunedServices, s => s.Contains("400011", System.StringComparison.Ordinal));
    }

    [Fact]
    public void 导出用同一判据_客户端不会出现点开空窗的入口()
    {
        var json = NpcVisualsExport.Build(Snapshot(withVendorRow: true, withSkillRow: true), out var count);
        Assert.Equal(2, count);

        using var doc = JsonDocument.Parse(json);
        var rows = doc.RootElement.GetProperty("npcs").EnumerateArray()
            .ToDictionary(r => r.GetProperty("npcId").GetInt32());

        // 表里没有 400010 的商人/训练位：9（对话+商人）应退化成 1（只有对话）。
        Assert.Equal((int)NpcServiceBits.Gossip, rows[400010].GetProperty("services").GetInt32());
        Assert.Equal(
            (int)(NpcServiceBits.Vendor | NpcServiceBits.Trainer | NpcServiceBits.Gossip),
            rows[400011].GetProperty("services").GetInt32());
    }

    // ────────────────────────── 校验回执：摘位必须说出来 ──────────────────────────

    [Fact]
    public void 校验回执把摘位念出来而不是默默通过()
    {
        var (_, receipt) = NpcContentLoader.Compile(Snapshot(withVendorRow: false, withSkillRow: false));

        // 两只 NPC 各被摘掉商人位与训练位 = 4 位；明细按 NPC 各一条。
        Assert.Equal(4, receipt.PrunedServiceBits);
        Assert.Equal(2, NpcValidateReport.PrunedServices(receipt).Length);

        var message = NpcValidateReport.Message(receipt);
        Assert.Equal("校验通过（4 个服务位因内容为空将被摘除）", message);
    }

    [Fact]
    public void 没有问题也没有摘位时就是校验通过()
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 400012, ModelKey = "npc_ok", Reaction = (byte)NpcStance.Friendly, Name = "丙",
            ServiceFlags = (uint)NpcServiceBits.Vendor,
        });
        snapshot.VendorRows.Add(new NpcVendorRowDto
        {
            NpcId = 400012, Slot = 0, ItemId = 750001, BuyPriceOverride = 10, SellPriceOverride = 5,
        });

        var (_, receipt) = NpcContentLoader.Compile(snapshot);
        Assert.Equal(0, receipt.ProblemCount);
        Assert.Equal(0, receipt.PrunedServiceBits);
        Assert.Empty(NpcValidateReport.PrunedServices(receipt));
        Assert.Equal("校验通过", NpcValidateReport.Message(receipt));
    }

    [Fact]
    public void 有问题时优先报问题_摘位让位()
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 400013, ModelKey = "npc_bad", Reaction = (byte)NpcStance.Friendly, Name = "丁",
            ServiceFlags = (uint)NpcServiceBits.Vendor,
        });
        // ItemId=0 是坏行：装配期要报 vendor_bad_item，位也就不该被当成"有数据"。
        snapshot.VendorRows.Add(new NpcVendorRowDto { NpcId = 400013, Slot = 0, ItemId = 0 });

        var (_, receipt) = NpcContentLoader.Compile(snapshot);
        Assert.True(receipt.ProblemCount > 0);
        Assert.StartsWith("发现 ", NpcValidateReport.Message(receipt), System.StringComparison.Ordinal);
    }
}
