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
using Orleans;
using Xunit;
using World = Horizon.Orleans.Interface.World;

namespace Horizon.QuestContractTests;

/// <summary>
/// 网关"任务清单"那一格（1383 → 1384）里**按发布者收窄可接列表**的行为断言。
/// <para>
/// 身份判定本身是纯函数，已由 <c>DsEntityIdentityTests</c> 钉过；这里钉的是**胶水**：
/// 认不出玩家指的这个对象时，处理方到底有没有去要那张表。现场表现是"对着一个不存在的对象
/// 也能刷出整个任务目录"——它不会抛异常，也不会让任何一层报错，只有把"一次都不该调用"
/// 写成断言才看得见。
/// </para>
/// <para>
/// 脚手架沿用 <see cref="QuestHandlerInteractTests"/>：真 <see cref="DsEntityRegistry"/> + 假集群
/// （Moq 只代理接口；进程内构造 Grain 会让 testhost 静默秒退，见 docs/QUEST_SYSTEM_DESIGN.md §7.2）。
/// </para>
/// </summary>
public class QuestHandlerOfferListTests
{
    private const ulong CharacterId = 8899UL;
    private const ulong EntityId = 4242UL;
    private const int NpcTemplateId = 1;
    private const byte ScriptedKindNpc = 2;
    private const int CatalogVersion = 424242;

    private readonly Mock<IQuestGrain> _quest = new();
    private readonly Mock<World.IQuestManagerGrain> _manager = new();
    private readonly List<(int Kind, int TemplateId, int Level)> _offerCalls = new();

    private QuestHandler Build(bool withDescriptor, ulong? descriptorEntityId = EntityId,
                              byte scriptedKind = ScriptedKindNpc, int templateId = NpcTemplateId)
    {
        var registry = new DsEntityRegistry();
        if (withDescriptor)
        {
            registry.Apply(new DsEntityDescriptorBatch
            {
                ShardId = 0L,
                Descriptors = new[]
                {
                    new DsEntityDescriptorDto
                    {
                        EntityId = descriptorEntityId!.Value,
                        ScriptedKind = scriptedKind,
                        TemplateId = templateId,
                        SpawnPointId = -1,
                    },
                },
            });
        }

        _quest.Setup(q => q.GetActiveQuestsAsync()).ReturnsAsync(new List<QuestData>());
        _quest.Setup(q => q.GetCompletedQuestsAsync()).ReturnsAsync(new List<QuestData>());
        _manager.Setup(m => m.TakeRevokedQuestsAsync(It.IsAny<Guid>())).ReturnsAsync(Array.Empty<int>());
        _manager.Setup(m => m.GetCatalogVersionAsync()).ReturnsAsync(CatalogVersion);
        _manager.Setup(m => m.GetOfferedQuestsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<int[]>(), It.IsAny<int[]>()))
                .ReturnsAsync((int kind, int template, int level, int[] _, int[] _) =>
                {
                    _offerCalls.Add((kind, template, level));
                    return new List<World.QuestOfferSummary>
                    {
                        new() { QuestId = 90001, QuestName = "金·初入门墙", GiverName = "铸剑师", ContentVersion = 2 },
                    };
                });

        var character = new Mock<ICharacterGrain>();
        character.Setup(c => c.GetLastPositionAsync()).ReturnsAsync((0f, 0f, 0f, 0f));

        var cluster = new Mock<IClusterClient>();
        cluster.Setup(c => c.GetGrain<IQuestGrain>(It.IsAny<Guid>(), It.IsAny<string>())).Returns(_quest.Object);
        cluster.Setup(c => c.GetGrain<ICharacterGrain>(It.IsAny<long>(), It.IsAny<string>())).Returns(character.Object);
        cluster.Setup(c => c.GetGrain<World.IQuestManagerGrain>(It.IsAny<long>(), It.IsAny<string>())).Returns(_manager.Object);

