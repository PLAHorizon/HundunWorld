using System;
using System.IO;
using Horizon.Game.Message.Network;
using Horizon.Game.Sim.Quests;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// D11 交付对象校验：<b>任务指定了交付者，就只能在那一位面前交</b>。
/// <para>
/// 不测这条的话，症状是玩家在任意地点把任务交掉——奖励照发、进度照清，没有任何异常。
/// 而任务模板里的 <c>turn_in_kind / turn_in_template_id</c> 两列会一直是"写了没人读"的死列。
/// </para>
/// </summary>
public class QuestTurnInCoreTests
{
    private readonly ITestOutputHelper _log;

    public QuestTurnInCoreTests(ITestOutputHelper log) => _log = log;

    [Theory]
    [InlineData(1, 100, 0, 0, 1, 100)]      // 交付者留空 → 沿用发布者
    [InlineData(1, 100, 1, 200, 1, 200)]    // 显式指定交付者
    [InlineData(1, 0, 1, 200, 1, 200)]      // 发布者未配 → 用交付者
    [InlineData(0, 0, 0, 0, 0, 0)]          // 都没配 → 不限制
    public void ResolveRequired_FallsBackToGiver(int gk, int gt, int tk, int tt, int expectedKind, int expectedTemplate)
    {
        var (kind, template) = QuestTurnInCore.ResolveRequired(gk, gt, tk, tt);
        Assert.Equal(expectedKind, kind);
        Assert.Equal(expectedTemplate, template);
    }

    [Fact]
    public void Evaluate_DistinguishesEveryRejection()
    {
        // 不限制：带不带对象都通过。
        Assert.True(QuestTurnInCore.Evaluate(0, 0, 0UL, false, 0, 0).Ok);

        // 要求指定交付者：
        var missing = QuestTurnInCore.Evaluate(1, 200, 0UL, false, 0, 0);
        Assert.False(missing.Ok);
        Assert.Equal(QuestTurnInReject.MissingEntity, missing.Reason);

        var unresolved = QuestTurnInCore.Evaluate(1, 200, 77UL, false, 0, 0);
        Assert.False(unresolved.Ok);
        Assert.Equal(QuestTurnInReject.UnresolvedEntity, unresolved.Reason);

        var wrongKind = QuestTurnInCore.Evaluate(1, 200, 77UL, true, 3, 200);
        Assert.False(wrongKind.Ok);
        Assert.Equal(QuestTurnInReject.WrongKind, wrongKind.Reason);

        var wrongTemplate = QuestTurnInCore.Evaluate(1, 200, 77UL, true, 1, 201);
        Assert.False(wrongTemplate.Ok);
        Assert.Equal(QuestTurnInReject.WrongTemplate, wrongTemplate.Reason);

        var ok = QuestTurnInCore.Evaluate(1, 200, 77UL, true, 1, 200);
        Assert.True(ok.Ok);
        Assert.Equal(QuestTurnInReject.None, ok.Reason);

        _log.WriteLine(ok.Describe());
        Assert.Contains("turnin_req=200", ok.Describe());
    }

    /// <summary>
    /// 接线检查：内核再对，没人调用也等于没做。这里钉住三处真实入口。
    /// </summary>
    [Fact]
    public void Wiring_HandlerAndClientActuallyUseIt()
    {
        var repo = RepoRoot();

        // 请求体带实体 id（客户端→服务端）。
        var entityProperty = typeof(CompleteQuestRequest).GetProperty("EntityId");
        Assert.NotNull(entityProperty);
        Assert.Equal(typeof(ulong), entityProperty!.PropertyType);

        var handler = File.ReadAllText(Path.Combine(repo, "Horizon.Game.Core", "Handlers", "QuestHandler.cs"));
        Assert.Contains("ValidateTurnInAsync", handler, StringComparison.Ordinal);
        Assert.Contains("QuestTurnInCore.Evaluate", handler, StringComparison.Ordinal);
        // 取目录失败必须拒绝，不能放行。
        Assert.Contains("按拒绝处理", handler, StringComparison.Ordinal);

        var client = File.ReadAllText(Path.Combine(repo, "HundunWorld", "Source", "Game", "Network", "QuestClientService.cs"));
        Assert.Contains("EntityId = entityId", client, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Horizon.sln"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("找不到仓库根（Horizon.sln）");
    }
}
