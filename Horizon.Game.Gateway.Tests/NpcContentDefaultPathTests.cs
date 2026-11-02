using System;
using System.IO;
using Horizon.Game.Core.Sim.Server;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// NPC 内容默认装载路径：没配 <c>--npc-content</c> 时，服务要能自己找到导出文件。
/// <para>
/// 不测这条的话，症状是"NPC 编辑器里配了 5 个 NPC，进游戏一个也看不见"——
/// 刷点/货架/教学/作息全在快照里，服务不装就等于没配，而且服务与客户端都不报错。
/// </para>
/// </summary>
public class NpcContentDefaultPathTests
{
    private readonly ITestOutputHelper _log;

    public NpcContentDefaultPathTests(ITestOutputHelper log) => _log = log;

    [Fact]
    public void ProbeList_FindsOnlyExistingFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "npc_default_" + Guid.NewGuid().ToString("N"));
        var empty = Path.Combine(root, "empty");
        var withFile = Path.Combine(root, "withfile");
        Directory.CreateDirectory(empty);
        Directory.CreateDirectory(withFile);

        try
        {
            Assert.Equal(string.Empty, NpcContentLoader.ResolveDefaultPath(new[] { empty, "" }));

            var expected = Path.Combine(withFile, NpcContentLoader.DefaultContentFileName);
            File.WriteAllText(expected, "{}");

            var found = NpcContentLoader.ResolveDefaultPath(new[] { empty, withFile });
            Assert.Equal(expected, found);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* 临时目录清不掉不影响断言 */ }
        }
    }

    [Fact]
    public void ProductionProbe_FindsRepoSnapshot()
    {
        var found = NpcContentLoader.ResolveDefaultPath();
        _log.WriteLine("resolved=" + found);

        Assert.False(string.IsNullOrWhiteSpace(found), "仓库里找不到默认 NPC 内容导出（db_content.json）");
        Assert.True(File.Exists(found), "返回的默认路径指向一个不存在的文件：" + found);

        // 路径必须落在导出的那一份上，别悄悄退到开发用的 dev_content.json。
        Assert.Equal(NpcContentLoader.DefaultContentFileName, Path.GetFileName(found));
    }
}
