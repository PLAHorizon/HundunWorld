using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Game.Core.Configuration;
using Horizon.Game.Core.Sim.Server;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// Task D.7.4：带宽预算守门单元测试。
/// <para>
/// 验证 <see cref="GatewaySyncDispatcher"/> 的 per-session 带宽计数器、100kbps 阈值限流降频
/// （20Hz→10Hz）、连续 3 秒低于阈值回升、告警日志不重复 spam。
/// </para>
/// <para>
/// 测试策略：直接实例化 <see cref="GatewaySyncDispatcher.SessionBandwidthTracker"/> 公共嵌套类，
/// 通过 <c>RecordBytes(bytes, nowUtc, owner)</c> 注入受控时间戳，避免依赖真实系统时钟。
/// 窗口间隔使用 1.5 秒（而非刚好 1.0 秒）以避免 TimeSpan 比较的浮点边界问题。
/// </para>
/// </summary>
public class BandwidthBudgetTests
{
    // 窗口间隔 1.5 秒，确保 elapsed > 1.0s 触发窗口滚动。
    private const double WindowIntervalSeconds = 1.5;

    /// <summary>
    /// 计算在指定窗口秒数下超过指定 kbps 阈值所需的最小字节数。
    /// kbps = bytes * 8 / 1024 / seconds > threshold → bytes > threshold * 1024 * seconds / 8
    /// </summary>
    private static int BytesForKbps(double kbps, double seconds) => (int)(kbps * 1024 * seconds / 8) + 1000;

    /// <summary>
    /// 创建带捕获日志的 GatewaySyncDispatcher 实例。
    /// 依赖接口（source/registry/sink）用 Mock 占位，不参与带宽测试逻辑。
    /// </summary>
    private static (GatewaySyncDispatcher dispatcher, CapturingLogger logger) CreateDispatcher(
        double thresholdKbps = 100.0,
        int normalHz = 20,
        int throttledHz = 10,
        int degradedHz = 5,
        int recoverySeconds = 3)
    {
        var source = new Mock<IZoneShardFanoutSource>();
        var registry = new Mock<ISessionRegistry>();
        var sink = new Mock<IClientPacketSink>();
        var logger = new CapturingLogger();
        var dispatcher = new GatewaySyncDispatcher(
            source.Object, registry.Object, sink.Object, logger, enabled: true)
        {
            BandwidthBudget = new BandwidthBudgetOptions
            {
                BudgetKbps = thresholdKbps,
                NormalSnapshotHz = normalHz,
                ThrottledSnapshotHz = throttledHz,
                DegradedSnapshotHz = degradedHz,
                RecoverySeconds = recoverySeconds,
            },
        };
        return (dispatcher, logger);
    }

    // ── D.4.1 / D.7.4：per-session 带宽计数器基础行为 ──────────────────

    [Fact]
    public void GetSessionSnapshotHz_UntrackedSession_ReturnsNormalHz()
    {
        var (dispatcher, _) = CreateDispatcher();

        // 未跟踪的 session 返回 NormalSnapshotHz（默认 20）。
        Assert.Equal(20, dispatcher.GetSessionSnapshotHz(999));
    }

    [Fact]
    public void GetSessionSnapshotHz_TrackedSession_ReturnsTrackerHz()
    {
        var (dispatcher, _) = CreateDispatcher();

        // RecordSend 后创建 tracker，初始 Hz = NormalSnapshotHz。
        dispatcher.RecordSend(1, 500);
        Assert.Equal(20, dispatcher.GetSessionSnapshotHz(1));
    }

    [Fact]
    public void GetBandwidthSnapshot_ReturnsAllTrackedSessions()
    {
        var (dispatcher, _) = CreateDispatcher();

        // 注册两个 session。
        dispatcher.RecordSend(101, 1000);
        dispatcher.RecordSend(202, 2000);

        var snapshot = dispatcher.GetBandwidthSnapshot();
        Assert.Equal(2, snapshot.Count);
        Assert.True(snapshot.ContainsKey(101));
        Assert.True(snapshot.ContainsKey(202));
        // 窗口未滚动时 kbps 为初始值 0。
        Assert.Equal(0.0, snapshot[101]);
        Assert.Equal(0.0, snapshot[202]);
    }

    // ── D.4.2 / D.7.4：带宽超阈值触发降频（20Hz → 10Hz） ───────────────

