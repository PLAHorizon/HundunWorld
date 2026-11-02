using System;
using System.Threading.Tasks;
using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Game.Sim.Economy;
using Horizon.Game.Sim.Npcs;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 学配方的**执行面**：只能学这位教的、并按教学行收学费、学不成原路退回。
/// <para>
/// §11.51 修的时候只有源码守卫；这里用真 <see cref="DsEntityRegistry"/> + 真装配的 NPC 内容 + 假集群
/// 把 <c>RouteHandlerAsync</c> 跑一遍，钉四件事：不教的配方**一分钱不扣**、教的配方**按 800 金扣一次**、
/// "已经学会了"要**把 800 退回**、没有训练师位的 NPC 直接拒。
/// </para>
/// <para>
/// <see cref="NpcContent"/> 是进程级静态：装合成内容必须与其余 NPC 测试同处一个**禁用并行**的集合，
/// 否则并行跑的"对话记好感"之类用例会读到这份合成快照，红在毫不相干的断言上。
/// </para>
/// </summary>
[Collection(MonsterContentStaticCollection.Name)]
public class CraftLearnGateHandlerTests
{
    private const ulong CharacterId = 8802UL;
    private const ulong EntityId = 70002UL;
    private const int TaughtRecipe = 900001;
    private const int OtherRecipe = 900002;

