using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 会话级静态账的清账闸门。<b>这一批判据扫的是客户端源码</b>，因为要盯的那一层必然碰
/// FlaxEngine（<c>PlanetRegionTransfers</c> / <c>PlanetRegionWatches</c> 都在 <c>Source/Game</c> 里），
/// 编译不进这个测试工程；能钉住的就只剩两件事：<em>每个 public static 状态都被点名</em>、
/// <em>清账真的排在装配之前</em>。
/// <para>为什么需要它：Flax 的脚本装配跨 PIE 会话存活，这些静态不会跟着一次新会话重新初始化。
/// 上一轮留下的"在飞没回来"会让新会话第一次换区被自己挡下；上一轮装好的
/// <c>GroundElevationCm</c> 探针指着<em>上一轮那个已经 Dispose 掉的</em> <c>PlanetHeightField</c>。
/// 两种症状都是"换区上行一条都不发"，日志里看不到原因。</para>
/// <para>判据刻意<em>不手抄状态清单</em>：手抄那份会跟着账本增长而失效——日后加一个静态而忘了
/// 在清账里处理它，清单里没有它，这条照样绿。改成扫源码取清单，加什么就被自动盯上什么；
/// 不想被盯就得在排除名单里写清为什么不归这里管，而名单里的名字必须真的还存在。</para>
/// </summary>
public class PlanetRegionSessionResetGateTests
{
    private sealed record Ledger(string File, string Method,
                                 (string Name, string? Proof)[] Wiring,
                                 string[] MustCall, string[] Anchors);

    private static readonly Ledger[] Ledgers =
    {
        new("PlanetRegionTransfers.cs", "BeginSession",
            Wiring: new (string, string?)[]
            {
                // 接线（不是状态）：装配每轮重新赋值。清成 null 会把"这层压根没接会话"
                // 和"会话刚换掉"压成同一种症状，排查时反而更难归因。
                ("SessionProvider", null),
                ("CharacterIdProvider", null),
                // 账本体是 readonly 字段，不能整体赋值，所以排除理由是"它自己那句话真的在体里"
                ("Tracker", "Tracker.Reset()"),
            },
            MustCall: new[] { "Tracker.Reset()", "PlanetRegionWatches.Reset()" },
            Anchors: new[] { "Caliber", "CaliberStatus", "GroundElevationCm", "LastAppliedAck", "LastReceiptAtMs" }),

        new("PlanetRegionWatches.cs", "Reset",
            Wiring: Array.Empty<(string, string?)>(),
            MustCall: Array.Empty<string>(),
            Anchors: new[] { "Pending", "SubscribedRegionIds", "LastRequestMs", "OwnerRegionId" }),
    };

    [Fact]
    public void 换区上行的每项静态账都在BeginSession里被点名() => AssertLedger(Ledgers[0]);

    [Fact]
    public void 快照订阅的每项静态账都在Reset里被点名() => AssertLedger(Ledgers[1]);

    private static void AssertLedger(Ledger ledger)
    {
        var code = StripComments(Read(ledger.File));
        var members = PublicStaticMembers(code);
        var body = MethodBody(code, ledger.Method);
        var cleared = ClearedNames(body);

        // 反空转第一层：清单为空（文件读错/尺子失效）时，下面的覆盖检查会自动全绿
        Assert.True(members.Count >= 8,
            $"{ledger.File} 只扫出 {members.Count} 个 public static 成员：这把尺子现在盯不住任何账");
        Assert.True(cleared.Count >= 8,
            $"{ledger.File} 的 {ledger.Method} 里只找到 {cleared.Count} 条赋值：清账函数大概被掏空了");

        var wiring = ledger.Wiring.ToDictionary(w => w.Name, w => w.Proof, StringComparer.Ordinal);

        // 排除名单不许过期（名单里的名字已经不在源码里 = 白留一个洞）
        foreach (var name in wiring.Keys)
            Assert.True(members.Contains(name),
                $"{ledger.File} 里已经没有 public static {name}，排除名单还留着它：现在这个洞挡的是不存在的成员");

        foreach (var name in members)
        {
            if (wiring.TryGetValue(name, out string? proof))
            {
                // 排除的理由如果写着"靠那句调用清"，那句就必须真的在体里——只写在注释里不算
                if (proof is not null)
                    Assert.Equal(1, Count(body, proof));
                continue;
            }
            Assert.True(cleared.Contains(name),
                $"{ledger.File} 的 public static {name} 既没被 {ledger.Method} 清掉，也不在排除名单里："
                + "它会带着上一轮会话的值进入新会话");
        }

        foreach (var needle in ledger.MustCall)
            Assert.Equal(1, Count(body, needle));
        foreach (var anchor in ledger.Anchors)
        {
            // 锚要同时钉住两头：声明侧扫得到（否则"这个成员不再被清"会因为尺子压根没看见它而静默通过），
            // 清账侧也点名了（否则它不再被清就直接红在这里）
            Assert.True(members.Contains(anchor),
                $"{ledger.File} 的 public static {anchor} 扫不出来了：尺子失效，覆盖检查现在盯不住这一项");
            Assert.True(cleared.Contains(anchor),
                $"{anchor} 不再被 {ledger.Method} 清了（它是这几条判据的锚，漏清的症状见本文件头部）");
        }
    }

