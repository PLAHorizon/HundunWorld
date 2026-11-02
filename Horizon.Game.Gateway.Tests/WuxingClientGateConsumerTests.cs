using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 动态门槛在**玩家看得见的那一侧**有消费者。
/// <para>
/// §14 的四族条件（好感 / 材料 / 基础属性 / 技能树完整性）在服务端判定与数据里都验过了，
/// 但"数据里有、代码不判/不显示"是本仓反复踩过的同一类事故：门槛字段躺在 JSON 里，
/// 客户端解析了一半、显示层根本没用，玩家就只能"读卷被拒之后"才知道缺什么——
/// 那等于把 §14 的可玩性退化成惩罚式试错。
/// </para>
/// <para>
/// 所以这里钉三段链：解析（读 JSON 键）→ 判定文案（消费解析出来的模型属性）→ 页面真的调用了它。
/// 少任何一段，树上就会有一族门槛永远不显示。
/// </para>
/// </summary>
public class WuxingClientGateConsumerTests
{
    private const string DatabaseFile = @"Combat\Skills\SkillDatabase.cs";
    private const string TreeFile = @"Combat\Skills\WuxingSkillTree.cs";
    private const string PageFile = @"UI\Ink\Pages\Character\SkillTreePage.cs";

    /// <summary>JSON 里的原始门槛键：解析侧必须逐族都读。</summary>
    private static readonly string[] RawGateKeys =
    {
        "favorNeed", "materials", "attrNeed", "loopNeed", "needPairsCompleted", "evolveFrom",
    };

    /// <summary>模型属性名：显示侧必须逐族都用到（只解析不用 = 玩家看不见）。</summary>
    private static readonly string[] ModelGateNames =
    {
        "FavorNeed", "Materials", "AttrNeed", "LoopNeed", "NeedPairs", "EvolveFrom",
    };

    [Fact]
    public void ClientParsesEveryDynamicGateFamily()
    {
        var text = ReadClient(DatabaseFile);
        if (text.Length == 0) return;      // 客户端源码不在本工作区：没有链可钉，判跳过而不是造假红
        var missing = RawGateKeys.Where(k => !text.Contains(k, StringComparison.Ordinal)).ToList();

        Assert.True(missing.Count == 0,
            $"客户端没解析这些门槛族：{string.Join(", ", missing)}。" +
            "服务端仍然会按它们拒绝，玩家只在被拒那一刻才知道缺什么");
    }

    [Fact]
    public void BlockerTextConsumesEveryGateFamilyItParses()
    {
        var text = ReadClient(TreeFile);
        if (text.Length == 0) return;
        var missing = ModelGateNames.Where(k => !text.Contains(k, StringComparison.Ordinal)).ToList();

        Assert.True(missing.Count == 0,
            $"门槛文案没用到这些族：{string.Join(", ", missing)}——解析了却不显示，等于没有");
    }

    [Fact]
    public void SkillTreePageActuallyAsksForTheBlockerText()
    {
        var text = ReadClient(PageFile);
        if (text.Length == 0) return;

        // 只要有一处调用就够了；关键是"存在调用点"，否则上面两段链在运行时是死代码。
        Assert.True(text.Contains("DescribeBlockers", StringComparison.Ordinal),
            "技能树页没有调用门槛文案：解析与文案都成了死代码，玩家看到的树永远像可以学");
    }

    /// <summary>
    /// §6.2 的"源式圆满"这一族门槛要能被玩家**推进**，而不只是能被判定。
    /// <para>
    /// 服务端早就允许 <c>UpgradeSkillAsync</c> 逐级往上，但玩家侧一个发送方都没有：
    /// 于是"练满"这条前置只能靠 GM 指令或永远卡住——演化式（道阶/混沌，即"超越天阶"那一段）
    /// 整条上限形同虚设。这里钉两端：树页必须调升级入口，入口必须真的把请求发上线。
    /// </para>
    /// </summary>
    [Fact]
    public void MaxLevelGateHasAPlayerReachableEntry()
    {
        var page = ReadClient(PageFile);
        var handler = ReadClient(@"Network\MessageHandlers\SkillUpgradeHandler.cs");
        if (page.Length == 0 || handler.Length == 0) return;

        Assert.True(page.Contains("SkillUpgradeHandler", StringComparison.Ordinal),
            "技能树页没有精研（升级）入口：演化门槛要求源式练满，玩家却没有任何办法把它练满");
        Assert.True(handler.Contains("UpgradeSkillRequest", StringComparison.Ordinal),
            "精研入口没有把请求发上线：只在本地改文案，服务端永远不知道你练了");
    }