    [Fact]
    public void BandwidthAboveThreshold_TriggersThrottle()
    {
        var (dispatcher, _) = CreateDispatcher();
        var tracker = new GatewaySyncDispatcher.SessionBandwidthTracker();
        var t0 = DateTime.UtcNow;

        // 累计超阈值字节（窗口未滚动，状态不变）。
        var bytes = BytesForKbps(100.0, WindowIntervalSeconds); // ~20300 字节
        tracker.RecordBytes(bytes, t0, dispatcher);
        Assert.Equal(20, tracker.CurrentSnapshotHz);

        // 1.5 秒后触发窗口滚动：kbps > 100 → 降频到 10Hz。
        tracker.RecordBytes(1, t0.AddSeconds(WindowIntervalSeconds), dispatcher);
        Assert.Equal(10, tracker.CurrentSnapshotHz);
        Assert.True(tracker.CurrentBandwidthKbps > 100.0);
    }

    [Fact]
    public void CustomThreshold_RespectedByTracker()
    {
        // 自定义阈值 50 kbps。
        var (dispatcher, _) = CreateDispatcher(thresholdKbps: 50.0);
        var tracker = new GatewaySyncDispatcher.SessionBandwidthTracker();
        var t0 = DateTime.UtcNow;

        // 超过 50 kbps 的字节数。
        var bytes = BytesForKbps(50.0, WindowIntervalSeconds); // ~10500 字节
        tracker.RecordBytes(bytes, t0, dispatcher);
        tracker.RecordBytes(1, t0.AddSeconds(WindowIntervalSeconds), dispatcher);
        Assert.Equal(10, tracker.CurrentSnapshotHz);
    }

    // ── D.4.3 / D.7.4：带宽恢复触发升频（10Hz → 20Hz） ─────────────────

    [Fact]
    public void BandwidthRecovery_RestoresNormalHz()
    {
        var (dispatcher, _) = CreateDispatcher();
        var tracker = new GatewaySyncDispatcher.SessionBandwidthTracker();
        var t0 = DateTime.UtcNow;

        // 先触发降频。
        var bytes = BytesForKbps(100.0, WindowIntervalSeconds);
        tracker.RecordBytes(bytes, t0, dispatcher);
        tracker.RecordBytes(1, t0.AddSeconds(WindowIntervalSeconds), dispatcher);
        Assert.Equal(10, tracker.CurrentSnapshotHz);

        // 连续 3 个低带宽窗口 → 回升到 20Hz。
        tracker.RecordBytes(1, t0.AddSeconds(WindowIntervalSeconds * 2), dispatcher); // 第 1 个低带宽窗口
        Assert.Equal(10, tracker.CurrentSnapshotHz);
        tracker.RecordBytes(1, t0.AddSeconds(WindowIntervalSeconds * 3), dispatcher); // 第 2 个低带宽窗口
        Assert.Equal(10, tracker.CurrentSnapshotHz);
        tracker.RecordBytes(1, t0.AddSeconds(WindowIntervalSeconds * 4), dispatcher); // 第 3 个低带宽窗口 → 回升
        Assert.Equal(20, tracker.CurrentSnapshotHz);
    }

    [Fact]
    public void BandwidthRecovery_UnderTwoWindows_DoesNotRestore()
    {
        var (dispatcher, _) = CreateDispatcher();
        var tracker = new GatewaySyncDispatcher.SessionBandwidthTracker();
        var t0 = DateTime.UtcNow;

        // 先触发降频。
        var bytes = BytesForKbps(100.0, WindowIntervalSeconds);
        tracker.RecordBytes(bytes, t0, dispatcher);
        tracker.RecordBytes(1, t0.AddSeconds(WindowIntervalSeconds), dispatcher);
        Assert.Equal(10, tracker.CurrentSnapshotHz);

        // 仅连续 2 个低带宽窗口（< RecoverySeconds=3），不应回升。
        tracker.RecordBytes(1, t0.AddSeconds(WindowIntervalSeconds * 2), dispatcher);
        tracker.RecordBytes(1, t0.AddSeconds(WindowIntervalSeconds * 3), dispatcher);
        Assert.Equal(10, tracker.CurrentSnapshotHz);
    }

    // ── D.4.2 / D.7.4：告警日志不重复 spam ─────────────────────────────

