using System.Linq;
using Horizon.Game.Sim.Quests;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 任务链拓扑（编辑器「任务链」面板的排序与连边）。
/// <para>
/// 不测这条的话，症状是"任务链看起来是 3→1→2"，而玩家进游戏必须按 1→2→3——
/// 编辑器和运行期各排一次序、谁都不报错。成环与未知前置也必须**显示出来**，
/// 而不是整页空白（运营刚配错的那一分钟正是最需要看见的时候）。
/// </para>
/// </summary>
public class QuestChainCoreTests
{
    private readonly ITestOutputHelper _log;

    public QuestChainCoreTests(ITestOutputHelper log) => _log = log;

    private static QuestChainNode Node(int id, params int[] prereq) =>
        new(id, "任务" + id, 1, 2, prereq, 0);

    [Fact]
    public void LinearChain_IsOrderedByPrerequisite()
    {
        var result = QuestChainCore.Build(new[] { Node(3, 2), Node(1), Node(2, 1) });
        _log.WriteLine(result.Describe());

        Assert.Empty(result.Problems);
        Assert.Equal(new[] { 1, 2, 3 }, result.TopologicalOrder.ToArray());
        Assert.Equal(new[] { 1 }, result.Roots.ToArray());
        Assert.Equal(2, result.Edges.Count);
        Assert.Equal(3, result.Nodes.Count);
    }

    [Fact]
    public void Branch_KeepsStableSiblingOrder()
    {
        var result = QuestChainCore.Build(new[] { Node(4, 1), Node(1), Node(2, 1) });

        Assert.Empty(result.Problems);
        // 同层按 questId 升序：1，然后 2、4。
        Assert.Equal(new[] { 1, 2, 4 }, result.TopologicalOrder.ToArray());
    }

    [Fact]
    public void UnknownPrerequisite_IsReportedButNodeStays()
    {
        var result = QuestChainCore.Build(new[] { Node(2, 99) });

        Assert.Single(result.Nodes);
        Assert.Equal(new[] { 2 }, result.Roots.ToArray());
        Assert.Contains("unknown_prerequisite:2:99", result.Problems);
        Assert.Empty(result.Edges);
    }

    [Fact]
    public void Cycle_IsReportedAndNodesStillListed()
    {
        var result = QuestChainCore.Build(new[] { Node(1, 2), Node(2, 1) });

        Assert.Equal(2, result.Nodes.Count);          // 不吞
        Assert.Empty(result.Roots);
        Assert.Contains(result.Problems, p => p.StartsWith("cycle:", System.StringComparison.Ordinal));
        Assert.Equal(new[] { 1, 2 }, result.TopologicalOrder.ToArray());
    }

    [Fact]
    public void DuplicateOrBadIds_AreReported()
    {
        var result = QuestChainCore.Build(new[] { Node(1), Node(1), Node(0) });

        Assert.Single(result.Nodes);
        Assert.Contains("duplicate_node:1", result.Problems);
        Assert.Contains("bad_quest_id:0", result.Problems);
    }

    [Fact]
    public void EmptyInput_ProducesEmptyChain()
    {
        var result = QuestChainCore.Build(System.Array.Empty<QuestChainNode>());
        Assert.Empty(result.Nodes);
        Assert.Empty(result.Problems);
        Assert.Equal(0, result.TopologicalOrder.Count);
    }

    /// <summary>
    /// 接线检查：链图内核再对，CLI 没这个动词、编辑器没这个页签，运营就看不到。
    /// </summary>
    [Fact]
    public void Wiring_CliVerbAndEditorTabExist()
    {
        var repo = System.IO.Directory.GetCurrentDirectory();
        while (repo != null && !System.IO.File.Exists(System.IO.Path.Combine(repo, "Horizon.sln")))
            repo = System.IO.Directory.GetParent(repo)?.FullName;
        Assert.NotNull(repo);

        var cli = System.IO.File.ReadAllText(System.IO.Path.Combine(repo!, "Horizon.NpcCli", "Program.cs"));
        Assert.Contains("\"quest-chain\" => RunQuestChain", cli, System.StringComparison.Ordinal);
        Assert.Contains("RunQuestChain", cli, System.StringComparison.Ordinal);

        var window = System.IO.File.ReadAllText(System.IO.Path.Combine(
            repo!, "HundunWorld", "Plugins", "NpcEditor", "Source", "NpcEditorEditor", "NpcEditorWindow.cs"));
        Assert.Contains("\"任务链\"", window, System.StringComparison.Ordinal);
        Assert.Contains("NpcCli.Run(\"quest-chain ", window, System.StringComparison.Ordinal);
    }
}