    private static NpcContentSnapshot Snapshot(uint serviceFlags)
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = 1, Name = "铸剑师", ModelKey = "npc_smith",
            Reaction = (byte)NpcStance.Friendly, ServiceFlags = serviceFlags,
        });
        snapshot.TrainerRows.Add(new NpcTrainerRowDto
        {
            NpcId = 1, SkillId = TaughtRecipe, ServiceKind = 2, CostGold = 800, Seq = 0,
        });

        snapshot.Recipes.Add(new NpcRecipeSnapshotDto
        {
            RecipeId = TaughtRecipe, Name = "锻造·玄铁重铠", ProductItemId = 91002, LearnSourceKind = 1,
            Materials = { new NpcMaterialNeedDto { ItemId = 700011, Count = 2 } },
            QualityGapCaps = { 0, 1, 2, 3 }, QualityChanceBps = { 10000, 6000, 3000, 1000 },
        });
        // 别人教的配方：同样存在内容里，但这位的教学行里没有它。
        snapshot.Recipes.Add(new NpcRecipeSnapshotDto
        {
            RecipeId = OtherRecipe, Name = "别家的图纸", ProductItemId = 91003, LearnSourceKind = 1,
            Materials = { new NpcMaterialNeedDto { ItemId = 700011, Count = 1 } },
            QualityGapCaps = { 0, 1, 2, 3 }, QualityChanceBps = { 10000, 6000, 3000, 1000 },
        });
        return snapshot;
    }

    private static (CraftHandler Handler, Mock<ICurrencyGrain> Wallet, Mock<ICraftingGrain> Craft) Create(
        uint serviceFlags, bool learnSucceeds)
    {
        var (bundle, receipt) = NpcContentLoader.Compile(Snapshot(serviceFlags));
        Assert.Equal(0, receipt.ProblemCount);
        NpcContent.Install(bundle);

        var registry = new DsEntityRegistry();
        registry.Apply(new DsEntityDescriptorBatch
        {
            ShardId = 0L,
            Descriptors = new[]
            {
                new DsEntityDescriptorDto
                {
                    EntityId = EntityId, ScriptedKind = 2, TemplateId = 1, SpawnPointId = -1,
                    X = 0f, Y = 0f, Z = 0f,
                },
            },
        });

        var wallet = new Mock<ICurrencyGrain>();
        wallet.Setup(w => w.SpendAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>()))
              .ReturnsAsync(new WalletMutation { Ok = true });
        wallet.Setup(w => w.AddAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>()))
              .ReturnsAsync(new WalletMutation { Ok = true });

        var craft = new Mock<ICraftingGrain>();
        craft.Setup(c => c.LearnRecipeAsync(It.IsAny<CraftingRecipe>())).ReturnsAsync(learnSucceeds);

        var character = new Mock<ICharacterGrain>();
        character.Setup(c => c.GetLastPositionAsync()).ReturnsAsync((0f, 0f, 0f, 0f));

        var cluster = new Mock<IClusterClient>();
        cluster.Setup(c => c.GetGrain<ICharacterGrain>(It.IsAny<long>(), It.IsAny<string>())).Returns(character.Object);
        cluster.Setup(c => c.GetGrain<ICurrencyGrain>(It.IsAny<long>(), It.IsAny<string>())).Returns(wallet.Object);
        cluster.Setup(c => c.GetGrain<ICraftingGrain>(It.IsAny<Guid>(), It.IsAny<string>())).Returns(craft.Object);

        var handler = new CraftHandler(
            NullLogger<MessageHandlerBase>.Instance, cluster.Object, new HorizonMessageAdapter(), registry, shardRouter: null);
        return (handler, wallet, craft);
    }

    private static HorizonMessagePacket Learn(int recipeId) => new()
    {
        Header = new MessageHeader
        {
            MessageType = MessageType.Crafting,
            ServiceType = ServiceType.Game,
            CharacterId = (long)CharacterId,
        },
        Body = new CraftingRequest
        {
            CharacterId = CharacterId,
            RecipeId = recipeId,
            EntityId = EntityId,
            Action = 1,   // 1 = 学
            Count = 1,
        },
    };

    [Fact]
    public async Task 学这位不教的配方_一分钱不扣且不落库()
    {
        var (handler, wallet, craft) = Create((uint)NpcServiceBits.CraftTrainer, learnSucceeds: true);
        try
        {
            var (_, packet) = await handler.RouteHandlerAsync(Learn(OtherRecipe));

            var response = Assert.IsType<CraftingResponse>(packet.Body);
            Assert.False(response.Success);
            Assert.Contains("不教", response.Message);
            wallet.Verify(w => w.SpendAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>()), Times.Never);
            craft.Verify(c => c.LearnRecipeAsync(It.IsAny<CraftingRecipe>()), Times.Never);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public async Task 学这位教的配方_按教学行扣800金并落库()
    {
        var (handler, wallet, craft) = Create((uint)NpcServiceBits.CraftTrainer, learnSucceeds: true);
        try
        {
            var (_, packet) = await handler.RouteHandlerAsync(Learn(TaughtRecipe));

            var response = Assert.IsType<CraftingResponse>(packet.Body);
            Assert.True(response.Success, response.Message);
            wallet.Verify(w => w.SpendAsync(WalletCurrency.Gold, 800, It.IsAny<string>()), Times.Once);
            craft.Verify(c => c.LearnRecipeAsync(It.IsAny<CraftingRecipe>()), Times.Once);
            wallet.Verify(w => w.AddAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>()), Times.Never);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public async Task 已经学会了_学费要原路退回()
    {
        var (handler, wallet, craft) = Create((uint)NpcServiceBits.CraftTrainer, learnSucceeds: false);
        try
        {
            var (_, packet) = await handler.RouteHandlerAsync(Learn(TaughtRecipe));

            var response = Assert.IsType<CraftingResponse>(packet.Body);
            Assert.False(response.Success);
            wallet.Verify(w => w.SpendAsync(WalletCurrency.Gold, 800, It.IsAny<string>()), Times.Once);
            wallet.Verify(w => w.AddAsync(WalletCurrency.Gold, 800, It.IsAny<string>()), Times.Once);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public async Task 没有训练师位的NPC_直接拒且不扣费()
    {
        var (handler, wallet, craft) = Create(serviceFlags: 0u, learnSucceeds: true);
        try
        {
            var (_, packet) = await handler.RouteHandlerAsync(Learn(TaughtRecipe));

            var response = Assert.IsType<CraftingResponse>(packet.Body);
            Assert.False(response.Success);
            Assert.Contains("不教配方", response.Message);
            wallet.Verify(w => w.SpendAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>()), Times.Never);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }
}