using System;

namespace Horizon.Game.Sim.Npcs;

/// <summary>商人货架的一行（<c>NpcVendorEntry</c> 的内核形态）。字段名与表列一一对应，不在此处换算单位。</summary>
/// <param name="ItemId">物品模板 id。<b>0 不是合法物品</b>：与指示器的 <c>QuestId</c> 同一条纪律，
/// 非正数一律进拒绝分支并回同一套对账字段，不静默当成"第一件货"。</param>
/// <param name="Slot">窗口里的位置（0 起，左上到右下）。规格 §5.1：<b>顺序是数据</b>，不是物品 id 序——
/// 玩家记得住"补货的那瓶药在第二格"，所以这个值不能由内核重新排。</param>
/// <param name="BuyPriceOverride">买入价覆盖（玩家付的钱）。≤0 = 不覆盖，取模板价。</param>
/// <param name="SellPriceOverride">收购价覆盖（玩家卖得的钱）。≤0 = 不覆盖，取模板价。</param>
/// <param name="StockMax">库存上限。<b>0 = 不限库存</b>（照 WoW 的 <c>maxcount=0</c> 口径），
/// 正数才是上限——这条语义必须由内核独占解释，不能让 UI 各自猜。</param>
/// <param name="RestockSeconds">补货周期秒数。只在 <paramref name="StockMax"/> 为正数时有意义
/// （与 WoW 文档一致：<c>maxcount=0</c> 时 <c>incrtime</c> 也应为 0）。</param>
public readonly struct NpcVendorRow(
    int itemId,
    int slot,
    long buyPriceOverride,
    long sellPriceOverride,
    int stockMax,
    int restockSeconds)
{
    public readonly int ItemId = itemId;
    public readonly int Slot = slot;
    public readonly long BuyPriceOverride = buyPriceOverride;
    public readonly long SellPriceOverride = sellPriceOverride;
    public readonly int StockMax = stockMax;
    public readonly int RestockSeconds = restockSeconds;

    /// <summary>这一行是否声明了"覆盖模板价"（<see cref="NpcCommerceCore"/> 的优先级判据用）。</summary>
    public readonly bool HasBuyOverride => BuyPriceOverride > 0;

    /// <summary>同上，收购侧。</summary>
    public readonly bool HasSellOverride => SellPriceOverride > 0;
}

/// <summary>物品模板上的价格（从 <c>ItemTemplateEntity</c> 的 <c>buy_price</c>/<c>sell_price</c> 取来的两份整数）。</summary>
/// <remarks>
/// <b>为什么单独拿这一个结构出来</b>：这两列今天在全仓<b>零消费者</b>（规格 §5.3 已核实：
/// <c>grep SellPrice|BuyPrice</c> 在 Game.Core / Orleans.Grains / Game.Sim 全空）。
/// 本内核是它们的第一也是唯一读者——所以"0 到底算免费还是算不可成交"这件事必须由这里下一次决定，
/// 并且把决定写进拒绝理由码，而不是留给每个调用点各猜一次。
/// </remarks>
public readonly struct NpcItemPriceRow(long buyPrice, long sellPrice)
{
    public readonly long BuyPrice = buyPrice;
    public readonly long SellPrice = sellPrice;
}

/// <summary>价格求值的拒绝理由。<b>每个不可成交的出口都回这一套字段</b>（同族拒绝分支纪律）。</summary>
public enum NpcPriceReject : byte
{
    /// <summary>可以成交。</summary>
    None = 0,

    /// <summary>买不到：货架行与模板都没给出正买入价。</summary>
    NoBuyPrice = 1,

    /// <summary>卖不掉：这一行没声明收购价、模板的 <c>sell_price</c> 也不是正数（= 这类物品商人不收）。</summary>
    NoSellPrice = 2,

    /// <summary>行本身坏（物品 id 非正）。</summary>
    BadItem = 3,
}

/// <summary>一次价格求值的回执。</summary>
public readonly struct NpcPriceVerdict(long amount, NpcPriceReject reason, bool usedOverride, int priceSource)
{
    /// <summary>成交金额（买=玩家付出，卖=玩家得到）。拒绝时为 0，<b>不给未定义值</b>。</summary>
    public readonly long Amount = amount;

    public readonly NpcPriceReject Reason = reason;

    /// <summary>这一价是货架覆盖来的还是模板来的（诊断与编辑器提示都要能分辨）。</summary>
    public readonly bool UsedOverride = usedOverride;

    /// <summary>来源档：1=商人覆盖、2=物品模板。<b>拒绝时为 0</b>。</summary>
    public readonly int PriceSource = priceSource;

    public readonly bool IsOk => Reason == NpcPriceReject.None;

    public readonly string Describe() =>
        $"price={Amount} reason={(byte)Reason} src={PriceSource} ok={(IsOk ? 1 : 0)}";
}

