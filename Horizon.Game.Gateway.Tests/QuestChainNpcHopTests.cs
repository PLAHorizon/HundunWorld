using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Game.Sim.Quests;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 任务链可以**跨 NPC**：原话是"发布任务（或任务链几点 NPC）"。
/// <para>
/// 链图的边只由前置关系产生，而"这一环由谁发布、交给谁"是节点上的两格——所以同一张图里
/// 可以出现"A 发布 → B 交付 → B 再发布下一环"的多 NPC 走法。这里用一个 1→2→3 的合成链钉住：
/// 拓扑序、边、以及每个节点上的发布/交付 NPC 号都不被 `Build` 抹掉。
/// </para>
/// </summary>
public class QuestChainNpcHopTests
{
    [Fact]
    public void 链可以跨NPC_发布与交付分别落在不同NPC上()
    {
        var nodes = new List<QuestChainNode>
        {
            // 第一环：NPC 1 发布，交给 NPC 2。
            new(90001, "入门一", 1, 2, Array.Empty<int>(), 0),
            // 第二环：NPC 2 发布（这就是"链跨 NPC"），交给 NPC 3。
            new(90002, "入门二", 2, 3, new[] { 90001 }, 1),
        };

        var chain = QuestChainCore.Build(nodes);

        Assert.Empty(chain.Problems);
        Assert.Equal(new[] { 90001, 90002 }, chain.TopologicalOrder.ToArray());

        var first = chain.Nodes.Single(n => n.QuestId == 90001);
        Assert.Equal(1, first.GiverTemplateId);
        Assert.Equal(2, first.TurnInTemplateId);

        var second = chain.Nodes.Single(n => n.QuestId == 90002);
        Assert.Equal(2, second.GiverTemplateId);
        Assert.Equal(3, second.TurnInTemplateId);

        // 边只有一条：90001 → 90002（NPC 号不参与连边，只决定"去哪说下一句"）。
        Assert.Single(chain.Edges);
        Assert.Equal(90001, chain.Edges[0].FromQuestId);
        Assert.Equal(90002, chain.Edges[0].ToQuestId);

        // 根只有第一环：NPC 2 既是第一环的交付者又是第二环的发布者，但第二环有前置，不是根。
        Assert.Equal(new[] { 90001 }, chain.Roots.ToArray());
    }
}