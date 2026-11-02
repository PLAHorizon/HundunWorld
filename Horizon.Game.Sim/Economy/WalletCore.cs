using System;

namespace Horizon.Game.Sim.Economy;

/// <summary>
/// 货币类型的编号（<see cref="CurrencyEntity"/> 的 <c>currency_type</c> 列取值）。
/// <para>
/// <b>只登记确定要用的那一个</b>：1 = 金币。2..7 是预留号段（荣誉/徽章/代币一类），
/// 但本工程今天没有任何系统产出它们，所以这里<b>不发明名字</b>——写一堆"看起来存在"的货币常量，
/// 会让人以为那套玩法已经接好了。
/// </para>
/// </summary>
public static class WalletCurrency
{
    /// <summary>金币。</summary>
    public const int Gold = 1;

    /// <summary>已登记的货币类型上界（含）。越界一律拒，不静默当金币处理。</summary>
    public const int MaxRegistered = 1;

    /// <summary>该类型是否是已登记的货币。</summary>
    public static bool IsRegistered(int currencyType) => currencyType >= 1 && currencyType <= MaxRegistered;
}

/// <summary>一次钱包操作的拒绝分档。<b>每条拒绝都要回同一组字段</b>（同族拒绝分支纪律）。</summary>
public enum WalletReject : byte
{
    /// <summary>成功。</summary>
    None = 0,

    /// <summary>金额不是正数。<b>0 不是"成功扣了 0 块钱"，它是坏请求</b>——
    /// 放行会让"免费买卖"借着一次 0 金额调用混进流水。</summary>
    BadAmount = 1,

    /// <summary>余额不足（玩家态档，不产生任何副作用）。</summary>
    InsufficientFunds = 2,

    /// <summary>加钱会越过上限（防溢出把余额变成负数）。</summary>
    Overflow = 3,

    /// <summary>未登记的货币类型。</summary>
    UnknownCurrency = 4,
}

/// <summary>一次钱包操作的回执：结果、逐档理由、操作前后的余额。</summary>
/// <param name="Ok">是否成功。</param>
/// <param name="BalanceBefore">操作前余额。</param>
/// <param name="BalanceAfter">操作后余额（失败时 == <paramref name="BalanceBefore"/>，不做半截扣减）。</param>
/// <param name="Amount">本次金额（始终回显，便于日志对账）。</param>
/// <param name="Reject">拒绝档。</param>
public readonly struct WalletOp(bool ok, long balanceBefore, long balanceAfter, long amount, WalletReject reject)
{
    public readonly bool Ok = ok;
    public readonly long BalanceBefore = balanceBefore;
    public readonly long BalanceAfter = balanceAfter;
    public readonly long Amount = amount;
    public readonly WalletReject Reject = reject;

    /// <summary>ASCII 自证串（中文标签会吞掉紧随其后的数字）。</summary>
    public readonly string Describe() =>
        $"wallet_ok={(Ok ? 1 : 0)} before={BalanceBefore} after={BalanceAfter} amount={Amount} reject={(byte)Reject}";
}

/// <summary>
/// 钱包的确定性整数运算（**唯一一份**）。
/// <para>
/// <b>为什么不把加减写在 Grain 里</b>：余额是"能被刷出钱"的地方，扣减/累加/越界三件事必须只有一份实现，
/// 且必须能在没有 Orleans 的单测里穷举边界。Grain 只负责"读状态 → 调这里 → 落状态 → 记账"。
/// </para>
/// <para>
/// <b>溢出与负数</b>：加钱越界拒（<see cref="WalletReject.Overflow"/>），不加钳位——
/// 钳位会让一次溢出错觉成"成功入账到上限"，之后没人会去查那笔差额去哪了。
/// </para>
/// </summary>
public static class WalletCore
{
    /// <summary>判据版本（进经济审计口径）。</summary>
    public const int Version = 1;

    /// <summary>余额上限：<c>long.MaxValue</c> 留出余量，避免与"累加总和"相撞。</summary>
    public const long MaxBalance = 9_000_000_000_000L;

    /// <summary>
    /// 扣钱。<b>余额不足时余额原样返回</b>（调用方据此断言"没扣"）。
    /// </summary>
    public static WalletOp Spend(long balance, long amount)
    {
        if (amount <= 0) return new WalletOp(false, balance, balance, amount, WalletReject.BadAmount);
        if (balance < amount) return new WalletOp(false, balance, balance, amount, WalletReject.InsufficientFunds);

        return new WalletOp(true, balance, balance - amount, amount, WalletReject.None);
    }

    /// <summary>加钱。</summary>
    public static WalletOp Earn(long balance, long amount)
    {
        if (amount <= 0) return new WalletOp(false, balance, balance, amount, WalletReject.BadAmount);
        if (balance < 0) balance = 0; // 坏状态按 0 起算，而不是让负数继续传染
        if (amount > MaxBalance - balance) return new WalletOp(false, balance, balance, amount, WalletReject.Overflow);

        return new WalletOp(true, balance, balance + amount, amount, WalletReject.None);
    }
}
