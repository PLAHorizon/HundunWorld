using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message;
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
/// 制作（<c>Action=0</c>）的**执行面**：设施身份对号、扣料/收工费/发成品的顺序，以及四条失败路径的补偿。
/// <para>
/// 与 §11.51 的"学配方"同处理器不同分支：学只验扣费与退款，做东西要验
/// ①站错设施**一件料都不扣**；②成功后料、工费、成品三样各恰一次；
/// ③材料不足把**已扣的那几样**退回；④工费不够退料；⑤成品放不下**料与工费都退**。
/// 这五条里任何一条漏了，症状都是"制作失败但东西/钱少了"，而面板只会显示一句失败。
/// </para>
/// </summary>
[Collection(MonsterContentStaticCollection.Name)]
public class CraftActionHandlerTests
{
    private const ulong CharacterId = 8805UL;
    private const ulong SmithEntityId = 70007UL;
    private const ulong StoveEntityId = 70008UL;
    private const int SmithNpcId = 1;
    private const int StoveNpcId = 2;
    private const int StationRecipe = 900101;
    private const int FreeRecipe = 900102;
    private const int ProductA = 91002;
    private const int ProductB = 91003;
    private const int MaterialX = 700011;
    private const int MaterialY = 700012;
    private const long CraftFee = 50;

    private sealed class Harness
    {
        public CraftHandler Handler = null!;
        public Mock<ICurrencyGrain> Wallet = null!;
        public Mock<IInventoryGrain> Inventory = null!;
        public Mock<ICraftingGrain> Craft = null!;
    }

    private static NpcContentSnapshot Snapshot() => new()
    {
        ContentRevision = 1,
        Templates =
        {
            new NpcTemplateSnapshot
            {
                NpcId = SmithNpcId, Name = "铸剑师", ModelKey = "npc_smith",
                Reaction = (byte)NpcStance.Friendly,
                ServiceFlags = (uint)(NpcServiceBits.CraftTrainer | NpcServiceBits.CraftStation),
            },
            new NpcTemplateSnapshot
            {
                NpcId = StoveNpcId, Name = "丹炉", ModelKey = "npc_stove",
                Reaction = (byte)NpcStance.Friendly,
                ServiceFlags = (uint)NpcServiceBits.CraftStation,
            },
        },
        Recipes =
        {
            new NpcRecipeSnapshotDto
            {
                RecipeId = StationRecipe, Name = "锻造·玄铁重铠", ProductItemId = ProductA,
                CostGold = CraftFee, StationNpcId = SmithNpcId,
                Materials =
                {
                    new NpcMaterialNeedDto { ItemId = MaterialX, Count = 2 },
                    new NpcMaterialNeedDto { ItemId = MaterialY, Count = 1 },
                },
                QualityGapCaps = { 0, 1, 2, 3 }, QualityChanceBps = { 10000, 6000, 3000, 1000 },
            },
            // 未绑定设施的配方：按内核口径"≤0 随处可做"，但仍要有交互对象。
            new NpcRecipeSnapshotDto
            {
                RecipeId = FreeRecipe, Name = "通用丹药", ProductItemId = ProductB,
                CostGold = 0, StationNpcId = 0,
                Materials = { new NpcMaterialNeedDto { ItemId = MaterialX, Count = 1 } },
                QualityGapCaps = { 0, 1, 2, 3 }, QualityChanceBps = { 10000, 6000, 3000, 1000 },
            },
        },
    };