        return new QuestHandler(NullLogger<MessageHandlerBase>.Instance, cluster.Object,
                                 new HorizonMessageAdapter(), registry, shardRouter: null);
    }

    private async Task<QuestListResponseMessage> Send(QuestHandler handler, ulong giverEntityId)
    {
        var packet = new HorizonMessagePacket
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
                ActiveOnly = false,
                GiverEntityId = giverEntityId,
            },
        };

        var (success, response) = await handler.RouteHandlerAsync(packet);
        Assert.True(success);
        return Assert.IsType<QuestListResponseMessage>(response.Body);
    }

    [Fact]
    public async Task 不带发布者时按服务端解析出的零值请求全目录()
    {
        var response = await Send(Build(withDescriptor: true), giverEntityId: 0UL);

        // 0/0 = 不按发布者过滤，这是"内容全由服务端按发布态/等级/前置算"那条口径的落点。
        var call = Assert.Single(_offerCalls);
        Assert.Equal(0, call.Kind);
        Assert.Equal(0, call.TemplateId);
        // 线格式照发：名字与发布者标签来自服务端，不是客户端拼的。
        var offer = Assert.Single(response.Offers);
        Assert.Equal(90001, offer.QuestId);
        Assert.Equal("铸剑师", offer.GiverLabel);
        Assert.Equal(2, offer.ContentVersion);
        Assert.Equal(CatalogVersion, response.CatalogVersion);
    }

    [Fact]
    public async Task 发布者实体认不出时不给总表但清单与版本号照发()
    {
        // 注册表里根本没有这个 id：拿它换到整个目录，比"这次没看到可接任务"糟得多。
        var response = await Send(Build(withDescriptor: false), giverEntityId: EntityId);

        Assert.Empty(_offerCalls);
        Assert.Empty(response.Offers);
        Assert.Equal(CatalogVersion, response.CatalogVersion);
        // 关键：被拒的只是"可接收窄"这一步，任务清单本身不能被一起吞掉。
        Assert.Empty(response.Quests);
        _manager.Verify(m => m.GetCatalogVersionAsync(), Times.AtLeastOnce());
    }

    [Fact]
    public async Task 发布者由注册表解出而不是客户端报什么就用什么()
    {
        var handler = Build(withDescriptor: true);

        // 独立算一遍期望值：胶水必须转交权威纯函数对同一个注册表的判定，而不是它自己的猜测。
        var registry = new DsEntityRegistry();
        registry.Apply(new DsEntityDescriptorBatch
        {
            ShardId = 0L,
            Descriptors = new[]
            {
                new DsEntityDescriptorDto { EntityId = EntityId, ScriptedKind = ScriptedKindNpc,
                                            TemplateId = NpcTemplateId, SpawnPointId = -1 },
            },
        });
        Assert.True(QuestInteractionAuthority.TryResolveGiver(
            registry, 0L, EntityId, (0f, 0f, 0f), out int expectedKind, out int expectedTemplate, out _));

        var response = await Send(handler, giverEntityId: EntityId);

        var call = Assert.Single(_offerCalls);
        Assert.Equal(expectedKind, call.Kind);
        Assert.Equal(expectedTemplate, call.TemplateId);
        Assert.NotEqual(0, call.Kind);
        Assert.Single(response.Offers);
    }

    [Fact]
    public async Task 发布者超出交互半径时同样不给总表()
    {
        // 距离核对发生在服务端解析这一步，客户端把 id 报得再准也没用。
        var cluster = new Mock<IClusterClient>();
        var far = new Mock<ICharacterGrain>();
        far.Setup(c => c.GetLastPositionAsync()).ReturnsAsync((5000f, 0f, 0f, 0f));
        cluster.Setup(c => c.GetGrain<IQuestGrain>(It.IsAny<Guid>(), It.IsAny<string>())).Returns(_quest.Object);
        cluster.Setup(c => c.GetGrain<ICharacterGrain>(It.IsAny<long>(), It.IsAny<string>())).Returns(far.Object);
        cluster.Setup(c => c.GetGrain<World.IQuestManagerGrain>(It.IsAny<long>(), It.IsAny<string>())).Returns(_manager.Object);

        var handler = new QuestHandler(NullLogger<MessageHandlerBase>.Instance, cluster.Object,
                                       new HorizonMessageAdapter(), BuildRegistry(), shardRouter: null);

        var response = await Send(handler, giverEntityId: EntityId);

        Assert.Empty(_offerCalls);
        Assert.Empty(response.Offers);
    }

    private static DsEntityRegistry BuildRegistry()
    {
        var registry = new DsEntityRegistry();
        registry.Apply(new DsEntityDescriptorBatch
        {
            ShardId = 0L,
            Descriptors = new[]
            {
                new DsEntityDescriptorDto { EntityId = EntityId, ScriptedKind = ScriptedKindNpc,
                                            TemplateId = NpcTemplateId, SpawnPointId = -1 },
            },
        });
        return registry;
    }
}
