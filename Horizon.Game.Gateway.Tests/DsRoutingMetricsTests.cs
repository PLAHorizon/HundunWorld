using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Horizon.Game.Gateway.Configuration;
using Horizon.Game.Gateway.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 6.3 P0 ① — UE5 DS 路由决策指标测试。
/// <para>
/// 覆盖 <see cref="DsSyncDataPlane.TryRouteAsync"/> 的指标语义：
/// <c>horizon.sync.ds.route.attempts</c>（每次启用态路由尝试 +1）、
/// <c>horizon.sync.ds.route.fallback</c>（按 reason 维度：<c>no_ds</c>/<c>breaker</c>/<c>timeout</c>/<c>transport_error</c>）、
/// <c>horizon.sync.ds.route.latency.ms</c>（成功转发耗时）。开关关闭时不记 attempts
/// （disabled 由 handler 门记录，见 <c>DsRoutingTests.UseDS_False_Handshake_RecordsFallbackDisabled</c>）。
/// </para>
/// <para>
/// 本类与 <c>DsRoutingTests</c>/<c>DsSyncDataPlaneBreakerTests</c> 同属
/// <see cref="SyncMetricsIsolatedCollection"/>（DisableParallelization）——三者都记录
/// <c>horizon.sync.ds.route.*</c> 静态仪表，并行会跨测试串扰测量，破坏「无 attempts」类
/// 缺席断言；串行化后各自捕获仅含本测试事件。
/// </para>
/// </summary>
[Collection(SyncMetricsIsolatedCollection.Name)]
public class DsRoutingMetricsTests
{
    private static readonly byte[] Frame = new byte[4];

    /// <summary>占一个端口后释放，得到对端必然不存在的端点（故障分类机器相关：refuse 或 hang→timeout）。</summary>
    private static int DeadPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static DsSyncDataPlane CreatePlane(DSRegistry registry, bool useDS, string? region = null, int grayRatio = 100, bool strictEmptyRegion = false)
        => new(registry, NullLogger<DsSyncDataPlane>.Instance,
            new MutableOptionsMonitor(new GatewayOptions
            {
                UseDS = useDS,
                Region = region ?? "华东",
                DsGrayRatio = grayRatio,
                StrictEmptyRegionMode = strictEmptyRegion,
            }));

    /// <summary>阶段 6.3 P1⑥+ — 严格模式：未声明 region 的 DS 被有 region 网关排除 → wrong_region 回退，不触碰网络。</summary>
    [Fact]
    public async Task Enabled_StrictMode_EmptyRegionDs_FallbackWrongRegion_DoesNotTouchDs()
    {
        using var capture = new SyncMetricsCapture();
        var registry = new DSRegistry();
        registry.RegisterOrUpdate("ds-legacy", new long[] { 0 }, "127.0.0.1", 1); // 未声明 region；端口 1 若触碰必失败
        var plane = CreatePlane(registry, useDS: true, strictEmptyRegion: true); // 网关区域默认“华东” + 严格模式

        Assert.False(await plane.TryRouteAsync(0, 42, Frame));

        lock (capture.Counters)
        {
            Assert.Contains(("horizon.sync.ds.route.attempts", 1L, null), capture.Counters);
            Assert.Contains(("horizon.sync.ds.route.fallback", 1L, "wrong_region"), capture.Counters);
            Assert.DoesNotContain(capture.Counters, m => m.Reason == "no_ds"); // 有注册（拓扑问题），非容量问题
        }
    }

