using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.Core.Observability;

/// <summary>
/// 阶段 6.3 P2 — 修正计数发布器：在 Silo 进程内经 <see cref="MeterListener"/> 捕获
/// <c>horizon.sync.corrections</c> 仪表增量，周期性批量上送 <see cref="ICorrectionCounterStore"/>
/// （Redis INCRBY）。网关自动回滚门的「修正率」信号即由此跨进程汇聚（Silo→Redis→网关）；
/// 阶段 6.3 P2⑨ 起同时按 <c>reason</c> 标签维度上送（HINCRBY 哈希），供灰度对比面板按修正原因并排展示。
/// </summary>
/// <remarks>
/// <para>防丢语义：上送失败把增量加回本地累计，下轮 <see cref="FlushAsync"/> 重试——Silo 崩溃间隙
/// 的未上送量丢失可接受（修正计数是质量信号，非账务），但单次 Redis 抖动不丢。总数与按 reason
/// 两条通道相互独立（各自失败各自加回重试），互不阻塞。</para>
/// <para>发布间隔由宿主（<see cref="CorrectionPublisherHostedService"/> 等）控制；间隔远小于网关
/// 采样窗口（默认 30s），保证网关每个窗口都能读到新鲜增量。</para>
/// </remarks>
public sealed class CorrectionPublisher : IDisposable
{
    private readonly ICorrectionCounterStore _store;
    private readonly ILogger? _logger;
    private readonly MeterListener _listener = new();
    private readonly object _lock = new();

    private long _pending;
    private readonly Dictionary<string, long> _pendingByReason = new();

    public CorrectionPublisher(ICorrectionCounterStore store, ILogger? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger;

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == SyncMetrics.Meter.Name
                && string.Equals(instrument.Name, "horizon.sync.corrections", StringComparison.Ordinal))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            if (!string.Equals(instrument.Name, "horizon.sync.corrections", StringComparison.Ordinal)) return;
            lock (_lock)
            {
                _pending += value;
                // 按 reason 标签维度累计（ZoneShardGrain 上报带 CorrectionReason 标签；
                // 无标签的修正只进总数，不进按 reason 哈希——两条通道各自完整）。
                string? reason = null;
                foreach (var tag in tags)
                {
                    if (tag.Key == "reason") { reason = tag.Value?.ToString(); break; }
                }
                if (reason is not null)
                {
                    _pendingByReason.TryGetValue(reason, out var cur);
                    _pendingByReason[reason] = cur + value;
                }
            }
        });
    }

    /// <summary>开始监听仪表（幂等；未调用前不捕获任何增量）。</summary>
    public void Start() => _listener.Start();

    /// <summary>当前未上送累计量（诊断/测试）。</summary>
    public long Pending { get { lock (_lock) return _pending; } }

    /// <summary>当前未上送累计量（按 reason；诊断/测试）。</summary>
    public IReadOnlyDictionary<string, long> PendingByReason
    {
        get { lock (_lock) return new Dictionary<string, long>(_pendingByReason); }
    }

    /// <summary>
    /// 把自上次上送以来累积的修正增量写入 store（总数 INCRBY + 按 reason HINCRBY）并清零本地累计。
    /// 返回本次上送量（无增量返回 0）。任一通道上送失败把该通道增量加回累计并抛异常
    /// （由宿主捕获记录，下轮重试；另一通道不受影响）。
    /// </summary>
    public async Task<long> FlushAsync()
    {
        Dictionary<string, long> byReason;
        long delta;
        lock (_lock)
        {
            delta = _pending;
            _pending = 0;
            byReason = _pendingByReason.Count > 0 ? new Dictionary<string, long>(_pendingByReason) : new();
            _pendingByReason.Clear();
        }
        if (delta <= 0 && byReason.Count == 0) return delta;

        // 先按 reason 上送（哈希通道），再上送总数——两条独立，各自失败各自加回。
        if (byReason.Count > 0)
        {
            try
            {
                await _store.IncrementCorrectionsByReasonAsync(byReason);
            }
            catch (Exception ex)
            {
                // 按 reason 通道失败：本批已排空但两条都未落盘（总数通道尚未执行）——
                // 按 reason 增量与总数增量**一并加回**，下轮重试（INCRBY/HINCRBY 幂等叠加，
                // 即使失败前部分字段已写入，重试也不丢不重）。
                lock (_lock)
                {
                    foreach (var kv in byReason)
                    {
                        _pendingByReason.TryGetValue(kv.Key, out var cur);
                        _pendingByReason[kv.Key] = cur + kv.Value;
                    }
                    _pending += delta;
                }
                _logger?.LogError(ex, "CorrectionPublisher：上送按 reason 修正计数失败（{Count} 项），已加回累计待下轮重试。", byReason.Count);
                throw;
            }
        }

        if (delta > 0)
        {
            try
            {
                await _store.IncrementCorrectionsAsync(delta);
            }
            catch (Exception ex)
            {
                // 未上送量加回累计（下轮重试），避免单次 Redis 抖动丢计数。
                lock (_lock) { _pending += delta; }
                _logger?.LogError(ex, "CorrectionPublisher：上送修正计数失败（{Delta}），已加回累计待下轮重试。", delta);
                throw;
            }
        }

        return delta;
    }

    public void Dispose() => _listener.Dispose();
}
