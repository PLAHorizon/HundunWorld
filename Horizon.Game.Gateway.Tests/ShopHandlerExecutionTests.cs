using System;
using System.IO;
using System.Threading.Tasks;
using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Message;
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
/// 商人（开窗/买/卖）的**执行面**：价格与库存都由服务端算，失败路径要把钱、库存、物品各自还回去。
/// <para>
/// 已有 <see cref="NpcShopLedgerTests"/> 钉的是库存记账内核；这里用真 <see cref="NpcContent"/> 装配 +
/// 真 <see cref="DsEntityRegistry"/> + 假集群把 <c>RouteHandlerAsync</c> 整条跑一遍，钉的是
/// <b>四个权威之间怎么编排</b>：开窗价来自货架覆盖、买按"钱→库存→背包"顺序且金额是服务端价、
/// 背包满时钱与库存都退回、"卖"先扣物后给钱、非商人位一次都不碰钱包/库存/背包。
/// </para>
/// <para>
/// 与制作/教学的执行面测试同处一个**禁用并行**的集合：<see cref="NpcContent"/> 是进程级静态。
/// </para>
/// </summary>
[Collection(MonsterContentStaticCollection.Name)]
public class ShopHandlerExecutionTests
{
    private const ulong CharacterId = 8803UL;
    private const ulong EntityId = 70003UL;
    private const int VendorNpcId = 1;
    private const int ItemId = 750001;
    private const int Slot = 0;
    private const long BuyPrice = 100;
    private const long SellPrice = 40;
    private const long StartGold = 1000;

    private sealed class Harness
    {
        public ShopHandler Handler = null!;
        public Mock<ICurrencyGrain> Wallet = null!;
        public Mock<INpcShopGrain> Stock = null!;
        public Mock<IInventoryGrain> Inventory = null!;
    }

    private static NpcContentSnapshot Snapshot(uint serviceFlags) => new()
    {
        ContentRevision = 1,
        Templates =
        {
            new NpcTemplateSnapshot
            {
                NpcId = VendorNpcId, Name = "杂货商", ModelKey = "npc_vendor",
                Reaction = (byte)NpcStance.Friendly, ServiceFlags = serviceFlags,
            },
        },
        VendorRows =
        {
            new NpcVendorRowDto
            {
                NpcId = VendorNpcId, Slot = Slot, ItemId = ItemId,
                BuyPriceOverride = BuyPrice, SellPriceOverride = SellPrice,
                StockMax = 5, RestockSeconds = 60,
            },
        },
    };

    private static Harness Create(uint serviceFlags, bool addItemSucceeds = true, bool consumeSucceeds = true)
    {
        var (bundle, receipt) = NpcContentLoader.Compile(Snapshot(serviceFlags));
        Assert.Equal(0, receipt.ProblemCount);
        NpcContent.Install(bundle);
        return Build(EntityId, VendorNpcId, addItemSucceeds, consumeSucceeds);
    }

