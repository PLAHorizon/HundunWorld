using System;
using Horizon.Game.Gateway.Services;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 2.5 — DSRegistry（UE5 DS 注册表）生命周期测试。
/// <para>
/// 覆盖：注册/解析、心跳续约、过期摘除、摘除、幂等 upsert、同 shard 多 DS 选最新、
/// 参数校验。时钟注入（可变时钟盒）使过期语义可确定性地测试（不依赖真实时间流逝）。
/// </para>
/// </summary>
public class DSRegistryTests
{
    /// <summary>可变时钟盒：测试通过推进 <see cref="Now"/> 驱动过期语义。</summary>
    private sealed class Clock
    {
        public DateTimeOffset Now = new(2026, 8, 11, 0, 0, 0, TimeSpan.Zero);
    }

    private static DSRegistry CreateRegistry(Clock clock)
        => new(staleAfter: TimeSpan.FromSeconds(90), clock: () => clock.Now);

    // ── 阶段 6.3 P1⑥：region 过滤（防跨区盲路由）──

    [Fact]
    public void TryResolve_WithRegion_MatchesSameRegion_RejectsCrossRegion()
    {
        var clock = new Clock();
        var registry = CreateRegistry(clock);
        registry.RegisterOrUpdate("ds-east", new long[] { 0 }, "127.0.0.1", 5001, region: "华东");
        registry.RegisterOrUpdate("ds-south", new long[] { 0 }, "127.0.0.1", 5002, region: "华南");

        // 华东网关 → 华东 DS（跨区 DS 被过滤）。
        var status = registry.TryResolve(0, "华东", out var ep);
        Assert.Equal(DsResolveStatus.Ok, status);
        Assert.Equal("ds-east", ep.DsId);
        Assert.Equal("华东", ep.Region);

        // 华南网关 → 华南 DS。
        Assert.Equal(DsResolveStatus.Ok, registry.TryResolve(0, "华南", out var epSouth));
        Assert.Equal("ds-south", epSouth.DsId);

        // 无任何匹配区域的 DS（华北）→ WrongRegion（区别于 NoDs）。
        Assert.Equal(DsResolveStatus.WrongRegion, registry.TryResolve(0, "华北", out _));
    }

    [Fact]
    public void TryResolve_NoRegionOnDs_ServesAnyRegion_AndEmptyRequestIsUnrestricted()
    {
        var clock = new Clock();
        var registry = CreateRegistry(clock);
        // 未设区（空）的 DS = 不限区：可服务任意区域网关（灰度期兼容旧注册）。
        registry.RegisterOrUpdate("ds-legacy", new long[] { 0 }, "127.0.0.1", 5001);

        Assert.Equal(DsResolveStatus.Ok, registry.TryResolve(0, "华东", out _));
        Assert.Equal(DsResolveStatus.Ok, registry.TryResolve(0, "华南", out _));
        // 网关未配置区域（空）→ 不限区。
        Assert.Equal(DsResolveStatus.Ok, registry.TryResolve(0, "", out _));
        Assert.Equal(DsResolveStatus.Ok, registry.TryResolve(0, null, out _));

        // 2 参重载（无 region）向后兼容：仍可解析。
        Assert.True(registry.TryResolve(0, out var ep));
        Assert.Equal("ds-legacy", ep.DsId);
    }

    [Fact]
    public void TryResolve_RegionFiltered_MixedRegions_PicksBestOfRegion()
    {
        var clock = new Clock();
        var registry = CreateRegistry(clock);
        // 同一 shard：华东 ds-a（心跳旧）、华东 ds-b（心跳新）、华南 ds-c（心跳最新）。
        registry.RegisterOrUpdate("ds-a", new long[] { 0 }, "127.0.0.1", 5001, region: "华东");
        clock.Now = clock.Now.AddSeconds(30);
        registry.RegisterOrUpdate("ds-b", new long[] { 0 }, "127.0.0.1", 5002, region: "华东");
        clock.Now = clock.Now.AddSeconds(30);
        registry.RegisterOrUpdate("ds-c", new long[] { 0 }, "127.0.0.1", 5003, region: "华南");

        // 华东网关：应在华东 DS 中取心跳最新者（ds-b），绝不明路由到心跳最新但跨区的 ds-c。
        Assert.Equal(DsResolveStatus.Ok, registry.TryResolve(0, "华东", out var ep));
        Assert.Equal("ds-b", ep.DsId);
        Assert.Equal(5002, ep.Port);
    }

