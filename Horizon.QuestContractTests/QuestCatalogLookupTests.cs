using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Game.Sim.Quests;
using Horizon.QuestCli;
using Xunit;

namespace Horizon.QuestContractTests;

/// <summary>
/// "按名字填 id"的解析规则（<see cref="QuestCatalogLookup"/>）。
/// <para>
/// 这条通路服务的是目标里"奖励物品关联**所有**游戏物品"和"发布者可关联物品/怪物/NPC"：
/// 目录侧已经把整张物品表（171 件）都给了编辑器，但作者若必须记住 700001 是哪块矿，
/// "能关联"就还差最后一步。规则放在 Sim 是因为 CLI 才是唯一写库通道——
/// 两边各写一份就会出现"编辑器认的名字 CLI 不认"。
/// </para>
/// <para>
/// 连库那几条（<c>HORIZON_QUEST_DB=1</c>）拿的是**真目录**，其中"每个唯一名字都必须能解回自己"
/// 那条是全局不变式：库里哪天多出重名物品，它会先在这里变红，而不是等作者把奖励发错东西。
/// </para>
/// </summary>
public class QuestCatalogLookupTests
{
    private static bool Enabled => !string.IsNullOrWhiteSpace(ConnectionConfig.Resolve(out _));

    private static readonly QuestCatalogCandidate[] Sample =
    {
        new(700001, "铁矿石", "材料"),
        new(700002, "青竹", "材料"),
        new(10008, "新手短剑", "武器"),
        new(20001, "新手长剑", "武器"),
        new(10002, "新手项链", "饰品"),
        new(810101, "金锋斩·秘籍", "秘籍"),
    };

    private static int Resolve(string text, out IReadOnlyList<QuestCatalogCandidate> matches)
        => QuestCatalogLookup.Resolve(Sample, text, out matches);

    [Fact]
    public void 纯数字优先且不许被同名条目劫持()
    {
        var id = Resolve("700001", out var matches);
        Assert.Equal(700001, id);
        Assert.Empty(matches);
    }

    [Fact]
    public void 数字即便不在目录里也原样放行交给校验器()
    {
        // 目录里没有 999999；这里不能"猜"成别的条目，也不能吞成 0——
        // "这个 id 不存在"必须由校验器 Q014/Q013/Q015 说，而不是解析层改数据。
        var id = Resolve("999999", out var matches);
        Assert.Equal(999999, id);
        Assert.Empty(matches);
    }

    [Fact]
    public void 整名相等直接命中()
    {
        Assert.Equal(10002, Resolve("新手项链", out _));
        Assert.Equal(810101, Resolve("金锋斩·秘籍", out _));
    }

    [Fact]
    public void 首尾空白不得影响解析()
    {
        Assert.Equal(700002, Resolve("  青竹  ", out _));
    }

    [Fact]
    public void 整名相等必须赢过子串()
    {
        // "新手短剑"同时是它自己的整名、也是"新手*"三条的子串；必须命中它那一条。
        var id = Resolve("新手短剑", out var matches);
        Assert.Equal(10008, id);
        Assert.Empty(matches);
    }

    [Fact]
    public void 唯一子串也算命中()
    {
        Assert.Equal(700001, Resolve("铁矿", out _));
        Assert.Equal(810101, Resolve("金锋斩", out _));
    }

