using System;
using Horizon.Game.Sim.Economy;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 钱包算术的可执行断言。
/// <para>
/// 为什么这一层要单独测：余额是唯一"能被刷钱"的字段。判据放在内核里、测试不启集群，
/// 才能把边界（正好扣完 / 少 1 文 / 正好到上限 / 多 1 文）穷举干净；
/// 放进 Grain 里就只能靠"连一次真 Silo"来验，实际没人会每次都跑。
/// </para>
/// <para>每条拒绝都断言"余额分毫未动"——拒绝的语义是"没有发生"，不是"发生了但返回 false"。</para>
/// </summary>
public class WalletCoreTests
{
    private readonly ITestOutputHelper _log;

    public WalletCoreTests(ITestOutputHelper log) => _log = log;

    [Fact]
    public void Spend_ExactAndNormalCases()
    {
        var normal = WalletCore.Spend(500, 120);
        Assert.True(normal.Ok);
        Assert.Equal(500, normal.BalanceBefore);
        Assert.Equal(380, normal.BalanceAfter);
        Assert.Equal(WalletReject.None, normal.Reject);

        // 正好扣完是合法的（余额 0 不是"没钱"以外的任何语义）。
        var exact = WalletCore.Spend(120, 120);
        Assert.True(exact.Ok);
        Assert.Equal(0, exact.BalanceAfter);
    }

    [Fact]
    public void Spend_InsufficientLeavesBalanceUntouched()
    {
        var op = WalletCore.Spend(119, 120);
        Assert.False(op.Ok);
        Assert.Equal(WalletReject.InsufficientFunds, op.Reject);
        Assert.Equal(119, op.BalanceBefore);
        Assert.Equal(119, op.BalanceAfter); // 一分不能少

        // 0 余额 + 任何正数金额
        Assert.False(WalletCore.Spend(0, 1).Ok);
    }

    [Fact]
    public void Spend_NonPositiveAmountIsARepresentableError()
    {
        // 0 金额不是"成功扣了 0"，放行会让免费买卖借 0 金额混进流水。
        foreach (var amount in new long[] { 0, -1, long.MinValue })
        {
            var op = WalletCore.Spend(1000, amount);
            Assert.False(op.Ok);
            Assert.Equal(WalletReject.BadAmount, op.Reject);
            Assert.Equal(1000, op.BalanceAfter);
        }
    }

    [Fact]
    public void Earn_BoundaryAtMaxBalance()
    {
        var ok = WalletCore.Earn(WalletCore.MaxBalance - 10, 10);
        Assert.True(ok.Ok);
        Assert.Equal(WalletCore.MaxBalance, ok.BalanceAfter);

        // 多 1 文就必须拒，而不是钳到上限（钳位会把溢出伪装成成功入账）。
        var overflow = WalletCore.Earn(WalletCore.MaxBalance, 1);
        Assert.False(overflow.Ok);
        Assert.Equal(WalletReject.Overflow, overflow.Reject);
        Assert.Equal(WalletCore.MaxBalance, overflow.BalanceAfter);

        Assert.Equal(WalletReject.BadAmount, WalletCore.Earn(0, 0).Reject);
        Assert.Equal(WalletReject.BadAmount, WalletCore.Earn(0, -5).Reject);
    }

    [Fact]
    public void Earn_NegativeBalanceIsNormalizedToZero()
    {
        // 坏状态（历史脏数据）从 0 起算，不让负数继续传染。
        var op = WalletCore.Earn(-500, 100);
        Assert.True(op.Ok);
        Assert.Equal(0, op.BalanceBefore);
        Assert.Equal(100, op.BalanceAfter);
    }

    /// <summary>随机操作序列：余额永远落在 [0, MaxBalance]，且成功操作的余额变化与金额严格相等。</summary>
    [Fact]
    public void RandomSequence_KeepsBalanceInRangeAndConservesAmounts()
    {
        var rng = new Random(20261026);
        long balance = 0;

        for (var i = 0; i < 5000; i++)
        {
            var amount = rng.Next(0, 1000) == 0 ? -rng.Next(1, 50) : rng.Next(1, 5000);
            var spend = rng.Next(2) == 0;

            var before = balance;
            var op = spend ? WalletCore.Spend(balance, amount) : WalletCore.Earn(balance, amount);
            balance = op.BalanceAfter;

            Assert.InRange(balance, 0, WalletCore.MaxBalance);

            if (op.Ok)
            {
                Assert.Equal(before, op.BalanceBefore);
                Assert.Equal(spend ? before - amount : before + amount, balance);
            }
            else
            {
                Assert.Equal(before, balance); // 拒绝 = 什么都没发生
            }
        }

        _log.WriteLine($"final_balance={balance}");
    }

    [Fact]
    public void CurrencyRegistry_OnlyGoldIsRegistered()
    {
        Assert.True(WalletCurrency.IsRegistered(WalletCurrency.Gold));
        Assert.False(WalletCurrency.IsRegistered(0));
        Assert.False(WalletCurrency.IsRegistered(-1));
        Assert.False(WalletCurrency.IsRegistered(WalletCurrency.MaxRegistered + 1));
    }
}
