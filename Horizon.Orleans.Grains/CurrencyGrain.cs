using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Sim.Economy;
using Horizon.Model.GameModel;
using Horizon.Orleans.Interface;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace Horizon.Orleans.Grains;

/// <summary>钱包状态（按货币类型存余额与累计收支）。</summary>
/// <remarks>
/// <b>累计收支单列</b>：<c>CurrencyEntity</c> 的 <c>total_earned/total_spent</c> 是审计口径，
/// 只靠余额差反推不出来（一笔 100 入账 + 一笔 100 支出，余额差是 0）。所以两份字典都要存。
/// </remarks>
[GenerateSerializer]
public sealed class CurrencyState
{
    /// <summary>货币类型 → 余额。</summary>
    [Id(0)] public Dictionary<int, long> Balances { get; set; } = new();

    /// <summary>货币类型 → 累计产出。</summary>
    [Id(1)] public Dictionary<int, long> TotalEarned { get; set; } = new();

    /// <summary>货币类型 → 累计消耗。</summary>
    [Id(2)] public Dictionary<int, long> TotalSpent { get; set; } = new();
}

/// <summary>
/// 角色钱包 Grain（按角色 id 分键）。
/// <para>
/// <b>本类只做四件事</b>：读状态 → 调 <see cref="WalletCore"/> → 落状态 → 通知经济监控。
/// 所有"能不能扣/会不会溢出"的判据都在内核里，所以钱算术可以在无集群的单测里穷举
/// （<c>WalletCoreTests</c>），这里只剩接线。
/// </para>
/// <para>
/// <b>失败不落状态</b>：余额不足时连 <c>WriteStateAsync</c> 都不调用——
/// 一次失败请求就产生一次存储写入，会把"没扣"这件事变得不可证明。
/// </para>
/// </summary>
public sealed class CurrencyGrain : Grain, ICurrencyGrain
{
    private readonly ILogger<CurrencyGrain> _logger;
    private readonly IPersistentState<CurrencyState> _state;

    /// <summary>经济逐笔流水（可空：单测/降级装配没有这张表时，钱包仍要能工作）。</summary>
    private readonly IDataContext<GameEntityContext, EconomyLedgerEntity, long>? _ledger;

    /// <summary>经济监控是全局单例（<c>IGrainWithIntegerKey</c>），本工程约定用键 0。</summary>
    private const long EconomyMonitorKey = 0L;

    public CurrencyGrain(
        ILogger<CurrencyGrain> logger,
        [PersistentState("wallet", "GameStore")] IPersistentState<CurrencyState> state,
        IDataContext<GameEntityContext, EconomyLedgerEntity, long>? ledger = null)
    {
        _logger = logger;
        _state = state;
        _ledger = ledger;
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _state.State.Balances ??= new Dictionary<int, long>();
        _state.State.TotalEarned ??= new Dictionary<int, long>();
        _state.State.TotalSpent ??= new Dictionary<int, long>();
        return base.OnActivateAsync(cancellationToken);
    }

    public Task<long> GetBalanceAsync(int currencyType)
    {
        if (!WalletCurrency.IsRegistered(currencyType)) return Task.FromResult(0L);
        return Task.FromResult(_state.State.Balances.TryGetValue(currencyType, out var balance) ? balance : 0L);
    }

    public Task<WalletMutation> SpendAsync(int currencyType, long amount, string sink)
        => ApplyAsync(currencyType, amount, sink, spend: true);

    public Task<WalletMutation> AddAsync(int currencyType, long amount, string source)
        => ApplyAsync(currencyType, amount, source, spend: false);

