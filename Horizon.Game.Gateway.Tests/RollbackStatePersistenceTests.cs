using System;
using Horizon.Game.Gateway.Configuration;
using Horizon.Game.Gateway.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 6.3 P2 — 自动回滚门 override 跨网关重启持久化测试。
/// <para>
/// 覆盖：Arm 持久化 → 新实例（模拟重启）恢复续切（冷却内/冷却外）、Clear 删除、
/// 写失败降级（内存态仍权威）、读失败干净启动、UseDS 开关门控、TTL 计算、
/// Redis 键格式（重启后恢复依赖键一致）与空连接串禁用。
/// </para>
/// <para>本类不记录静态仪表（coordinator 直接驱动），无需串行集合。</para>
/// </summary>
public class RollbackStatePersistenceTests
{
    private sealed class Clock
    {
        public DateTimeOffset Now = new(2026, 8, 11, 0, 0, 0, TimeSpan.Zero);
    }

    /// <summary>内存版持久化存储（代理 Redis；可注入读写故障）。</summary>
    private sealed class MemoryRollbackStateStore : IRollbackStateStore
    {
        public RollbackPersistedState? Stored;
        public TimeSpan? LastTtl;
        public int DeleteCalls;
        public bool FailWrite;
        public bool FailRead;

        public RollbackPersistedState? Read()
        {
            if (FailRead) throw new InvalidOperationException("模拟存储读故障");
            return Stored;
        }

        public void Write(RollbackPersistedState state, TimeSpan ttl)
        {
            if (FailWrite) throw new InvalidOperationException("模拟存储写故障");
            Stored = state;
            LastTtl = ttl;
        }

        public void Delete()
        {
            DeleteCalls++;
            Stored = null;
        }
    }

    private static GatewayOptions Options(bool useDS = true, int? cooldownSeconds = null)
        => new()
        {
            UseDS = useDS,
            DsRollback = new GatewayOptions.DsRollbackOptions
            {
                WindowSeconds = 30,
                MinAttemptsPerWindow = 10,
                FallbackRateThreshold = 0.5,
                LatencyMsThreshold = 1000,
                CorrectionsPerWindowThreshold = 0,
                ConsecutiveBadWindowsToTrigger = 2,
                ConsecutiveHealthyWindowsToRecover = 3,
                OverrideCooldownSeconds = cooldownSeconds ?? 60,
            },
        };

    // ── 重启恢复：Arm 持久化 → 新实例续切 ────────────────────────────────

    [Fact]
    public void Arm_Persists_NewInstanceRestores_ResumesRollback()
    {
        var clock = new Clock();
        var store = new MemoryRollbackStateStore();
        var monitor = new MutableOptionsMonitor(Options(cooldownSeconds: 60));
        var coordinator1 = new RollbackCoordinator(monitor, () => clock.Now, store);

        coordinator1.Arm();
        Assert.True(coordinator1.IsOverrideArmed);
        Assert.True(store.Stored is { Armed: true }); // 已持久化
        Assert.True(store.LastTtl >= TimeSpan.FromSeconds(120)); // 剩余冷却 60s + 余量 60s

        // 模拟网关重启：同 store 新建协调器（同一时钟）→ 恢复续切。
        var coordinator2 = new RollbackCoordinator(monitor, () => clock.Now, store);
        Assert.True(coordinator2.IsOverrideArmed);
        Assert.True(coordinator2.IsRollbackActive); // 冷却未过 → 继续强制回切

        // 冷却到期 → 放行评估恢复（armed 保持，等待判定宿主）。
        clock.Now = clock.Now.AddSeconds(61);
        Assert.False(coordinator2.IsRollbackActive);
        Assert.True(coordinator2.IsOverrideArmed);
    }

    [Fact]
    public void Restore_ExpiredCooldown_ArmedButInactive_ClearDeletes()
    {
        var clock = new Clock();
        var store = new MemoryRollbackStateStore
        {
            Stored = new RollbackPersistedState(true, clock.Now.AddSeconds(-10)), // 冷却已过
        };

        var coordinator = new RollbackCoordinator(new MutableOptionsMonitor(Options()), () => clock.Now, store);
        Assert.True(coordinator.IsOverrideArmed); // 恢复 armed（进入健康评估语义）
        Assert.False(coordinator.IsRollbackActive); // 冷却已过 → 放行

        coordinator.Clear();
        Assert.False(coordinator.IsOverrideArmed);
        Assert.Null(store.Stored); // 持久化键已删
    }

    [Fact]
    public void Clear_DeletesPersistedState()
    {
        var clock = new Clock();
        var store = new MemoryRollbackStateStore();
        var coordinator = new RollbackCoordinator(new MutableOptionsMonitor(Options()), () => clock.Now, store);

        coordinator.Arm();
        Assert.NotNull(store.Stored);

        coordinator.Clear();
        Assert.False(coordinator.IsOverrideArmed);
        Assert.Null(store.Stored);
        Assert.True(store.DeleteCalls >= 1);
    }