    [Fact]
    public void OverThresholdWarning_FiresPerLevelTransition_NotSpammed()
    {
        var (dispatcher, logger) = CreateDispatcher();
        var tracker = new GatewaySyncDispatcher.SessionBandwidthTracker();
        var t0 = DateTime.UtcNow;

        var bytes = BytesForKbps(100.0, WindowIntervalSeconds);

        // 第一次超阈值 → N→T 告警一次。
        tracker.RecordBytes(bytes, t0, dispatcher);
        tracker.RecordBytes(1, t0.AddSeconds(WindowIntervalSeconds), dispatcher);
        Assert.Equal(1, logger.Entries.Count(e => e.Level == LogLevel.Warning));

        // 同窗口内的重复字节记录不产生额外告警（无 spam）。
        tracker.RecordBytes(100, t0.AddSeconds(WindowIntervalSeconds), dispatcher);
        tracker.RecordBytes(50, t0.AddSeconds(WindowIntervalSeconds), dispatcher);
        Assert.Equal(1, logger.Entries.Count(e => e.Level == LogLevel.Warning));

        // 第二次持续超阈值 → T→D 升级（合法的独立降级事件，仅触发一次告警）。
        tracker.RecordBytes(bytes, t0.AddSeconds(WindowIntervalSeconds), dispatcher);
        tracker.RecordBytes(1, t0.AddSeconds(WindowIntervalSeconds * 2), dispatcher);
        Assert.Equal(2, logger.Entries.Count(e => e.Level == LogLevel.Warning));
    }

    [Fact]
    public void OverThresholdWarning_FiresAgain_AfterRecovery()
    {
        var (dispatcher, logger) = CreateDispatcher();
        var tracker = new GatewaySyncDispatcher.SessionBandwidthTracker();
        var t0 = DateTime.UtcNow;

        var bytes = BytesForKbps(100.0, WindowIntervalSeconds);

        // 第一次降频 → 告警 #1。
        tracker.RecordBytes(bytes, t0, dispatcher);
        tracker.RecordBytes(1, t0.AddSeconds(WindowIntervalSeconds), dispatcher);
        Assert.Equal(1, logger.Entries.Count(e => e.Level == LogLevel.Warning));

        // 连续 3 个低带宽窗口 → 恢复（_overThresholdWarned 重置为 false）。
        tracker.RecordBytes(1, t0.AddSeconds(WindowIntervalSeconds * 2), dispatcher);
        tracker.RecordBytes(1, t0.AddSeconds(WindowIntervalSeconds * 3), dispatcher);
        tracker.RecordBytes(1, t0.AddSeconds(WindowIntervalSeconds * 4), dispatcher);
        Assert.Equal(20, tracker.CurrentSnapshotHz);

        // 再次超阈值 → 告警 #2。
        tracker.RecordBytes(bytes, t0.AddSeconds(WindowIntervalSeconds * 4), dispatcher);
        tracker.RecordBytes(1, t0.AddSeconds(WindowIntervalSeconds * 5), dispatcher);
        Assert.Equal(2, logger.Entries.Count(e => e.Level == LogLevel.Warning));
    }

    [Fact]
    public void EffectiveBudgetKbps_Default_IsConfiguredDefault()
    {
        // BandwidthBudget 为 null（或未设置）时 EffectiveBudgetKbps 应等于配置默认值
        // （当前 BandwidthBudgetOptions.BudgetKbps 默认 1000，与既有压测基线一致）。
        var source = new Mock<IZoneShardFanoutSource>();
        var registry = new Mock<ISessionRegistry>();
        var sink = new Mock<IClientPacketSink>();
        var dispatcher = new GatewaySyncDispatcher(source.Object, registry.Object, sink.Object, enabled: true);
        Assert.Equal(1000.0, dispatcher.EffectiveBudgetKbps);
    }

    [Fact]
    public void EffectiveBudgetKbps_Configured_IsRespected()
    {
        var (dispatcher, _) = CreateDispatcher(thresholdKbps: 50.0);
        Assert.Equal(50.0, dispatcher.EffectiveBudgetKbps);
    }