    /// <summary>阶段 6.3 P1⑥ — 跨区盲路由拦截：DS 在华南、网关在华东 → wrong_region 回退，不触碰网络。</summary>
    [Fact]
    public async Task Enabled_CrossRegionDs_FallbackWrongRegion_DoesNotTouchDs()
    {
        using var capture = new SyncMetricsCapture();
        var registry = new DSRegistry();
        registry.RegisterOrUpdate("ds-south", new long[] { 0 }, "127.0.0.1", 1, region: "华南"); // 端口 1：若触碰必然失败
        var plane = CreatePlane(registry, useDS: true); // 网关区域默认“华东”

        Assert.False(await plane.TryRouteAsync(0, 42, Frame));

        lock (capture.Counters)
        {
            Assert.Contains(("horizon.sync.ds.route.attempts", 1L, null), capture.Counters);
            Assert.Contains(("horizon.sync.ds.route.fallback", 1L, "wrong_region"), capture.Counters);
            Assert.DoesNotContain(capture.Counters, m => m.Reason == "no_ds"); // 与 no_ds 区分（拓扑问题而非容量问题）
        }
    }

    [Fact]
    public async Task Enabled_NoRegisteredDs_FallbackNoDs_CountsAttempt()
    {
        using var capture = new SyncMetricsCapture();
        var plane = CreatePlane(new DSRegistry(), useDS: true);

        Assert.False(await plane.TryRouteAsync(0, 42, Frame));

        lock (capture.Counters)
        {
            Assert.Contains(("horizon.sync.ds.route.attempts", 1L, null), capture.Counters);
            Assert.Contains(("horizon.sync.ds.route.fallback", 1L, "no_ds"), capture.Counters);
        }
    }

    [Fact]
    public async Task Enabled_DeadDs_FallbackByFailureReason_CountsAttempt()
    {
        using var capture = new SyncMetricsCapture();
        var registry = new DSRegistry();
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", DeadPort());
        var plane = CreatePlane(registry, useDS: true);

        Assert.False(await plane.TryRouteAsync(0, 42, Frame));

        lock (capture.Counters)
        {
            Assert.Contains(("horizon.sync.ds.route.attempts", 1L, null), capture.Counters);
            // 故障 DS 的失败分类是机器相关的（Windows loopback 释放端口后即时连接挂至
            // 2s 超时 → timeout；Linux 即时拒绝 → transport_error）——断言「二者其一」即可，
            // 语义均为「故障 DS → 回退且计数」。
            Assert.Contains(capture.Counters,
                m => m.Name == "horizon.sync.ds.route.fallback"
                     && m.Reason is "timeout" or "transport_error");
            Assert.DoesNotContain(capture.Counters, m => m.Reason == "no_ds");
        }
    }

    [Fact]
    public async Task BreakerTrip_ThenFallbackBreaker_ReasonsPerPhase()
    {
        using var capture = new SyncMetricsCapture();
        var registry = new DSRegistry();
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", DeadPort());
        var plane = CreatePlane(registry, useDS: true);

        // 连续 3 次失败（timeout 或 transport_error 任一）→ 熔断进入冷却。
        for (var i = 0; i < 3; i++)
        {
            Assert.False(await plane.TryRouteAsync(0, 42, Frame));
        }
        Assert.True(plane.IsDsInCooldown("ds-1"));

        // 第 4 次：冷却期内 → breaker 回退（不再打故障端点）。
        Assert.False(await plane.TryRouteAsync(0, 42, Frame));

        lock (capture.Counters)
        {
            Assert.Equal(4, capture.Counters.FindAll(m => m.Name == "horizon.sync.ds.route.attempts").Count);
            // 前 3 次为失败分类（timeout/transport_error 各 1..3，机器相关），第 4 次必为 breaker。
            var failures = capture.Counters.FindAll(
                m => m.Name == "horizon.sync.ds.route.fallback" && m.Reason is "timeout" or "transport_error");
            Assert.Equal(3, failures.Count);
            Assert.Contains(capture.Counters, m => m.Name == "horizon.sync.ds.route.fallback" && m.Reason == "breaker");
        }
    }

    [Fact]
    public async Task Disabled_NoAttemptsRecorded_AtPlaneLevel()
    {
        using var capture = new SyncMetricsCapture();
        var plane = CreatePlane(new DSRegistry(), useDS: false);

        Assert.False(await plane.TryRouteAsync(0, 42, Frame));

        // 开关关闭：plane 层不记任何路由指标（disabled 由 handler 门记录）。
        lock (capture.Counters)
        {
            Assert.Empty(capture.Counters);
        }
    }

