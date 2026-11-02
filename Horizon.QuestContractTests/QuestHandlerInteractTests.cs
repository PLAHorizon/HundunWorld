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

namespace Horizon.QuestContractTests;

/// <summary>
/// 网关处理链上那一格的**行为**断言：选中对象按 G 之后，<see cref="QuestHandler"/> 到底
/// 拿哪个任务 id、哪个目标下标去调 <c>UpdateQuestProgressAsync</c>，以及哪些情况下**一次都不该调**。
/// <para>
/// 身份与邻近判定本身已由 <c>DsEntityIdentityTests</c>（纯函数）与 <c>DsEntityDescriptorProductionTests</c>
/// （真 <c>DsWorldSession</c> 产出）钉过；这里补的是它们之外的**胶水**：拒绝短码怎么回、
/// "一次交互只推它自己那一类目标"、"已达标的任务不再被推"。这类东西出错的现场表现是
/// "玩家按了 G，任务纹丝不动"或"聊一句就把击杀目标推进了"，都不会抛异常。
/// </para>
/// <para>
/// 用真 <see cref="DsEntityRegistry"/> + 假集群（Moq 只代理接口，不进程内构造 Grain——
/// 后者会让 testhost 静默秒退，见 <c>docs/QUEST_SYSTEM_DESIGN.md</c> §7.2）。
/// </para>
/// </summary>
public class QuestHandlerInteractTests
{
    private const ulong CharacterId = 7788UL;
    private const int QuestId = 20001;
    private const int NpcTemplateId = 1;      // 铸剑师（金）
    private const int MonsterTemplateId = 4;

    private readonly Mock<IQuestGrain> _quest = new();
    private readonly List<(int QuestId, int Index, int Delta)> _progressCalls = new();