    [Fact]
    public void TryResolve_RegionFiltered_NoDsVsWrongRegion_Distinguished()
    {
        var clock = new Clock();
        var registry = CreateRegistry(clock);

        // shard 0：无任何注册 → NoDs。
        Assert.Equal(DsResolveStatus.NoDs, registry.TryResolve(0, "华东", out _));

        // shard 1：有注册但跨区 → WrongRegion。
        registry.RegisterOrUpdate("ds-south", new long[] { 1 }, "127.0.0.1", 5002, region: "华南");
        Assert.Equal(DsResolveStatus.WrongRegion, registry.TryResolve(1, "华东", out _));
        // 同区仍 Ok。
        Assert.Equal(DsResolveStatus.Ok, registry.TryResolve(1, "华南", out _));
    }

    [Fact]
    public void RegisterOrUpdate_WithRegion_AppearsInSnapshot()
    {
        var clock = new Clock();
        var registry = CreateRegistry(clock);
        registry.RegisterOrUpdate("ds-east", new long[] { 0 }, "127.0.0.1", 5001, region: "华东");
        registry.RegisterOrUpdate("ds-legacy", new long[] { 1 }, "127.0.0.1", 5002);

        var snapshot = registry.Snapshot();
        Assert.Contains(snapshot, e => e.DsId == "ds-east" && e.Region == "华东");
        Assert.Contains(snapshot, e => e.DsId == "ds-legacy" && e.Region == "");
    }

    [Fact]
    public void RestoreSnapshot_WithRegion_RegionPreserved_AndFiltered()
    {
        var clock = new Clock();
        var registry = CreateRegistry(clock);
        registry.RestoreSnapshot(new[]
        {
            new DsEndpointInfo("ds-east", new long[] { 0 }, "127.0.0.1", 5001, clock.Now.AddSeconds(-10), "华东"),
            new DsEndpointInfo("ds-south", new long[] { 0 }, "127.0.0.1", 5002, clock.Now.AddSeconds(-10), "华南"),
        });

        // 快照恢复的 region 参与路由过滤（华东网关只路由华东 DS）。
        Assert.Equal(DsResolveStatus.Ok, registry.TryResolve(0, "华东", out var ep));
        Assert.Equal("ds-east", ep.DsId);
        Assert.Equal(DsResolveStatus.WrongRegion, registry.TryResolve(0, "华北", out _));
    }

    // ── 阶段 6.3 P1⑥+：空 region 严格模式（未声明 region 的旧 DS 仅服务空 region 网关）──

    [Fact]
    public void TryResolve_StrictMode_EmptyRegionDs_ServesOnlyEmptyRegionGateway()
    {
        var clock = new Clock();
        var registry = CreateRegistry(clock);
        registry.RegisterOrUpdate("ds-legacy", new long[] { 0 }, "127.0.0.1", 5001); // 未声明 region

        // 严格 + 有 region 网关 → 空区 DS 被排除（WrongRegion：有注册但拓扑不可用，区别于 NoDs）。
        Assert.Equal(DsResolveStatus.WrongRegion, registry.TryResolve(0, "华东", strictEmptyRegion: true, out _));
        Assert.Equal(DsResolveStatus.WrongRegion, registry.TryResolve(0, "华南", strictEmptyRegion: true, out _));
        // 严格 + 空 region 网关（未设区）→ 过滤不生效，空区 DS 可服务。
        Assert.Equal(DsResolveStatus.Ok, registry.TryResolve(0, "", strictEmptyRegion: true, out _));
        Assert.Equal(DsResolveStatus.Ok, registry.TryResolve(0, null, strictEmptyRegion: true, out _));
        // 宽松 3 参重载与 2 参旧重载不受严格模式影响（向后兼容）。
        Assert.Equal(DsResolveStatus.Ok, registry.TryResolve(0, "华东", out _));
        Assert.True(registry.TryResolve(0, out var ep));
        Assert.Equal("ds-legacy", ep.DsId);
    }

