using System;
using System.Collections.Generic;
using System.IO;
using MemoryPack;
using Xunit;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 好感度快照这条新下行链的三个易错点。
/// <para>
/// §14 的玄阶门槛要显示"火域好感 24/60"，数据只能来自 FavorabilityGrain；本仓把这份快照挂在
/// <b>技能目录下行</b>上（打开树页本来就拉一次），于是链变成：
/// 网关读 grain → 塞进回包 → MemoryPack 上线 → 客户端缓存 → 门槛文案消费。
/// 每一环都可能是"代码写了、线上没生效"的那一环。
/// </para>
/// </summary>
public class SkillCatalogFavorSnapshotTests
{
    private const string GatewayHandlerFile = @"Horizon.Game.Core\Handlers\SkillHandler.cs";
    private const string CatalogServiceFile = @"HundunWorld\Source\Game\Services\SkillCatalogService.cs";
    private const string TreeFile = @"HundunWorld\Source\Game\Combat\Skills\WuxingSkillTree.cs";

    /// <summary>
    /// "没取到"(null) 与"确认零好感"(空表) 必须在过线之后仍然可分。
    /// 两者一旦合并：读失败会被显示成"你一点好感都没有"（假缺项，人会白去刷怪），
    /// 新角色会被显示成"读不到"（该给的进度反而没了）。
    /// </summary>
    [Fact]
    public void UnknownFavorabilityStaysDistinctFromConfirmedZeroAcrossTheWire()
    {
        var withData = new SkillCatalogSyncMessage
        {
            Success = true,
            CharacterId = 8UL,
            FavorByDomain = new Dictionary<int, int> { [4] = 24, [1] = 0 },
        };
        var roundTripped = MemoryPackSerializer.Deserialize<SkillCatalogSyncMessage>(
            MemoryPackSerializer.Serialize(withData));

        Assert.NotNull(roundTripped);
        Assert.NotNull(roundTripped!.FavorByDomain);
        Assert.Equal(24, roundTripped.FavorByDomain![4]);
        // 0 也必须原样带过去：那是一次真实的"刷过但没到位"，与"没数"不是一回事
        Assert.True(roundTripped.FavorByDomain.ContainsKey(1), "值为 0 的域被序列化丢掉了，UI 会把\"确认为 0\"当成\"不知道\"");
        Assert.Equal(0, roundTripped.FavorByDomain[1]);

        var unknown = new SkillCatalogSyncMessage { Success = true, CharacterId = 8UL };
        var unknownRoundTrip = MemoryPackSerializer.Deserialize<SkillCatalogSyncMessage>(
            MemoryPackSerializer.Serialize(unknown));
        Assert.Null(unknownRoundTrip!.FavorByDomain);
    }

    /// <summary>
    /// 网关读好感必须用 <c>GrainKeys.ForEntity</c>，不能顺手抄背包那把大端 Guid 键。
    /// 这个错配本仓已经踩过两次（任务发奖、击杀好感）：写侧一把键、读侧另一把键，
    /// 两边都"成功"，玩家永远看到 0。<b>而且它不会报错</b>，所以钉在源码上。
    /// </summary>
    [Fact]
    public void GatewayReadsFavorabilityOnTheSameKeyTheWritersUse()
    {
        var text = Read(GatewayHandlerFile);
        // 锚在**方法定义**上：文件里第一个出现的 "FillFavorSnapshotAsync" 是调用点，
        // 从调用点截一段会把"用了哪把键"整个错过（本测试首跑就是这么红的）。
        int start = text.IndexOf("private async Task FillFavorSnapshotAsync", StringComparison.Ordinal);
        Assert.True(start >= 0, "网关没有好感快照回填这一段：下行字段会是死的");
        // 取方法体（到下一个 XML 文档注释为止），别拿整个文件的用点当证据
        int end = text.IndexOf("/// <summary>", start, StringComparison.Ordinal);
        var body = end > start ? text.Substring(start, end - start) : text.Substring(start);

        Assert.True(body.Contains("GrainKeys.ForEntity", StringComparison.Ordinal),
            "读好感用的不是 ForEntity 键：写入侧（击杀/任务）都落在那把键上，这里会永远读到空表");
        Assert.False(body.Contains("ToInventoryKey", StringComparison.Ordinal),
            "回填好感误用了背包键（GrainKeyMapper.ToInventoryKey）：跨键读，快照恒空");
        Assert.True(body.Contains("IFavorabilityGrain", StringComparison.Ordinal),
            "没有向 FavorabilityGrain 取数：这个字段没人填");
    }

    /// <summary>客户端拿到快照后要真的用掉，否则协议加了、显示照旧（G17 那一类死字段）。</summary>
    [Fact]
    public void ClientDisplaysFavorProgressFromTheSnapshot()
    {
        var service = Read(CatalogServiceFile);
        Assert.True(service.Contains("FavorByDomain", StringComparison.Ordinal),
            "客户端缓存没接这个快照：网关回填了也没人读");

        var tree = Read(TreeFile);
        Assert.True(tree.Contains("FavorOf", StringComparison.Ordinal),
            "门槛文案没消费好感快照：玄阶仍然只说\"需 60\"，玩家不知道还差多少");
    }

    private static string Read(string relativeToRepoRoot)
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, relativeToRepoRoot);
        if (!File.Exists(path)) return string.Empty;     // 客户端源码不在本工作区：没有链可钉
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
