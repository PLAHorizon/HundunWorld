using System.Collections.Generic;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface.World;
using MemoryPack;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 网关补的两个指示器事实：**条件不足的条数**与**这个发布者是不是交付者**。
/// <para>
/// 前者让灰 <c>!</c>（"以后会有"）在客户端可达——可接清单已按等级/前置过滤，被挡下的任务
/// 客户端原本一无所知；后者修掉"发布者头上亮黄 <c>?</c>、点进去却被交付者校验拒掉"。
/// 两者都不改 Grain 接口，只读已有目录快照。
/// </para>
/// </summary>
public class QuestIndicatorServerTests
{
    private static QuestCatalogEntry Entry(
        int questId, int giverKind = 1, int giverTemplateId = 2,
        int levelRequire = 0, int[]? prereqs = null, int publishState = 1,
        int repeatLimit = 0, int turnInKind = 0, int turnInTemplateId = 0) => new()
    {
        QuestId = questId,
        GiverKind = giverKind,
        GiverTemplateId = giverTemplateId,
        LevelRequire = levelRequire,
        PrerequisiteQuestIds = prereqs ?? System.Array.Empty<int>(),
        PublishState = publishState,
        RepeatLimit = repeatLimit,
        TurnInKind = turnInKind,
        TurnInTemplateId = turnInTemplateId,
    };

    [Fact]
    public void 等级不够与前置不够各进一档_可接的不计()
    {
        var entries = new[]
        {
            Entry(1, levelRequire: 50),                 // 等级不够
            Entry(2, prereqs: new[] { 99 }),            // 前置没做
            Entry(3),                                   // 现在就能接
            Entry(4, levelRequire: 50, prereqs: new[] { 99 }), // 两个都不满足：只进等级档
        };

        var locked = QuestIndicatorOfferCore.CountLocked(
            entries, giverKind: 1, giverTemplateId: 2, playerLevel: 10,
            completedQuestIds: System.Array.Empty<int>(), activeQuestIds: System.Array.Empty<int>());

        Assert.Equal(2, locked.ByLevel);
        Assert.Equal(1, locked.ByPrereq);
        Assert.Equal(3, locked.Total);
    }

    [Fact]
    public void 已接已交下线与别的发布者都不算接不了()
    {
        var entries = new[]
        {
            Entry(1, levelRequire: 50),                 // 已接：进行中，不算"接不了"
            Entry(2, levelRequire: 50),                 // 交过且不可重复
            Entry(3, levelRequire: 50),                 // 交过但可重复 ⇒ 仍可接
            Entry(4, levelRequire: 50, publishState: 0),// 下线
            Entry(5, levelRequire: 50, giverTemplateId: 9), // 别的 NPC
        };

        var locked = QuestIndicatorOfferCore.CountLocked(
            entries, 1, 2, playerLevel: 10,
            completedQuestIds: new[] { 2, 3 }, activeQuestIds: new[] { 1 });

        Assert.Equal(0, locked.Total);
    }

    [Fact]
    public void 前置都做完就该由可接清单接管_不再计入接不了()
    {
        var entries = new[] { Entry(1, prereqs: new[] { 7 }) };
        var locked = QuestIndicatorOfferCore.CountLocked(
            entries, 1, 2, playerLevel: 10, completedQuestIds: new[] { 7 }, activeQuestIds: System.Array.Empty<int>());

        Assert.Equal(0, locked.Total);
    }

    [Fact]
    public void 没有发布者身份或空目录时不猜()
    {
        Assert.Equal(0, QuestIndicatorOfferCore.CountLocked(new[] { Entry(1, levelRequire: 50) }, 0, 0, 1,
            System.Array.Empty<int>(), System.Array.Empty<int>()).Total);
        Assert.Equal(0, QuestIndicatorOfferCore.CountLocked(null, 1, 2, 1,
            System.Array.Empty<int>(), System.Array.Empty<int>()).Total);
    }

    [Fact]
    public void 交付者留空沿用发布者_显式配置则按显式()
    {
        Assert.True(QuestIndicatorOfferCore.IsTurnInHere(Entry(1), giverKind: 1, giverTemplateId: 2));

        Assert.False(QuestIndicatorOfferCore.IsTurnInHere(
            Entry(1, turnInKind: 1, turnInTemplateId: 3), 1, 2));
        Assert.True(QuestIndicatorOfferCore.IsTurnInHere(
            Entry(1, turnInKind: 1, turnInTemplateId: 3), 1, 3));

        // 发布者身份缺失时不猜"就是我"。
        Assert.False(QuestIndicatorOfferCore.IsTurnInHere(Entry(1), 0, 0));
        Assert.False(QuestIndicatorOfferCore.IsTurnInHere(null, 1, 2));
    }

    /// <summary>两个新字段必须真的经 MessageUnion/MemoryPack 走到客户端（追加在末尾，槽位不影响旧字段）。</summary>
    [Fact]
    public void 新字段随报文往返()
    {
        MessageUnion original = new QuestListResponseMessage
        {
            LockedOfferCount = 3,
            GiverEntityId = 70001UL,
            Quests = new List<QuestSummary>
            {
                new() { QuestId = 1001, TurnInHere = true },
                new() { QuestId = 1002, TurnInHere = false },
            },
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var back = MemoryPackSerializer.Deserialize<MessageUnion>(bytes) as QuestListResponseMessage;

        Assert.NotNull(back);
        Assert.Equal(3, back!.LockedOfferCount);
        Assert.Equal(70001UL, back.GiverEntityId);
        Assert.True(back.Quests[0].TurnInHere);
        Assert.False(back.Quests[1].TurnInHere);
    }
}