    [Fact]
    public void InvalidBandwidthBudget_Validator_FallsBackToDefaults()
    {
        // 非法配置（预算<=0、频率乱序）应回退默认且不抛异常。
        var source = new Mock<IZoneShardFanoutSource>();
        var registry = new Mock<ISessionRegistry>();
        var sink = new Mock<IClientPacketSink>();
        var dispatcher = new GatewaySyncDispatcher(source.Object, registry.Object, sink.Object, enabled: true)
        {
            BandwidthBudget = new BandwidthBudgetOptions { BudgetKbps = -5, NormalSnapshotHz = 5, ThrottledSnapshotHz = 20 },
        };
        Assert.Equal(100.0, dispatcher.EffectiveBudgetKbps);
        Assert.Equal(20, dispatcher.NormalSnapshotHz);
        Assert.Equal(10, dispatcher.ThrottledSnapshotHz);
        Assert.Equal(5, dispatcher.DegradedSnapshotHz);
    }

    // ── 2.4：三级降级顺序（20→10→5）与逐级恢复（5→10→20） ──────────────

    [Fact]
    public void SustainedOverBudget_ThreeTierDegrade_20To10To5()
    {
        var (dispatcher, _) = CreateDispatcher(thresholdKbps: 100.0);
        var tracker = new GatewaySyncDispatcher.SessionBandwidthTracker { SessionId = 42 };
        var t0 = DateTime.UtcNow;
        var bytes = BytesForKbps(600.0, WindowIntervalSeconds); // 600kbps 远超红线

        // 窗口 1：累计（未滚动，保持 20Hz）。
        tracker.RecordBytes(bytes, t0, dispatcher);
        Assert.Equal(20, tracker.CurrentSnapshotHz);

        // 窗口 2：滚动 → 第一级降频 20→10，字段裁剪开启。
        tracker.RecordBytes(bytes, t0.AddSeconds(WindowIntervalSeconds), dispatcher);
        Assert.Equal(10, tracker.CurrentSnapshotHz);
        Assert.True(tracker.IsFieldTrimmingEnabled);

        // 窗口 3：持续超预算 → 第二级降频 10→5，实体裁剪开启。
        tracker.RecordBytes(bytes, t0.AddSeconds(WindowIntervalSeconds * 2), dispatcher);
        Assert.Equal(5, tracker.CurrentSnapshotHz);
        Assert.True(tracker.IsFieldTrimmingEnabled);
        Assert.True(tracker.IsEntityTrimmingEnabled);
    }

    [Fact]
    public void Recovery_FromDegraded_StepwiseRestore_5To10To20()
    {
        var (dispatcher, _) = CreateDispatcher(thresholdKbps: 100.0);
        var tracker = new GatewaySyncDispatcher.SessionBandwidthTracker { SessionId = 42 };
        var t0 = DateTime.UtcNow;
        var highBytes = BytesForKbps(600.0, WindowIntervalSeconds);
        var lowBytes = BytesForKbps(20.0, WindowIntervalSeconds); // 20kbps 远低于红线

        // 降到 5Hz（Degraded）。
        tracker.RecordBytes(highBytes, t0, dispatcher);
        tracker.RecordBytes(highBytes, t0.AddSeconds(WindowIntervalSeconds), dispatcher);
        tracker.RecordBytes(highBytes, t0.AddSeconds(WindowIntervalSeconds * 2), dispatcher);
        Assert.Equal(5, tracker.CurrentSnapshotHz);

        // 连续 3 个低带宽窗口 → 回升中档 10Hz。
        tracker.RecordBytes(lowBytes, t0.AddSeconds(WindowIntervalSeconds * 3), dispatcher);
        tracker.RecordBytes(lowBytes, t0.AddSeconds(WindowIntervalSeconds * 4), dispatcher);
        tracker.RecordBytes(lowBytes, t0.AddSeconds(WindowIntervalSeconds * 5), dispatcher);
        Assert.Equal(10, tracker.CurrentSnapshotHz);

        // 再连续 3 个低带宽窗口 → 回升正常 20Hz。
        tracker.RecordBytes(lowBytes, t0.AddSeconds(WindowIntervalSeconds * 6), dispatcher);
        tracker.RecordBytes(lowBytes, t0.AddSeconds(WindowIntervalSeconds * 7), dispatcher);
        tracker.RecordBytes(lowBytes, t0.AddSeconds(WindowIntervalSeconds * 8), dispatcher);
        Assert.Equal(20, tracker.CurrentSnapshotHz);
        Assert.False(tracker.IsFieldTrimmingEnabled);
        Assert.False(tracker.IsEntityTrimmingEnabled);
    }

