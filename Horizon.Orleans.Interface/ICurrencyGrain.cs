using System.Threading.Tasks;
using Orleans;

namespace Horizon.Orleans.Interface;

/// <summary>
/// 角色钱包 Grain 契约（按角色 id 分键）。
/// <para>
/// <b>为什么需要一个独立钱包</b>：商人买卖、训练师学费、制作工费三条链都要"扣钱"，
/// 而本工程今天**没有任何地方持有余额**——<c>CurrencyEntity</c> 表存在但全仓零消费者，
/// <c>CharacterGrain.AddGoldAsync</c> 只出现在注释里。没有权威余额，三条链都无从谈起。
/// </para>
/// <para>
/// <b>算术不在 Grain 里</b>：扣减/累加/越界只有一份实现（<c>Horizon.Game.Sim.Economy.WalletCore</c>），
/// Grain 只做"读状态 → 调内核 → 落状态 → 通知经济监控"。这样"会不会把余额扣成负数"这类问题
/// 可以在不启集群的单测里穷举。
/// </para>
/// </summary>
[global::Orleans.CodeGeneration.Version(1)]
public interface ICurrencyGrain : IGrainWithIntegerKey
{
    /// <summary>查余额（未登记货币返回 0；不抛）。</summary>
    Task<long> GetBalanceAsync(int currencyType);

    /// <summary>
    /// 扣款（原子：不足时一分不扣）。
    /// </summary>
    /// <param name="currencyType">货币类型（<c>WalletCurrency.Gold</c>）。</param>
    /// <param name="amount">金额，必须为正。</param>
    /// <param name="sink">去向（"npc_vendor:400001" / "npc_trainer:1:810101"），用于经济审计。</param>
    Task<WalletMutation> SpendAsync(int currencyType, long amount, string sink);

    /// <summary>入账（原子：越界时一分不入）。</summary>
    /// <param name="source">来源（"quest:1001" / "npc_vendor:sell"），用于经济审计。</param>
    Task<WalletMutation> AddAsync(int currencyType, long amount, string source);
}

/// <summary>
/// 一次钱包变更的回执。
/// <para><b>每条出口都带同一组字段</b>：成功与失败都能读出"操作前/后余额、金额、拒绝档"，
/// 因此"没扣"与"扣了 0"在回执上不同形（记忆 [同族拒绝分支要回同一套对账字段]）。</para>
/// </summary>
[GenerateSerializer]
public sealed class WalletMutation
{
    [Id(0)] public bool Ok { get; set; }

    [Id(1)] public int CurrencyType { get; set; }

    [Id(2)] public long Amount { get; set; }

    [Id(3)] public long BalanceBefore { get; set; }

    [Id(4)] public long BalanceAfter { get; set; }

    /// <summary><c>Horizon.Game.Sim.Economy.WalletReject</c> 的数值（接口层不引用 Sim 的枚举类型，
    /// 保持契约程序集零业务依赖；数值由内核定义）。</summary>
    [Id(5)] public int Reject { get; set; }

    /// <summary>来源/去向标签（审计用；不进任何判据）。</summary>
    [Id(6)] public string Label { get; set; } = string.Empty;

    public override string ToString() =>
        $"wallet_ok={(Ok ? 1 : 0)} before={BalanceBefore} after={BalanceAfter} amount={Amount} reject={Reject} label={Label}";
}
