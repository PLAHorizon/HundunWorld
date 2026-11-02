using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        // 允许一个已知例外：Orleans.Interface 不在客户端引用里，但它带着 Grain 契约，
        // 拷过去只是为了排查方便。例外必须写在这里而不是"断言放宽"。
        var extra = refreshed.Where(name => !referenced.Contains(name))
                             .Where(name => name != "Horizon.Orleans.Interface")
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
    public void 引用解析必须认得出本轮补进去的三个库()
    {
        // 突变验证用的正向锚：这三个是 2026-10-06 实测"Tools 陈旧"的那三个，
        // 如果哪天有人把清单缩回三个，前两条会红，而这条保证前两条不是因为解析空了才绿。
        var referenced = ClientReferencedAssemblies();
        Assert.Contains("Horizon.Game.Sim", referenced);
        Assert.Contains("Horizon.Game.ECS.Arch", referenced);
        Assert.Contains("Horizon.Game.World", referenced);
    }
}
