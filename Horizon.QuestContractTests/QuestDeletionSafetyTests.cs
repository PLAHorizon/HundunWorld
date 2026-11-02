using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Horizon.QuestContractTests;

/// <summary>
/// "作废后的任务可以删掉，但**不影响已接该任务的角色**继续任务和后续查看"这条承诺的**结构依据**。
/// <para>
/// 为什么要有这条闸门：这句话今天是成立的，但成立的原因是结构性的，不是运气——
/// 接取时 <c>QuestGrain.AcceptFromTemplateAsync</c> 已经把 名字/描述/目标/奖励 抄进了角色自己的
/// <c>QuestData</c>（<c>AcceptQuestAsync(...)</c> + <c>AddQuestObjectiveAsync(...)</c>），
/// 而完成路径 <c>CompleteQuestAsync</c> 与网关发奖只看角色侧返回的 <c>result.Rewards</c>，
/// 全程不回查模板表。所以模板行被物理删掉之后，持有者照样能推进、提交、看详情。
/// </para>
/// <para>
/// 一旦有人在完成/放弃/列表这些路径里加一句模板查询，上面这段推断就崩了——那时删一条模板
/// 会开始影响玩家（交不了任务、详情变空）。这种破坏不会让任何现有测试变红，只会让线上出现
/// "上周还好的任务这周交不了"，所以把它钉在源码形状上。
/// </para>
/// </summary>
public class QuestDeletionSafetyTests
{
    private const string AcceptMethod = "public async Task<QuestData?> AcceptFromTemplateAsync";

    [Fact]
    public void 模板上下文只许在接取路径里用()
    {
        var path = Path.Combine(RepoRoot(), "Horizon.Orleans.Grains", "QuestGrain.cs");
        Assert.True(File.Exists(path), "找不到 QuestGrain.cs：" + path);
        var source = File.ReadAllText(path);

        var start = source.IndexOf(AcceptMethod, StringComparison.Ordinal);
        Assert.True(start >= 0,
            "没找到 " + AcceptMethod + " 的声明——锚点失效会让本条变成空转（永远绿），先修锚点再谈通过。");

        // 下一个方法声明就是本方法体的右边界（同类里缩进一致，取 "\n        public " 足够精确）。
        var end = source.IndexOf("\n        public ", start + AcceptMethod.Length, StringComparison.Ordinal);
        Assert.True(end > start, "没找到 AcceptFromTemplateAsync 之后的第一个方法声明，右边界失效。");

        var body = source.Substring(start, end - start);
        var insideCount = CountOccurrences(body, "_templates");
        Assert.True(insideCount >= 2,
            "接取路径里只出现 " + insideCount + " 次 _templates（预期至少 2：判空 + 查询）。" +
            "多半是锚点切错了位置——这种情况下本条会假绿，所以宁可判红。");

        // 允许出现在方法体之外的两处：字段声明（readonly ...）与构造函数注入（_templates = templates）。
        // 其余任何位置出现，都意味着有别的路径在读模板表。
        var suspicious = new List<string>();
        var offset = 0;
        foreach (var line in source.Split('\n'))
        {
            var lineStart = offset;
            offset += line.Length + 1;
            if (!line.Contains("_templates", StringComparison.Ordinal)) continue;
            if (lineStart >= start && lineStart < end) continue;
            if (line.Contains("readonly", StringComparison.Ordinal)) continue;
            if (line.Contains("_templates = templates", StringComparison.Ordinal)) continue;
            suspicious.Add(line.Trim());
        }

        Assert.True(suspicious.Count == 0,
            "模板上下文在接取路径之外被使用了：" + string.Join(" | ", suspicious) +
            "。这会让\"删掉模板不影响已接玩家\"不再成立（持有人在完成/查看时会去查一张已不存在的行），" +
            "要么把那段逻辑改回只读角色自己的状态，要么先改这条承诺与界面上的说法。");
    }

    [Fact]
    public void 闸门锚点在当前源码形态下确实能命中()
    {
        // 防空转的自我约束：上面那条一旦锚不到就会 Assert 失败，但"命中了几行"也要有下限，
        // 否则一个恰好包含两个 _templates 的错误切片也可能让它假绿。
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.Orleans.Grains", "QuestGrain.cs"));
        Assert.True(CountOccurrences(source, "_templates") >= 4,
            "整个文件里 _templates 不足 4 处（声明 + 注入 + 判空 + 查询），源码形态已变，需重新核对本闸门。");
        Assert.Contains(AcceptMethod, source);
    }

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        var index = text.IndexOf(token, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(token, index + token.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; directory != null && depth < 12; depth++, directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Horizon.sln"))) return directory.FullName;
        }

        throw new InvalidOperationException("从 " + AppContext.BaseDirectory + " 向上没找到 Horizon.sln");
    }
}