    [Fact]
    public void 歧义一律不猜并把候选交回去()
    {
        var id = Resolve("新手", out var matches);
        Assert.Equal(QuestCatalogLookup.Unresolved, id);
        Assert.Equal(3, matches.Count);                       // 短剑 / 长剑 / 项链
        Assert.Contains("匹配到多个", QuestCatalogLookup.Describe(matches));
        // 候选顺序按"名字短在前 + id 升序"，保证同样的输入每次提示一样
        Assert.Equal(new[] { 10002, 10008, 20001 }, matches.Select(entry => entry.TemplateId).OrderBy(value => value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("不存在的东西")]
    public void 空输入与查不到都返回不解析而不是抛(string? text)
    {
        Assert.Equal(QuestCatalogLookup.Unresolved, Resolve(text!, out _));
    }

    [Fact]
    public void 超长数字串不得被当成id()
    {
        // 溢出保护：不解析成数字，也就不会悄悄变成一个巨大的模板 id。
        Assert.Equal(QuestCatalogLookup.Unresolved, Resolve("99999999999999", out _));
    }

    [Fact]
    public void 目录为空时任何名字都解不出()
    {
        Assert.Equal(QuestCatalogLookup.Unresolved,
                     QuestCatalogLookup.Resolve(Array.Empty<QuestCatalogCandidate>(), "铁矿石", out _));
        Assert.Equal(QuestCatalogLookup.Unresolved,
                     QuestCatalogLookup.Resolve(null, "铁矿石", out _));
    }

    [Fact]
    public void 真目录里的每一件唯一名字都必须能解回自己()
    {
        if (!Enabled) return;

        var connection = ConnectionConfig.Resolve(out var hint);
        Assert.False(string.IsNullOrWhiteSpace(connection), "连不上 Game 库：" + hint);

        using var store = new QuestStore(connection!);
        var catalog = store.AllItems()
            .Select(item => new QuestCatalogCandidate(item.Id, item.ItemName ?? string.Empty, item.ItemType.ToString()))
            .ToList();
        Assert.True(catalog.Count > 100, "物品目录条数异常（" + catalog.Count + "），本条会失去意义");

        var groups = catalog.GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        var ambiguousNames = groups.Where(group => group.Count() > 1).Select(group => group.Key).ToArray();

        // 重名本身不判红（内容侧的事），但必须被点名——它会让"按名字填 id"在这几个名字上失效。
        foreach (var group in groups.Where(group => group.Count() > 1))
        {
            var id = QuestCatalogLookup.Resolve(catalog, group.Key, out var matches);
            Assert.Equal(QuestCatalogLookup.Unresolved, id);
            Assert.Equal(group.Count(), matches.Count);
        }

        var resolved = 0;
        foreach (var entry in groups.Where(group => group.Count() == 1).Select(group => group.First()))
        {
            var id = QuestCatalogLookup.Resolve(catalog, entry.Name, out var matches);
            Assert.True(id == entry.TemplateId,
                        "唯一名字『" + entry.Name + "』没解回 " + entry.TemplateId + "，而是 " + id + "（" + QuestCatalogLookup.Describe(matches) + "）");
            resolved++;
        }
        Assert.Equal(groups.Length - ambiguousNames.Length, resolved);
    }

    [Fact]
    public void 材料档位用名字就能选中()
    {
        if (!Enabled) return;

        var connection = ConnectionConfig.Resolve(out var hint);
        Assert.False(string.IsNullOrWhiteSpace(connection), "连不上 Game 库：" + hint);

        using var store = new QuestStore(connection!);
        var catalog = store.AllItems()
            .Where(item => item.ItemType == 4)
            .Select(item => new QuestCatalogCandidate(item.Id, item.ItemName ?? string.Empty, "材料"))
            .ToList();
        Assert.Equal(41, catalog.Count);

        var iron = catalog.First(entry => entry.TemplateId == 700001);
        var bamboo = catalog.First(entry => entry.TemplateId == 700002);
        Assert.Equal(700001, QuestCatalogLookup.Resolve(catalog, iron.Name, out _));
        Assert.Equal(700002, QuestCatalogLookup.Resolve(catalog, bamboo.Name, out _));
    }

    /// <summary>
    /// 插件里有一份**同规则**的解析器（Flax 插件刻意不把服务端程序集拖进脚本域），所以三步顺序必须两边一致。
    /// <para>
    /// 这是**源码级结构闸门**：它管不住"两边都写错同一个地方"，管的是"只改一边"。
    /// 行为面由本文件上面那些用例覆盖服务端那一份；插件那一份由 <c>_verify.sh</c> 保证编得过、由本条保证顺序。
    /// </para>
    /// </summary>
    [Fact]
    public void 名称解析规则的三步顺序两边不得漂移()
    {
        var root = RepoRoot();
        var simFull = System.IO.File.ReadAllText(System.IO.Path.Combine(root, @"Horizon.Game.Sim\Quests\QuestCatalogLookup.cs"));
        var pluginFull = System.IO.File.ReadAllText(System.IO.Path.Combine(root,
            @"HundunWorld\Plugins\QuestEditor\Source\QuestEditorEditor\QuestEditorWindow.cs"));

        // 顺序只看方法体：注释被切在锚点之前，拿全文比顺序会误判。
        var sim = Slice(simFull, "public static int Resolve(");
        var plugin = Slice(pluginFull, "private int ResolveTemplateId(");

        AssertOrder(sim, "IsAllDigits", "string.Equals(", ".IndexOf(", "服务端那份");
        AssertOrder(plugin, "int.TryParse", "string.Equals(", ".IndexOf(", "插件那份");

        // 两份都必须"歧义不猜"：只有一项才返回 id。
        Assert.True(sim.Contains("== 1"), "服务端那份少了『只此一项才返回』的分支");
        Assert.True(plugin.Contains("== 1"), "插件那份少了『只此一项才返回』的分支");

        // 两份互指（写在各自的文档注释里）：拆掉注释等于拆掉漂移保护，这里先红给你看。
        Assert.True(pluginFull.Contains("QuestCatalogLookup.cs"), "插件那份不再指向服务端那份实现");
        Assert.True(simFull.Contains("QuestEditorWindow"), "服务端那份不再指向插件那份实现");
    }

    /// <summary>从锚点切到文件尾，避免把无关代码里的同名 API 算进顺序判断。</summary>
    private static string Slice(string source, string anchor)
    {
        var at = source.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(at >= 0, "找不到锚点 \"" + anchor + "\"——实现被改名或搬家了，请先确认闸门锚点还成立，别直接删断言。");
        return source.Substring(at);
    }

    private static void AssertOrder(string source, string first, string second, string third, string who)
    {
        var a = source.IndexOf(first, StringComparison.Ordinal);
        var b = source.IndexOf(second, StringComparison.Ordinal);
        var c = source.IndexOf(third, StringComparison.Ordinal);
        Assert.True(a >= 0 && b > a && c > b,
            who + " 的解析顺序不再是 数字 → 整名相等 → 子串匹配（a=" + a + " b=" + b + " c=" + c + "）");
    }

    private static string RepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.GetFiles("*.sln").Any()) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