    /// <summary>内容已由调用方装配（<see cref="Create"/> 或随包文件）时，只搭四个权威的假实现。</summary>
    private static Harness Build(ulong entityId, int templateId, bool addItemSucceeds = true, bool consumeSucceeds = true)
    {
        var registry = new DsEntityRegistry();
        registry.Apply(new DsEntityDescriptorBatch
        {
            ShardId = 0L,
            Descriptors = new[]
            {
                new DsEntityDescriptorDto
                {
                    EntityId = entityId, ScriptedKind = 2, TemplateId = templateId, SpawnPointId = -1,
                    X = 0f, Y = 0f, Z = 0f,
                },
            },
        });

        var wallet = new Mock<ICurrencyGrain>();
        wallet.Setup(w => w.GetBalanceAsync(It.IsAny<int>())).ReturnsAsync(StartGold);
        // 余额按实参变化：货架价不同（随包杂货商 20 铜）时不能拿固定 300 充数。
        wallet.Setup(w => w.SpendAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>()))
              .ReturnsAsync((int _, long amount, string _) => new WalletMutation
              {
                  Ok = true, Amount = amount,
                  BalanceBefore = StartGold, BalanceAfter = StartGold - amount,
              });
        wallet.Setup(w => w.AddAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>()))
              .ReturnsAsync((int _, long amount, string _) => new WalletMutation
              {
                  Ok = true, Amount = amount,
                  BalanceBefore = StartGold, BalanceAfter = StartGold + amount,
              });

        var stock = new Mock<INpcShopGrain>();
        stock.Setup(s => s.GetStockAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
             .ReturnsAsync((int _, int stockMax, int _) => stockMax == 0
                 ? new ShopStockView { Ok = true, Remaining = -1, Unlimited = true, Purchased = 0 }
                 : new ShopStockView { Ok = true, Remaining = stockMax, Unlimited = false, Purchased = 0 });
        stock.Setup(s => s.TakeAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
             .ReturnsAsync(new ShopStockView { Ok = true, Remaining = 2, Purchased = 3 });
        stock.Setup(s => s.ReturnAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
             .ReturnsAsync(new ShopStockView { Ok = true, Remaining = 5, Purchased = 0 });

        var inventory = new Mock<IInventoryGrain>();
        inventory.Setup(i => i.AddItemAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
                 .ReturnsAsync(addItemSucceeds);
        inventory.Setup(i => i.ConsumeByTemplateAsync(It.IsAny<ulong>(), It.IsAny<int>(), It.IsAny<int>()))
                 .ReturnsAsync(consumeSucceeds);

        var character = new Mock<ICharacterGrain>();
        character.Setup(c => c.GetLastPositionAsync()).ReturnsAsync((0f, 0f, 0f, 0f));

        var cluster = new Mock<IClusterClient>();
        cluster.Setup(c => c.GetGrain<ICharacterGrain>(It.IsAny<long>(), It.IsAny<string>())).Returns(character.Object);
        cluster.Setup(c => c.GetGrain<ICurrencyGrain>(It.IsAny<long>(), It.IsAny<string>())).Returns(wallet.Object);
        cluster.Setup(c => c.GetGrain<INpcShopGrain>(It.IsAny<long>(), It.IsAny<string>())).Returns(stock.Object);
        cluster.Setup(c => c.GetGrain<IInventoryGrain>(It.IsAny<Guid>(), It.IsAny<string>())).Returns(inventory.Object);

        var handler = new ShopHandler(
            NullLogger<MessageHandlerBase>.Instance, cluster.Object, new HorizonMessageAdapter(), registry, shardRouter: null);

        return new Harness { Handler = handler, Wallet = wallet, Stock = stock, Inventory = inventory };
    }

    private static HorizonMessagePacket Packet(MessageUnion body) => new()
    {
        Header = new MessageHeader
        {
            MessageType = MessageType.Shop,
            ServiceType = ServiceType.Trade,
            CharacterId = (long)CharacterId,
        },
        Body = body,
    };

    [Fact]
    public async Task 开窗_价与剩余都来自服务端货架行()
    {
        var h = Create((uint)NpcServiceBits.Vendor);
        try
        {
            var (_, packet) = await h.Handler.RouteHandlerAsync(Packet(new ShopOpenRequestMessage
            {
                CharacterId = CharacterId, EntityId = EntityId,
            }));

            var result = Assert.IsType<ShopOpenResultMessage>(packet.Body);
            Assert.True(result.Ok, result.Reason);
            Assert.Equal(VendorNpcId, result.NpcId);
            Assert.Equal(StartGold, result.Gold);

            var item = Assert.Single(result.Items);
            Assert.Equal(Slot, item.Slot);
            Assert.Equal(ItemId, item.ItemId);
            Assert.Equal(BuyPrice, item.BuyPrice);
            Assert.Equal(SellPrice, item.SellPrice);
            Assert.Equal(5, item.Remaining);
            Assert.False(item.Unlimited);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public async Task 买_按服务端价扣钱记库存再进包()
    {
        var h = Create((uint)NpcServiceBits.Vendor);
        try
        {
            var (_, packet) = await h.Handler.RouteHandlerAsync(Packet(new ShopBuyRequestMessage
            {
                CharacterId = CharacterId, EntityId = EntityId, Slot = Slot, Count = 3,
            }));

            var result = Assert.IsType<ShopBuyResultMessage>(packet.Body);
            Assert.True(result.Ok, result.Reason);
            Assert.Equal(BuyPrice * 3, result.TotalCost);
            Assert.Equal(StartGold - BuyPrice * 3, result.GoldAfter);

            h.Wallet.Verify(w => w.SpendAsync(WalletCurrency.Gold, BuyPrice * 3, $"npc_buy:{VendorNpcId}:{ItemId}"), Times.Once);
            h.Stock.Verify(s => s.TakeAsync(ItemId, 3, 5, 60), Times.Once);
            h.Inventory.Verify(i => i.AddItemAsync(ItemId, 3, It.IsAny<int>()), Times.Once);
            // 成功路径不需要补偿
            h.Stock.Verify(s => s.ReturnAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public async Task 背包满_钱与库存都要退回()
    {
        var h = Create((uint)NpcServiceBits.Vendor, addItemSucceeds: false);
        try
        {
            var (_, packet) = await h.Handler.RouteHandlerAsync(Packet(new ShopBuyRequestMessage
            {
                CharacterId = CharacterId, EntityId = EntityId, Slot = Slot, Count = 3,
            }));

            var result = Assert.IsType<ShopBuyResultMessage>(packet.Body);
            Assert.False(result.Ok);
            Assert.Equal("inventory_full", result.Reason);
            Assert.Equal(StartGold, result.GoldAfter);   // 回执里的余额要回到扣款前

            h.Stock.Verify(s => s.ReturnAsync(ItemId, 3, 5, 60), Times.Once);
            h.Wallet.Verify(w => w.AddAsync(WalletCurrency.Gold, BuyPrice * 3,
                It.Is<string>(label => label.StartsWith("npc_buy_addfail:", StringComparison.Ordinal))), Times.Once);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public async Task 卖_先扣物后给钱_单价来自收购覆盖价()
    {
        var h = Create((uint)NpcServiceBits.Vendor);
        try
        {
            var (_, packet) = await h.Handler.RouteHandlerAsync(Packet(new ShopSellRequestMessage
            {
                CharacterId = CharacterId, EntityId = EntityId, ItemId = ItemId, Count = 2,
            }));

            var result = Assert.IsType<ShopSellResultMessage>(packet.Body);
            Assert.True(result.Ok, result.Reason);
            Assert.Equal(SellPrice, result.UnitPrice);
            Assert.Equal(StartGold + SellPrice * 2, result.GoldAfter);

            h.Inventory.Verify(i => i.ConsumeByTemplateAsync(CharacterId, ItemId, 2), Times.Once);
            h.Wallet.Verify(w => w.AddAsync(WalletCurrency.Gold, SellPrice * 2, $"npc_sell:{VendorNpcId}:{ItemId}"), Times.Once);
            // 卖出不改货架
            h.Stock.Verify(s => s.TakeAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    /// <summary>拒绝路径要配正向对照：同一个夹具只翻一个标志位（商人位），其余全同。</summary>
    [Fact]
    public async Task 没有商人位的NPC_开窗被拒且不碰钱货包()
    {
        var plain = Create(serviceFlags: 0u);
        try
        {
            var (_, packet) = await plain.Handler.RouteHandlerAsync(Packet(new ShopOpenRequestMessage
            {
                CharacterId = CharacterId, EntityId = EntityId,
            }));

            var result = Assert.IsType<ShopOpenResultMessage>(packet.Body);
            Assert.False(result.Ok);
            Assert.Equal("not_a_vendor", result.Reason);
            plain.Wallet.Verify(w => w.GetBalanceAsync(It.IsAny<int>()), Times.Never);
            plain.Stock.Verify(s => s.GetStockAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }

        // 正向对照：同一身份、同一位置，只把商人位勾上就必须能开窗
        var vendor = Create((uint)NpcServiceBits.Vendor);
        try
        {
            var (_, packet) = await vendor.Handler.RouteHandlerAsync(Packet(new ShopOpenRequestMessage
            {
                CharacterId = CharacterId, EntityId = EntityId,
            }));

            var result = Assert.IsType<ShopOpenResultMessage>(packet.Body);
            Assert.True(result.Ok, result.Reason);
            Assert.Single(result.Items);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    /// <summary>
    /// 随包货架驱动一次真实会话：杂货商 400001 的四件货必须原样出现在开窗回执里，
    /// 并且买/卖都按货架覆盖价成交。这条把"数据导出了"与"玩家能交易"接在一起，
    /// 防止只改了 db_content.json 而价/库存字段漂移时仍然全绿。
    /// </summary>
    [Fact]
    public async Task 随包货架_杂货商四件商品可开窗买卖()
    {
        var receipt = NpcContentLoader.LoadFile(ShippedContentPath(), out var error);
        Assert.NotNull(receipt);
        Assert.Equal(0, receipt!.ProblemCount);

        const ulong vendorEntity = 70004UL;
        const int generalVendor = 400001;
        try
        {
            var h = Build(vendorEntity, generalVendor);

            var (_, openPacket) = await h.Handler.RouteHandlerAsync(Packet(new ShopOpenRequestMessage
            {
                CharacterId = CharacterId, EntityId = vendorEntity,
            }));
            var opened = Assert.IsType<ShopOpenResultMessage>(openPacket.Body);
            Assert.True(opened.Ok, opened.Reason);
            Assert.Equal(generalVendor, opened.NpcId);
            Assert.Equal(StartGold, opened.Gold);

            var expected = new (int Slot, int ItemId, long Buy, long Sell)[]
            {
                (0, 700002, 150, 30),
                (1, 50001, 20, 8),
                (2, 50002, 24, 10),
                (3, 700031, 60, 20),
            };
            Assert.Equal(expected.Length, opened.Items.Length);
            for (var i = 0; i < expected.Length; i++)
            {
                var row = opened.Items[i];
                Assert.Equal(expected[i].Slot, row.Slot);
                Assert.Equal(expected[i].ItemId, row.ItemId);
                Assert.Equal(expected[i].Buy, row.BuyPrice);
                Assert.Equal(expected[i].Sell, row.SellPrice);
                Assert.True(row.Unlimited);
                Assert.Equal(-1, row.Remaining);
            }

            // 买：20 铜买 1 瓶金创药；扣钱、记库存、进背包各一次，金额来自货架覆盖价。
            var (_, buyPacket) = await h.Handler.RouteHandlerAsync(Packet(new ShopBuyRequestMessage
            {
                CharacterId = CharacterId, EntityId = vendorEntity, Slot = 1, Count = 1,
            }));
            var bought = Assert.IsType<ShopBuyResultMessage>(buyPacket.Body);
            Assert.True(bought.Ok, bought.Reason);
            Assert.Equal(20, bought.TotalCost);
            Assert.Equal(StartGold - 20, bought.GoldAfter);
            h.Wallet.Verify(w => w.SpendAsync(WalletCurrency.Gold, 20, $"npc_buy:{generalVendor}:50001"), Times.Once);
            h.Stock.Verify(s => s.TakeAsync(50001, 1, 0, 0), Times.Once);
            h.Inventory.Verify(i => i.AddItemAsync(50001, 1, It.IsAny<int>()), Times.Once);

            // 卖：同一件按收购覆盖价 8 铜回收；先扣物后给钱，且不动货架。
            var (_, sellPacket) = await h.Handler.RouteHandlerAsync(Packet(new ShopSellRequestMessage
            {
                CharacterId = CharacterId, EntityId = vendorEntity, ItemId = 50001, Count = 1,
            }));
            var sold = Assert.IsType<ShopSellResultMessage>(sellPacket.Body);
            Assert.True(sold.Ok, sold.Reason);
            Assert.Equal(8, sold.UnitPrice);
            Assert.Equal(StartGold + 8, sold.GoldAfter);
            h.Inventory.Verify(i => i.ConsumeByTemplateAsync(CharacterId, 50001, 1), Times.Once);
            h.Wallet.Verify(w => w.AddAsync(WalletCurrency.Gold, 8, $"npc_sell:{generalVendor}:50001"), Times.Once);
            h.Stock.Verify(s => s.TakeAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Once);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    private static string ShippedContentPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "HundunWorld", "Data", "Npcs", "db_content.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("找不到随包导出的 NPC 内容");
    }
}
