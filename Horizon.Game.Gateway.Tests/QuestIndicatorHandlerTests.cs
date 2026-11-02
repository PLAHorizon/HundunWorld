using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// <c>QuestList</c> 的执行面：清单回包必须补上"接不了有几条"与"这个发布者是不是交付者"。
/// <para>
/// 内核与纯函数的判据已有单测；这里证明**处理器真的把它们填进了报文**（真注册表 + 假集群跑
/// <c>RouteHandlerAsync</c>），并钉住两条"不猜"：请求不带发布者、发布者认不出时，两个事实都保持 0/false。
/// </para>
/// </summary>
public class QuestIndicatorHandlerTests
{
    private const ulong CharacterId = 8806UL;
    private const ulong EntityId = 70006UL;

    private sealed class Harness
    {
        public QuestHandler Handler = null!;
        public Mock<Horizon.Orleans.Interface.World.IQuestManagerGrain> Manager = null!;
    }

    private static Harness Create(
        ulong resolvedTemplateId = 2,
        int activeQuestId = 0,
        int extraActiveQuestId = 0)
    {
        var registry = new DsEntityRegistry();
        registry.Apply(new DsEntityDescriptorBatch
        {
            ShardId = 0L,
            Descriptors = new[]
            {
                new DsEntityDescriptorDto
                {
                    EntityId = EntityId, ScriptedKind = 2, TemplateId = (int)resolvedTemplateId, SpawnPointId = -1,
                    X = 0f, Y = 0f, Z = 0f,
                },
            },
        });

        var manager = new Mock<Horizon.Orleans.Interface.World.IQuestManagerGrain>();
        manager.Setup(m => m.TakeRevokedQuestsAsync(It.IsAny<Guid>()))
               .ReturnsAsync(Array.Empty<int>());
        manager.Setup(m => m.GetOfferedQuestsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
                   It.IsAny<int[]>(), It.IsAny<int[]>()))
               .ReturnsAsync(new List<Horizon.Orleans.Interface.World.QuestOfferSummary>());
        manager.Setup(m => m.GetCatalogVersionAsync()).ReturnsAsync(1);
        manager.Setup(m => m.GetCatalogAsync(It.IsAny<bool>())).ReturnsAsync(
            new Horizon.Orleans.Interface.World.QuestCatalogSnapshot
            {
                CatalogVersion = 1,
                Entries = new[]
                {
                    // 等级不够
                    new Horizon.Orleans.Interface.World.QuestCatalogEntry
                    {
                        QuestId = 90001, QuestName = "高等级", PublishState = 1,
                        GiverKind = 1, GiverTemplateId = 2, LevelRequire = 99,
                    },
                    // 前置没做
                    new Horizon.Orleans.Interface.World.QuestCatalogEntry
                    {
                        QuestId = 90002, QuestName = "有前置", PublishState = 1,
                        GiverKind = 1, GiverTemplateId = 2,
                        PrerequisiteQuestIds = new[] { 80001 },
                    },
                    // 现在就能接
                    new Horizon.Orleans.Interface.World.QuestCatalogEntry
                    {
                        QuestId = 90003, QuestName = "可接", PublishState = 1,
                        GiverKind = 1, GiverTemplateId = 2, TurnInKind = 1, TurnInTemplateId = 2,
                    },
                    // 交付者是别的 NPC
                    new Horizon.Orleans.Interface.World.QuestCatalogEntry
                    {
                        QuestId = 90004, QuestName = "别处交", PublishState = 1,
                        GiverKind = 1, GiverTemplateId = 2, TurnInKind = 1, TurnInTemplateId = 5,
                    },
                },
            });

        var quest = new Mock<IQuestGrain>();
        quest.Setup(q => q.GetActiveQuestsAsync()).ReturnsAsync(BuildActive(activeQuestId, extraActiveQuestId));
        quest.Setup(q => q.GetCompletedQuestsAsync()).ReturnsAsync(new List<QuestData>());

        var character = new Mock<ICharacterGrain>();
        character.Setup(c => c.GetLastPositionAsync()).ReturnsAsync((0f, 0f, 0f, 0f));
        character.Setup(c => c.GetSyncProfileAsync()).ReturnsAsync(new CharacterSyncProfile { Level = 10 });

        var cluster = new Mock<IClusterClient>();
        cluster.Setup(c => c.GetGrain<IQuestGrain>(It.IsAny<Guid>(), It.IsAny<string>())).Returns(quest.Object);
        cluster.Setup(c => c.GetGrain<ICharacterGrain>(It.IsAny<long>(), It.IsAny<string>())).Returns(character.Object);
        cluster.Setup(c => c.GetGrain<Horizon.Orleans.Interface.World.IQuestManagerGrain>(It.IsAny<long>()))
               .Returns(manager.Object);

        var handler = new QuestHandler(
            NullLogger<MessageHandlerBase>.Instance, cluster.Object, new HorizonMessageAdapter(), registry, shardRouter: null);
        return new Harness { Handler = handler, Manager = manager };
    }

    private static List<QuestData> BuildActive(int questId, int extraQuestId)
    {
        var list = new List<QuestData>();
        if (questId > 0) list.Add(new QuestData { QuestId = questId, QuestName = "身上任务" });
        if (extraQuestId > 0) list.Add(new QuestData { QuestId = extraQuestId, QuestName = "身上任务二" });
        return list;
    }

    private static HorizonMessagePacket List(ulong entityId) => new()
    {
        Header = new MessageHeader
        {
            MessageType = MessageType.QuestListRequest,
            ServiceType = ServiceType.Quest,
            CharacterId = (long)CharacterId,
        },
        Body = new QuestListRequestMessage
        {
            CharacterId = CharacterId,
            GiverEntityId = entityId,
            ActiveOnly = false,
        },
    };

    [Fact]
    public async Task 清单回包补上接不了条数与交付者身份()
    {
        var h = Create(activeQuestId: 90003, extraActiveQuestId: 90004);

        var (_, packet) = await h.Handler.RouteHandlerAsync(List(EntityId));

        var response = Assert.IsType<QuestListResponseMessage>(packet.Body);

        // 90001 等级不够 + 90002 前置不够 = 2；90003/90004 是已接，不算"接不了"。
        Assert.Equal(2, response.LockedOfferCount);

        var byId = new Dictionary<int, QuestSummary>();
        foreach (var q in response.Quests) byId[q.QuestId] = q;
        Assert.True(byId[90003].TurnInHere, "交付者就是这位发布者，应为 true");
        Assert.False(byId[90004].TurnInHere, "交付者是别人，应为 false");
    }

    [Fact]
    public async Task 没带发布者实体时两个事实都不猜()
    {
        var h = Create(activeQuestId: 90003);

        var (_, packet) = await h.Handler.RouteHandlerAsync(List(0UL));

        var response = Assert.IsType<QuestListResponseMessage>(packet.Body);
        Assert.Equal(0, response.LockedOfferCount);
        Assert.All(response.Quests, q => Assert.False(q.TurnInHere));
        h.Manager.Verify(m => m.GetCatalogAsync(It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task 发布者认不出时也不调用目录()
    {
        var h = Create(activeQuestId: 90003);

        var (_, packet) = await h.Handler.RouteHandlerAsync(List(999999UL));

        var response = Assert.IsType<QuestListResponseMessage>(packet.Body);
        Assert.Equal(0, response.LockedOfferCount);
        h.Manager.Verify(m => m.GetCatalogAsync(It.IsAny<bool>()), Times.Never);
    }
}
