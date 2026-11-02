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
/// 网关在"与 NPC 对话"这一刻到底给哪一域记了几笔好感——<b>行为</b>断言，不是源码扫描。
/// <para>
/// <see cref="NpcTalkFavorTests"/> 只能钉"代码里存在这一段、参数写在调用里"；
/// 这里用真 <see cref="DsEntityRegistry"/> + 假集群把 <c>RouteHandlerAsync</c> 整条跑一遍，
/// 钉的是实际传给 <c>AddAsync</c> 的四个值：域号、点数、来源键、来源上限。
/// </para>
/// <para>
/// 之所以要钉到参数级：① NPC 与怪物<b>共用同一个整数 id 空间</b>（都落在 <c>MonsterTemplateId</c> 上，
/// 只靠 <c>ScriptedKind</c> 分流），类别闸一旦失效，"砍一只墨麒麟"就会记成"和铸剑师聊过天"；
/// ② 对话若走默认来源上限 20，站在 NPC 面前连点二十次就白拿一整个 §14 玄阶 <c>favorNeed</c>。
/// </para>
/// </summary>
public class GatewayNpcTalkFavorTests
{
    private const ulong CharacterId = 8801UL;
    private const ulong EntityId = 70001UL;

    private static (QuestHandler handler, Mock<IFavorabilityGrain> favor) CreateHandler(
        byte scriptedKind, int templateId)
    {
        var registry = new DsEntityRegistry();
        registry.Apply(new DsEntityDescriptorBatch
        {
            ShardId = 0L,
            Descriptors = new[]
            {
                // 实体与角色都放在原点：距离必然达标，测的就只剩"类别 + 模板 id → 域 + 上限"
                new DsEntityDescriptorDto
                {
                    EntityId = EntityId,
                    ScriptedKind = scriptedKind,   // 2 = ScriptedKind.Npc
                    TemplateId = templateId,
                    SpawnPointId = -1,
                    X = 0f, Y = 0f, Z = 0f,
                },
            },
        });

        var favor = new Mock<IFavorabilityGrain>();
        favor.Setup(f => f.AddAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()))
             .ReturnsAsync(0);

        // 没有进行中的任务：本次交互不推进任何目标，只验好感入账
        var quest = new Mock<IQuestGrain>();
        quest.Setup(q => q.GetActiveQuestsAsync()).ReturnsAsync(new List<QuestData>());

        var character = new Mock<ICharacterGrain>();
        character.Setup(c => c.GetLastPositionAsync())
                 .ReturnsAsync((0f, 0f, 0f, 0f));

        var cluster = new Mock<IClusterClient>();
        cluster.Setup(c => c.GetGrain<IQuestGrain>(It.IsAny<Guid>(), It.IsAny<string>()))
               .Returns(quest.Object);
        cluster.Setup(c => c.GetGrain<ICharacterGrain>(It.IsAny<long>(), It.IsAny<string>()))
               .Returns(character.Object);
        cluster.Setup(c => c.GetGrain<IFavorabilityGrain>(It.IsAny<Guid>(), It.IsAny<string>()))
               .Returns(favor.Object);