    private static Harness Create(
        bool craftSucceeds = true, bool spendSucceeds = true, bool productFits = true, int failConsumeItem = 0)
    {
        var (bundle, receipt) = NpcContentLoader.Compile(Snapshot());
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
                    EntityId = SmithEntityId, ScriptedKind = 2, TemplateId = SmithNpcId, SpawnPointId = -1,
                    X = 0f, Y = 0f, Z = 0f,
                },
                new DsEntityDescriptorDto
                {
                    EntityId = StoveEntityId, ScriptedKind = 2, TemplateId = StoveNpcId, SpawnPointId = -1,
                    X = 0f, Y = 0f, Z = 0f,
                },
            },
        });

        var wallet = new Mock<ICurrencyGrain>();
        wallet.Setup(w => w.SpendAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>()))
              .ReturnsAsync(spendSucceeds
                  ? new WalletMutation { Ok = true, BalanceBefore = 1000, BalanceAfter = 1000 - CraftFee }
                  : new WalletMutation { Ok = false, Reject = (int)WalletReject.InsufficientFunds });
        wallet.Setup(w => w.AddAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>()))
              .ReturnsAsync(new WalletMutation { Ok = true });

        var inventory = new Mock<IInventoryGrain>();
        inventory.Setup(i => i.GetInventoryAsync()).ReturnsAsync(new InventoryInfo
        {
            Items = new List<ItemInfo>
            {
                new() { ItemId = 1, TemplateId = MaterialX, Count = 5 },
                new() { ItemId = 2, TemplateId = MaterialY, Count = 3 },
            },
        });
        inventory.Setup(i => i.ConsumeByTemplateAsync(It.IsAny<ulong>(), It.IsAny<int>(), It.IsAny<int>()))
                 .ReturnsAsync((ulong _, int templateId, int _) => templateId != failConsumeItem);
        inventory.Setup(i => i.AddItemAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
                 .ReturnsAsync((int templateId, int _, int _) => templateId != ProductA || productFits);

        var craft = new Mock<ICraftingGrain>();
        craft.Setup(c => c.CraftItemAsync(It.IsAny<int>(), It.IsAny<Dictionary<long, int>>()))
             .ReturnsAsync((int recipeId, Dictionary<long, int> _) => new CraftingResult
             {
                 Success = craftSucceeds, RecipeId = recipeId,
                 Message = craftSucceeds ? string.Empty : "这一式还没练熟",
             });

        var character = new Mock<ICharacterGrain>();
        character.Setup(c => c.GetLastPositionAsync()).ReturnsAsync((0f, 0f, 0f, 0f));

        var cluster = new Mock<IClusterClient>();
        cluster.Setup(c => c.GetGrain<ICharacterGrain>(It.IsAny<long>(), It.IsAny<string>())).Returns(character.Object);
        cluster.Setup(c => c.GetGrain<ICurrencyGrain>(It.IsAny<long>(), It.IsAny<string>())).Returns(wallet.Object);
        cluster.Setup(c => c.GetGrain<IInventoryGrain>(It.IsAny<Guid>(), It.IsAny<string>())).Returns(inventory.Object);
        cluster.Setup(c => c.GetGrain<ICraftingGrain>(It.IsAny<Guid>(), It.IsAny<string>())).Returns(craft.Object);

        var handler = new CraftHandler(
            NullLogger<MessageHandlerBase>.Instance, cluster.Object, new HorizonMessageAdapter(), registry, shardRouter: null);

        return new Harness { Handler = handler, Wallet = wallet, Inventory = inventory, Craft = craft };
    }

    private static HorizonMessagePacket Craft(int recipeId, ulong entityId) => new()
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
            EntityId = entityId,
            Action = 0,   // 0 = 制作（1=学，2=清单）
            Count = 1,
        },
    };

    [Fact]
    public async Task 站在别的设施前_拒绝且一件料都不扣()
    {
        var h = Create();
        try
        {
            // 配方要求铸剑师的设施，玩家却在丹炉前
            var (_, packet) = await h.Handler.RouteHandlerAsync(Craft(StationRecipe, StoveEntityId));

            var response = Assert.IsType<CraftingResponse>(packet.Body);
            Assert.False(response.Success);
            Assert.Equal("这里不是做这件东西的设施", response.Message);

            h.Craft.Verify(c => c.CraftItemAsync(It.IsAny<int>(), It.IsAny<Dictionary<long, int>>()), Times.Never);
            h.Inventory.Verify(i => i.ConsumeByTemplateAsync(It.IsAny<ulong>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
            h.Wallet.Verify(w => w.SpendAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>()), Times.Never);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public async Task 站在正确设施前_按配方扣料收工费并发成品()
    {
        var h = Create();
        try
        {
            var (_, packet) = await h.Handler.RouteHandlerAsync(Craft(StationRecipe, SmithEntityId));

            var response = Assert.IsType<CraftingResponse>(packet.Body);
            Assert.True(response.Success, response.Message);
            Assert.Equal(CraftFee, response.ConsumedGold);

            // 持有量快照是**服务端从背包取的**（700011=5、700012=3），不是客户端上报
            h.Craft.Verify(c => c.CraftItemAsync(StationRecipe, It.Is<Dictionary<long, int>>(held =>
                held[MaterialX] == 5 && held[MaterialY] == 3)), Times.Once);
            h.Inventory.Verify(i => i.ConsumeByTemplateAsync(CharacterId, MaterialX, 2), Times.Once);
            h.Inventory.Verify(i => i.ConsumeByTemplateAsync(CharacterId, MaterialY, 1), Times.Once);
            h.Wallet.Verify(w => w.SpendAsync(WalletCurrency.Gold, CraftFee, $"npc_craft:{StationRecipe}"), Times.Once);
            h.Inventory.Verify(i => i.AddItemAsync(ProductA, 1, It.IsAny<int>()), Times.Once);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public async Task 材料不足_已扣的那几样要退回且不收工费()
    {
        // 第二样材料扣不动
        var h = Create(failConsumeItem: MaterialY);
        try
        {
            var (_, packet) = await h.Handler.RouteHandlerAsync(Craft(StationRecipe, SmithEntityId));

            var response = Assert.IsType<CraftingResponse>(packet.Body);
            Assert.False(response.Success);
            Assert.Equal("材料不足", response.Message);

            h.Inventory.Verify(i => i.AddItemAsync(MaterialX, 2, It.IsAny<int>()), Times.Once);
            h.Inventory.Verify(i => i.AddItemAsync(MaterialY, It.IsAny<int>(), It.IsAny<int>()), Times.Never);
            h.Wallet.Verify(w => w.SpendAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>()), Times.Never);
            h.Inventory.Verify(i => i.AddItemAsync(ProductA, 1, It.IsAny<int>()), Times.Never);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public async Task 工费不够_退料且不发货()
    {
        var h = Create(spendSucceeds: false);
        try
        {
            var (_, packet) = await h.Handler.RouteHandlerAsync(Craft(StationRecipe, SmithEntityId));

            var response = Assert.IsType<CraftingResponse>(packet.Body);
            Assert.False(response.Success);
            Assert.Equal("金币不足，付不起工费", response.Message);

            h.Inventory.Verify(i => i.AddItemAsync(MaterialX, 2, It.IsAny<int>()), Times.Once);
            h.Inventory.Verify(i => i.AddItemAsync(MaterialY, 1, It.IsAny<int>()), Times.Once);
            h.Inventory.Verify(i => i.AddItemAsync(ProductA, 1, It.IsAny<int>()), Times.Never);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public async Task 成品放不下_料与工费都退回()
    {
        var h = Create(productFits: false);
        try
        {
            var (_, packet) = await h.Handler.RouteHandlerAsync(Craft(StationRecipe, SmithEntityId));

            var response = Assert.IsType<CraftingResponse>(packet.Body);
            Assert.False(response.Success);
            Assert.Equal("背包放不下成品", response.Message);

            h.Inventory.Verify(i => i.AddItemAsync(MaterialX, 2, It.IsAny<int>()), Times.Once);
            h.Inventory.Verify(i => i.AddItemAsync(MaterialY, 1, It.IsAny<int>()), Times.Once);
            h.Wallet.Verify(w => w.AddAsync(WalletCurrency.Gold, CraftFee, $"npc_craft_refund:{StationRecipe}"), Times.Once);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public async Task 合成判定失败_不扣料不收钱()
    {
        var h = Create(craftSucceeds: false);
        try
        {
            var (_, packet) = await h.Handler.RouteHandlerAsync(Craft(StationRecipe, SmithEntityId));

            var response = Assert.IsType<CraftingResponse>(packet.Body);
            Assert.False(response.Success);
            Assert.Equal("这一式还没练熟", response.Message);

            h.Inventory.Verify(i => i.ConsumeByTemplateAsync(It.IsAny<ulong>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
            h.Wallet.Verify(w => w.SpendAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>()), Times.Never);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public async Task 未绑设施的配方_在任何NPC面前都能做()
    {
        var h = Create();
        try
        {
            // 丹炉没有"教配方"位，但配方不要求设施 ⇒ 只要交互对象成立即可
            var (_, packet) = await h.Handler.RouteHandlerAsync(Craft(FreeRecipe, StoveEntityId));

            var response = Assert.IsType<CraftingResponse>(packet.Body);
            Assert.True(response.Success, response.Message);
            h.Craft.Verify(c => c.CraftItemAsync(FreeRecipe, It.IsAny<Dictionary<long, int>>()), Times.Once);
            h.Inventory.Verify(i => i.AddItemAsync(ProductB, 1, It.IsAny<int>()), Times.Once);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }
}