    [Fact]
    public void RestoredOverride_RespectsUseDSGate_HotReEnable_Reapplies()
    {
        var clock = new Clock();
        var store = new MemoryRollbackStateStore
        {
            Stored = new RollbackPersistedState(true, clock.Now.AddSeconds(30)),
        };
        var monitor = new MutableOptionsMonitor(Options(useDS: false)); // 重启时开关已关（运维手动处置）

        var coordinator = new RollbackCoordinator(monitor, () => clock.Now, store);
        Assert.True(coordinator.IsOverrideArmed);
        Assert.False(coordinator.IsRollbackActive); // UseDS=false：不强制回切

        // 热开：恢复的 override 立即重新生效（与内存态 armed 的热开语义一致）。
        monitor.CurrentValue = Options(useDS: true);
        Assert.True(coordinator.IsRollbackActive);
    }

    // ── 失败降级 ────────────────────────────────────────────────────────

    [Fact]
    public void PersistWriteFailure_StillArmsInMemory()
    {
        var clock = new Clock();
        var store = new MemoryRollbackStateStore { FailWrite = true };
        var coordinator = new RollbackCoordinator(new MutableOptionsMonitor(Options()), () => clock.Now, store);

        coordinator.Arm(); // 写失败：不抛、不阻断回滚
        Assert.True(coordinator.IsOverrideArmed);
        Assert.True(coordinator.IsRollbackActive);
        Assert.Null(store.Stored); // 未持久化（跨重启不保证，但本进程生效）
    }

    [Fact]
    public void RestoreReadFailure_CleanStart()
    {
        var clock = new Clock();
        var store = new MemoryRollbackStateStore { FailRead = true, Stored = new RollbackPersistedState(true, clock.Now.AddSeconds(30)) };

        var coordinator = new RollbackCoordinator(new MutableOptionsMonitor(Options()), () => clock.Now, store);
        Assert.False(coordinator.IsOverrideArmed); // 读失败：干净启动（门重新评估）
        Assert.False(coordinator.IsRollbackActive);
    }

    [Fact]
    public void NullStore_BehavesAsBefore_NoPersistence()
    {
        var clock = new Clock();
        var coordinator = new RollbackCoordinator(new MutableOptionsMonitor(Options()), () => clock.Now);
        coordinator.Arm();
        Assert.True(coordinator.IsRollbackActive);
        coordinator.Clear();
        Assert.False(coordinator.IsOverrideArmed);
    }

    // ── 数据面 wiring：恢复的 override 真正阻断 DS 路由 ──────────────────

    [Fact]
    public async System.Threading.Tasks.Task RestoredOverride_BlocksDsRouting_AfterRestart()
    {
        var clock = new Clock();
        var store = new MemoryRollbackStateStore();
        var monitor1 = new MutableOptionsMonitor(Options());
        var coordinator1 = new RollbackCoordinator(monitor1, () => clock.Now, store);
        coordinator1.Arm(); // 重启前触发回切并持久化

        // 模拟重启：新协调器（恢复 override）+ 新数据面（同一注册表/开关）。
        var monitor2 = new MutableOptionsMonitor(Options(useDS: true));
        var coordinator2 = new RollbackCoordinator(monitor2, () => clock.Now, store);
        var registry = new DSRegistry();
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", 1); // 端口 1：若触碰必然失败
        var plane = new DsSyncDataPlane(registry, NullLogger<DsSyncDataPlane>.Instance, monitor2, rollbackCoordinator: coordinator2);

        Assert.True(coordinator2.IsRollbackActive); // 恢复续切
        Assert.False(plane.IsEnabled); // 强制回切：即使 UseDS=true 也不路由 DS
        Assert.False(await plane.TryRouteAsync(0, 42, new byte[4])); // 短路，不触碰网络

        // 冷却到期 → 放行评估恢复。
        clock.Now = clock.Now.AddSeconds(61);
        Assert.True(plane.IsEnabled);
    }

    // ── Redis 存储 ──────────────────────────────────────────────────────

    [Fact]
    public void RedisStore_KeyFormat_MustMatchCoordinatorWrite()
    {
        Assert.Equal("cluster:dev:ds_rollback_override:TYMYD-Gateway-001",
            RedisRollbackStateStore.GetKey("dev", "TYMYD-Gateway-001"));
        Assert.Equal("cluster:prod-1:ds_rollback_override:g2",
            RedisRollbackStateStore.GetKey("prod-1", "g2"));
    }

    [Fact]
    public void RedisStore_EmptyConnectionString_Disabled_NoRedisTouch()
    {
        var store = new RedisRollbackStateStore("", "dev", "g1");
        Assert.Null(store.Read()); // 禁用：不触碰 Redis
        store.Write(new RollbackPersistedState(true, DateTimeOffset.UtcNow.AddSeconds(30)), TimeSpan.FromSeconds(120));
        store.Delete(); // 空操作不抛
    }
}