    [Fact]
    public void 清账排在装配之前_且实机回执里看得见它跑过()
    {
        var driver = StripComments(Read("PlanetNearFieldDriver.cs"));
        const string call = "PlanetRegionTransfers.BeginSession();";
        Assert.Equal(1, Count(driver, call));
        int reset = driver.IndexOf(call, StringComparison.Ordinal);

        // 顺序有意义：BeginSession 清的就是口径、探针与会话接线这三样。
        // 把它挪到 SetCaliber 之后，症状是"装上就被自己抹掉"，而每条换区上行都报"客户端口径没填"——
        // 那条理由读起来像 DEM 没加载，实际是装配顺序错了。
        foreach (var installs in new[] { "PlanetRegionTransfers.SetCaliber(", "GroundElevationCm =", "SessionProvider =" })
        {
            int at = driver.IndexOf(installs, StringComparison.Ordinal);
            Assert.True(at >= 0, $"驱动里找不到 {installs}：这条顺序判据的参照没了，需要改判据而不是让它空过");
            Assert.True(at > reset, $"{installs} 排在 BeginSession 之前：装上就会被清掉");
        }

        // 跑了看不见的检查等于没跑：回执文件里要留一行，且那行念的是清完之后的账
        int say = driver.IndexOf("Say(\"session_reset", StringComparison.Ordinal);
        Assert.True(say > reset, "BeginSession 之后没有 session_reset 那行 Say：实机上没法证明这轮真的清过账");
        Assert.Contains("PlanetRegionTransfers.Tracker.Describe()", driver, StringComparison.Ordinal);
    }

    [Fact]
    public void 这把尺子自己先量得准()
    {
        // 扫描型判据最常见的失效形状就是"安静地找到 0 条"。所以先拿一段已知形状的源码喂它，
        // 要求它数出已知的名字、并<em>不</em>把 const/方法/类名算进来。
        const string fixture = @"
public static class Demo {
    public static readonly Widget Ledger = new();
    public const long Budget = 2000;
    public static int Counter = 7;
    public static string Status { get; set; } = ""x"";
    public static Func<int, double?> Probe;
    public static long NowMs() => 0;
    public static void BeginSession()
    {
        Counter = 0;
        Status = ""clean"";
        Probe = null;
        Ledger.Reset();
    }
}";
        Assert.Equal(new[] { "Ledger", "Counter", "Status", "Probe" }, PublicStaticMembers(fixture));

        var body = MethodBody(fixture, "BeginSession");
        Assert.Equal(new[] { "Counter", "Status", "Probe" }, ClearedNames(body));
        Assert.Equal(1, Count(body, "Ledger.Reset()"));
    }

    // ==================== 尺子 ====================

    /// <summary>
    /// public static 状态成员的名字。方法（<c>名字(</c>）与 const（压根不是 static）都不算，
    /// 类/结构声明行也不算——它们不是"会跨会话留下来的东西"。
    /// </summary>
    private static List<string> PublicStaticMembers(string code)
    {
        var names = new List<string>();
        foreach (var line in code.Split('\n'))
        {
            var m = Regex.Match(line, @"^\s*public\s+static\s+(?:readonly\s+)?([\w\.<>\[\]\?,\s]+?)\s+(\w+)\s*(=|;|\{|\()");
            if (!m.Success) continue;
            if (m.Groups[3].Value == "(") continue;                      // 方法，不是状态
            var type = m.Groups[1].Value.Trim();
            if (type is "class" or "struct" or "enum" or "interface" or "record") continue;
            names.Add(m.Groups[2].Value);
        }
        return names;
    }

    /// <summary>方法体（含首尾花括号）。注释已经在外层抹掉，所以这里的括号计数不会被说明文字带偏。</summary>
    private static string MethodBody(string code, string method)
    {
        int head = code.IndexOf("void " + method + "(", StringComparison.Ordinal);
        Assert.True(head >= 0, $"源码里没找到 void {method}(...)：清账函数被改名或删掉了");
        int open = code.IndexOf('{', head);
        Assert.True(open > head, $"{method} 找不到方法体开头");
        int depth = 0;
        for (int i = open; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0) return code.Substring(open, i - open + 1);
        }
        throw new InvalidOperationException($"{method} 的花括号不配对");
    }

    /// <summary>体里的赋值目标（按出现顺序）。跨行的赋值不在形状之内——真出现时 ClearedNames 数不满，会红在这里而不是静默漏掉。</summary>
    private static List<string> ClearedNames(string body) =>
        Regex.Matches(body, @"(?m)^\s*([A-Za-z_]\w*)\s*=\s*[^;]+;").Select(m => m.Groups[1].Value).ToList();

    /// <summary>抹掉每行 <c>//</c> 之后的部分：针不许命中说明书（本文件的头部注释里就写着这些名字）。</summary>
    private static string StripComments(string text) =>
        string.Join("\n", text.Split('\n').Select(line =>
        {
            int cut = line.IndexOf("//", StringComparison.Ordinal);
            return cut >= 0 ? line.Substring(0, cut) : line;
        }));

    private static string Read(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Source", "Game", "Planet", file));

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + 1, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>从测试输出目录往上找到仓库根（与 <c>PlanetRegionCaliberGateTests</c> 同一手法）。</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Horizon.Planet", "Horizon.Planet.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