    [Fact]
    public async Task Success_RecordsLatency_NoFallback()
    {
        using var capture = new SyncMetricsCapture();
        await using var fakeDs = new FakeDs();
        var registry = new DSRegistry();
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", fakeDs.Port);
        var plane = CreatePlane(registry, useDS: true);

        Assert.True(await plane.TryRouteAsync(0, 42, Frame));

        lock (capture.Counters)
        {
            Assert.Contains(("horizon.sync.ds.route.attempts", 1L, null), capture.Counters);
            Assert.DoesNotContain(capture.Counters, m => m.Name == "horizon.sync.ds.route.fallback");
        }
        lock (capture.Latencies)
        {
            Assert.NotEmpty(capture.Latencies); // 成功转发耗时已记录
        }
    }

    // ── 阶段 6.3 P1⑦：维度标签（shard_id/region/result[/ds_id]）──

    [Fact]
    public async Task Enabled_NoDs_AttemptsTagged_ResultNoDs_ShardRegion_NoDsId()
    {
        using var capture = new SyncMetricsCapture();
        var plane = CreatePlane(new DSRegistry(), useDS: true);

        Assert.False(await plane.TryRouteAsync(0, 42, Frame));

        lock (capture.Tagged)
        {
            // attempts 在结果点记录：{shard_id=0, region=华东, result=no_ds}，无 ds_id（未解析出 DS）。
            var attempt = capture.Tagged.FindAll(m => m.Name == "horizon.sync.ds.route.attempts");
            Assert.Single(attempt);
            Assert.Equal("no_ds", attempt[0].Tags["result"]);
            Assert.Equal("0", attempt[0].Tags["shard_id"]);
            Assert.Equal("华东", attempt[0].Tags["region"]);
            Assert.False(attempt[0].Tags.ContainsKey("ds_id"));

            // fallback：{reason=no_ds, shard_id, region}。
            var fb = capture.Tagged.FindAll(m => m.Name == "horizon.sync.ds.route.fallback");
            Assert.Single(fb);
            Assert.Equal("no_ds", fb[0].Tags["reason"]);
            Assert.Equal("0", fb[0].Tags["shard_id"]);
            Assert.Equal("华东", fb[0].Tags["region"]);
        }
    }

    [Fact]
    public async Task Enabled_GrayExcluded_AttemptsTagged_ResultGrayExcluded_NoDsId()
    {
        using var capture = new SyncMetricsCapture();
        var plane = CreatePlane(new DSRegistry(), useDS: true, grayRatio: 0); // 全排除

        Assert.False(await plane.TryRouteAsync(0, 42, Frame));

        lock (capture.Tagged)
        {
            var attempt = capture.Tagged.FindAll(m => m.Name == "horizon.sync.ds.route.attempts");
            Assert.Single(attempt);
            Assert.Equal("gray_excluded", attempt[0].Tags["result"]);
            Assert.Equal("0", attempt[0].Tags["shard_id"]);
            Assert.Equal("华东", attempt[0].Tags["region"]);
            Assert.False(attempt[0].Tags.ContainsKey("ds_id")); // 灰度门在解析前：无 DS 上下文

            var fb = capture.Tagged.FindAll(m => m.Name == "horizon.sync.ds.route.fallback");
            Assert.Single(fb);
            Assert.Equal("gray_excluded", fb[0].Tags["reason"]);
            Assert.Equal("0", fb[0].Tags["shard_id"]);
        }
    }

