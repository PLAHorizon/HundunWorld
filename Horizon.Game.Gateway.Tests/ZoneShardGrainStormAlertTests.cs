using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Horizon.Orleans.Grains.World;
using Microsoft.Extensions.Logging;
using Moq;
using Orleans;
using Orleans.Runtime;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 任务 10.6 — 修正风暴运维告警单元测试。
/// 验证 ZoneShardGrain.RecordCorrectionAndCheckStorm：
/// 某玩家 2 秒内 5 次修正触发 LogWarning 告警含 PlayerId/Count/Window，
/// 且 10 秒内同玩家不重复告警。
/// 被测代码：ZoneShardGrain.cs:1682（RecordCorrectionAndCheckStorm）。
/// </summary>
public class ZoneShardGrainStormAlertTests
{
    /// <summary>
    /// 捕获 ILogger 日志调用的 Spy。
    /// </summary>
    private sealed class LogSpy : ILogger<ZoneShardGrain>
    {
        public sealed record LogEntry(LogLevel Level, string Message, object[] Args);

        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var msg = formatter(state, exception);
            // 解析结构化参数（Orleans LoggerExtensions.LogWarning 使用 IReadOnlyList<KeyValuePair<string, object>>）
            var args = Array.Empty<object>();
            if (state is IReadOnlyList<KeyValuePair<string, object>> kvps)
            {
                args = new object[kvps.Count];
                for (int i = 0; i < kvps.Count; i++)
                    args[i] = kvps[i].Value;
            }
            Entries.Add(new LogEntry(logLevel, msg, args));
        }
    }

    private static (ZoneShardGrain grain, LogSpy spy) CreateGrainWithSpy()
    {
        var spy = new LogSpy();
        var mockState = new Mock<IPersistentState<ZoneShardState>>();
        mockState.SetupGet(s => s.State).Returns(new ZoneShardState());
        var grain = new ZoneShardGrain(spy, mockState.Object);

        var grainId = GrainId.Create(GrainType.Create("ZoneShard"), "1");
        var mockContext = new Mock<IGrainContext>();
        mockContext.SetupGet(c => c.GrainId).Returns(grainId);
        var contextField = typeof(Grain).GetField("<GrainContext>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        contextField?.SetValue(grain, mockContext.Object);

        return (grain, spy);
    }

    /// <summary>通过反射调用 private RecordCorrectionAndCheckStorm。</summary>
    private static void InvokeRecordCorrectionAndCheckStorm(ZoneShardGrain grain, ulong entityId)
    {
        var method = typeof(ZoneShardGrain).GetMethod(
            "RecordCorrectionAndCheckStorm",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        method!.Invoke(grain, new object[] { entityId });
    }

    // ─── 5 次修正触发 LogWarning 告警 ───

    [Fact]
    public void StormAlert_5CorrectionsIn2Seconds_TriggersLogWarning()
    {
        var (grain, spy) = CreateGrainWithSpy();
        const ulong playerId = 1001L;

        // 2 秒内 5 次修正（快速调用，远小于 2 秒窗口）
        for (int i = 0; i < 5; i++)
        {
            InvokeRecordCorrectionAndCheckStorm(grain, playerId);
        }

        // 应触发 LogWarning 告警
        var warnings = spy.Entries.FindAll(e => e.Level == LogLevel.Warning);
        Assert.True(warnings.Count >= 1,
            $"5 次修正应触发 LogWarning 告警，实际 {warnings.Count} 次");
    }

    // ─── 告警含 PlayerId/Count/Window ───

    [Fact]
    public void StormAlert_AlertContainsPlayerIdCountWindow()
    {
        var (grain, spy) = CreateGrainWithSpy();
        const ulong playerId = 2002L;

        for (int i = 0; i < 5; i++)
        {
            InvokeRecordCorrectionAndCheckStorm(grain, playerId);
        }

        var warning = spy.Entries.Find(e => e.Level == LogLevel.Warning);
        Assert.NotNull(warning);

        // 告警消息应含 PlayerId/Count/Window
        var msg = warning!.Message;
        Assert.Contains("PlayerId", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Count", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Window", msg, StringComparison.OrdinalIgnoreCase);

        // 结构化参数应含 playerId=2002、Count=5、Window=2
        Assert.Contains(playerId, warning.Args);
        Assert.Contains(5, warning.Args);     // Count=5（CorrectionStormThreshold）
        Assert.Contains(2.0f, warning.Args);  // Window=2.0s（CorrectionStormWindowSeconds）
    }

    // ─── 10 秒内同玩家不重复告警 ───

    [Fact]
    public void StormAlert_NoRepeatWithin10Seconds_SamePlayer()
    {
        var (grain, spy) = CreateGrainWithSpy();
        const ulong playerId = 3003L;

        // 第一次：5 次修正触发告警
        for (int i = 0; i < 5; i++)
        {
            InvokeRecordCorrectionAndCheckStorm(grain, playerId);
        }
        var firstWarnings = spy.Entries.FindAll(e => e.Level == LogLevel.Warning);
        Assert.True(firstWarnings.Count >= 1, "第一次应触发告警");

        // 立即再触发 5 次（在 10 秒冷却期内）→ 不应重复告警
        for (int i = 0; i < 5; i++)
        {
            InvokeRecordCorrectionAndCheckStorm(grain, playerId);
        }
        var totalWarnings = spy.Entries.FindAll(e => e.Level == LogLevel.Warning);

        // 10 秒冷却期内同玩家不重复告警（仍只有第一次的告警）
        Assert.Equal(firstWarnings.Count, totalWarnings.Count);
    }

    // ─── 不同玩家独立计数 ───

    [Fact]
    public void StormAlert_DifferentPlayers_IndependentCounting()
    {
        var (grain, spy) = CreateGrainWithSpy();
        const ulong playerA = 4001L;
        const ulong playerB = 4002L;

        // 玩家 A 3 次修正（未达阈值 5）
        for (int i = 0; i < 3; i++)
        {
            InvokeRecordCorrectionAndCheckStorm(grain, playerA);
        }
        var warningsAfterA = spy.Entries.FindAll(e => e.Level == LogLevel.Warning);
        Assert.Empty(warningsAfterA); // 3 次 < 5，不告警

        // 玩家 B 5 次修正（达阈值）
        for (int i = 0; i < 5; i++)
        {
            InvokeRecordCorrectionAndCheckStorm(grain, playerB);
        }
        var warningsAfterB = spy.Entries.FindAll(e => e.Level == LogLevel.Warning);
        Assert.True(warningsAfterB.Count >= 1, "玩家 B 5 次应触发告警");

        // 玩家 A 再 2 次（累计 5 次）→ 应触发告警
        for (int i = 0; i < 2; i++)
        {
            InvokeRecordCorrectionAndCheckStorm(grain, playerA);
        }
        var warningsFinal = spy.Entries.FindAll(e => e.Level == LogLevel.Warning);
        Assert.True(warningsFinal.Count >= 2, "玩家 A 累计 5 次也应触发告警");
    }

    // ─── 未达阈值不告警 ───

    [Fact]
    public void StormAlert_BelowThreshold_NoAlert()
    {
        var (grain, spy) = CreateGrainWithSpy();
        const ulong playerId = 5001L;

        // 4 次修正（未达阈值 5）
        for (int i = 0; i < 4; i++)
        {
            InvokeRecordCorrectionAndCheckStorm(grain, playerId);
        }

        var warnings = spy.Entries.FindAll(e => e.Level == LogLevel.Warning);
        Assert.Empty(warnings); // 4 次 < 5，不告警
    }

    // ─── 告警消息含风暴关键字 ───

    [Fact]
    public void StormAlert_MessageContainsStormKeyword()
    {
        var (grain, spy) = CreateGrainWithSpy();
        const ulong playerId = 6001L;

        for (int i = 0; i < 5; i++)
        {
            InvokeRecordCorrectionAndCheckStorm(grain, playerId);
        }

        var warning = spy.Entries.Find(e => e.Level == LogLevel.Warning);
        Assert.NotNull(warning);
        // 消息应含"修正风暴告警"或"Storm"关键字
        var msg = warning!.Message;
        Assert.True(
            msg.Contains("风暴", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("Storm", StringComparison.OrdinalIgnoreCase),
            $"告警消息应含风暴关键字，实际：{msg}");
    }
}