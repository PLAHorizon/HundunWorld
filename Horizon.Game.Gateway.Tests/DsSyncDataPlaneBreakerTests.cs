using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Horizon.Game.Gateway.Configuration;
using Horizon.Game.Gateway.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 2.5 — DsSyncDataPlane 路由级熔断（DS 故障联动摘除）测试。
/// <para>
/// 覆盖：连续失败达阈值 → 临时摘除（冷却期回退）；冷却到期 → 自动探测恢复且成功重置计数；
/// 单次失败后成功不触发熔断；开关关闭零交互。时钟注入驱动冷却语义，死端口模拟故障 DS，
/// 环回监听器模拟恢复后的 DS。
/// </para>
/// <para>
/// 本类记录 <c>horizon.sync.ds.route.*</c> 静态仪表（熔断路径的 attempts/fallback），
/// 与 <c>DsRoutingMetricsTests</c>/<c>DsRoutingTests</c> 同属 <see cref="SyncMetricsIsolatedCollection"/> 串行集合。
/// </para>
/// </summary>
[Collection(SyncMetricsIsolatedCollection.Name)]
public class DsSyncDataPlaneBreakerTests
{
    /// <summary>可变时钟盒。</summary>
    private sealed class Clock
    {
        public DateTimeOffset Now = new(2026, 8, 11, 0, 0, 0, TimeSpan.Zero);
    }

    /// <summary>环回 TCP 监听器，模拟 UE5 DS 接收端（接受一次连接后停止）。</summary>
    private sealed class FakeDs : IAsyncDisposable
    {
        private readonly TcpListener _listener;

        public int Port { get; }

        public FakeDs()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = AcceptOnceAsync();
        }

        private async Task AcceptOnceAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                var lenBuf = new byte[4];
                await stream.ReadExactlyAsync(lenBuf);
                var len = BinaryPrimitives.ReadInt32BigEndian(lenBuf);
                await stream.ReadExactlyAsync(new byte[len]);
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

    /// <summary>占用一个端口后立即释放 → 该端口连接被拒绝（模拟故障 DS）。</summary>
    private static int DeadPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static DsSyncDataPlane CreatePlane(
        DSRegistry registry, Clock clock, int threshold = 3, TimeSpan? cooldown = null)
        => new(registry, NullLogger<DsSyncDataPlane>.Instance,
            new MutableOptionsMonitor(new GatewayOptions { UseDS = true }),
            failureThreshold: threshold,
            cooldown: cooldown ?? TimeSpan.FromSeconds(30), clock: () => clock.Now);

    private static DSRegistry CreateRegistry(Clock clock)
        => new(staleAfter: TimeSpan.FromSeconds(90), clock: () => clock.Now);

    private static readonly byte[] Frame = new byte[4]; // 任意帧字节

    [Fact]
    public async Task ConsecutiveFailures_ReachThreshold_EntersCooldown_FallsBack()
    {
        var clock = new Clock();
        var registry = CreateRegistry(clock);
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", DeadPort());
        var plane = CreatePlane(registry, clock, threshold: 3, cooldown: TimeSpan.FromSeconds(30));

        // 前 2 次失败：未达阈值，不熔断，仍回退。
        Assert.False(await plane.TryRouteAsync(0, 42, Frame));
        Assert.False(await plane.TryRouteAsync(0, 42, Frame));
        Assert.False(plane.IsDsInCooldown("ds-1"));

        // 第 3 次失败：达到阈值 → 进入冷却期（临时摘除）。
        Assert.False(await plane.TryRouteAsync(0, 42, Frame));
        Assert.True(plane.IsDsInCooldown("ds-1"));

        // 冷却期内：继续回退，且不再尝试连接（注册表仍解析到该 DS，但熔断拦截）。
        Assert.False(await plane.TryRouteAsync(0, 42, Frame));
        Assert.True(plane.IsDsInCooldown("ds-1"));
    }

    [Fact]
    public async Task CooldownExpiry_AutoRetries_AndSuccessRestores()
    {
        var clock = new Clock();
        var registry = CreateRegistry(clock);
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", DeadPort());
        var plane = CreatePlane(registry, clock, threshold: 3, cooldown: TimeSpan.FromSeconds(30));

        for (var i = 0; i < 3; i++)
        {
            Assert.False(await plane.TryRouteAsync(0, 42, Frame));
        }
        Assert.True(plane.IsDsInCooldown("ds-1"));

        // DS 恢复：重新注册到新的可用端口（模拟重启后心跳续约携带新端点），冷却到期。
        await using var ds = new FakeDs();
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", ds.Port);
        clock.Now = clock.Now.AddSeconds(31);

        // 冷却到期后首个包自动探测 → 成功 → 熔断复位。
        Assert.True(await plane.TryRouteAsync(0, 42, Frame));
        Assert.False(plane.IsDsInCooldown("ds-1"));
    }

    [Fact]
    public async Task SingleFailure_ThenSuccess_DoesNotTripBreaker()
    {
        var clock = new Clock();
        var registry = CreateRegistry(clock);
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", DeadPort());
        var plane = CreatePlane(registry, clock, threshold: 3);

        // 1 次失败 → 成功（新监听器）→ 计数复位。
        Assert.False(await plane.TryRouteAsync(0, 42, Frame));
        await using var ds = new FakeDs();
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", ds.Port);
        Assert.True(await plane.TryRouteAsync(0, 42, Frame));
        Assert.False(plane.IsDsInCooldown("ds-1"));

        // 复位后再 2 次失败仍不达阈值（需累计 3 次连续失败）。
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", DeadPort());
        Assert.False(await plane.TryRouteAsync(0, 42, Frame));
        Assert.False(await plane.TryRouteAsync(0, 42, Frame));
        Assert.False(plane.IsDsInCooldown("ds-1"));
    }

    [Fact]
    public async Task Disabled_ReturnsFalse_WithoutBreakerInteraction()
    {
        var clock = new Clock();
        var registry = CreateRegistry(clock);
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", DeadPort());
        var plane = new DsSyncDataPlane(registry, NullLogger<DsSyncDataPlane>.Instance,
            new MutableOptionsMonitor(new GatewayOptions { UseDS = false }));

        for (var i = 0; i < 5; i++)
        {
            Assert.False(await plane.TryRouteAsync(0, 42, Frame));
        }
        Assert.False(plane.IsDsInCooldown("ds-1"));
    }
}
