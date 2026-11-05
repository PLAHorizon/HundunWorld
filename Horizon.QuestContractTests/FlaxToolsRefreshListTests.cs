using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Horizon.QuestContractTests;

/// <summary>
/// T13 的闸门：客户端脚本引用清单（<c>HundunWorld/Source/Game/Game.Build.cs</c> 的 <c>dlls</c> 数组）
/// 与 Flax Tools 刷新清单（<c>scripts/copy-shared-dlls-to-flax-tools.ps1</c> 的 <c>$Assemblies</c>）
/// 是**同一份信息的两处手工拷贝**，而 Flax.Build 解析脚本程序集优先用引擎 <c>Binaries\Tools</c> 里的副本。
/// <para>
/// 两边一旦漂移，表现是"改了共享库却在编辑器里没反应"，而仓库门禁 <c>scripts/verify-client-scripts.sh</c>
/// 特意把仓库新鲜产物排在前面、剔掉 Tools 同名引用，于是**门禁绿而编辑器吃旧 DLL**。
/// 2026-10-06 实测就撞上这个：刷新清单只覆盖 Message/Core/Orleans.Interface，
/// 而客户端还引用 Sim（<c>QuestObjectiveCodec</c>、<c>HasProducer</c> 住在这里）、ECS.Arch、World，
/// 三个在 Tools 里都是陈旧的。本文件把这件事从"文档里写过了"升级成"改漏了会判红"。
/// </para>
/// </summary>
public class FlaxToolsRefreshListTests
{
    private const string BuildFile = @"HundunWorld\Source\Game\Game.Build.cs";
    private const string ScriptFile = @"scripts\copy-shared-dlls-to-flax-tools.ps1";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.GetFiles("*.sln").Any()) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>客户端引用的 Horizon 程序集（去掉 <c>.dll</c>）。</summary>
    private static SortedSet<string> ClientReferencedAssemblies()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), BuildFile));
        var names = Regex.Matches(source, "\"(Horizon\\.[A-Za-z0-9_.]+)\\.dll\"")
                        .Cast<Match>()
                        .Select(match => match.Groups[1].Value)
                        .Distinct();
        return new SortedSet<string>(names);
    }

    /// <summary>刷新脚本 <c>$Assemblies</c> 默认清单里的程序集名。</summary>
    private static SortedSet<string> RefreshListAssemblies()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), ScriptFile));
        var block = Regex.Match(source, @"\$Assemblies\s*=\s*@\((?<body>.*?)\n\s*\)", RegexOptions.Singleline);
        Assert.True(block.Success, "没在刷新脚本里找到 $Assemblies 数组——脚本结构变了，请先确认本闸门锚点还成立，别直接改掉断言。");

        // 注释行（# 开头）里的名字不算清单成员：脚本里有一段解释性注释提到了若干程序集名。
        var bodyLines = block.Groups["body"].Value
            .Replace("\r", string.Empty)
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("#", StringComparison.Ordinal));

        var names = new List<string>();
        foreach (var line in bodyLines)
            names.AddRange(Regex.Matches(line, "'([^']+)'").Cast<Match>().Select(match => match.Groups[1].Value));

        Assert.NotEmpty(names);
        return new SortedSet<string>(names);
    }

    [Fact]
    public void 客户端引用的每个horizon程序集都必须在刷新清单里()
    {
        var referenced = ClientReferencedAssemblies();
        Assert.NotEmpty(referenced);                       // 防空转：解析不到引用就不算通过
        var refreshed = RefreshListAssemblies();

        var missing = referenced.Where(name => !refreshed.Contains(name)).ToArray();
        Assert.Empty(missing);   // 漏一个 → 编辑器就会编译/运行在旧副本上（T13）
    }

    [Fact]
    public void 刷新清单里不得留着客户端根本不引用的名字()
    {
        var referenced = ClientReferencedAssemblies();
        var refreshed = RefreshListAssemblies();

        // 已知例外必须写在这里、带理由，而不是把断言放宽：
        //   Orleans.Interface —— 带 Grain 契约，客户端不引用，拷过去只为排查方便；
        //   Core.Abstract —— 客户端只用到 AppType/PassportType（已改由 Share.Client 提供），
        //                    但 DS 侧仍需要它，所以刷新清单继续带上它。
        var allowedExtras = new[] { "Horizon.Orleans.Interface", "Horizon.Core.Abstract" };
        var extra = refreshed.Where(name => !referenced.Contains(name))
                             .Where(name => !allowedExtras.Contains(name))
                             .ToArray();
        Assert.Empty(extra);
    }

    [Fact]
    public void 刷新清单按名字拼出的工程目录必须真的存在()
    {
        // 脚本是这么找源产物的：<RepoRoot>\<程序集名>\bin\<配置>\net10.0\<程序集名>.dll，
        // 即"程序集名 == 工程目录名"。只在别的项目 bin 里出现过的那份拷贝（例如 DsHost 里的 Sim）不满足这条规则。
        var root = RepoRoot();
        foreach (var name in RefreshListAssemblies())
        {
            var csproj = Path.Combine(root, name, name + ".csproj");
            Assert.True(File.Exists(csproj),
                $"{name} 在刷新清单里，但 {name}\\{name}.csproj 不存在——脚本会把它当 missing 跳过，等于没刷。");
        }
    }

    [Fact]
    public void 引用解析必须认得出客户端真正依赖的那几个库()
    {
        // 这条是前两条的正向对照：万一解析哪天空了，两边都空就会让前两条一起绿。
        // 2026-10-06 它钉的是当时实测陈旧的 Sim / ECS.Arch / World；2026-10-07 客户端瘦身成
        // 只装 Contracts（Sim、World 的权威算法留在服务端）。锚点若还钉着那两个全量名字，
        // 就等于要求客户端去引用它故意不引用的库——那不是"同步清单"，是把瘦身改回去。
        // 所以名字跟着瘦身改口径，作用不变：必须解析出现在客户端清单里的具体名字。
        var referenced = ClientReferencedAssemblies();
        Assert.Contains("Horizon.Game.Sim.Contracts", referenced);
        Assert.Contains("Horizon.Game.ECS.Arch", referenced);
        Assert.Contains("Horizon.Game.World.Contracts", referenced);
        Assert.Contains("Horizon.Planet", referenced);        // 2026-11-03 起：星球契约进客户端
    }

    /// <summary>
    /// 前三条都是拿正则读这个 <c>.ps1</c> 的，所以"脚本本身跑不跑得起来"完全不在覆盖范围内。
    /// 2026-11-04 实测到两层：我往清单里加 <c>Horizon.Planet</c> 时写了中文注释，Windows PowerShell 5.1
    /// 按 ANSI(GBK) 读无 BOM 的 <c>.ps1</c>，某些 UTF-8 中文字节序列会被解成它当作定界符的字符——
    /// 先是整段解析失败（"逗号后面缺少表达式"），改成 ASCII 之后又发现**仓库里原有的中文注释
    /// 会把紧跟其后的赋值行吞进注释**（那行以 mojibake 反引号结尾），于是自动探测引擎目录那段
    /// 从来没执行过，报的是 <c>$engine 未定义</c>。四种读法里只有"文件必须纯 ASCII"能同时挡住这两类，
    /// 而且不需要在测试里起 PowerShell 进程（第一版那么做，结果撞上 stdout/stderr 互相阻塞，一次跑十分钟）。
    /// </summary>
    [Fact]
    public void 刷新清单脚本必须只含ASCII字符()
    {
        var script = Path.Combine(RepoRoot(), ScriptFile);
        var text = File.ReadAllText(script);
        var bad = text.Select((c, i) => (c, i)).Where(x => x.c > 127).Take(5).ToList();
        Assert.True(bad.Count == 0,
            "PowerShell 5.1 按 ANSI 读无 BOM 的 .ps1，非 ASCII 注释可能被解成引号或反引号，" +
            "轻则整段解析失败、重则把下一行代码吞进注释。第一个越界字符在偏移 " +
            string.Join(",", bad.Select(b => b.i.ToString())) +
            "：" + string.Join(",", bad.Select(b => "U+" + ((int)b.c).ToString("X4"))));
    }
}