    [Fact]
    public void BandwidthDiagnosticEvents_FireOnThrottleAndRecovery()
    {
        var (dispatcher, _) = CreateDispatcher(thresholdKbps: 100.0);
        var throttledEvents = new List<(long SessionId, double Kbps, int FromHz, int ToHz)>();
        var recoveredEvents = new List<(long SessionId, double Kbps, int FromHz, int ToHz)>();
        dispatcher.BandwidthThrottled += (sid, kbps, from, to) =>
            throttledEvents.Add((sid, kbps, from, to));
        dispatcher.BandwidthRecovered += (sid, kbps, from, to) =>
            recoveredEvents.Add((sid, kbps, from, to));

        var tracker = new GatewaySyncDispatcher.SessionBandwidthTracker { SessionId = 42 };
        var t0 = DateTime.UtcNow;
        var highBytes = BytesForKbps(600.0, WindowIntervalSeconds);
        var lowBytes = BytesForKbps(20.0, WindowIntervalSeconds);

        // 20→10 触发一次 Throttled 事件。
        tracker.RecordBytes(highBytes, t0, dispatcher);
        tracker.RecordBytes(highBytes, t0.AddSeconds(WindowIntervalSeconds), dispatcher);
        Assert.Single(throttledEvents);
        Assert.Equal((42L, 10), (throttledEvents[0].SessionId, throttledEvents[0].ToHz));

        // 10→5 再触发一次 Throttled 事件（T→D 也触发一次）。
        tracker.RecordBytes(highBytes, t0.AddSeconds(WindowIntervalSeconds * 2), dispatcher);
        Assert.Equal(2, throttledEvents.Count);
        Assert.Equal(5, throttledEvents[1].ToHz);

        // 5→10→20 各触发一次 Recovered 事件。
        tracker.RecordBytes(lowBytes, t0.AddSeconds(WindowIntervalSeconds * 3), dispatcher);
        tracker.RecordBytes(lowBytes, t0.AddSeconds(WindowIntervalSeconds * 4), dispatcher);
        tracker.RecordBytes(lowBytes, t0.AddSeconds(WindowIntervalSeconds * 5), dispatcher);
        Assert.Single(recoveredEvents);
        Assert.Equal(10, recoveredEvents[0].ToHz);

        tracker.RecordBytes(lowBytes, t0.AddSeconds(WindowIntervalSeconds * 6), dispatcher);
        tracker.RecordBytes(lowBytes, t0.AddSeconds(WindowIntervalSeconds * 7), dispatcher);
        tracker.RecordBytes(lowBytes, t0.AddSeconds(WindowIntervalSeconds * 8), dispatcher);
        Assert.Equal(2, recoveredEvents.Count);
        Assert.Equal(20, recoveredEvents[1].ToHz);
    }

    [Fact]
    public void HundredPlayers_Under100kbpsPerSession_NoThrottle()
    {
        // 2.4：100 并发玩家 × 每玩家 10 实体更新，断言每玩家平均带宽 < 100 kbps（spec 5.5.1.6 a）。
        // 此处验证带宽估算与限流联动：每 session 仅 20kbps 流量时不得触发限流。
        var (dispatcher, _) = CreateDispatcher(thresholdKbps: 100.0);
        var trackers = new List<GatewaySyncDispatcher.SessionBandwidthTracker>();
        var t0 = DateTime.UtcNow;
        var lowBytes = BytesForKbps(20.0, WindowIntervalSeconds); // 20kbps << 100kbps

        for (int i = 0; i < 100; i++)
        {
            var tracker = new GatewaySyncDispatcher.SessionBandwidthTracker { SessionId = i };
            trackers.Add(tracker);
            dispatcher.RecordSend(i, lowBytes);
            tracker.RecordBytes(lowBytes, t0, dispatcher);
        }

        // 滚动窗口后全部保持 20Hz，无任何 session 触发限流。
        for (int i = 0; i < 100; i++)
        {
            trackers[i].RecordBytes(1, t0.AddSeconds(WindowIntervalSeconds), dispatcher);
            Assert.Equal(20, trackers[i].CurrentSnapshotHz);
        }
    }

    // ── 捕获日志的 ILogger 实现 ──────────────────────────────────────────

    /// <summary>
    /// 捕获日志条目的 ILogger 实现，用于验证告警日志的触发次数与去重行为。
    /// </summary>
    private sealed class CapturingLogger : ILogger<GatewaySyncDispatcher>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