/// <summary>库存求值的拒绝理由。</summary>
public enum NpcStockReject : byte
{
    /// <summary>有货。</summary>
    None = 0,

    /// <summary>卖光了，等补货。</summary>
    OutOfStock = 1,

    /// <summary>行不可用（物品 id 非正）。</summary>
    BadItem = 2,

    /// <summary>补货周期配成 0 但有上限——<b>这种行永远不会补货</b>，是数据缺陷不是玩法。
    /// 单列一档是为了让它能在 <c>validate</c> 里被数出来，而不是静默表现为"卖完就没了"。</summary>
    RestockNotConfigured = 3,
}

/// <summary>一次库存求值的回执。</summary>
public readonly struct NpcStockVerdict(bool unlimited, int remaining, long nextRestockSeconds, NpcStockReject reason)
{
    /// <summary>不限库存（<c>StockMax == 0</c>）。为真时 <see cref="Remaining"/> 无意义，固定回 −1 而不是 int.Max，
    /// 免得调用方拿它去做减法算溢出。</summary>
    public readonly bool Unlimited = unlimited;

    public readonly int Remaining = remaining;

    /// <summary>下一次补货的服务器秒（<see cref="NpcCommerceCore"/> 的时钟口径）。没有周期时回 −1。</summary>
    public readonly long NextRestockSeconds = nextRestockSeconds;

    public readonly NpcStockReject Reason = reason;

    public readonly bool IsAvailable => Unlimited || Remaining > 0;

    public readonly string Describe() =>
        $"stock_unlimited={(Unlimited ? 1 : 0)} stock_left={Remaining} next_restock={NextRestockSeconds} reason={(byte)Reason}";
}

/// <summary>
/// 商人定价与库存的确定性内核（规格 §5）。
/// <para>
/// <b>三条口径一次定死，避免每个调用点各猜</b>：
/// ① 价格优先级 = 商人覆盖 &gt; 物品模板（设计文档 D8；这样那两列第一次有人读，又保住"这家卖得贵"的表达力）。
/// ② 金额为 0 = <b>不可成交</b>，不是免费（免费买卖会被拿去刷钱刷物；要免费送东西应该走任务奖励通路）。
/// ③ <c>StockMax == 0</c> = 不限库存（照 WoW 的 <c>maxcount</c> 文档口径）。
/// </para>
/// <para>
/// <b>时钟只接服务器秒</b>：本类不读 <c>DateTime.Now</c>。补货是可重放的世界状态，
/// 走 <c>Horizon.DsReplayHarness</c> 那条线时必须能由 (锚点秒, 周期, 现在秒) 完全决定
/// （记忆：等价测试的参照要另写暴力版——单测里那份逐周期推进的参照就是干这个的）。
/// </para>
/// </summary>
public static class NpcCommerceCore
{
    /// <summary>判据版本。</summary>
    public const int Version = 1;

    /// <summary>来源档：商人覆盖。</summary>
    public const int SourceVendorOverride = 1;

    /// <summary>来源档：物品模板。</summary>
    public const int SourceItemTemplate = 2;

    /// <summary>
    /// 买入价（玩家付钱买货架上的货）。
    /// </summary>
    /// <param name="row">货架行。</param>
    /// <param name="item">该物品的模板价。找不到物品时传 <c>default</c>（两价皆 0）——
    /// 于是这里回 <see cref="NpcPriceReject.NoBuyPrice"/>；<b>调用方要另报"物品不存在"</b>，
    /// 本类不猜物品在不在，因为它没有表。</param>
    public static NpcPriceVerdict ResolveBuyPrice(in NpcVendorRow row, in NpcItemPriceRow item)
    {
        if (row.ItemId <= 0) return new NpcPriceVerdict(0, NpcPriceReject.BadItem, false, 0);

        if (row.HasBuyOverride) return new NpcPriceVerdict(row.BuyPriceOverride, NpcPriceReject.None, true, SourceVendorOverride);
        if (item.BuyPrice > 0) return new NpcPriceVerdict(item.BuyPrice, NpcPriceReject.None, false, SourceItemTemplate);

        return new NpcPriceVerdict(0, NpcPriceReject.NoBuyPrice, false, 0);
    }