    private QuestHandler Build(byte scriptedKind, int templateId, float entityX = 0f, float entityZ = 0f,
                              (float X, float Y, float Z, float Rotation)? characterPosition = null,
                              bool withDescriptor = true)
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
                        EntityId = 4242UL,
                        ScriptedKind = scriptedKind,
                        TemplateId = templateId,
                        SpawnPointId = -1,
                        X = entityX,
                        Y = 0f,
                        Z = entityZ,
                    },
                },
            });
        }

        _quest.Setup(q => q.GetActiveQuestsAsync()).ReturnsAsync(ActiveQuests());
        _quest.Setup(q => q.UpdateQuestProgressAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
              .ReturnsAsync((int questId, int index, int delta) =>
              {
                  _progressCalls.Add((questId, index, delta));
                  return true;
              });

        var character = new Mock<ICharacterGrain>();
        character.Setup(c => c.GetLastPositionAsync())
                 .ReturnsAsync(characterPosition ?? (0f, 0f, 0f, 0f));

        var cluster = new Mock<IClusterClient>();
        cluster.Setup(c => c.GetGrain<IQuestGrain>(It.IsAny<Guid>(), It.IsAny<string>())).Returns(_quest.Object);
        cluster.Setup(c => c.GetGrain<ICharacterGrain>(It.IsAny<long>(), It.IsAny<string>())).Returns(character.Object);
        cluster.Setup(c => c.GetGrain<IFavorabilityGrain>(It.IsAny<Guid>(), It.IsAny<string>()))
               .Returns(new Mock<IFavorabilityGrain>().Object);

        return new QuestHandler(NullLogger<MessageHandlerBase>.Instance, cluster.Object,
                               new HorizonMessageAdapter(), registry, shardRouter: null);
    }

    /// <summary>两个目标：第 0 条击杀、第 1 条对话——用来验证"只推它那一类"。**故意把对话放在后面**，好让下标错位一眼可见。</summary>
    private static List<QuestData> ActiveQuests(int status = 0) => new()
    {
        new QuestData
        {
            QuestId = QuestId,
            QuestName = "探针任务",
            Status = status,
            Objectives = new List<QuestObjectiveData>
            {
                new() { ObjectiveType = "kill_monster:" + MonsterTemplateId, Description = "击杀" },
                new() { ObjectiveType = "talk_npc:" + NpcTemplateId, Description = "对话" },
            },
        },
    };

    private static HorizonMessagePacket Interact() => new()
    {
        Header = new MessageHeader
        {
            MessageType = MessageType.QuestInteractRequest,
            ServiceType = ServiceType.Quest,
            CharacterId = (long)CharacterId,
        },
        Body = new QuestInteractRequestMessage { CharacterId = CharacterId, EntityId = 4242UL },
    };

    private static async Task<QuestInteractResultMessage> Send(QuestHandler handler)
    {
        var (success, packet) = await handler.RouteHandlerAsync(Interact());
        Assert.True(success);
        return Assert.IsType<QuestInteractResultMessage>(packet.Body);
    }

    [Fact]
    public async Task 与npc对话只推进那条对话目标且下标正确()
    {
        var result = await Send(Build(scriptedKind: 2, templateId: NpcTemplateId));

        Assert.True(result.Ok, "交互被拒：" + result.Reason);
        // 成功时 Reason 留**空串**。客户端 `ShowInteractionToast` 是按 `ok` 布尔分支的（空串不是判据），
        // 所以这条钉的是"服务端别往成功回执里塞占位码"——真塞了，日志与排查会被假错误码带偏。
        Assert.Equal(string.Empty, result.Reason);
        Assert.Equal(1, result.AdvancedCount);
        // 对话目标是 Objectives[1]：调成 [0] 就是"聊一句替玩家打了怪"。
        var call = Assert.Single(_progressCalls);
        Assert.Equal(QuestId, call.QuestId);
        Assert.Equal(1, call.Index);
        Assert.Equal(1, call.Delta);
    }

    [Fact]
    public async Task 已达标可提交的任务不再被同一次对话推进()
    {
        var handler = Build(scriptedKind: 2, templateId: NpcTemplateId);
        _quest.Setup(q => q.GetActiveQuestsAsync()).ReturnsAsync(ActiveQuests(status: 1));

        var result = await Send(handler);

        Assert.True(result.Ok, result.Reason);
        Assert.Equal(0, result.AdvancedCount);
        Assert.Empty(_progressCalls);
    }

    [Fact]
    public async Task 已完成的单条目标不重复推进()
    {
        var handler = Build(scriptedKind: 2, templateId: NpcTemplateId);
        var quests = ActiveQuests();
        quests[0].Objectives[1].IsCompleted = true;
        _quest.Setup(q => q.GetActiveQuestsAsync()).ReturnsAsync(quests);

        var result = await Send(handler);

        Assert.True(result.Ok, result.Reason);
        Assert.Equal(0, result.AdvancedCount);
        Assert.Empty(_progressCalls);
    }

    [Fact]
    public async Task 怪物不是对话对象_一次都不该推进()
    {
        var result = await Send(Build(scriptedKind: 1, templateId: MonsterTemplateId));

        Assert.False(result.Ok);
        Assert.Equal("not_interactable", result.Reason);
        Assert.Equal(0, result.AdvancedCount);
        Assert.Empty(_progressCalls);
    }

    [Fact]
    public async Task 距离超限要拒且零推进()
    {
        // 实体在原点，角色在 5000 之外（半径 800）。位置缓存是 ECS Z-up、描述符是 Flax Y-up，
        // 但无论怎么换轴，这个距离都远超半径——放这里就是要它必然超限。
        var result = await Send(Build(scriptedKind: 2, templateId: NpcTemplateId,
                                      characterPosition: (5000f, 0f, 0f, 0f)));

        Assert.False(result.Ok);
        Assert.Equal("too_far", result.Reason);
        Assert.Empty(_progressCalls);
    }

    [Fact]
    public async Task 注册表里没有的实体id要拒()
    {
        var result = await Send(Build(scriptedKind: 2, templateId: NpcTemplateId, withDescriptor: false));

        Assert.False(result.Ok);
        Assert.Equal("unknown_entity", result.Reason);
        Assert.Empty(_progressCalls);
    }

    [Fact]
    public async Task 角色位置读不到时不降级放行()
    {
        // 位置缓存缺失是运维态问题；当成"免检通过"就是留一个恒开的洞，所以必须拒。
        var handler = Build(scriptedKind: 2, templateId: NpcTemplateId);
        var noPos = new Mock<ICharacterGrain>();
        noPos.Setup(c => c.GetLastPositionAsync()).ReturnsAsync((default((float, float, float, float)?)!));
        var cluster = new Mock<IClusterClient>();
        cluster.Setup(c => c.GetGrain<IQuestGrain>(It.IsAny<Guid>(), It.IsAny<string>())).Returns(_quest.Object);
        cluster.Setup(c => c.GetGrain<ICharacterGrain>(It.IsAny<long>(), It.IsAny<string>())).Returns(noPos.Object);
        var probe = new QuestHandler(NullLogger<MessageHandlerBase>.Instance, cluster.Object,
                                     new HorizonMessageAdapter(),
                                     NewRegistry(), shardRouter: null);

        var result = await Send(probe);

        Assert.False(result.Ok);
        Assert.Equal("no_position", result.Reason);
        Assert.Empty(_progressCalls);
    }

    private static DsEntityRegistry NewRegistry()
    {
        var registry = new DsEntityRegistry();
        registry.Apply(new DsEntityDescriptorBatch
        {
            ShardId = 0L,
            Descriptors = new[]
            {
                new DsEntityDescriptorDto { EntityId = 4242UL, ScriptedKind = 2, TemplateId = NpcTemplateId, SpawnPointId = -1 },
            },
        });
        return registry;
    }
}
