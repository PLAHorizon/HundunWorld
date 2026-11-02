using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Sim.Npcs;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// A1 的"号段即身份"：不在已登记号段里的编号必须**装载期拒绝**，而不是跑起来之后才在统计里露馅。
/// </summary>
public class NpcIdSegmentTests
{
    [Theory]
    [InlineData(1, NpcIdSegment.Legacy)]
    [InlineData(3, NpcIdSegment.Legacy)]
    [InlineData(4, NpcIdSegment.OutOfRange)]
    [InlineData(150000, NpcIdSegment.OutOfRange)]
    [InlineData(399999, NpcIdSegment.OutOfRange)]
    [InlineData(400000, NpcIdSegment.Service)]
    [InlineData(499999, NpcIdSegment.Service)]
    [InlineData(500000, NpcIdSegment.Hostile)]
    [InlineData(599999, NpcIdSegment.Hostile)]
    [InlineData(600000, NpcIdSegment.OutOfRange)]
    [InlineData(-1, NpcIdSegment.OutOfRange)]
    public void SegmentBoundaries(int npcId, NpcIdSegment expected)
    {
        Assert.Equal(expected, NpcIdSegmentCore.SegmentOf(npcId));
        Assert.Equal(expected != NpcIdSegment.OutOfRange, NpcIdSegmentCore.IsAllowed(npcId));
    }

    [Fact]
    public void Loader_RejectsOutOfSegmentId()
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 150000, Name = "错号段的路人", Level = 10, Reaction = (byte)NpcStance.Friendly,
        });

        var (bundle, receipt) = NpcContentLoader.Compile(snapshot);

        Assert.Contains("npc_id_out_of_segment:150000", receipt.Problems);
        Assert.Equal(0, bundle.ByNpcId.Count); // 拒绝之外还**不许进内容包**——否则模板仍可被刷点引用
    }

    [Fact]
    public void Loader_RejectsStanceThatContradictsSegment()
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
        // 敌对号段配成友好：会变成"站在敌人号段里却永远打不到"。
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 500001, Name = "假山贼", Level = 10, Reaction = (byte)NpcStance.Friendly,
        });
        // 服务号段配成敌对：会变成"能砍掉一个本该提供服务的 NPC"。
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 400001, Name = "敌对货郎", Level = 10, Reaction = (byte)NpcStance.Hostile,
        });

        var (_, receipt) = NpcContentLoader.Compile(snapshot);

        Assert.Contains("npc_segment_stance_mismatch:500001:0", receipt.Problems);
        Assert.Contains("npc_segment_stance_mismatch:400001:2", receipt.Problems);
    }
}
