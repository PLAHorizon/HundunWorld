using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Sim.Npcs;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace Horizon.Orleans.Grains;

/// <summary>
/// NPC 货架库存状态（每个 NPC 一份）。
/// <para>只存"已买数 + 补货锚点"，剩余量由 <c>NpcCommerceCore</c> 按时间算出来。</para>
/// </summary>
[GenerateSerializer]
public sealed class NpcShopState
{
    /// <summary>物品 id → 记账状态。</summary>
    [Id(0)] public Dictionary<int, ShopStockState> Items { get; set; } = new();
}

/// <summary>单个物品的记账状态。</summary>
[GenerateSerializer]
public sealed class ShopStockState
{
    [Id(0)] public int Purchased { get; set; }

    [Id(1)] public long AnchorSeconds { get; set; }
}

/// <summary>
/// NPC 货架库存 Grain 实现：读状态 → 调 <see cref="NpcShopLedgerCore"/> → 落状态。
/// <para>判据全在内核里，这里只接线；失败路径不写状态（"没卖"必须能由存储层证明）。</para>
/// </summary>
public sealed class NpcShopGrain : Grain, INpcShopGrain
{
    private readonly ILogger<NpcShopGrain> _logger;
    private readonly IPersistentState<NpcShopState> _state;

    public NpcShopGrain(
        ILogger<NpcShopGrain> logger,
        [PersistentState("npc_shop", "GameStore")] IPersistentState<NpcShopState> state)
    {
        _logger = logger;
        _state = state;
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _state.State.Items ??= new Dictionary<int, ShopStockState>();
        return base.OnActivateAsync(cancellationToken);
    }

    public Task<ShopStockView> GetStockAsync(int itemId, int stockMax, int restockSeconds)
    {
        var row = Row(itemId, stockMax, restockSeconds);
        var entry = Entry(itemId);
        var verdict = NpcShopLedgerCore.Peek(in row, in entry, Now());
        return Task.FromResult(View(true, 0, verdict, entry));
    }

    public async Task<ShopStockView> TakeAsync(int itemId, int count, int stockMax, int restockSeconds)
    {
        var row = Row(itemId, stockMax, restockSeconds);
        var entry = Entry(itemId);
        var now = Now();

        var record = NpcShopLedgerCore.TryRecord(in row, in entry, count, now, out var next, out var verdict);
        if (!record.Ok)
        {
            _logger.LogDebug("NpcShop {Npc} take rejected: {Detail}", this.GetPrimaryKeyLong(), record.Describe());
            return View(false, (int)record.Reject, verdict, entry);
        }

        _state.State.Items[itemId] = new ShopStockState
        {
            Purchased = next.Purchased,
            AnchorSeconds = next.AnchorSeconds,
        };
        await _state.WriteStateAsync();

        return View(true, (int)ShopStockReject.None, verdict, next);
    }

    public async Task<ShopStockView> ReturnAsync(int itemId, int count, int stockMax, int restockSeconds)
    {
        if (itemId <= 0 || count <= 0)
        {
            var bad = Row(itemId, stockMax, restockSeconds);
            return View(false, (int)ShopStockReject.BadCount, NpcShopLedgerCore.Peek(in bad, Entry(itemId), Now()), Entry(itemId));
        }

        var entry = Entry(itemId);
        // 冲正：已卖数往下减，下限 0；锚点不动（它描述的是"本周期从何时起算"）。
        var next = new ShopStockEntry(Math.Max(0, entry.Purchased - count), entry.AnchorSeconds);
        _state.State.Items[itemId] = new ShopStockState { Purchased = next.Purchased, AnchorSeconds = next.AnchorSeconds };
        await _state.WriteStateAsync();

        var row = Row(itemId, stockMax, restockSeconds);
        var verdict = NpcShopLedgerCore.Peek(in row, in next, Now());
        return View(true, (int)ShopStockReject.None, verdict, next);
    }

    private static NpcVendorRow Row(int itemId, int stockMax, int restockSeconds)
        => new NpcVendorRow(itemId, 0, 0, 0, stockMax, restockSeconds);

    private ShopStockEntry Entry(int itemId)
    {
        if (_state.State.Items.TryGetValue(itemId, out var stored) && stored != null)
            return new ShopStockEntry(stored.Purchased, stored.AnchorSeconds);

        return ShopStockEntry.Empty;
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static ShopStockView View(bool ok, int reject, NpcStockVerdict verdict, ShopStockEntry entry) => new ShopStockView
    {
        Ok = ok,
        Reject = reject,
        Remaining = verdict.Remaining,
        Unlimited = verdict.Unlimited,
        Purchased = entry.Purchased,
    };
}
