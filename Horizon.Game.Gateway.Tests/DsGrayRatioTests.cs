using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Gateway.Configuration;
using Horizon.Game.Gateway.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 6.3 P1 — 按比例灰度放量（DsGrayRatio + 会话一致性哈希粘滞路由）测试。
/// <para>
/// 覆盖 <see cref="DsSyncDataPlane.IsInGrayCohort"/> 的边界/单调/分布语义，
/// 以及 <see cref="DsSyncDataPlane.TryRouteAsync"/> 在比例门下的行为（gray_excluded 回退、
/// 会话粘滞、0/100 全排除/全量）。纯函数部分直接断言；wiring 部分用环回监听器验证
/// 「同会话多包判定恒定」与「排除会话不触碰 DS」。
/// </para>
/// <para>
/// 本类记录 <c>horizon.sync.ds.route.*</c> 静态仪表，与 <c>DsRoutingMetricsTests</c> 等
/// 同属 <see cref="SyncMetricsIsolatedCollection"/> 串行集合。
/// </para>
/// </summary>
[Collection(SyncMetricsIsolatedCollection.Name)]
public class DsGrayRatioTests
{
    private static readonly byte[] Frame = new byte[4];

    // ── 纯函数：边界 / 单调 / 分布 ────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(99)]
    [InlineData(100)]
    [InlineData(101)] // 越界钳制
    [InlineData(-1)]
    public void IsInGrayCohort_RatioBoundaries_AllOrNothingOrClamped(int ratio)
    {
        // 0 → 全排除；100 → 全量（含越界钳制 101→100、-1→0）。中间比例必有部分在/部分不在。
        foreach (var id in new long[] { 0, 1, 42, 999_999, -7 })
        {
            if (ratio <= 0)
            {
                Assert.False(DsSyncDataPlane.IsInGrayCohort(id, ratio), $"id={id}, ratio={ratio} 应全排除");
            }
            else if (ratio >= 100)
            {
                Assert.True(DsSyncDataPlane.IsInGrayCohort(id, ratio), $"id={id}, ratio={ratio} 应全量");
            }
        }

        if (ratio is >= 1 and <= 99)
        {
            // 中间比例：宽样本（0..999）上既有在也有不在（分布非全有/全无）。
            // 注意：小样本（如 10 个 ID）在极端比例（99%）下可能全在——属抽样非分布问题。
            var inCohort = 0;
            for (long id = 0; id < 1000; id++)
            {
                if (DsSyncDataPlane.IsInGrayCohort(id, ratio)) inCohort++;
            }
            Assert.InRange(inCohort, 1, 999);
        }
    }

    [Fact]
    public void IsInGrayCohort_Monotonic_RatioIncreaseOnlyAdds()
    {
        // 单调性：比例上调时群组内会话不会移出（只进不出）——调档不造成会话切换宿主。
        foreach (var id in new long[] { 0, 1, 7, 42, 100, 777, 123_456 })
        {
            var previous = false;
            for (var r = 0; r <= 100; r++)
            {
                var current = DsSyncDataPlane.IsInGrayCohort(id, r);
                Assert.False(previous && !current, $"id={id}：比例 {r - 1}→{r} 出现移出（违反单调）");
                previous = current;
            }
        }
    }

    [Fact]
    public void IsInGrayCohort_Spread_SequentialIdsRoughlyRatioProportional()
    {
        // Knuth 乘法哈希：连续 ID 按比例均匀分散（非聚簇到一段连续 ID）。
        var countAt10 = 0;
        for (long id = 0; id < 1000; id++)
        {
            if (DsSyncDataPlane.IsInGrayCohort(id, 10)) countAt10++;
        }
        Assert.InRange(countAt10, 50, 150); // 期望 ≈100（10%），宽松界证明均匀而非聚簇

        var countAt50 = 0;
        for (long id = 0; id < 100; id++)
        {
            if (DsSyncDataPlane.IsInGrayCohort(id, 50)) countAt50++;
        }
        Assert.InRange(countAt50, 25, 75); // 期望 ≈50
    }

    [Fact]
    public void IsInGrayCohort_MatchesDocumentedKnuthHash_Exactly()
    {
        // 跨语言可复算性：固化几个已知 (id, ratio) 判定，任何哈希算法漂移都会破坏本测试。
        // 若未来 UE5 侧需对齐比例路由，按注释公式复算即可。
        Assert.True(DsSyncDataPlane.IsInGrayCohort(0, 1));   // 0 * φ mod 100 = 0 < 1
        Assert.False(DsSyncDataPlane.IsInGrayCohort(1, 1));  // 1 * φ mod 100 = 61 ≥ 1
        Assert.True(DsSyncDataPlane.IsInGrayCohort(0, 50));
        Assert.False(DsSyncDataPlane.IsInGrayCohort(42, 10)); // 42 * φ mod 100 = 62 ≥ 10
    }

    // ── plane wiring：比例门行为 / 会话粘滞 / 0/100 ──────────────────────

    [Fact]
    public async Task RatioZero_AllSessionsExcluded_FallbackGrayExcluded_WithoutTouchingDs()
    {
        using var capture = new SyncMetricsCapture();
        await using var fakeDs = new MultiAcceptDs();
        var registry = new DSRegistry();
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", fakeDs.Port);
        var plane = new DsSyncDataPlane(registry, NullLogger<DsSyncDataPlane>.Instance,
            new MutableOptionsMonitor(new GatewayOptions { UseDS = true, DsGrayRatio = 0 }));

        Assert.False(await plane.TryRouteAsync(0, 42, Frame));

        lock (capture.Counters)
        {
            Assert.Contains(capture.Counters,
                m => m.Name == "horizon.sync.ds.route.fallback" && m.Reason == "gray_excluded");
        }
        Assert.Equal(0, fakeDs.AcceptedCount); // 排除会话未触碰 DS
    }

