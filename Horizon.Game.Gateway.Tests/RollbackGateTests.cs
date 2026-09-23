using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core.Observability;
using Horizon.Game.Gateway.Configuration;
using Horizon.Game.Gateway.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 6.3 P2 — 自动回滚门测试。
/// <para>
/// 覆盖：coordinator 状态机（Arm/Clear/冷却/开关感知）、判定宿主（连续坏窗口触发、
/// 冷却期不回切、冷却后连续健康窗口自愈、根因复发重 Arm、零流量防误报、单窗抖动不触发）、
/// sampler（MeterListener 累积/窗口重置）、plane↔coordinator wiring（override 生效强制回切）。
/// </para>
/// <para>
/// 本类记录 <c>horizon.sync.ds.route.*</c> / <c>horizon.sync.ds.rollbacks.*</c> 静态仪表，
/// 与 DsRoutingMetricsTests 等同属 <see cref="SyncMetricsIsolatedCollection"/> 串行集合。
/// </para>
/// </summary>
[Collection(SyncMetricsIsolatedCollection.Name)]
public class RollbackGateTests
{
    // ── 固定时钟与 fake sampler ────────────────────────────────────────

    private sealed class Clock
    {
        public DateTimeOffset Now = new(2026, 8, 11, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeSampler : IRollbackWindowSampler
    {
        public RollbackWindow Next { get; set; }
        public int EnsureWatchingCalls;
        public int StopCalls;

        public void EnsureWatching() => Interlocked.Increment(ref EnsureWatchingCalls);
        public Task<RollbackWindow> SampleWindowAsync() => Task.FromResult(Next);
        public void Stop() => Interlocked.Increment(ref StopCalls);
    }

    private static GatewayOptions Options(
        bool useDS = true,
        int? windowSeconds = null,
        int? minAttempts = null,
        double? fallbackRate = null,
        double? latencyMs = null,
        long? correctionsPerWindow = null,
        int? badWindows = null,
        int? healthyWindows = null,
        int? cooldownSeconds = null)
        => new()
        {
            UseDS = useDS,
            DsRollback = new GatewayOptions.DsRollbackOptions
            {
                WindowSeconds = windowSeconds ?? 30,
                MinAttemptsPerWindow = minAttempts ?? 10,
                FallbackRateThreshold = fallbackRate ?? 0.5,
                LatencyMsThreshold = latencyMs ?? 1000,
                CorrectionsPerWindowThreshold = correctionsPerWindow ?? 0,
                ConsecutiveBadWindowsToTrigger = badWindows ?? 2,
                ConsecutiveHealthyWindowsToRecover = healthyWindows ?? 3,
                OverrideCooldownSeconds = cooldownSeconds ?? 60,
            },
        };

    private static (RollbackGateHostedService Service, RollbackCoordinator Coordinator, FakeSampler Sampler, Clock Clock)
        CreateGate(Clock? clock = null, GatewayOptions? options = null)
    {
        var c = clock ?? new Clock();
        var monitor = new MutableOptionsMonitor(options ?? Options());
        var coordinator = new RollbackCoordinator(monitor, () => c.Now);
        var sampler = new FakeSampler();
        var service = new RollbackGateHostedService(
            coordinator, sampler, monitor, NullLogger<RollbackGateHostedService>.Instance);
        return (service, coordinator, sampler, c);
    }

    // ── coordinator 状态机 ─────────────────────────────────────────────

    [Fact]
    public void Coordinator_ArmThenClear_CooldownExpiry_Flow()
    {
        var clock = new Clock();
        var monitor = new MutableOptionsMonitor(Options(useDS: true, cooldownSeconds: 60));
        var coordinator = new RollbackCoordinator(monitor, () => clock.Now);

        Assert.False(coordinator.IsRollbackActive);
        Assert.False(coordinator.IsOverrideArmed);

        coordinator.Arm();
        Assert.True(coordinator.IsOverrideArmed);
        Assert.True(coordinator.IsRollbackActive); // 冷却期内强制回切

        // 冷却未到期：即使时钟推进 59s 仍 active。
        clock.Now = clock.Now.AddSeconds(59);
        Assert.True(coordinator.IsRollbackActive);

        // 冷却到期：放行评估恢复（IsRollbackActive=false），但 override 仍 armed（待判定宿主 Clear）。
        clock.Now = clock.Now.AddSeconds(2);
        Assert.False(coordinator.IsRollbackActive);
        Assert.True(coordinator.IsOverrideArmed);

        coordinator.Clear();
        Assert.False(coordinator.IsOverrideArmed);
        Assert.False(coordinator.IsRollbackActive);
    }

    [Fact]
    public void Coordinator_Arm_WhenUseDSFalse_IsNoOp()
    {
        var clock = new Clock();
        var monitor = new MutableOptionsMonitor(Options(useDS: false));
        var coordinator = new RollbackCoordinator(monitor, () => clock.Now);

        coordinator.Arm();
        Assert.False(coordinator.IsOverrideArmed); // 开关本已关闭：override 无意义
        Assert.False(coordinator.IsRollbackActive);
    }

    [Fact]
    public void Coordinator_UseDSToggledOff_WhileArmed_BecomesInactive()
    {
        var clock = new Clock();
        var monitor = new MutableOptionsMonitor(Options(useDS: true));
        var coordinator = new RollbackCoordinator(monitor, () => clock.Now);

        coordinator.Arm();
        Assert.True(coordinator.IsRollbackActive);

        // 运维热关 UseDS：override 不应阻碍（且避免「关→开」时被过期 override 卡死）。
        monitor.CurrentValue = Options(useDS: false);
        Assert.False(coordinator.IsRollbackActive);
        Assert.True(coordinator.IsOverrideArmed); // 但 override 本身仍在，热开后会重新生效

        // 热开：override 仍在冷却期内 → 立即重新生效（而非卡死/误放行）。
        monitor.CurrentValue = Options(useDS: true);
        Assert.True(coordinator.IsRollbackActive);
    }

    // ── 判定宿主：连续窗口 / 冷却 / 自愈 ───────────────────────────────

    [Fact]
    public void Gate_SingleBadWindow_DoesNotTrigger()
    {
        var (service, coordinator, sampler, _) = CreateGate();
        var bad = new RollbackWindow(Attempts: 100, Fallbacks: 100, Corrections: 0, LatencySumMs: 5000, LatencyCount: 100);

        service.EvaluateWindowForTest(bad, Options());
        Assert.False(coordinator.IsOverrideArmed); // 1/2 坏窗口：未触发
        Assert.StartsWith("bad(", service.LastDecision);
    }

    [Fact]
    public void Gate_ConsecutiveBadWindows_TriggersRollback_ResetsCounter()
    {
        using var capture = new SyncMetricsCapture();
        var (service, coordinator, sampler, _) = CreateGate();
        var bad = new RollbackWindow(Attempts: 100, Fallbacks: 90, Corrections: 0, LatencySumMs: 500, LatencyCount: 100);

        service.EvaluateWindowForTest(bad, Options());
        service.EvaluateWindowForTest(bad, Options()); // 第 2 个连续坏窗口 → 触发

        Assert.True(coordinator.IsOverrideArmed);
        Assert.True(coordinator.IsRollbackActive);
        lock (capture.Counters)
        {
            Assert.Contains(capture.Counters, m => m.Name == "horizon.sync.ds.rollbacks.triggered");
        }

        // 触发后坏计数归零：冷却期内再来 1 个坏窗口不重 Arm（不刷新冷却），override 保持生效。
        service.EvaluateWindowForTest(bad, Options());
        Assert.True(coordinator.IsOverrideArmed);
        Assert.True(coordinator.IsRollbackActive);
        Assert.StartsWith("bad(", service.LastDecision);
    }

    [Fact]
    public void Gate_LatencyThreshold_Alone_Triggers()
    {
        var (service, coordinator, sampler, _) = CreateGate();
        var slow = new RollbackWindow(Attempts: 100, Fallbacks: 0, Corrections: 0, LatencySumMs: 500_000, LatencyCount: 100); // 均值 5000ms

        service.EvaluateWindowForTest(slow, Options());
        service.EvaluateWindowForTest(slow, Options());

        Assert.True(coordinator.IsOverrideArmed); // 回退率 0 但延迟超阈 → 触发
    }

    // ── 修正率信号（阶段 6.3 P2：跨进程汇聚的 horizon.sync.corrections）──

    [Fact]
    public void Gate_CorrectionsThreshold_Alone_Triggers()
    {
        using var capture = new SyncMetricsCapture();
        var (service, coordinator, sampler, _) = CreateGate(options: Options(correctionsPerWindow: 50));
        // 修正信号独立于网关路由量：attempts=0（<10 流量不足）但修正超阈 → 窗口参与判定。
        var highCorrections = new RollbackWindow(Attempts: 0, Fallbacks: 0, Corrections: 100, LatencySumMs: 0, LatencyCount: 0);

        service.EvaluateWindowForTest(highCorrections, Options(correctionsPerWindow: 50));
        Assert.False(coordinator.IsOverrideArmed); // 1/2 坏窗口
        Assert.StartsWith("bad(", service.LastDecision);

        service.EvaluateWindowForTest(highCorrections, Options(correctionsPerWindow: 50));
        Assert.True(coordinator.IsOverrideArmed); // 连续 2 窗修正超阈 → 触发
        Assert.True(coordinator.IsRollbackActive);
        lock (capture.Counters)
        {
            Assert.Contains(capture.Counters, m => m.Name == "horizon.sync.ds.rollbacks.triggered");
        }
    }

    [Fact]
    public void Gate_CorrectionsBelowThreshold_IsHealthy()
    {
        var (service, coordinator, sampler, _) = CreateGate(options: Options(correctionsPerWindow: 50));
        var normal = new RollbackWindow(Attempts: 100, Fallbacks: 0, Corrections: 30, LatencySumMs: 500, LatencyCount: 100);

        service.EvaluateWindowForTest(normal, Options(correctionsPerWindow: 50));
        Assert.False(coordinator.IsOverrideArmed);
        Assert.StartsWith("healthy", service.LastDecision);
    }

    [Fact]
    public void Gate_CorrectionsThreshold_Zero_DisablesCorrectionSignal()
    {
        // 默认 0 = 禁用：修正信号不参与判定（attempts 不足即 insufficient_traffic，哪怕修正量巨大）。
        var (service, coordinator, sampler, _) = CreateGate(); // 默认 threshold=0
        var hugeCorrectionsNoTraffic = new RollbackWindow(Attempts: 0, Fallbacks: 0, Corrections: 10_000, LatencySumMs: 0, LatencyCount: 0);

        service.EvaluateWindowForTest(hugeCorrectionsNoTraffic, Options());
        Assert.False(coordinator.IsOverrideArmed);
        Assert.StartsWith("insufficient_traffic", service.LastDecision);
    }

    [Fact]
    public void Gate_CorrectionsTrigger_ThenHealthyRecovers_AfterCooldown()
    {
        var clock = new Clock();
        var (service, coordinator, sampler, _) = CreateGate(clock, Options(correctionsPerWindow: 50));
        var high = new RollbackWindow(Attempts: 100, Fallbacks: 0, Corrections: 80, LatencySumMs: 500, LatencyCount: 100);
        var healthy = new RollbackWindow(Attempts: 100, Fallbacks: 0, Corrections: 10, LatencySumMs: 500, LatencyCount: 100);

        service.EvaluateWindowForTest(high, Options(correctionsPerWindow: 50));
        service.EvaluateWindowForTest(high, Options(correctionsPerWindow: 50)); // 触发
        Assert.True(coordinator.IsOverrideArmed);

        // 冷却到期 → 连续 3 个健康窗口 → 自愈（修正率回落）。
        clock.Now = clock.Now.AddSeconds(61);
        service.EvaluateWindowForTest(healthy, Options(correctionsPerWindow: 50));
        service.EvaluateWindowForTest(healthy, Options(correctionsPerWindow: 50));
        service.EvaluateWindowForTest(healthy, Options(correctionsPerWindow: 50));
        Assert.False(coordinator.IsOverrideArmed);
    }

    [Fact]
    public void Gate_CooldownPeriod_RollbackStaysActive_NoEarlyRecovery()
    {
        var clock = new Clock();
        var (service, coordinator, sampler, _) = CreateGate(clock, Options(cooldownSeconds: 60));
        var bad = new RollbackWindow(Attempts: 100, Fallbacks: 90, Corrections: 0, LatencySumMs: 500, LatencyCount: 100);
        var healthy = new RollbackWindow(Attempts: 100, Fallbacks: 0, Corrections: 0, LatencySumMs: 500, LatencyCount: 100);

        service.EvaluateWindowForTest(bad, Options(cooldownSeconds: 60));
        service.EvaluateWindowForTest(bad, Options(cooldownSeconds: 60)); // 触发

        // 冷却期内：即使窗口健康也不允许评估恢复（IsRollbackActive 恒 true，判定宿主在冷却期不推进健康计数）。
        clock.Now = clock.Now.AddSeconds(30);
        service.EvaluateWindowForTest(healthy, Options(cooldownSeconds: 60));
        Assert.True(coordinator.IsOverrideArmed);
        Assert.True(coordinator.IsRollbackActive);
    }

    [Fact]
    public void Gate_AfterCooldown_ConsecutiveHealthyWindows_Recovers()
    {
        var clock = new Clock();
        var (service, coordinator, sampler, _) = CreateGate(clock, Options());
        var bad = new RollbackWindow(Attempts: 100, Fallbacks: 90, Corrections: 0, LatencySumMs: 500, LatencyCount: 100);
        var healthy = new RollbackWindow(Attempts: 100, Fallbacks: 0, Corrections: 0, LatencySumMs: 500, LatencyCount: 100);

        service.EvaluateWindowForTest(bad, Options());
        service.EvaluateWindowForTest(bad, Options()); // 触发，冷却 60s

        // 冷却期内的健康窗口不推进恢复（override 仍在冷却中，不得提前恢复）。
        clock.Now = clock.Now.AddSeconds(30);
        service.EvaluateWindowForTest(healthy, Options());
        Assert.True(coordinator.IsOverrideArmed);
        Assert.StartsWith("healthy", service.LastDecision); // 冷却期健康窗口不计数

        // 冷却到期（t+61s）→ 连续 3 个健康窗口 → Clear 自愈。
        clock.Now = clock.Now.AddSeconds(31);
        service.EvaluateWindowForTest(healthy, Options());
        Assert.True(coordinator.IsOverrideArmed); // 1/3
        Assert.StartsWith("recovering(1/3)", service.LastDecision);
        service.EvaluateWindowForTest(healthy, Options());
        Assert.True(coordinator.IsOverrideArmed); // 2/3
        service.EvaluateWindowForTest(healthy, Options());
        Assert.False(coordinator.IsOverrideArmed); // 3/3 → 恢复
        Assert.StartsWith("recovering(3/3)", service.LastDecision);
    }

    [Fact]
    public void Gate_RelapseDuringRecovery_ReArms_FreshCooldown()
    {
        var clock = new Clock();
        var (service, coordinator, sampler, _) = CreateGate(clock, Options());
        var bad = new RollbackWindow(Attempts: 100, Fallbacks: 90, Corrections: 0, LatencySumMs: 500, LatencyCount: 100);
        var healthy = new RollbackWindow(Attempts: 100, Fallbacks: 0, Corrections: 0, LatencySumMs: 500, LatencyCount: 100);

        service.EvaluateWindowForTest(bad, Options());
        service.EvaluateWindowForTest(bad, Options()); // 触发
        clock.Now = clock.Now.AddSeconds(61); // 冷却到期

        service.EvaluateWindowForTest(healthy, Options()); // 1 个健康窗口（冷却已过，开始计数）
        Assert.True(coordinator.IsOverrideArmed);
        Assert.StartsWith("recovering(1/3)", service.LastDecision);

        // 根因复发：恢复评估期出现坏窗口 → 立即重 Arm（刷新冷却），恢复计数清零。
        service.EvaluateWindowForTest(bad, Options());
        Assert.True(coordinator.IsOverrideArmed);
        Assert.True(coordinator.IsRollbackActive); // 新冷却立即生效
        Assert.StartsWith("relapse", service.LastDecision);

        // 冷却期内健康窗口不计数；到期后连续 3 个健康窗口 → 恢复。
        clock.Now = clock.Now.AddSeconds(30);
        service.EvaluateWindowForTest(healthy, Options());
        Assert.StartsWith("healthy", service.LastDecision); // 冷却期内不计数
        clock.Now = clock.Now.AddSeconds(31);
        service.EvaluateWindowForTest(healthy, Options());
        service.EvaluateWindowForTest(healthy, Options());
        service.EvaluateWindowForTest(healthy, Options());
        Assert.False(coordinator.IsOverrideArmed); // 新冷却后 3 个健康窗口 → 恢复
    }

    [Fact]
    public void Gate_InsufficientTraffic_DoesNotMoveCounters()
    {
        var (service, coordinator, sampler, _) = CreateGate();
        var low = new RollbackWindow(Attempts: 3, Fallbacks: 3, Corrections: 0, LatencySumMs: 3000, LatencyCount: 3); // 100% 回退但 <10 尝试

        service.EvaluateWindowForTest(low, Options());
        Assert.False(coordinator.IsOverrideArmed);
        Assert.StartsWith("insufficient_traffic", service.LastDecision);

        // 低流量窗口不消耗坏窗口计数：后续连续 2 个足量坏窗口仍能触发（计数未被低流量窗口污染）。
        var bad = new RollbackWindow(Attempts: 100, Fallbacks: 90, Corrections: 0, LatencySumMs: 500, LatencyCount: 100);
        service.EvaluateWindowForTest(bad, Options());
        service.EvaluateWindowForTest(bad, Options());
        Assert.True(coordinator.IsOverrideArmed);
    }

    [Fact]
    public void Gate_UseDSFalse_ZeroSideEffects()
    {
        var (service, coordinator, sampler, _) = CreateGate(options: Options(useDS: false));
        var bad = new RollbackWindow(Attempts: 100, Fallbacks: 100, Corrections: 0, LatencySumMs: 9999, LatencyCount: 100);

        service.EvaluateWindowForTest(bad, Options(useDS: false));
        service.EvaluateWindowForTest(bad, Options(useDS: false));

        // 开关关闭：数据面无尝试，理论上不会有窗口数据；即便有，Arm 也是 no-op（coordinator 自门控）。
        Assert.False(coordinator.IsOverrideArmed);
        Assert.False(coordinator.IsRollbackActive);
    }

    // ── sampler：MeterListener 累积 / 窗口重置 ─────────────────────────

    [Fact]
    public async Task Sampler_AccumulatesRealMeterEvents_WindowReset_Works()
    {
        using var capture = new SyncMetricsCapture(); // 保持监听避免仪表被回收
        var sampler = new RollbackWindowSampler(NullLogger<RollbackWindowSampler>.Instance);
        sampler.EnsureWatching();

        // 手动打仪表增量（真实静态仪表 + 真实 MeterListener 路径）。
        SyncMetrics.DsRouteAttempts.Add(40);
        SyncMetrics.DsRouteFallback.Add(10, new KeyValuePair<string, object?>("reason", "timeout"));
        SyncMetrics.DsRouteLatencyMs.Record(200);
        SyncMetrics.DsRouteLatencyMs.Record(300);

        var w1 = await sampler.SampleWindowAsync();
        Assert.Equal(40, w1.Attempts);
        Assert.Equal(10, w1.Fallbacks);
        Assert.Equal(0.25, w1.FallbackRate, 3);
        Assert.Equal(2, w1.LatencyCount);
        Assert.Equal(250, w1.MeanLatencyMs, 3);
        Assert.Equal(0, w1.Corrections); // 未注入 store：修正信号恒 0

        // 窗口消费即重置：无新事件时下一窗口为空。
        var w2 = await sampler.SampleWindowAsync();
        Assert.Equal(0, w2.Attempts);
        Assert.Equal(0, w2.Fallbacks);
        Assert.Equal(0, w2.LatencyCount);

        // 新事件进入下一窗口（不串窗）。
        SyncMetrics.DsRouteAttempts.Add(5);
        var w3 = await sampler.SampleWindowAsync();
        Assert.Equal(5, w3.Attempts);

        sampler.Stop();
    }

    // ── plane ↔ coordinator wiring ─────────────────────────────────────

    [Fact]
    public async Task Plane_CoordinatorOverride_ForceDisablesRouting_EvenWhenUseDSTrue()
    {
        var clock = new Clock();
        var monitor = new MutableOptionsMonitor(Options(useDS: true));
        var coordinator = new RollbackCoordinator(monitor, () => clock.Now);
        var registry = new DSRegistry();
        registry.RegisterOrUpdate("ds-1", new long[] { 0 }, "127.0.0.1", 1); // 端口 1：若触碰必然失败
        var plane = new DsSyncDataPlane(
            registry, NullLogger<DsSyncDataPlane>.Instance, monitor, rollbackCoordinator: coordinator);

        Assert.True(plane.IsEnabled); // 无 override：正常

        coordinator.Arm();
        Assert.False(plane.IsEnabled); // override 生效：强制回切（即使 UseDS=true）
        Assert.False(await plane.TryRouteAsync(0, 42, new byte[4])); // 短路，不触碰网络

        clock.Now = clock.Now.AddSeconds(61); // 冷却到期
        Assert.True(plane.IsEnabled); // 放行评估恢复

        coordinator.Clear();
        Assert.True(plane.IsEnabled);
    }

    [Fact]
    public void Plane_CoordinatorNull_BehavesAsBefore()
    {
        var monitor = new MutableOptionsMonitor(Options(useDS: true));
        var plane = new DsSyncDataPlane(new DSRegistry(), NullLogger<DsSyncDataPlane>.Instance, monitor);
        Assert.True(plane.IsEnabled);
    }
}
