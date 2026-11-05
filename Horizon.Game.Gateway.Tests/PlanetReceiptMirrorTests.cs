using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 回执键的<em>两侧对账</em>：近景驱动打出来的键，与 <c>Tools/planet-session-check.py</c> 要找的键。
/// <para>为什么需要：§13.67 的反证里 P7 活了一轮——针写的是"回执里有一行以 <c>actor_pose</c> 开头"，
/// 而驱动里有<em>三条</em>这样的行，于是成功路径那行的键名被改掉也不红；真实后果是判据器读不到
/// <c>verdict</c>，"探过没有"变成一个只有读侧知道的事。那是同族缺陷的一般形状：
/// <b>生产侧写的键与消费侧找的键没有对账，两边各自演进，红只在其中一侧出现</b>。</para>
/// <para>两个方向都判，因为它们失败的方式不同：</para>
/// <list type="number">
/// <item><description>消费侧找一个不再被生产的键 → 那条断言永远不会绿（或在真回执上静默 FAIL），
/// 而判据器自己的 <c>--self-test</c> 用的是<em>自己造的</em>夹具，照样全绿。</description></item>
/// <item><description>生产侧加了一个没人读的键 → 屏幕上多一行，谁也不看它；
/// 这正是"声明了却没人写的键"那一族，只是方向反过来。</description></item>
/// </list>
/// <para>注意只判<em>行首键</em>（一行开头的 <c>key=</c>），不判一行里的字段
/// （<c>sent=</c> / <c>verdict=</c> 这些是 <c>kv()</c> 在行内取的，属于第二层契约）。</para>
/// </summary>
public class PlanetReceiptMirrorTests
{
    private static string Driver() => StripComments(Read("HundunWorld/Source/Game/Planet/PlanetNearFieldDriver.cs"));
    private static string Checker() => StripComments(Read("Tools/planet-session-check.py"));

