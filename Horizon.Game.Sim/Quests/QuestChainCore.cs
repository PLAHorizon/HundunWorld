using System;
using System.Collections.Generic;

namespace Horizon.Game.Sim.Quests;

/// <summary>任务链上的一环（只带"够画图与排序"的字段）。</summary>
public readonly struct QuestChainNode(
    int questId,
    string name,
    int giverTemplateId,
    int turnInTemplateId,
    int[] prerequisites,
    int orderSeq)
{
    public readonly int QuestId = questId;
    public readonly string Name = name ?? string.Empty;
    public readonly int GiverTemplateId = giverTemplateId;

    /// <summary>交付者模板号（0 = 沿用发布者，画图时按此回退）。</summary>
    public readonly int TurnInTemplateId = turnInTemplateId;

    public readonly int[] Prerequisites = prerequisites ?? Array.Empty<int>();

    /// <summary>同一 NPC 下的窗口排序（越小越靠前）。</summary>
    public readonly int OrderSeq = orderSeq;
}

/// <summary>任务链的一条依赖边（<c>From</c> 必须先完成才有 <c>To</c>）。</summary>
public readonly struct QuestChainEdge(int fromQuestId, int toQuestId)
{
    public readonly int FromQuestId = fromQuestId;
    public readonly int ToQuestId = toQuestId;
}

/// <summary>任务链构建结果。<b>坏数据只报不吞</b>：未知前置/成环都要进 Problems，节点照样留在 Nodes 里。</summary>
public sealed class QuestChainResult
{
    public List<QuestChainNode> Nodes { get; } = new();

    /// <summary>按"前置 → 后继"方向的有向边。</summary>
    public List<QuestChainEdge> Edges { get; } = new();

    /// <summary>没有前置（或前置都不在本图里）的起点任务。</summary>
    public List<int> Roots { get; } = new();

    /// <summary>拓扑序（稳定：同层按 questId 升序）。成环的节点排在末尾并计入 Problems。</summary>
    public List<int> TopologicalOrder { get; } = new();

    public List<string> Problems { get; } = new();

    public string Describe() => $"chain_nodes={Nodes.Count} chain_edges={Edges.Count} chain_roots={Roots.Count} chain_problems={Problems.Count}";
}

/// <summary>
/// 任务链拓扑（编辑器「任务链」面板的唯一排序/连边实现）。
/// <para>
/// <b>为什么排序不交给 UI</b>：同一条链在"画图"和"列清单"两处各排一次，迟早会一处分叉、
/// 另一处还按旧序——玩家看到的 1→2→3 与后端算的 3→2→1 都"各自正确"。
/// </para>
/// <para>
/// <b>成环不当异常抛出</b>：运营刚把 A 的前置配成 B、B 的前置配成 A 的那一分钟，
/// 编辑器必须还能打开并把这两环显示出来（附一条 <c>cycle:</c> 问题），
/// 而不是整页空白或崩掉。
/// </para>
/// </summary>
public static class QuestChainCore
{
    /// <summary>判据版本：改排序或连边语义时递增。</summary>
    public const int Version = 1;

    /// <summary>构建链图。节点重复时保留第一条并记 <c>duplicate_node:</c>。</summary>
    public static QuestChainResult Build(IReadOnlyList<QuestChainNode> quests)
    {
        var result = new QuestChainResult();
        var count = quests?.Count ?? 0;
        if (count == 0) return result;

        var byId = new Dictionary<int, QuestChainNode>(count);
        for (var i = 0; i < count; i++)
        {
            var node = quests![i];
            if (node.QuestId <= 0)
            {
                result.Problems.Add($"bad_quest_id:{node.QuestId}");
                continue;
            }

            if (byId.ContainsKey(node.QuestId))
            {
                result.Problems.Add($"duplicate_node:{node.QuestId}");
                continue;
            }

            byId[node.QuestId] = node;
        }

        // 边：只连接"两边都在本图里"的前置。指向图外的前置单独记一条——
        // 静默丢掉会让链看起来"从头开始"，而玩家进游戏会发现自己缺一个前置。
        foreach (var node in byId.Values)
        {
            for (var i = 0; i < node.Prerequisites.Length; i++)
            {
                var prereq = node.Prerequisites[i];
                if (prereq <= 0) continue;
                if (!byId.ContainsKey(prereq))
                {
                    result.Problems.Add($"unknown_prerequisite:{node.QuestId}:{prereq}");
                    continue;
                }

                result.Edges.Add(new QuestChainEdge(prereq, node.QuestId));
            }
        }

        // Kahn 拓扑：入度为 0 的按 questId 升序出队（稳定顺序），保证两次构建逐条相同。
        var indegree = new Dictionary<int, int>(byId.Count);
        foreach (var id in byId.Keys) indegree[id] = 0;
        foreach (var edge in result.Edges) indegree[edge.ToQuestId]++;

        var ready = new List<int>();
        foreach (var pair in indegree)
            if (pair.Value == 0) ready.Add(pair.Key);
        ready.Sort();
        foreach (var id in ready) result.Roots.Add(id);

        var emitted = new HashSet<int>();
        while (ready.Count > 0)
        {
            var current = ready[0];
            ready.RemoveAt(0);
            result.TopologicalOrder.Add(current);
            emitted.Add(current);

            for (var i = 0; i < result.Edges.Count; i++)
            {
                var edge = result.Edges[i];
                if (edge.FromQuestId != current) continue;
                indegree[edge.ToQuestId]--;
                if (indegree[edge.ToQuestId] == 0)
                {
                    ready.Add(edge.ToQuestId);
                    ready.Sort();
                }
            }
        }

        if (emitted.Count != byId.Count)
        {
            // 成环（或指进环里）：把剩下的按 id 排后排进末尾并明确报出来。
            var leftovers = new List<int>();
            foreach (var id in byId.Keys)
                if (!emitted.Contains(id)) leftovers.Add(id);
            leftovers.Sort();

            result.Problems.Add("cycle:" + string.Join(",", leftovers));
            result.TopologicalOrder.AddRange(leftovers);
        }

        // 节点按拓扑序输出（编辑器照这个顺序画），坏数据已进 Problems。
        for (var i = 0; i < result.TopologicalOrder.Count; i++)
            result.Nodes.Add(byId[result.TopologicalOrder[i]]);

        return result;
    }
}