    [Fact]
    public async Task Enabled_Success_AttemptsTagged_ResultSuccess_WithDsId_LatencyTagged()
    {
        using var capture = new SyncMetricsCapture();
        await using var fakeDs = new FakeDs();
        var registry = new DSRegistry();
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", fakeDs.Port);
        var plane = CreatePlane(registry, useDS: true);

        Assert.True(await plane.TryRouteAsync(0, 42, Frame));

        lock (capture.Tagged)
        {
            var attempt = capture.Tagged.FindAll(m => m.Name == "horizon.sync.ds.route.attempts");
            Assert.Single(attempt);
            Assert.Equal("success", attempt[0].Tags["result"]);
            Assert.Equal("ds-1", attempt[0].Tags["ds_id"]); // 成功路径：已解析出 DS
            Assert.Equal("0", attempt[0].Tags["shard_id"]);
            Assert.Equal("华东", attempt[0].Tags["region"]);
            Assert.DoesNotContain(capture.Tagged, m => m.Name == "horizon.sync.ds.route.fallback");
        }
        lock (capture.Latencies)
        {
            Assert.NotEmpty(capture.Latencies); // 成功耗时已记录（标签经 Tagged 断言）
        }
    }

    [Fact]
    public async Task Breaker_AttemptsTagged_ResultBreaker_WithDsId()
    {
        using var capture = new SyncMetricsCapture();
        var registry = new DSRegistry();
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", DeadPort());
        var plane = CreatePlane(registry, useDS: true);

        for (var i = 0; i < 3; i++)
        {
            Assert.False(await plane.TryRouteAsync(0, 42, Frame)); // 触发熔断
        }
        Assert.False(await plane.TryRouteAsync(0, 42, Frame)); // 冷却期内 → breaker

        lock (capture.Tagged)
        {
            var breaker = capture.Tagged.FindAll(m => m.Name == "horizon.sync.ds.route.attempts" && m.Tags["result"] == "breaker");
            Assert.Single(breaker);
            Assert.Equal("ds-1", breaker[0].Tags["ds_id"]); // 熔断回退：DS 上下文可定位
            Assert.Equal("0", breaker[0].Tags["shard_id"]);
            var fb = capture.Tagged.FindAll(m => m.Name == "horizon.sync.ds.route.fallback" && m.Tags["reason"] == "breaker");
            Assert.Single(fb);
            Assert.Equal("ds-1", fb[0].Tags["ds_id"]);
        }
    }

    [Fact]
    public async Task Attempts_Total_AcrossResults_Preserved()
    {
        // 维度标签不改变总量语义：每启用态路由恰好 1 次 attempts（跨 result/shard 求和不变）——
        // 回滚门（MeterListener 跨标签求和）与 ramp-check（跨行求和）的既有消费方不受影响。
        using var capture = new SyncMetricsCapture();
        var plane = CreatePlane(new DSRegistry(), useDS: true);

        await plane.TryRouteAsync(0, 42, Frame); // no_ds
        await plane.TryRouteAsync(1, 43, Frame); // no_ds（另一 shard）

        lock (capture.Tagged)
        {
            var attempts = capture.Tagged.FindAll(m => m.Name == "horizon.sync.ds.route.attempts");
            Assert.Equal(2, attempts.Count);
            Assert.Equal(2, attempts.Sum(m => m.Value));
            var shards = attempts.Select(m => m.Tags["shard_id"]).OrderBy(x => x).ToArray();
            Assert.Equal(new[] { "0", "1" }, shards);
        }
    }

    /// <summary>单连接环回 DS 监听器（与 DsRoutingTests.FakeDs 同构）。</summary>
    private sealed class FakeDs : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly TaskCompletionSource<byte[]> _receivedTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Port { get; }

        public FakeDs()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = AcceptLoopAsync();
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                var lenBuf = new byte[4];
                await stream.ReadExactlyAsync(lenBuf);
                var len = BinaryPrimitives.ReadInt32BigEndian(lenBuf);
                var body = new byte[len];
                await stream.ReadExactlyAsync(body);
                _receivedTcs.TrySetResult(body);
            }
            catch (Exception ex)
            {
                _receivedTcs.TrySetException(ex);
            }
            finally
            {
                _listener.Stop();
            }
        }

        public ValueTask DisposeAsync()
        {
            _listener.Stop();
            return ValueTask.CompletedTask;
        }
    }

}
