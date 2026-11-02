using System;
using Horizon.Game.Sim.Npcs;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 货架库存记账（<see cref="NpcShopLedgerCore"/>）的可执行断言。
/// <para>
/// 为什么单独测：库存是"玩家能感觉到、但没人会盯"的状态。存的是"已买数 + 锚点"，
/// 剩余量靠时间算——所以边界必须有断言：正好卖完、差一件、跨补货边界后重置、失败时状态不动。
/// 少一条，症状就是"补货后再也没货"或"卖光了还能买"，而两者都不会抛异常。
/// </para>
/// </summary>
public class NpcShopLedgerTests
{
    private readonly ITestOutputHelper _log;

    public NpcShopLedgerTests(ITestOutputHelper log) => _log = log;

    private static NpcVendorRow Row(int stockMax, int restock, int itemId = 750001)
        => new NpcVendorRow(itemId, 0, 100, 20, stockMax, restock);

    [Fact]
    public void UnlimitedStockNeverBlocksAndStillRecords()
    {
        var row = Row(stockMax: 0, restock: 0);
        var entry = ShopStockEntry.Empty;

        for (var i = 0; i < 5; i++)
        {
            var record = NpcShopLedgerCore.TryRecord(in row, in entry, 3, 1000, out var next, out var verdict);
            Assert.True(record.Ok);
            Assert.True(verdict.Unlimited);
            entry = next;
        }

        Assert.Equal(15, entry.Purchased);
        _log.WriteLine(entry.Purchased.ToString());
    }

    [Fact]
    public void LimitedStock_SellsUpToTheLimitThenRefuses()
    {
        var row = Row(stockMax: 5, restock: 60);
        var entry = ShopStockEntry.Empty;

        // 时刻必须落在**补货周期内**（锚点 0、周期 60 ⇒ now<60）：t=100 已经跨了一个周期，
        // 那时货架会如实重置到 5，测的就不是"卖到上限"了。
        var first = NpcShopLedgerCore.TryRecord(in row, in entry, 3, 30, out entry, out var v1);
        Assert.True(first.Ok);
        Assert.Equal(2, v1.Remaining); // 卖出前是 5，卖 3 后剩 2

        var second = NpcShopLedgerCore.TryRecord(in row, in entry, 2, 30, out var afterSecond, out var v2);
        Assert.True(second.Ok);
        Assert.Equal(0, v2.Remaining);
        Assert.Equal(5, afterSecond.Purchased);

        // 差一件也必须拒，且状态与拒绝前逐字段相同（不做半截扣减）。
        var third = NpcShopLedgerCore.TryRecord(in row, in afterSecond, 1, 30, out var afterThird, out var v3);
        Assert.False(third.Ok);
        Assert.Equal(ShopStockReject.NotEnoughStock, third.Reject);
        Assert.Equal(afterSecond.Purchased, afterThird.Purchased);
        Assert.Equal(afterSecond.AnchorSeconds, afterThird.AnchorSeconds);
        Assert.Equal(0, v3.Remaining);
    }

    [Fact]
    public void RestockBoundaryResetsCounterAndMovesAnchor()
    {
        var row = Row(stockMax: 5, restock: 60);
        var entry = ShopStockEntry.Empty;

        // t=100 卖光 5 件，锚点在 0。
        Assert.True(NpcShopLedgerCore.TryRecord(in row, in entry, 5, 100, out entry, out _).Ok);
        Assert.Equal(5, entry.Purchased);

        // t=59 还没跨周期：仍然没货。
        var before = NpcShopLedgerCore.Peek(in row, in entry, 59);
        Assert.Equal(0, before.Remaining);

        // t=60 跨过边界：剩余回到上限，可以再卖。
        var afterRestock = NpcShopLedgerCore.Peek(in row, in entry, 60);
        Assert.Equal(5, afterRestock.Remaining);

        var sale = NpcShopLedgerCore.TryRecord(in row, in entry, 2, 60, out var newEntry, out _);
        Assert.True(sale.Ok);
        Assert.Equal(2, newEntry.Purchased);   // 重置后从 2 起算
        Assert.Equal(60, newEntry.AnchorSeconds); // 锚点前移
    }

    [Fact]
    public void BadRequestsDoNotMutateState()
    {
        var row = Row(stockMax: 5, restock: 60);
        var entry = new ShopStockEntry(2, 10);

        foreach (var count in new[] { 0, -1 })
        {
            var record = NpcShopLedgerCore.TryRecord(in row, in entry, count, 20, out var next, out _);
            Assert.False(record.Ok);
            Assert.Equal(ShopStockReject.BadCount, record.Reject);
            Assert.Equal(entry.Purchased, next.Purchased);
            Assert.Equal(entry.AnchorSeconds, next.AnchorSeconds);
        }

        var badRow = Row(stockMax: 5, restock: 60, itemId: 0);
        var bad = NpcShopLedgerCore.TryRecord(in badRow, in entry, 1, 20, out var n2, out _);
        Assert.Equal(ShopStockReject.BadRow, bad.Reject);
        Assert.Equal(entry.Purchased, n2.Purchased);
    }

    /// <summary>
    /// 属性：任何成功卖出的件数都让 <c>Purchased</c> 单调不减（未跨补货时），
    /// 且任意时刻 <c>Peek.Remaining ∈ [0, StockMax]</c>。
    /// </summary>
    [Fact]
    public void RandomSales_NeverExceedCapAndNeverGoNegative()
    {
        var rng = new Random(20261026);
        var row = Row(stockMax: 7, restock: 30);
        var entry = ShopStockEntry.Empty;
        long now = 0;

        for (var i = 0; i < 2000; i++)
        {
            now += rng.Next(0, 5);
            var count = rng.Next(0, 4);

            var record = NpcShopLedgerCore.TryRecord(in row, in entry, count, now, out var next, out var verdict);
            Assert.InRange(verdict.Remaining, 0, row.StockMax);

            if (record.Ok)
            {
                entry = next;
                Assert.True(entry.Purchased >= count);
                Assert.True(entry.AnchorSeconds <= now);
            }
            else
            {
                Assert.Equal(entry.Purchased, next.Purchased);
            }
        }

        _log.WriteLine($"purchased={entry.Purchased} anchor={entry.AnchorSeconds} now={now}");
    }
}