    private async Task<WalletMutation> ApplyAsync(int currencyType, long amount, string label, bool spend)
    {
        label ??= string.Empty;

        if (!WalletCurrency.IsRegistered(currencyType))
        {
            // 未登记货币：拒绝并如实回执（不静默当成金币）。
            return new WalletMutation
            {
                Ok = false, CurrencyType = currencyType, Amount = amount,
                BalanceBefore = 0, BalanceAfter = 0,
                Reject = (int)WalletReject.UnknownCurrency, Label = label,
            };
        }

        var before = _state.State.Balances.TryGetValue(currencyType, out var current) ? current : 0L;
        var op = spend ? WalletCore.Spend(before, amount) : WalletCore.Earn(before, amount);

        if (!op.Ok)
        {
            _logger.LogDebug("Wallet {Key} {Op} rejected: {Detail}", this.GetPrimaryKeyLong(), spend ? "spend" : "add", op.Describe());
            return new WalletMutation
            {
                Ok = false, CurrencyType = currencyType, Amount = amount,
                BalanceBefore = op.BalanceBefore, BalanceAfter = op.BalanceAfter,
                Reject = (int)op.Reject, Label = label,
            };
        }

        _state.State.Balances[currencyType] = op.BalanceAfter;
        if (spend)
        {
            _state.State.TotalSpent.TryGetValue(currencyType, out var spent);
            _state.State.TotalSpent[currencyType] = spent + amount;
        }
        else
        {
            _state.State.TotalEarned.TryGetValue(currencyType, out var earned);
            _state.State.TotalEarned[currencyType] = earned + amount;
        }

        await _state.WriteStateAsync();

        // 逐笔流水：只追加、写失败不反悔已落库的钱包（但必须留日志，否则"少一笔"永远查不出）。
        await AppendLedgerAsync(currencyType, spend ? -amount : amount, op.BalanceAfter, label);

        // 经济监控是分析侧：它失败不能让一次已经落库的扣款变成"调用方以为失败"。
        // 但也不能静默——失败要留日志，否则"通胀报表少了一笔"永远查不出来。
        await NotifyEconomyMonitorAsync(currencyType, amount, label, spend);

        return new WalletMutation
        {
            Ok = true, CurrencyType = currencyType, Amount = amount,
            BalanceBefore = op.BalanceBefore, BalanceAfter = op.BalanceAfter,
            Reject = (int)WalletReject.None, Label = label,
        };
    }

    /// <summary>
    /// 追加一条逐笔流水（只追加）。失败只记日志：钱包已经落库，不能因为审计行写失败就让调用方以为没扣钱。
    /// </summary>
    private async Task AppendLedgerAsync(int currencyType, long delta, long balanceAfter, string reason)
    {
        if (_ledger == null) return;

        try
        {
            var text = string.IsNullOrEmpty(reason) ? "wallet" : reason;
            if (text.Length > 96) text = text[..96];
            await _ledger.AddAsync(new EconomyLedgerEntity
            {
                CharacterId = (ulong)this.GetPrimaryKeyLong(),
                CurrencyType = currencyType,
                Delta = delta,
                BalanceAfter = balanceAfter,
                Reason = text,
                CreatedAt = DateTime.UtcNow,
                IsValid = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Wallet {Key} 写经济流水失败（金额已落库，流水缺这一笔）：currency={Currency} delta={Delta} reason={Reason}",
                this.GetPrimaryKeyLong(), currencyType, delta, reason);
        }
    }

    private async Task NotifyEconomyMonitorAsync(int currencyType, long amount, string label, bool spend)
    {
        // 只有金币进经济报表；其他预留货币等真有产出时再扩展（不在这里假装已覆盖）。
        if (currencyType != WalletCurrency.Gold) return;

        try
        {
            var monitor = GrainFactory.GetGrain<IEconomyMonitorGrain>(EconomyMonitorKey);
            if (spend)
                await monitor.RecordCurrencyBurnAsync(amount, string.IsNullOrEmpty(label) ? "wallet_spend" : label, this.GetPrimaryKeyLong());
            else
                await monitor.RecordCurrencyMintAsync(amount, string.IsNullOrEmpty(label) ? "wallet_add" : label, this.GetPrimaryKeyLong());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Wallet {Key} 通知经济监控失败（金额已落库，报告会缺这一笔）：{Label}", this.GetPrimaryKeyLong(), label);
        }
    }
}
