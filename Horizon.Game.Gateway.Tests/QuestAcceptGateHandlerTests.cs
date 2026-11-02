using System;
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
/// 接任务的**执行面**（发布者校验）：真注册表 + 假集群，把 <c>RouteHandlerAsync</c> 整条跑一遍。
/// <para>
/// 这一层此前只有源码守卫（"调了 ValidateGiverAsync"），证明不了**拒绝真的发生、且没有落库**。
/// 本文件钉四件事：不带实体被拒且 `AcceptFromTemplateAsync` 一次都没调、站在别的 NPC 面前被拒、
/// 实体认不出被拒、目录读失败时**拒绝而不是放行**；另有一个正例证明门槛不是"全拒"。
/// </para>
/// </summary>
public class QuestAcceptGateHandlerTests
{
    private const ulong CharacterId = 8801UL;
    private const ulong EntityId = 70001UL;
    private const int QuestId = 90001;

    private static (QuestHandler Handler, Mock<IQuestGrain> Quest) Create(int giverTemplateId, bool catalogThrows = false)
    {
        var registry = new DsEntityRegistry();
        registry.Apply(new DsEntityDescriptorBatch
        {
            ShardId = 0L,
            Descriptors = new[]
            {
                // 实体与角色都在原点：距离必然达标，测的就只剩"身份对不对"。
                new DsEntityDescriptorDto
                {
                    EntityId = EntityId,
                    ScriptedKind = 2,        // ScriptedKind.Npc
                    TemplateId = 2,          // 实体真实身份 = 守门人
                    SpawnPointId = -1,
                    X = 0f, Y = 0f, Z = 0f,
                },
            },
        });

        var manager = new Mock<Horizon.Orleans.Interface.World.IQuestManagerGrain>();
        if (catalogThrows)
        {
            manager.Setup(m => m.GetCatalogAsync()).ThrowsAsync(new InvalidOperationException("catalog down"));
        }
        else
        {
            manager.Setup(m => m.GetCatalogAsync()).ReturnsAsync(new Horizon.Orleans.Interface.World.QuestCatalogSnapshot
            {
                CatalogVersion = 1,
                Entries = new[]
                {
                    new Horizon.Orleans.Interface.World.QuestCatalogEntry
                    {
                        QuestId = QuestId, QuestName = "入门一", PublishState = 1,
                        GiverKind = 1, GiverTemplateId = giverTemplateId,
                    },
                },
            });
        }

        var quest = new Mock<IQuestGrain>();
        quest.Setup(q => q.AcceptFromTemplateAsync(It.IsAny<int>(), It.IsAny<int>()))
             .ReturnsAsync(new QuestData { QuestId = QuestId, QuestName = "入门一" });

        var character = new Mock<ICharacterGrain>();
        character.Setup(c => c.GetLastPositionAsync()).ReturnsAsync((0f, 0f, 0f, 0f));
        character.Setup(c => c.GetSyncProfileAsync()).ReturnsAsync(new CharacterSyncProfile { Level = 10 });

        var cluster = new Mock<IClusterClient>();
        cluster.Setup(c => c.GetGrain<IQuestGrain>(It.IsAny<Guid>(), It.IsAny<string>())).Returns(quest.Object);
        cluster.Setup(c => c.GetGrain<ICharacterGrain>(It.IsAny<long>(), It.IsAny<string>())).Returns(character.Object);
        cluster.Setup(c => c.GetGrain<Horizon.Orleans.Interface.World.IQuestManagerGrain>(It.IsAny<long>())).Returns(manager.Object);

        var handler = new QuestHandler(
            NullLogger<MessageHandlerBase>.Instance, cluster.Object, new HorizonMessageAdapter(), registry, shardRouter: null);
        return (handler, quest);
    }

    private static HorizonMessagePacket Accept(ulong entityId) => new()
    {
        Header = new MessageHeader
        {
            MessageType = MessageType.AcceptQuest,
            ServiceType = ServiceType.Quest,
            CharacterId = (long)CharacterId,
        },
        Body = new AcceptQuestRequest { CharacterId = CharacterId, QuestId = QuestId, EntityId = entityId },
    };

    [Fact]
    public async Task 没带发布者实体_被拒且不落库()
    {
        var (handler, quest) = Create(giverTemplateId: 2);

        var (_, packet) = await handler.RouteHandlerAsync(Accept(0UL));

        var response = Assert.IsType<AcceptQuestResponse>(packet.Body);
        Assert.False(response.Success);
        Assert.Contains("发布者", response.Message);
        quest.Verify(q => q.AcceptFromTemplateAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task 站在别的NPC面前_被拒且不落库()
    {
        // 任务由 NPC 3 发布，玩家却站在 NPC 2 面前。
        var (handler, quest) = Create(giverTemplateId: 3);

        var (_, packet) = await handler.RouteHandlerAsync(Accept(EntityId));

        var response = Assert.IsType<AcceptQuestResponse>(packet.Body);
        Assert.False(response.Success);
        Assert.Contains("不在这个对象处", response.Message);
        quest.Verify(q => q.AcceptFromTemplateAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task 实体认不出_被拒且不落库()
    {
        var (handler, quest) = Create(giverTemplateId: 2);

        var (_, packet) = await handler.RouteHandlerAsync(Accept(999UL));

        var response = Assert.IsType<AcceptQuestResponse>(packet.Body);
        Assert.False(response.Success);
        Assert.Contains("附近没有", response.Message);
        quest.Verify(q => q.AcceptFromTemplateAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task 目录读失败_宁可拒绝也不放行()
    {
        var (handler, quest) = Create(giverTemplateId: 2, catalogThrows: true);

        var (_, packet) = await handler.RouteHandlerAsync(Accept(0UL));

        var response = Assert.IsType<AcceptQuestResponse>(packet.Body);
        Assert.False(response.Success);
        Assert.Contains("校验失败", response.Message);
        quest.Verify(q => q.AcceptFromTemplateAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task 站在正确的发布者面前_放行并落库一次()
    {
        var (handler, quest) = Create(giverTemplateId: 2);

        var (_, packet) = await handler.RouteHandlerAsync(Accept(EntityId));

        var response = Assert.IsType<AcceptQuestResponse>(packet.Body);
        Assert.True(response.Success, response.Message);
        quest.Verify(q => q.AcceptFromTemplateAsync(QuestId, 10), Times.Once);
    }

    [Fact]
    public async Task 不限制发布者的任务不需要实体()
    {
        // 模板里 giver_template_id = 0：自由接取是合法配置，不该被这道门误伤。
        var (handler, quest) = Create(giverTemplateId: 0);

        var (_, packet) = await handler.RouteHandlerAsync(Accept(0UL));

        var response = Assert.IsType<AcceptQuestResponse>(packet.Body);
        Assert.True(response.Success, response.Message);
        quest.Verify(q => q.AcceptFromTemplateAsync(QuestId, 10), Times.Once);
    }
}