    /// <summary>
    /// 收购价（玩家把东西卖给商人）。
    /// <para>这一侧同样是"覆盖 &gt; 模板 <c>sell_price</c>"。<b>商人的收购白名单因此就是"这一行或模板给了正收购价"</b>：
    /// WoW 观测的"这件不能卖到这里"（规格 §5.2）在我们这里不需要再加一张白名单表——
    /// <c>sell_price</c> 为 0 就是这个意思。若这个 0 语义与实际物品表冲突，见设计文档 §14 待读 2，需先普查再改判据。</para>
    /// </summary>
    public static NpcPriceVerdict ResolveSellPrice(in NpcVendorRow row, in NpcItemPriceRow item)
    {
        if (row.ItemId <= 0) return new NpcPriceVerdict(0, NpcPriceReject.BadItem, false, 0);

        if (row.HasSellOverride) return new NpcPriceVerdict(row.SellPriceOverride, NpcPriceReject.None, true, SourceVendorOverride);
        if (item.SellPrice > 0) return new NpcPriceVerdict(item.SellPrice, NpcPriceReject.None, false, SourceItemTemplate);

        return new NpcPriceVerdict(0, NpcPriceReject.NoSellPrice, false, 0);
    }

    /// <summary>
    /// 库存可见性 + 下一次补货时刻。
    /// </summary>
    /// <param name="row">货架行（只取 <c>StockMax</c> 与 <c>RestockSeconds</c>）。</param>
    /// <param name="purchasedSinceAnchor">从锚点秒起已被买走的件数。<b>由调用方（会话账本）提供</b>，
    /// 本类不持有状态——保持"世界状态存在别处、判据在这里"的分工，与怪物内核同一套纪律。</param>
    /// <param name="anchorSeconds">本行上一次补货（或首次上架）的服务器秒。</param>
    /// <param name="nowSeconds">当前服务器秒。<b>早于锚点的时钟回拨按"还没补货"处理</b>（不抛、不重置成满仓），
    /// 因为跨端秒数来自 tick 累加，回拨只可能来自装配错误——宁可少补一次。</param>
    public static NpcStockVerdict ResolveStock(in NpcVendorRow row, int purchasedSinceAnchor, long anchorSeconds, long nowSeconds)
    {
        if (row.ItemId <= 0) return new NpcStockVerdict(false, 0, -1, NpcStockReject.BadItem);

        // 0 = 不限库存（WoW maxcount 口径）。上限语义只有这一处解释。
        if (row.StockMax == 0) return new NpcStockVerdict(true, -1, -1, NpcStockReject.None);

        if (row.StockMax < 0) return new NpcStockVerdict(false, 0, -1, NpcStockReject.BadItem);

        var elapsed = nowSeconds - anchorSeconds;
        if (row.RestockSeconds <= 0)
        {
            // 有上限却没有周期 = 这批货卖完就永远没了。按"当前有没有剩"如实回，
            // 同时把这一档理由码单独列出，好在 validate 里被数出来（见 NpcStockReject 注释）。
            var left = Math.Max(0, row.StockMax - purchasedSinceAnchor);
            return new NpcStockVerdict(false, left, -1, left > 0 ? NpcStockReject.None : NpcStockReject.RestockNotConfigured);
        }

        if (elapsed < 0) elapsed = 0;

        // 过了几个完整周期就重置几次：库存是"重置到上限"而不是"每周期加一件"，
        // 所以任意周期数下的结果都一样——这里只需要知道"是否已经跨过一个周期边界"。
        var rested = elapsed >= row.RestockSeconds;
        var remaining = rested ? row.StockMax : Math.Max(0, row.StockMax - purchasedSinceAnchor);

        var next = anchorSeconds + (((elapsed / row.RestockSeconds) + 1) * (long)row.RestockSeconds);

        return new NpcStockVerdict(false, remaining, next, remaining > 0 ? NpcStockReject.None : NpcStockReject.OutOfStock);
    }

