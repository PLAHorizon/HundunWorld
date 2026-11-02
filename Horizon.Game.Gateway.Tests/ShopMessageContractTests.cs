using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using MemoryPack;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 商人消息契约的可执行断言。
/// <para>
/// <b>为什么消息要单独测</b>：请求/回执跨网关与客户端两个进程，字段错位不会编译失败——
/// 客户端只会看到"点了没反应"。这里的往返断言把"序列化后还剩什么"钉死；
/// 标签断言保证六条消息在 union 里互不冒充（共用标签会让回执被解析成请求）。
/// </para>
/// </summary>
public class ShopMessageContractTests
{
    [Fact]
    public void BuyRequest_RoundTripsThroughMemoryPack()
    {
        var original = new ShopBuyRequestMessage
        {
            CharacterId = 42,
            EntityId = 0x0000_0001_0000_0002,
            Slot = 3,
            Count = 2,
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var back = MemoryPackSerializer.Deserialize<ShopBuyRequestMessage>(bytes);

        Assert.NotNull(back);
        Assert.Equal(42UL, back!.CharacterId);
        Assert.Equal(0x0000_0001_0000_0002UL, back.EntityId);
        Assert.Equal(3, back.Slot);
        Assert.Equal(2, back.Count);
        Assert.Equal(MessageType.Shop, back.Type);
    }

    [Fact]
    public void OpenResult_RoundTripsItemsArray()
    {
        var original = new ShopOpenResultMessage
        {
            Ok = true,
            NpcId = 400001,
            Gold = 1234,
            Items = new[]
            {
                new ShopItemDto { Slot = 0, ItemId = 91001, BuyPrice = 500, SellPrice = 100, Remaining = 3 },
                new ShopItemDto { Slot = 1, ItemId = 91002, BuyPrice = 900, SellPrice = 180, Remaining = -1, Unlimited = true },
            },
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var back = MemoryPackSerializer.Deserialize<ShopOpenResultMessage>(bytes);

        Assert.NotNull(back);
        Assert.True(back!.Ok);
        Assert.Equal(400001, back.NpcId);
        Assert.Equal(1234, back.Gold);
        Assert.Equal(2, back.Items.Length);
        Assert.Equal(91001, back.Items[0].ItemId);
        Assert.Equal(500, back.Items[0].BuyPrice);
        Assert.True(back.Items[1].Unlimited);
    }

    [Fact]
    public void SellResult_RoundTripsEveryReceiptField()
    {
        var original = new ShopSellResultMessage
        {
            Ok = false,
            Reason = "not_enough_items",
            ItemId = 750001,
            Count = 5,
            UnitPrice = 20,
            GoldAfter = 88,
        };

        var back = MemoryPackSerializer.Deserialize<ShopSellResultMessage>(MemoryPackSerializer.Serialize(original));
        Assert.NotNull(back);
        Assert.False(back!.Ok);
        Assert.Equal("not_enough_items", back.Reason);
        Assert.Equal(750001, back.ItemId);
        Assert.Equal(5, back.Count);
        Assert.Equal(20, back.UnitPrice);
        Assert.Equal(88, back.GoldAfter);
    }

    /// <summary>
    /// 走**真实线上路径**：以 <see cref="MessageUnion"/> 序列化再反序列化，必须回到具体类型。
    /// <para>直接序列化具体类型测不出 union 标签是否生效——标签重复/漏注册时，
    /// 收方拿到的要么是别的类型，要么直接抛。这条才是"客户端点买、网关认得出"的证明。</para>
    /// </summary>
    [Fact]
    public void ShopMessages_RoundTripAsMessageUnion()
    {
        MessageUnion[] originals =
        {
            new ShopOpenRequestMessage { CharacterId = 7, EntityId = 99 },
            new ShopOpenResultMessage { Ok = true, NpcId = 400001, Gold = 10 },
            new ShopBuyRequestMessage { CharacterId = 7, EntityId = 99, Slot = 1, Count = 2 },
            new ShopBuyResultMessage { Ok = true, Slot = 1, Count = 2, TotalCost = 1000, GoldAfter = 500 },
            new ShopSellRequestMessage { CharacterId = 7, EntityId = 99, ItemId = 91001, Count = 1 },
            new ShopSellResultMessage { Ok = true, ItemId = 91001, Count = 1, UnitPrice = 100, GoldAfter = 600 },
        };

        foreach (var original in originals)
        {
            var bytes = MemoryPackSerializer.Serialize(original);
            var back = MemoryPackSerializer.Deserialize<MessageUnion>(bytes);

            Assert.NotNull(back);
            Assert.Equal(original.GetType(), back!.GetType());
        }
    }

    /// <summary>六条消息在 union 里的标签必须互不相同，且都落在 250..255。</summary>
    [Fact]
    public void ShopMessages_UseDistinctUnionTagsInReservedRange()
    {
        var types = new[]
        {
            typeof(ShopOpenRequestMessage), typeof(ShopOpenResultMessage),
            typeof(ShopBuyRequestMessage), typeof(ShopBuyResultMessage),
            typeof(ShopSellRequestMessage), typeof(ShopSellResultMessage),
        };

        var tags = new List<int>(types.Length);
        foreach (var type in types)
        {
            var attribute = type.GetCustomAttribute<NetworkMessageAttribute>();
            Assert.NotNull(attribute);
            tags.Add(attribute!.Tag);
        }

        Assert.Equal(types.Length, tags.Distinct().Count());
        Assert.All(tags, tag => Assert.InRange(tag, 250, 255));
    }

    /// <summary>所有商人消息都必须归属 Trade 服务：进程间路由按 ServiceType 分派。</summary>
    [Fact]
    public void ShopMessages_BelongToTradeService()
    {
        var types = new[]
        {
            typeof(ShopOpenRequestMessage), typeof(ShopOpenResultMessage),
            typeof(ShopBuyRequestMessage), typeof(ShopBuyResultMessage),
            typeof(ShopSellRequestMessage), typeof(ShopSellResultMessage),
        };

        foreach (var type in types)
        {
            var attribute = type.GetCustomAttribute<NetworkMessageAttribute>();
            Assert.NotNull(attribute);
            Assert.Equal(ServiceType.Trade, attribute!.Service);

            // 特性只是"声明"，**路由读的是实例上的基类属性**（网关按 Body 的 Type/ServiceType 匹配 handler）。
            // 2026-11-01 的现场事故：类里写了一个错名的 `ServiceType Service`，基类 ServiceType 恒为 0，
            // 商店请求全部被"无 handler 匹配消息"丢弃——特性断言全绿也照样坏。
            var body = (INetworkMessage)Activator.CreateInstance(type)!;
            Assert.Equal(MessageType.Shop, body.Type);
            Assert.Equal(ServiceType.Trade, body.ServiceType);
        }
    }
}
