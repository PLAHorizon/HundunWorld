using System;
using System.IO;
using System.Linq;
using Horizon.Game.Sim;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// §14「与某地区域生物/<b>NPC</b> 好感度」这条门槛里，NPC 那一半的入账口。
/// <para>
/// 击杀侧早就有账（<c>OrleansGameplayEventSink</c> → <c>IFavorabilityGrain.AddAsync</c>），
/// 而 NPC 侧此前**根本不存在**：交互链路已经能服务端权威地认定"这个人真的站在这位 NPC 面前说过话"
/// （<c>QuestInteractionAuthority.Evaluate</c> 返回的 <c>TemplateId</c> 从实体注册表解出，客户端自报的一律不用），
/// 却只拿它推进任务目标。<c>AddAsync</c> 的注释里连 <c>npc:10086</c> 这个 targetKey 示例都写好了，说明这是漏接而非设计。
/// </para>
/// </summary>
public class NpcTalkFavorTests
{
    private const string CatalogFile = @"Horizon.Game.Sim\NpcTemplateCatalog.cs";
    private const string HandlerFile = @"Horizon.Game.Core\Handlers\QuestHandler.cs";

    /// <summary>
    /// 每个<b>已登记</b> NPC 都必须落在 1..5 的某一域。
    /// 这条断言之所以要有：<c>WuxingDomain</c> 若给默认值 0，新增一位 NPC 忘记写域照样编过，
    /// 症状是"和这位 NPC 说话永远不涨好感"——不报错、不显示、玩家只能瞎猜。
    /// </summary>
    [Fact]
    public void EveryRegisteredNpcBelongsToAWuxingDomain()
    {
        var rows = NpcTemplateCatalog.RegisteredTemplates();
        Assert.True(rows.Length > 0, "NPC 目录空了：对话好感整条失去来源");

        var bad = rows.Where(r => r.WuxingDomain < 1 || r.WuxingDomain > 5)
                     .Select(r => $"{r.TemplateId}/{r.Name}->{r.WuxingDomain}")
                     .ToList();
        Assert.True(bad.Count == 0, $"这些 NPC 没有合法五行域，对话不会记账：{string.Join(", ", bad)}");

        // 同一域可以有多个 NPC，但一个 NPC 只能属一个域（好感按域累计，跨域会把一条来源记到两处）
        Assert.Equal(rows.Length, rows.Select(r => r.TemplateId).Distinct().Count());
    }

    [Fact]
    public void UnregisteredNpcCreditsNoFavorability()
    {
        Assert.Equal(0, NpcTemplateCatalog.DomainOf(0));
        Assert.Equal(0, NpcTemplateCatalog.DomainOf(-3));
        Assert.Equal(0, NpcTemplateCatalog.DomainOf(987654));

        // 兜底显示仍然要有（客户端不许出现空名），但显示与记账是两件事
        var fallback = NpcTemplateCatalog.Get(987654);
        Assert.False(string.IsNullOrWhiteSpace(fallback.Name));
        Assert.Equal(0, fallback.WuxingDomain);

        foreach (var row in NpcTemplateCatalog.RegisteredTemplates())
            Assert.Equal(row.WuxingDomain, NpcTemplateCatalog.DomainOf(row.TemplateId));
    }

    /// <summary>
    /// 网关侧必须真的按 NPC 类别去入账，而且带上"同一来源上限"。
    /// <para>
    /// 两条都有具体事故形状：① NPC 与怪物<b>共用同一个整数 id 空间</b>（都落在 <c>MonsterTemplateId</c> 字段上，
    /// 只靠 <c>ScriptedKind</c> 分流），少了类别闸，砍一只墨麒麟会被记成"和铸剑师聊过天"；
    /// ② 对话若走默认来源上限，站在 NPC 面前连点二十次就白拿一整个玄阶门槛。
    /// </para>
    /// </summary>
    [Fact]
    public void GatewayCreditsTalkOnlyForNpcKindAndCapsTheSource()
    {
        var text = Read(HandlerFile);
        int start = text.IndexOf("private async Task CreditNpcTalkFavorAsync", StringComparison.Ordinal);
        Assert.True(start >= 0, "网关没有 NPC 对话好感入账这一段：§14 的 NPC 半边仍然没有口");

        int end = text.IndexOf("/// <summary>", start, StringComparison.Ordinal);
        var body = end > start ? text.Substring(start, end - start) : text.Substring(start);

        Assert.Contains("QuestEntityKind.Npc", body);
        Assert.Contains("NpcTemplateCatalog.DomainOf", body);
        Assert.True(body.Contains("AddAsync(") && body.Contains("TalkFavorPerTargetCap"),
            "对话好感没传来源上限：连点就能吃满一整个玄阶 favorNeed");

        // 入账点必须挂在交互裁决通过之后（放在 verdict.Ok 之前就会给伪造实体记账）
        int verdictOk = text.IndexOf("if (!verdict.Ok) return", StringComparison.Ordinal);
        int call = text.IndexOf("CreditNpcTalkFavorAsync(request.CharacterId", StringComparison.Ordinal);
        Assert.True(verdictOk >= 0 && call > verdictOk,
            "对话好感在裁决通过之前入账：等于把记账交给客户端自报");
    }

    private static string Read(string relativeToRepoRoot)
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, relativeToRepoRoot);
        Assert.True(File.Exists(path), $"找不到文件：{path}");
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Horizon.sln"))) dir = dir.Parent;
        Assert.True(dir != null, "找不到仓库根（Horizon.sln）");
        return dir!.FullName;
    }
}