    [Fact]
    public void TryResolve_StrictMode_ExcludesEmptyRegionDs_EvenWhenNewestHeartbeat()
    {
        var clock = new Clock();
        var registry = CreateRegistry(clock);
        registry.RegisterOrUpdate("ds-east", new long[] { 0 }, "127.0.0.1", 5002, region: "华东");
        clock.Now = clock.Now.AddSeconds(30);
        registry.RegisterOrUpdate("ds-south", new long[] { 0 }, "127.0.0.1", 5003, region: "华南");
        clock.Now = clock.Now.AddSeconds(30);
        registry.RegisterOrUpdate("ds-legacy", new long[] { 0 }, "127.0.0.1", 5001); // 空区，心跳最新

        // 宽松：空区 ds-legacy 不限区 → 按心跳最新被选中（旧语义）。
        Assert.Equal(DsResolveStatus.Ok, registry.TryResolve(0, "华东", out var lenient));
        Assert.Equal("ds-legacy", lenient.DsId);
        // 严格：空区 ds-legacy 被排除 → 取同区 ds-east（区域匹配优先于心跳新鲜度）。
        Assert.Equal(DsResolveStatus.Ok, registry.TryResolve(0, "华东", strictEmptyRegion: true, out var strict));
        Assert.Equal("ds-east", strict.DsId);
        // 严格 + 无同区 DS 的区域 → WrongRegion（空区被排除 = 拓扑问题，非容量问题）。
        Assert.Equal(DsResolveStatus.WrongRegion, registry.TryResolve(0, "华北", strictEmptyRegion: true, out _));
    }

    [Fact]
    public void RegisterOrUpdate_ThenResolve_ReturnsEndpointAndShards()
    {
        var clock = new Clock();
        var registry = CreateRegistry(clock);
        registry.RegisterOrUpdate("ds-1", new long[] { 0, 1 }, "127.0.0.1", 5001);

        Assert.True(registry.TryResolve(0, out var ep0));
        Assert.Equal("ds-1", ep0.DsId);
        Assert.Equal("127.0.0.1", ep0.Host);
        Assert.Equal(5001, ep0.Port);
        Assert.Contains(0L, ep0.ShardIds);
        Assert.Contains(1L, ep0.ShardIds);

        Assert.True(registry.TryResolve(1, out _));
        Assert.False(registry.TryResolve(2, out _));
    }

    [Fact]
    public void Resolve_NoEntry_ReturnsFalse()
    {
        var registry = CreateRegistry(new Clock());
        Assert.False(registry.TryResolve(0, out var ep));
        Assert.Null(ep);
    }

    [Fact]
    public void Heartbeat_RefreshesStaleness()
    {
        var clock = new Clock();
        var registry = CreateRegistry(clock);
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", 5001);

        // t+80s：仍在 90s 阈值内 → 可解析
        clock.Now = clock.Now.AddSeconds(80);
        Assert.True(registry.TryResolve(0, out _));

        // 心跳续约（t+80s）→ t+100s 仍有效（距上次心跳 20s）
        registry.Heartbeat("ds-1");
        clock.Now = clock.Now.AddSeconds(20);
        Assert.True(registry.TryResolve(0, out _));

        // t+175s：距续约心跳 95s > 90s → 失效
        clock.Now = clock.Now.AddSeconds(75);
        Assert.False(registry.TryResolve(0, out _));
    }

    [Fact]
    public void PruneStale_RemovesOnlyExpired()
    {
        var clock = new Clock();
        var registry = CreateRegistry(clock);
        registry.RegisterOrUpdate("ds-a", new long[] { 0 }, "127.0.0.1", 5001);
        clock.Now = clock.Now.AddSeconds(50);
        registry.RegisterOrUpdate("ds-b", new long[] { 1 }, "127.0.0.1", 5002);

        clock.Now = clock.Now.AddSeconds(50); // t+100s：ds-a 过期（100>90），ds-b 新鲜（50<90）
        var removed = registry.PruneStale();

        Assert.Equal(1, removed);
        Assert.False(registry.TryResolve(0, out _));
        Assert.True(registry.TryResolve(1, out _));
        Assert.Single(registry.Snapshot());
    }

