using System;
using System.IO;
using Horizon.Game.Message;
using Horizon.Game.Message.Network;
using Horizon.Game.Sim.Quests;
using MemoryPack;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 任务**发布者**校验：任务指定了发布者，就只能在那一处接（与交付同一纪律）。
/// <para>
/// 缺陷形状（与制作清单/动作那对同形）：可接列表按发布者过滤，而接取动作只带 questId——
/// 直连一条消息就能在任何地方接走高等级任务。判据复用 <see cref="QuestTurnInCore.Evaluate"/>，
/// 本文件另外钉住"EntityId 真的随报文走到服务端"（MemoryPack 槽位）。
/// </para>
/// </summary>
public class QuestGiverGateTests
{
    [Fact]
    public void 未指定发布者时不限制_带不带实体都放行()
    {
        Assert.True(QuestGiverCore.Evaluate(0, 0, 0UL, false, 0, 0).Ok);
        Assert.True(QuestGiverCore.Evaluate(1, 0, 0UL, false, 0, 0).Ok);
    }

    [Fact]
    public void 指定发布者时_四类拒绝分得开()
    {
        // 没带实体
        Assert.Equal(QuestTurnInReject.MissingEntity, QuestGiverCore.Evaluate(1, 2, 0UL, false, 0, 0).Reason);
        // 带了但服务端认不出
        Assert.Equal(QuestTurnInReject.UnresolvedEntity, QuestGiverCore.Evaluate(1, 2, 900UL, false, 0, 0).Reason);
        // 类别不对（递给了物件/怪物）
        Assert.Equal(QuestTurnInReject.WrongKind, QuestGiverCore.Evaluate(1, 2, 900UL, true, 3, 2).Reason);
        // 类别对、模板不对（站在别的 NPC 面前）
        Assert.Equal(QuestTurnInReject.WrongTemplate, QuestGiverCore.Evaluate(1, 2, 900UL, true, 1, 3).Reason);
    }

    [Fact]
    public void 站在正确的发布者面前才放行()
    {
        var verdict = QuestGiverCore.Evaluate(1, 2, 900UL, true, 1, 2);
        Assert.True(verdict.Ok);
        Assert.Equal(QuestTurnInReject.None, verdict.Reason);
    }

    [Fact]
    public void 四类拒绝各有一句给玩家看的理由且互不相同()
    {
        var messages = new[]
        {
            QuestGiverCore.RejectMessage(QuestTurnInReject.MissingEntity),
            QuestGiverCore.RejectMessage(QuestTurnInReject.UnresolvedEntity),
            QuestGiverCore.RejectMessage(QuestTurnInReject.WrongKind),
            QuestGiverCore.RejectMessage(QuestTurnInReject.WrongTemplate),
        };

        for (var i = 0; i < messages.Length; i++)
        {
            Assert.False(string.IsNullOrWhiteSpace(messages[i]));
            for (var j = i + 1; j < messages.Length; j++)
                Assert.NotEqual(messages[i], messages[j]);
        }
    }

    [Fact]
    public void 接取请求的发布者实体随报文往返()
    {
        MessageUnion original = new AcceptQuestRequest
        {
            CharacterId = 42,
            QuestId = 90001,
            EntityId = 0x0000_0001_0000_0002,
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var back = MemoryPackSerializer.Deserialize<MessageUnion>(bytes) as AcceptQuestRequest;

        Assert.NotNull(back);
        Assert.Equal(42UL, back!.CharacterId);
        Assert.Equal(90001, back.QuestId);
        Assert.Equal(0x0000_0001_0000_0002UL, back.EntityId);
    }

    [Fact]
    public void 服务端接取前必须核对发布者_客户端必须报实体()
    {
        var handler = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.Game.Core", "Handlers", "QuestHandler.cs"));
        Assert.Contains("await ValidateGiverAsync(request)", handler, StringComparison.Ordinal);
        Assert.Contains("QuestGiverCore.Evaluate(", handler, StringComparison.Ordinal);
        Assert.Contains("QuestGiverCore.RejectMessage(", handler, StringComparison.Ordinal);

        var client = File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Source", "Game", "Network", "QuestClientService.cs"));
        Assert.Contains("public bool Accept(int questId, ulong giverEntityId)", client, StringComparison.Ordinal);
        Assert.Contains("EntityId = giverEntityId,", client, StringComparison.Ordinal);

        var page = File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Source", "Game", "UI", "Ink", "Pages", "MenuQuestsPage.cs"));
        Assert.Contains("service.Accept(quest.QuestId, quest.GiverEntityId)", page, StringComparison.Ordinal);
        Assert.Contains("GetOffersWithGiver()", page, StringComparison.Ordinal);
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