    /// <summary>
    /// 一次购买是否放行。<b>把"价 / 库存 / 钱"三件事一起判，但拒绝理由分档</b>：
    /// 钱不够是玩家态、没货是世界态、没定价是数据缺陷——三档必须能被分别数出来，
    /// 否则"商人在某些服上买不了"这种报告就只能靠猜（记忆 [同族拒绝分支要回同一套对账字段]）。
    /// </summary>
    /// <param name="row">货架行。</param>
    /// <param name="item">模板价。</param>
    /// <param name="stock">已求好的库存回执（调用方算，避免这里重复取时钟）。</param>
    /// <param name="buyerGold">买家当前金币。</param>
    /// <param name="quantity">想买几件（&lt;1 直接按坏请求拒）。</param>
    public static NpcBuyVerdict TryBuy(in NpcVendorRow row, in NpcItemPriceRow item, in NpcStockVerdict stock, long buyerGold, int quantity)
    {
        var price = ResolveBuyPrice(row, item);
        if (!price.IsOk) return new NpcBuyVerdict(0, NpcBuyReject.PriceUnavailable, price.Reason, stock.Reason, 0);

        if (quantity < 1) return new NpcBuyVerdict(0, NpcBuyReject.BadQuantity, NpcPriceReject.None, stock.Reason, 0);

        if (!stock.IsAvailable) return new NpcBuyVerdict(0, NpcBuyReject.OutOfStock, NpcPriceReject.None, stock.Reason, 0);

        // 件数上限受库存约束（不限库存时不夹）：先夹数量再算钱，否则"钱不够"会盖掉"其实只剩 2 件"。
        var affordableCount = quantity;
        if (!stock.Unlimited && stock.Remaining < affordableCount) affordableCount = stock.Remaining;

        long total;
        try { total = checked(price.Amount * affordableCount); }
        catch (OverflowException) { return new NpcBuyVerdict(0, NpcBuyReject.PriceUnavailable, NpcPriceReject.None, stock.Reason, 0); }

        if (buyerGold < total) return new NpcBuyVerdict(total, NpcBuyReject.NotEnoughGold, NpcPriceReject.None, stock.Reason, affordableCount);

        return new NpcBuyVerdict(total, NpcBuyReject.None, NpcPriceReject.None, stock.Reason, affordableCount);
    }

    /// <summary>把内核档位翻译成一行 ASCII 诊断（CLI <c>preview-vendor</c> 用；键名全 ASCII 的既有理由）。</summary>
    public static string Describe(in NpcVendorRow row, in NpcItemPriceRow item)
    {
        var buy = ResolveBuyPrice(row, item);
        var sell = ResolveSellPrice(row, item);
        return $"slot={row.Slot} item={row.ItemId} buy[{buy.Describe()}] sell[{sell.Describe()}] " +
               $"stock_max={row.StockMax} restock={row.RestockSeconds}";
    }
}

/// <summary>购买拒绝的分档。</summary>
public enum NpcBuyReject : byte
{
    /// <summary>放行。</summary>
    None = 0,

    /// <summary>这一行没有可成交的买入价（数据缺陷档）。</summary>
    PriceUnavailable = 1,

    /// <summary>没货（世界状态档）。</summary>
    OutOfStock = 2,

    /// <summary>钱不够（玩家态档）。</summary>
    NotEnoughGold = 3,

    /// <summary>件数非法（坏请求档）。</summary>
    BadQuantity = 4,
}

/// <summary>一次购买请求的完整回执。<b>四条出口都带同一组字段</b>：总价、拒绝档、价格档、库存档、实际件数。</summary>
public readonly struct NpcBuyVerdict(
    long totalCost,
    NpcBuyReject reject,
    NpcPriceReject priceReason,
    NpcStockReject stockReason,
    int grantedCount)
{
    public readonly long TotalCost = totalCost;
    public readonly NpcBuyReject Reject = reject;
    public readonly NpcPriceReject PriceReason = priceReason;
    public readonly NpcStockReject StockReason = stockReason;

    /// <summary>实际能给的件数（被库存夹过）。拒绝时为 0 或夹后值，<b>由 <see cref="Reject"/> 决定它有没有意义</b>。</summary>
    public readonly int GrantedCount = grantedCount;

    public readonly bool IsOk => Reject == NpcBuyReject.None;

    public readonly string Describe() =>
        $"buy_ok={(IsOk ? 1 : 0)} buy_total={TotalCost} buy_granted={GrantedCount} " +
        $"buy_reject={(byte)Reject} price={(byte)PriceReason} stock={(byte)StockReason}";
}
