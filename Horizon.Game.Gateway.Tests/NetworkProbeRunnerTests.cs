using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HundunWorld.Game.Network;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// NetworkProbeRunner 单元测试。
/// 回归覆盖 BUG：成功探测后共享取消令牌被永久取消，导致所有后续连通性检查被短路返回 false，
/// 心跳连续超时后 ReconnectionManager 误判"本地网络不可用"，客户端永不重连（远程角色不可见）。
/// </summary>
public class NetworkProbeRunnerTests
{
    private static Task<bool> Reachable(string host, int port, CancellationToken token) => Task.FromResult(true);

    private static Task<bool> Unreachable(string host, int port, CancellationToken token) => Task.FromResult(false);

    /// <summary>任一主机连通时返回 true。</summary>
    [Fact]
    public async Task ProbeAnyAsync_WhenAnyHostReachable_ReturnsTrue()
    {
        var hosts = new[] { "host-a", "host-b", "host-c" };

        bool result = await NetworkProbeRunner.ProbeAnyAsync(
            hosts,
            53,
            (h, p, t) => h == "host-b" ? Reachable(h, p, t) : Unreachable(h, p, t),
            CancellationToken.None);

        Assert.True(result);
    }

    /// <summary>所有主机均不可达时返回 false。</summary>
    [Fact]
    public async Task ProbeAnyAsync_WhenAllHostsUnreachable_ReturnsFalse()
    {
        var hosts = new[] { "host-a", "host-b" };

        bool result = await NetworkProbeRunner.ProbeAnyAsync(hosts, 53, Unreachable, CancellationToken.None);

        Assert.False(result);
    }

    /// <summary>任一主机成功时，其余在途探测被取消（仅局部令牌，不影响调用方）。</summary>
    [Fact]
    public async Task ProbeAnyAsync_WhenOneHostReachable_CancelsRemainingInFlightProbes()
    {
        var cancelledRemaining = 0;
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Func<string, int, CancellationToken, Task<bool>> probe = (host, port, token) =>
        {
            if (host == "slow")
            {
                token.Register(() => Interlocked.Increment(ref cancelledRemaining));
                return pending.Task;
            }
            return Reachable(host, port, token);
        };

        bool result = await NetworkProbeRunner.ProbeAnyAsync(new[] { "slow", "fast" }, 53, probe, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(1, Volatile.Read(ref cancelledRemaining));
    }

    /// <summary>探测委托抛出异常时视为该主机不可达，继续探测其余主机。</summary>
    [Fact]
    public async Task ProbeAnyAsync_WhenProbeThrows_TreatsAsUnreachableAndContinues()
    {
        var calledHosts = new List<string>();
        Func<string, int, CancellationToken, Task<bool>> probe = (host, port, token) =>
        {
            lock (calledHosts)
            {
                calledHosts.Add(host);
            }
            if (host == "boom")
            {
                return Task.FromException<bool>(new InvalidOperationException("probe failed"));
            }
            return Reachable(host, port, token);
        };

        bool result = await NetworkProbeRunner.ProbeAnyAsync(new[] { "boom", "ok" }, 53, probe, CancellationToken.None);

        Assert.True(result);
        lock (calledHosts)
        {
            Assert.Contains("boom", calledHosts);
            Assert.Contains("ok", calledHosts);
        }
    }

    /// <summary>空主机列表返回 false。</summary>
    [Fact]
    public async Task ProbeAnyAsync_WhenHostsEmpty_ReturnsFalse()
    {
        bool result = await NetworkProbeRunner.ProbeAnyAsync(Array.Empty<string>(), 53, Reachable, CancellationToken.None);
        Assert.False(result);
    }

    /// <summary>null 主机列表返回 false。</summary>
    [Fact]
    public async Task ProbeAnyAsync_WhenHostsNull_ReturnsFalse()
    {
        bool result = await NetworkProbeRunner.ProbeAnyAsync(null, 53, Reachable, CancellationToken.None);
        Assert.False(result);
    }

    /// <summary>调用方令牌已取消时不发起任何探测，直接返回 false。</summary>
    [Fact]
    public async Task ProbeAnyAsync_WhenTokenAlreadyCancelled_DoesNotProbeAndReturnsFalse()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var probeCalls = 0;

        bool result = await NetworkProbeRunner.ProbeAnyAsync(
            new[] { "host-a", "host-b" },
            53,
            (h, p, t) =>
            {
                Interlocked.Increment(ref probeCalls);
                return Reachable(h, p, t);
            },
            cts.Token);

        Assert.False(result);
        Assert.Equal(0, Volatile.Read(ref probeCalls));
    }

    /// <summary>
    /// 核心回归：成功探测不得取消调用方共享令牌，后续探测仍可用。
    /// 旧实现（NetworkStateMonitor 在成功连接后调用共享 _cancellationTokenSource.Cancel()）
    /// 会使第二次探测被短路返回 false，本用例在旧行为下必然失败。
    /// </summary>
    [Fact]
    public async Task ProbeAnyAsync_SuccessDoesNotCancelSharedToken_SubsequentProbeStillWorks()
    {
        using var sharedCts = new CancellationTokenSource();
        var probeCalls = 0;
        Func<string, int, CancellationToken, Task<bool>> probe = (h, p, t) =>
        {
            Interlocked.Increment(ref probeCalls);
            return Reachable(h, p, t);
        };

        bool first = await NetworkProbeRunner.ProbeAnyAsync(new[] { "host-a", "host-b" }, 53, probe, sharedCts.Token);

        Assert.True(first);
        Assert.False(sharedCts.IsCancellationRequested, "成功探测后调用方共享取消令牌不得被取消");
        Assert.Equal(2, Volatile.Read(ref probeCalls));

        bool second = await NetworkProbeRunner.ProbeAnyAsync(new[] { "host-a", "host-b" }, 53, probe, sharedCts.Token);

        Assert.True(second, "共享令牌未被取消时，后续连通性探测必须仍然可用");
        Assert.Equal(4, Volatile.Read(ref probeCalls));
    }
}