    [Fact]
    public async Task RatioHundred_AllSessionsRoute_PreservesExistingBehavior()
    {
        await using var fakeDs = new MultiAcceptDs();
        var registry = new DSRegistry();
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", fakeDs.Port);
        var plane = new DsSyncDataPlane(registry, NullLogger<DsSyncDataPlane>.Instance,
            new MutableOptionsMonitor(new GatewayOptions { UseDS = true, DsGrayRatio = 100 }));

        Assert.True(await plane.TryRouteAsync(0, 42, Frame));
        await fakeDs.WaitForCountAsync(1);
        Assert.Equal(1, fakeDs.AcceptedCount);
    }

    [Fact]
    public async Task RatioMiddle_SessionSticky_ConsistentAcrossPackets()
    {
        await using var fakeDs = new MultiAcceptDs();
        var registry = new DSRegistry();
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", fakeDs.Port);
        var plane = new DsSyncDataPlane(registry, NullLogger<DsSyncDataPlane>.Instance,
            new MutableOptionsMonitor(new GatewayOptions { UseDS = true, DsGrayRatio = 50 }));

        // 取两个分属群组内/外的 ID（用生产同一判定函数挑选，验证 wiring 一致性）。
        long inId = -1, outId = -1;
        for (long id = 0; id < 100; id++)
        {
            if (DsSyncDataPlane.IsInGrayCohort(id, 50))
            {
                if (inId < 0) inId = id;
            }
            else if (outId < 0)
            {
                outId = id;
            }
            if (inId >= 0 && outId >= 0) break;
        }
        Assert.True(inId >= 0 && outId >= 0, "测试数据选取失败：ratio=50 下应同时存在群组内/外 ID");

        // 群组内：连续多包全部路由（会话粘滞，不逐包摇摆）。
        Assert.True(await plane.TryRouteAsync(0, inId, Frame));
        Assert.True(await plane.TryRouteAsync(0, inId, Frame));
        await fakeDs.WaitForCountAsync(2);
        Assert.Equal(2, fakeDs.AcceptedCount);

        // 群组外：连续多包全部排除（同样粘滞）。
        Assert.False(await plane.TryRouteAsync(0, outId, Frame));
        Assert.False(await plane.TryRouteAsync(0, outId, Frame));
        Assert.Equal(2, fakeDs.AcceptedCount); // 未增加（排除路径不建连，无竞态）
    }

    [Fact]
    public async Task RatioIncrease_ExcludedSession_JoinsCohort_ThenRoutes()
    {
        await using var fakeDs = new MultiAcceptDs();
        var registry = new DSRegistry();
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", fakeDs.Port);
        var monitor = new MutableOptionsMonitor(new GatewayOptions { UseDS = true, DsGrayRatio = 10 });
        var plane = new DsSyncDataPlane(registry, NullLogger<DsSyncDataPlane>.Instance, monitor);

        // 找到一个在 ratio=10 排除、ratio=20 纳入的 ID（单调性：上调只加入）。
        long id = -1;
        for (long candidate = 0; candidate < 1000; candidate++)
        {
            if (!DsSyncDataPlane.IsInGrayCohort(candidate, 10) && DsSyncDataPlane.IsInGrayCohort(candidate, 20))
            {
                id = candidate;
                break;
            }
        }
        Assert.True(id >= 0);

        Assert.False(await plane.TryRouteAsync(0, id, Frame)); // 10%：排除
        monitor.CurrentValue = new GatewayOptions { UseDS = true, DsGrayRatio = 20 }; // 热调档
        Assert.True(await plane.TryRouteAsync(0, id, Frame)); // 20%：纳入并路由
        await fakeDs.WaitForCountAsync(1);
        Assert.Equal(1, fakeDs.AcceptedCount);
    }

    /// <summary>多连接环回 DS 监听器：接受并完整读取每帧（计数 AcceptedCount）。</summary>
    private sealed class MultiAcceptDs : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        public int Port { get; }
        public int AcceptedCount => Volatile.Read(ref _acceptedCount);
        private int _acceptedCount;

        /// <summary>
        /// 等待 accept 循环处理完 expected 帧（轮询，最多 3s）。
        /// TryRouteAsync 在客户端 send 完成即返回，而计数在服务端读完整帧后才递增——
        /// 不等待直接断言会随机读到旧值（flaky），此处把异步落地收敛为确定性。
        /// </summary>
        public async Task WaitForCountAsync(int expected, int timeoutMs = 3000)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (AcceptedCount < expected && Environment.TickCount64 < deadline)
            {
                await Task.Delay(5);
            }
        }

        public MultiAcceptDs()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _loop = AcceptLoopAsync();
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    using var stream = client.GetStream();
                    var lenBuf = new byte[4];
                    await stream.ReadExactlyAsync(lenBuf);
                    var len = BinaryPrimitives.ReadInt32BigEndian(lenBuf);
                    var body = new byte[len];
                    await stream.ReadExactlyAsync(body);
                    Interlocked.Increment(ref _acceptedCount);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                // 测试断开竞态：忽略
            }
            finally
            {
                _listener.Stop();
            }
        }

        public ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            return ValueTask.CompletedTask;
        }
    }
}
