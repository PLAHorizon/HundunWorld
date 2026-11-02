using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Horizon.QuestContractTests;

/// <summary>
/// 任务管理 Grain 的**锁纪律**测试（源码结构级，不构造 Grain）。
/// <para>
/// 起因：<c>QuestManagerGrain.UnpublishAsync(forceAbandonActive:true)</c> 曾在持有
/// <c>_writeGate</c>（<c>SemaphoreSlim(1,1)</c>，<b>不可重入</b>）的区间里调用同样要这把锁的
/// <c>RecordRevocationsAsync</c> → 自己等自己。因为 <c>quest.manager</c> 是主键恒为 1 的单例 grain，
/// 一次点击就让整个任务系统排队到超时，且不留任何说明性日志。
/// </para>
/// <para>
/// 这个缺陷没法用"跑一下就行"的方式守住：进程内构造 Grain 会把 vstest 的 testhost 直接弄死
/// （Moq 代理 <c>IPersistentState</c> 那条路不通，见 <c>docs/TECH_DESIGN</c> 侧记录），
/// 真集群回归又要停掉开发中的宿主。所以这里守的是**结构不变量**：
/// 任何一个方法，若在"拿到锁"与"释放锁"之间调用了另一个也要拿这把锁的方法，就是死锁——当场判红。
/// </para>
/// </summary>
public class QuestGrainLockDisciplineTests
{
    private const string GrainPath = @"Horizon.Orleans.Grains\World\QuestManagerGrain.cs";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Horizon.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private sealed class Method
    {
        public string Name = string.Empty;
        public string Body = string.Empty;
    }

    /// <summary>按大括号配平切出每个方法的头部与函数体（只依赖 C# 的常规写法，不做语义解析）。</summary>
    private static List<Method> ReadMethods(string source)
    {
        var methods = new List<Method>();
        var header = new Regex(
            @"^\s*(?:public|private|internal|protected)[^\n;=]*?\b(?<name>[A-Za-z_]\w*)\s*\([^)]*\)\s*$",
            RegexOptions.Multiline);

        foreach (Match match in header.Matches(source))
        {
            var open = source.IndexOf('{', match.Index + match.Length - 1);
            if (open < 0) continue;
            // 头部末尾的 '{' 必须紧跟签名（中间只允许换行与空白）
            var between = source.Substring(match.Index + match.Length, open - (match.Index + match.Length));
            if (between.Trim().Length > 0) continue;

            var depth = 0;
            var end = -1;
            for (var i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0) { end = i; break; }
                }
            }
            if (end < 0) continue;

            methods.Add(new Method
            {
                Name = match.Groups["name"].Value,
                Body = source.Substring(open + 1, end - open - 1),
            });
        }

        return methods;
    }

    [Fact]
    public void 源文件能按预期切出方法()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), GrainPath));
        var methods = ReadMethods(source);

        // 解析器不可靠就等于测试永远绿；这里先把"它确实认出了这几个关键方法"钉住。
        foreach (var expected in new[] { "PublishAsync", "UnpublishAsync", "UpsertTemplateAsync", "RecordRevocationsAsync", "TakeRevokedQuestsAsync", "NotifyAcceptedAsync", "RemoveHolderAsync" })
            Assert.Contains(expected, methods.Select(method => method.Name));
    }

    [Fact]
    public void 持锁区间内不许调用同样要拿这把锁的方法()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), GrainPath));
        var methods = ReadMethods(source);

        // 自己会等锁的方法集合（含 Orleans 入口与私有辅助）
        var gateTakers = methods
            .Where(method => method.Body.Contains("_writeGate.WaitAsync"))
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(gateTakers.Count >= 5, "锁持有者识别异常，实际=" + string.Join(',', gateTakers));

        var violations = new List<string>();
        foreach (var method in methods)
        {
            var wait = method.Body.IndexOf("_writeGate.WaitAsync", StringComparison.Ordinal);
            if (wait < 0) continue;
            var release = method.Body.IndexOf("_writeGate.Release", wait, StringComparison.Ordinal);
            Assert.True(release > wait, method.Name + "：拿到锁却没找到配对的 Release");

            var held = method.Body.Substring(wait, release - wait);
            foreach (Match call in Regex.Matches(held, @"await\s+(?<callee>[A-Za-z_]\w*)\s*\("))
            {
                var callee = call.Groups["callee"].Value;
                if (gateTakers.Contains(callee))
                    violations.Add(method.Name + " 在持锁区间里 await 了同样要 _writeGate 的 " + callee);
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void 强制下线的撤销账必须在锁外记()
    {
        // 上面那条是通用纪律，这条把本次真正踩到的那个点单独钉住：
        // 将来有人"顺手把记撤销挪回锁里"时，失败信息会直接说出该挪出去的原因。
        var source = File.ReadAllText(Path.Combine(RepoRoot(), GrainPath));
        var methods = ReadMethods(source);
        var unpublish = methods.Single(method => method.Name == "UnpublishAsync");

        var record = unpublish.Body.IndexOf("RecordRevocationsAsync", StringComparison.Ordinal);
        var release = unpublish.Body.IndexOf("_writeGate.Release", StringComparison.Ordinal);
        Assert.True(record >= 0, "UnpublishAsync 里的强制撤销记账不见了");
        Assert.True(release >= 0 && record > release,
            "RecordRevocationsAsync 必须排在 _writeGate.Release 之后：SemaphoreSlim(1,1) 不可重入，"
            + "持锁期间等同一把锁会让单例 quest.manager grain 永久卡死");
    }
}