    /// <summary>
    /// 演化源的门槛要区分"没学过"和"学过但没练满"。
    /// <para>
    /// 服务端研读路径 (<c>SkillGrain.LearnFromBookAsync</c>) 除了习得还要求 <c>Level == MaxLevel</c>。
    /// 树页此前只报"演化源《X》"（未习得口径），于是"两式都学了、还在半路"会显示成"条件已具，待研读秘籍"，
    /// 玩家拿着书去读才被退回。不吃书（门槛未达不扣卷），但这条 §6.2 前置等于对玩家隐身。
    /// </para>
    /// <para>
    /// 第二条断言钉的是"圆满口径只有一处实现"：演化文案与道阶系计数各自写一遍 <c>Level &lt; MaxLevel</c>，
    /// 将来改一处漏一处，就会出现"树上说已圆满、混沌终式仍按不满算"的分裂。
    /// </para>
    /// </summary>
    [Fact]
    public void EvolveSourceBlockerDistinguishesLearnedFromMaxed()
    {
        var text = ReadClient(TreeFile);
        if (text.Length == 0) return;

        Assert.True(text.Contains("未圆满", StringComparison.Ordinal),
            "演化源门槛只判\"是否习得\"：练到一半的源式在树上看着像已满足，读卷才被服务端退回");

        int uses = 0;
        for (int i = text.IndexOf("EvolveSourceMaxingBlocker", StringComparison.Ordinal);
             i >= 0; i = text.IndexOf("EvolveSourceMaxingBlocker", i + 1, StringComparison.Ordinal))
            uses++;

        Assert.True(uses >= 3,
            $"圆满口径应当只有一处判定、被文案与道阶系计数共用（实测 {uses} 处引用，需 定义 + 2 处调用）");
    }

    /// <summary>
    /// 材料门槛要报"还差几件"，而不是永远挂着当缺项。
    /// <para>
    /// 权威背包快照早就在客户端（<c>PlayerInventoryService</c>：掉落/任务/研读扣料都走它），
    /// 所以材料这一族不需要新协议就能算出实数。把它一律列进缺项的后果不是显示粗糙：
    /// 页面会对着一个备齐了材料的玩家说"暂不可学"，而服务端此刻放行研读——假缺项会劝退合法操作。
    /// </para>
    /// <para>
    /// 第二条断言钉的是另一半：没有下行快照时不许把"不知道"渲染成"一件都没有"，
    /// 那会把一个本来能研读的式显示成缺料。
    /// </para>
    /// </summary>
    [Fact]
    public void MaterialGateCountsTheAuthoritativeInventorySnapshot()
    {
        var text = ReadClient(TreeFile);
        if (text.Length == 0) return;

        Assert.True(text.Contains("PlayerInventoryService", StringComparison.Ordinal),
            "材料门槛没有去核权威背包快照：备齐材料也会被判成缺项，玩家看着\"暂不可学\"就不会去读卷");
        Assert.True(text.Contains("HasServerData", StringComparison.Ordinal),
            "材料计数没有区分\"背包还没下行\"与\"真的没有\"：前者会被渲染成缺 0 件，等于凭空造缺项");
    }

    /// <summary>
    /// 基础属性门槛要与服务端比同一个量，并且备齐了就不该再算缺项。
    /// <para>
    /// 服务端 <c>WuxingDynamicGates.CheckAttributes</c> 比的是<b>加点数</b>
    /// （<c>AttributeAllocationData.Strength/Agility/Health/Intelligence</c>），不是最终属性值。
    /// 显示侧若去读面板上的"力量"（基值+装备+加点），玩家会看到"力量 60/40 已够"却被服务端拒绝，
    /// 或反过来：加点其实达标了，面板却因为身上没装备而显示不足。所以这里钉"取的是哪个源"。
    /// </para>
    /// </summary>
    [Fact]
    public void AttributeGateReadsTheAllocationSourceTheServerCompares()
    {
        var text = ReadClient(TreeFile);
        if (text.Length == 0) return;

        Assert.True(text.Contains("AttributeAllocationService", StringComparison.Ordinal),
            "基础属性门槛没有读权威加点数：与服务端比的不是同一个量，显示与拒绝会互相矛盾");
        Assert.True(text.Contains("HasServerData", StringComparison.Ordinal),
            "加点数下行前不许把\"不知道\"当成\"不足\"（那会凭空造缺项）");
    }

    private static string ReadClient(string relativeUnderSourceGame)
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "HundunWorld", "Source", "Game", relativeUnderSourceGame);
        if (!File.Exists(path))
        {
            // 客户端源码不在本工作区（例如只检出服务端）时不判失败：
            // 这条测试的意义在"改坏显示链会被抓住"，而不是"必须存在某个目录结构"。
            return string.Empty;
        }

        var text = File.ReadAllText(path);
        Assert.True(text.Length > 0, $"客户端文件为空：{path}");
        return text;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Horizon.sln"))) dir = dir.Parent;
        Assert.True(dir != null, "找不到仓库根（Horizon.sln）");
        return dir!.FullName;
    }
}
