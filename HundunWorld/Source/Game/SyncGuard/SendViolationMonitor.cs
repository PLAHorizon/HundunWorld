using System;
using System.Collections.Concurrent;
using System.Threading;
using Horizon.Game.ECS.Arch.SyncGuard.Contracts;
using HundunWorld.Game.Network;

namespace HundunWorld.Game.SyncGuard;

/// <summary>
/// 违规发送尝试告警限频器：按"实体 ID + 拒绝原因"维度限频，
/// 同一组合每秒最多输出 1 条日志；同时接入 <see cref="ClientSyncMetrics"/> 违规指标。
/// </summary>
public sealed class SendViolationMonitor : ISendViolationReporter
{
    /// <summary>限频键：实体 ID + 拒绝原因。</summary>
    private readonly record struct RateLimitKey(ulong EntityId, SendRejectReason Reason);

    /// <summary>上一次输出日志的时间戳（Stopwatch 时钟，ticks）。</summary>
    private readonly ConcurrentDictionary<RateLimitKey, long> _lastLoggedTicks = new();

    /// <summary>是否接入 ClientSyncMetrics 指标（默认开启）。</summary>
    public bool MetricsEnabled { get; set; } = true;

    /// <summary>累计违规上报次数（含限频内的全部尝试）。</summary>
    public long TotalViolations => Interlocked.Read(ref _totalViolations);
    private long _totalViolations;

    /// <inheritdoc />
    public void ReportViolation(in SendViolationInfo violation)
    {
        try
        {
            Interlocked.Increment(ref _totalViolations);

            if (MetricsEnabled)
            {
                ClientSyncMetrics.RecordOutboundViolation();
            }

            // 按"实体 ID + 拒绝原因"维度限频：同一组合每秒最多输出 1 条日志（spec 4.4.3、5.4.1 规则 3b）。
            var key = new RateLimitKey(violation.EntityId, violation.Reason);
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            var elapsed = _lastLoggedTicks.TryGetValue(key, out var last)
                ? (now - last) / (double)System.Diagnostics.Stopwatch.Frequency
                : double.MaxValue;

            if (elapsed < 1.0)
            {
                return;
            }

            _lastLoggedTicks[key] = now;

            System.Diagnostics.Debug.WriteLine(
                $"[SendViolationMonitor] 违规发送尝试: Entity={violation.EntityId}, Type={violation.EntityType}, Reason={violation.Reason}, Time={violation.OccurredAt:O}");
        }
        catch (Exception ex)
        {
            // 上报内部异常被吞并并仅输出一次错误日志，不影响调用方主流程（spec 5.4.3 异常 2）。
            System.Diagnostics.Debug.WriteLine($"[SendViolationMonitor] 违规上报内部异常: {ex.Message}");
        }
    }

    /// <summary>清理限频记录（断线重连时调用）。</summary>
    public void Reset()
    {
        _lastLoggedTicks.Clear();
    }
}