    [Fact]
    public void Unregister_RemovesEntry()
    {
        var registry = CreateRegistry(new Clock());
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", 5001);
        Assert.True(registry.TryResolve(0, out _));

        registry.Unregister("ds-1");

        Assert.False(registry.TryResolve(0, out _));
        Assert.Empty(registry.Snapshot());
    }

    [Fact]
    public void RegisterOrUpdate_SameDsId_UpsertsShardSet()
    {
        var registry = CreateRegistry(new Clock());
        registry.RegisterOrUpdate("ds-1", new long[] { 0, 1 }, "127.0.0.1", 5001);
        registry.RegisterOrUpdate("ds-1", new long[] { 2 }, "127.0.0.1", 5001);

        Assert.False(registry.TryResolve(0, out _));
        Assert.False(registry.TryResolve(1, out _));
        Assert.True(registry.TryResolve(2, out var ep));
        Assert.Single(ep.ShardIds);
    }

    [Fact]
    public void TryResolve_MultipleDsSameShard_PicksLatestHeartbeat()
    {
        var clock = new Clock();
        var registry = CreateRegistry(clock);
        registry.RegisterOrUpdate("ds-a", new long[] { 0 }, "127.0.0.1", 5001);
        clock.Now = clock.Now.AddSeconds(10);
        registry.RegisterOrUpdate("ds-b", new long[] { 0 }, "127.0.0.1", 5002);

        Assert.True(registry.TryResolve(0, out var ep));
        Assert.Equal("ds-b", ep.DsId); // 心跳最新者
    }

    [Fact]
    public void Register_InvalidArgs_Throws()
    {
        var registry = CreateRegistry(new Clock());

        Assert.Throws<ArgumentException>(() => registry.RegisterOrUpdate("", new long[] { 0 }, "127.0.0.1", 5001));
        Assert.Throws<ArgumentNullException>(() => registry.RegisterOrUpdate("ds-1", null!, "127.0.0.1", 5001));
        Assert.Throws<ArgumentException>(() => registry.RegisterOrUpdate("ds-1", new long[] { 0 }, " ", 5001));
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", 70000));
    }

    [Fact]
    public void RestoreSnapshot_FreshEntry_ImmediatelyResolvable_WithoutHeartbeat()
    {
        var clock = new Clock();
        var registry = CreateRegistry(clock);
        // 模拟快照：DS 最后一次心跳是 10s 前（仍新鲜）——网关重启后无需等下一轮心跳即可路由。
        var snapshot = new[]
        {
            new DsEndpointInfo("ds-1", new long[] { 0, 1 }, "127.0.0.1", 5001, clock.Now.AddSeconds(-10)),
        };

        registry.RestoreSnapshot(snapshot);

        Assert.True(registry.TryResolve(0, out var ep));
        Assert.Equal("ds-1", ep.DsId);
        Assert.True(registry.TryResolve(1, out _));
        Assert.False(registry.TryResolve(2, out _));
        // 心跳时间以快照存储值为准（未被刷新为当前时间）。
        Assert.Equal(clock.Now.AddSeconds(-10), ep.LastHeartbeatUtc);
    }

    [Fact]
    public void RestoreSnapshot_StaleEntry_InertUntilHeartbeat_ThenPruned()
    {
        var clock = new Clock();
        var registry = CreateRegistry(clock);
        // 快照条目心跳已过期（100s 前 > 90s 阈值）：恢复后不可路由（inert），心跳续约后恢复路由。
        registry.RestoreSnapshot(new[]
        {
            new DsEndpointInfo("ds-1", new long[] { 0 }, "127.0.0.1", 5001, clock.Now.AddSeconds(-100)),
        });

        Assert.False(registry.TryResolve(0, out _)); // inert：staleness 检查排除
        Assert.Equal(0, registry.CountAlive());

        registry.Heartbeat("ds-1");
        Assert.True(registry.TryResolve(0, out _));

        // 未续约的过期条目在下一轮 PruneStale 被摘除。
        registry.RestoreSnapshot(new[]
        {
            new DsEndpointInfo("ds-2", new long[] { 1 }, "127.0.0.1", 5002, clock.Now.AddSeconds(-100)),
        });
        Assert.Equal(1, registry.PruneStale());
        Assert.False(registry.TryResolve(1, out _));
    }
}