    /// <summary>驱动打出的行首键。<b>只认第一条实参就是字面量的那三种写法</b>
    /// （<c>Say("k …")</c>、<c>Say("k …" + x)</c>、<c>Say(string.Format(Inv, "k …", …))</c>）；
    /// <c>Say(e.Message)</c> / <c>Say(Path.Combine(…))</c> 里也能刮出引号串，
    /// 那些是内容不是键——上一版探针就是这么把 <c>Saved</c> 当成键的。</summary>
    private static HashSet<string> ProducerKeys()
    {
        var text = Driver();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(text, @"Say\(\s*(?:""([^""\n]*)""|string\.Format\(\s*Inv\s*,\s*""([^""\n]*)"")"))
        {
            string literal = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            var head = Regex.Match(literal, @"^\s*([A-Za-z_][A-Za-z0-9_]*)");
            if (head.Success) keys.Add(head.Groups[1].Value);
        }
        return keys;
    }

    /// <summary>判据器要找的行首键：每一处 <c>find(lines, "…")</c> 取第一个标识符。</summary>
    private static HashSet<string> ConsumerKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(Checker(), @"find\(lines,\s*""([^""]*)"""))
        {
            var head = Regex.Match(m.Groups[1].Value, @"^\s*([A-Za-z_][A-Za-z0-9_]*)");
            if (head.Success) keys.Add(head.Groups[1].Value);
        }
        return keys;
    }

    /// <summary>
    /// 生产侧那些<em>给人看、不给判据器看</em>的键。名单里每个名字都必须真的还在被打出来
    /// （过期名单 = 白留一个洞）；新加一条诊断就要在这里登记一次，登记的动作就是"这条不需要判"的决定。
    /// 这份名单是量出来的（<c>Tools/planet-receipt-keys.py</c> 那套提取式），不是记忆里的。
    /// </summary>
    private static readonly HashSet<string> KnownDiagnostics = new(StringComparer.Ordinal)
    {
        "actor_pose_send", "cache", "cache_bytes_used", "crash_message", "crash_stack", "crash_type",
        "crossing", "crossing_multi_tile", "crossings_total", "engine", "engine_residency", "final_window",
        "frames", "gravity", "height_field_error", "marker_parse_error", "plan_lag_frames", "policy",
        "prime", "pump", "pump_equation", "region", "settle_timeout", "settling", "swap_begin", "swap_repair",
        "terrain_created_by_script", "tick_crash_message", "tick_crash_type", "transfer_policy", "upload_ms",
        "uploader", "uploader_equation", "uploader_mode_warning",
    };

    [Fact]
    public void 判据器找的每个键都真的还会被打出来()
    {
        var producer = ProducerKeys();
        var consumer = ConsumerKeys();
        // 反空转：两边都刮到了东西才算这把尺子在量
        Assert.True(producer.Count >= 25, $"驱动里只刮出 {producer.Count} 个行首键：提取式失效，这条判据现在盯不住任何东西");
        Assert.True(consumer.Count >= 10, $"判据器里只刮出 {consumer.Count} 个要找的键：提取式失效");
        Assert.Contains("actor_pose", producer);       // 这三条是"刮到的东西"的锚，提取式一改就该红
        Assert.Contains("session_reset", producer);
        Assert.Contains("transfers", producer);

        foreach (var key in consumer)
            Assert.True(producer.Contains(key),
                $"判据器在找 <{key}>，而驱动已经不打这个键了：那条断言在真回执上永远读不到东西，"
                + "而判据器自己的 --self-test 用的是它自己造的夹具，照样全绿");
    }

    [Fact]
    public void 驱动打的每个键要么被读要么在诊断名单里登记过()
    {
        var producer = ProducerKeys();
        var consumer = ConsumerKeys();
        var unregistered = producer.Where(k => !consumer.Contains(k) && !KnownDiagnostics.Contains(k))
                                   .OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(unregistered.Count == 0,
            "这些键被打出来却没人读、也没登记成诊断：" + string.Join(", ", unregistered)
            + " —— 要么让判据器读它，要么登记进 KnownDiagnostics（登记这个动作就是「这条不需要判」的决定）");

        // 名单不许过期：登记过的名字如果已经不在了，那个洞就白留了
        var stale = KnownDiagnostics.Where(k => !producer.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0, "诊断名单里有已经不存在的键：" + string.Join(", ", stale));

        // 被读的键不许同时躺在诊断名单里（那等于一边判一边放行，两句话互相抵消）
        var doubleBooked = KnownDiagnostics.Where(consumer.Contains).ToList();
        Assert.True(doubleBooked.Count == 0,
            "这些键既被判据器读、又被登记成不需要判：" + string.Join(", ", doubleBooked));
    }

    [Fact]
    public void 这把尺子自己先量得准()
    {
        // 形状已知的一小段"驱动"+一小段"判据器"，要求提取式数出预期的那几个键。
        // 扫描型判据最常见的失效方式是安静地刮到 0 个，于是上面的双向对账自动全绿。
        const string driver = @"
    Say(""alpha=1"");
    Say(""beta "" + Thing.Describe());
    Say(string.Format(Inv, ""gamma={0} x={1}"", 3, 4));
    Say(e.Message + ""delta"");                       // 第一条实参不是字面量：不许算成键
    var p = Path.Combine(Globals.ProjectFolder, ""Saved"", ""x.on"");";
        var produced = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(driver,
                     @"Say\(\s*(?:""([^""\n]*)""|string\.Format\(\s*Inv\s*,\s*""([^""\n]*)"")"))
        {
            string lit = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            var head = Regex.Match(lit, @"^\s*([A-Za-z_][A-Za-z0-9_]*)");
            if (head.Success) produced.Add(head.Groups[1].Value);
        }
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, produced.OrderBy(k => k, StringComparer.Ordinal));

        const string checker = "a=find(lines, \"alpha=\")\nb=find(lines, \"beta on\")\nc=find(lines, \"not_a_key\")";
        var consumed = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(checker, @"find\(lines,\s*""([^""]*)"""))
        {
            var head = Regex.Match(m.Groups[1].Value, @"^\s*([A-Za-z_][A-Za-z0-9_]*)");
            if (head.Success) consumed.Add(head.Groups[1].Value);
        }
        Assert.Equal(new[] { "alpha", "beta", "not_a_key" }, consumed.OrderBy(k => k, StringComparer.Ordinal));
        // 双向对账在这段夹具上的结论必须与真文件用的是同一套规则：这里少了 beta 的孪生就红
        Assert.Contains("alpha", produced);
        Assert.DoesNotContain("delta", produced);
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relative));

    private static string StripComments(string text) =>
        string.Join("\n", text.Split('\n').Select(line =>
        {
            int cut = line.IndexOf("//", StringComparison.Ordinal);
            return cut >= 0 ? line.Substring(0, cut) : line;
        }));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Horizon.Planet", "Horizon.Planet.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