        var handler = new QuestHandler(
            NullLogger<MessageHandlerBase>.Instance,
            cluster.Object,
            new HorizonMessageAdapter(),
            registry,
            shardRouter: null);
        return (handler, favor);
    }

    private static HorizonMessagePacket Interact() => new()
    {
        Header = new MessageHeader
        {
            MessageType = MessageType.QuestInteractRequest,
            ServiceType = ServiceType.Quest,
            CharacterId = (long)CharacterId,
        },
        Body = new QuestInteractRequestMessage
        {
            CharacterId = CharacterId,
            EntityId = EntityId,
        },
    };

    /// <summary>铸剑师（模板 1）属金：一次对话应给域 1 记 1 点，来源键 <c>npc:1</c>，上限 5。</summary>
    [Fact]
    public async Task TalkingToRegisteredNpc_CreditsItsDomainWithTheTightenedCap()
    {
        var (handler, favor) = CreateHandler(scriptedKind: 2, templateId: 1);

        var (success, packet) = await handler.RouteHandlerAsync(Interact());

        Assert.True(success);
        var result = Assert.IsType<QuestInteractResultMessage>(packet.Body);
        Assert.True(result.Ok, $"交互被拒：{result.Reason}（注册表/距离/身份解析没走通，后面无从可验）");

        favor.Verify(f => f.AddAsync(1, 1, "npc:1", 5), Times.Once);
    }

    /// <summary>守门人=土(5)、游方商队=木(2)：域号必须来自目录而不是硬编码第一位。</summary>
    [Theory]
    [InlineData(2, 5)]      // 守门人 → 土
    [InlineData(3, 2)]      // 游方商队 → 木
    public async Task DomainComesFromTheNpcTemplate(int templateId, int expectedDomain)
    {
        var (handler, favor) = CreateHandler(scriptedKind: 2, templateId);

        await handler.RouteHandlerAsync(Interact());

        favor.Verify(f => f.AddAsync(expectedDomain, 1, $"npc:{templateId}", 5), Times.Once);
    }

    /// <summary>
    /// 类别闸真正要挡的是**能通过裁决**的那一类：道具/场景物件（Prop、Item）与 NPC 共用同一个整数 id 空间
    /// （都落在 <c>MonsterTemplateId</c> 字段上），所以"对着一口井（模板 1）交互"完全可能合法通过，
    /// 少了 <c>Kind == Npc</c> 这一行就会被记成"和铸剑师聊过天"。
    /// <para>
    /// 首版这里写的是怪物（<c>scriptedKind: 1</c>），结果红在最后一句断言上——怪物在
    /// <c>QuestInteractionAuthority.Evaluate</c> 里就已被 <c>not_interactable</c> 挡掉，压根到不了记账点，
    /// 于是那条"不记账"是**因为提前 return 而不是因为类别闸**成立。这种"测绿了但没测到东西"的写法
    /// 换成 Prop 才钉得住；下面把两种情况都留下，各自说明为什么不该记账。
    /// </para>
    /// </summary>
    [Fact]
    public async Task PropWithNpcTemplateId_PassesTheVerdictButCreditsNothing()
    {
        var (handler, favor) = CreateHandler(scriptedKind: 3, templateId: 1);   // 3 = ScriptedKind.Event → Prop

        var (_, packet) = await handler.RouteHandlerAsync(Interact());
        var result = Assert.IsType<QuestInteractResultMessage>(packet.Body);

        Assert.True(result.Ok, "道具本该可交互（任务里的'查看物件'靠它），否则这条测试退化成只验裁决");
        favor.Verify(f => f.AddAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()),
            Times.Never);
    }

    /// <summary>怪物连裁决都过不了，自然更不该有好感入账（记账点在裁决之后）。</summary>
    [Fact]
    public async Task MonsterIsNotInteractableAndCreditsNothing()
    {
        var (handler, favor) = CreateHandler(scriptedKind: 1, templateId: 1);   // 1 = ScriptedKind.Monster

        var (_, packet) = await handler.RouteHandlerAsync(Interact());
        var result = Assert.IsType<QuestInteractResultMessage>(packet.Body);

        Assert.False(result.Ok);
        Assert.Equal("not_interactable", result.Reason);
        favor.Verify(f => f.AddAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()),
            Times.Never);
    }

    /// <summary>未登记的"路人"没有域：不许给一个不存在的域加好感。</summary>
    [Fact]
    public async Task UnregisteredNpc_CreditsNothing()
    {
        var (handler, favor) = CreateHandler(scriptedKind: 2, templateId: 987654);

        await handler.RouteHandlerAsync(Interact());

        favor.Verify(f => f.AddAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()),
            Times.Never);
    }

    /// <summary>裁决不通过（实体 id 根本不存在）时不许入账——记账点必须在裁决之后。</summary>
    [Fact]
    public async Task UnknownEntity_CreditsNothing()
    {
        var (handler, favor) = CreateHandler(scriptedKind: 2, templateId: 1);

        var packet = new HorizonMessagePacket
        {
            Header = new MessageHeader
            {
                MessageType = MessageType.QuestInteractRequest,
                ServiceType = ServiceType.Quest,
                CharacterId = (long)CharacterId,
            },
            Body = new QuestInteractRequestMessage { CharacterId = CharacterId, EntityId = 424242UL },
        };
        var (_, response) = await handler.RouteHandlerAsync(packet);

        Assert.False(Assert.IsType<QuestInteractResultMessage>(response.Body).Ok);
        favor.Verify(f => f.AddAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()),
            Times.Never);
    }
}
