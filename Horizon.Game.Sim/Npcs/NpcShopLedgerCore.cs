using System;

namespace Horizon.Game.Sim.Npcs;

/// <summary>
/// 某个 NPC 某个货架格子的库存状态（**持久化的是这两个数，不是"还剩几件"**）。
/// <para>
/// <b>为什么存"已买数量 + 补货锚点"而不是存"剩余数量"</b>：剩余量是时间的函数
/// （<c>NpcCommerceCore.ResolveStock</c> 会按补货周期把它重置到上限）。存剩余量就必须有人定时刷新它，
/// 而"没人刷新的库存"和"真的卖光了"在玩家眼里完全一样。存输入量之后，任何时刻的剩余都能算出来，
/// 且服务停一晚再起来也不会得到一份过期的库存。
/// </para>
/// </summary>
/// <param name="Purchased">自锚点以来卖出的件数（&lt;0 按 0 处理）。</param>
/// <param name="AnchorSeconds">本行上一次补货（或首次上架）的服务器秒。</param>
public readonly struct ShopStockEntry(int purchased, long anchorSeconds)
{
    public readonly int Purchased = purchased < 0 ? 0 : purchased;
    public readonly long AnchorSeconds = anchorSeconds;

    /// <summary>空状态（从未卖出过）。</summary>
    public static ShopStockEntry Empty => new ShopStockEntry(0, 0);
}

/// <summary>一次"记账"的结果。<b>成功与失败都回同一组字段</b>。</summary>
public readonly struct ShopStockRecord(
    bool ok,
    ShopStockEntry next,
    NpcStockVerdict verdict,
    ShopStockReject reject)
{
    public readonly bool Ok = ok;

    /// <summary>记账后的新状态（失败时 == 传入的状态，<b>不做半截扣减</b>）。</summary>
    public readonly ShopStockEntry Next = next;

    /// <summary>本次判断所依据的库存回执（含剩余量与下次补货时刻）。</summary>
    public readonly NpcStockVerdict Verdict = verdict;

    public readonly ShopStockReject Reject = reject;

    public string Describe() =>
        $"stock_ok={(Ok ? 1 : 0)} left={Verdict.Remaining} unlimited={(Verdict.Unlimited ? 1 : 0)} " +
        $"purchased={Next.Purchased} anchor={Next.AnchorSeconds} reject={(byte)Reject}";
}

/// <summary>库存记账的拒绝分档。</summary>
public enum ShopStockReject : byte
{
    /// <summary>成功。</summary>
    None = 0,

    /// <summary>要买的件数不是正数（坏请求）。</summary>
    BadCount = 1,

    /// <summary>剩的不够（世界态）。</summary>
    NotEnoughStock = 2,

    /// <summary>行数据坏（物品 id 非正等）。</summary>
    BadRow = 3,
}

/// <summary>
/// 货架库存的确定性记账（**纯函数**：输入旧状态 + 请求，输出新状态与判断）。
/// <para>
/// 判据不在这里重写：剩余量/补货边界全部调用 <see cref="NpcCommerceCore.ResolveStock"/>，
/// 本类只负责"跨过补货边界时把计数清零、否则累加"。写成两份库存判据，
/// 就会出现"预览说还有 2 件、买的时候说没了"。
/// </para>
/// </summary>
public static class NpcShopLedgerCore
{
    /// <summary>判据版本。</summary>
    public const int Version = 1;

    /// <summary>
    /// 查当前剩余（不改状态）。
    /// </summary>
    public static NpcStockVerdict Peek(in NpcVendorRow row, in ShopStockEntry entry, long nowSeconds)
    {
        if (row.ItemId <= 0) return new NpcStockVerdict(false, 0, -1, NpcStockReject.BadItem);

        // 不限库存的行不看计数（但计数仍会被记下来供审计，见 TryRecord）。
        return NpcCommerceCore.ResolveStock(in row, entry.Purchased, entry.AnchorSeconds, nowSeconds);
    }

    /// <summary>
    /// 记一笔卖出。
    /// </summary>
    /// <param name="row">货架行。</param>
    /// <param name="entry">旧状态。</param>
    /// <param name="count">要卖几件。</param>
    /// <param name="nowSeconds">服务器秒。</param>
    /// <param name="next">新状态（失败时与 <paramref name="entry"/> 相同）。</param>
    /// <param name="verdict">判断依据（含剩余量与下次补货）。</param>
    public static ShopStockRecord TryRecord(
        in NpcVendorRow row,
        in ShopStockEntry entry,
        int count,
        long nowSeconds,
        out ShopStockEntry next,
        out NpcStockVerdict verdict)
    {
        next = entry;
        verdict = new NpcStockVerdict(false, 0, -1, NpcStockReject.BadItem);

        if (row.ItemId <= 0) return new ShopStockRecord(false, entry, verdict, ShopStockReject.BadRow);
        if (count <= 0)
        {
            verdict = Peek(in row, in entry, nowSeconds);
            return new ShopStockRecord(false, entry, verdict, ShopStockReject.BadCount);
        }

        verdict = Peek(in row, in entry, nowSeconds);

        // 有上限且剩的不够：一件都不卖（与钱包"余额不足一分不扣"同一条纪律）。
        if (!verdict.Unlimited && verdict.Remaining < count)
            return new ShopStockRecord(false, entry, verdict, ShopStockReject.NotEnoughStock);

        // 跨过补货边界 → 计数从本次重新起算（锚点前移到当前秒）。
        var restocked = !verdict.Unlimited
                        && row.RestockSeconds > 0
                        && verdict.Remaining == row.StockMax
                        && entry.Purchased > 0
                        && nowSeconds >= entry.AnchorSeconds + row.RestockSeconds;

        var basePurchased = restocked ? 0 : entry.Purchased;
        var anchor = restocked ? nowSeconds : entry.AnchorSeconds;

        next = new ShopStockEntry(basePurchased + count, anchor);

        // 成功时把 verdict 换成**卖出之后**的货架状态：调用方（商店回执/UI）要显示"还剩几件"，
        // 让它自己再 Peek 一次等于把同一条判据重算一遍，还能算错。
        // 失败分支保持"卖出前"的原值——失败 = 什么都没发生。
        verdict = Peek(in row, in next, nowSeconds);
        return new ShopStockRecord(true, next, verdict, ShopStockReject.None);
    }
}
