using System;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;
using Horizon.Game.Core.Observability;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.Gateway.Services;

/// <summary>
/// 阶段 6.3 P2 — 自动回滚门窗口采样器：经 <see cref="MeterListener"/> 在网关进程内捕获
/// <c>horizon.sync.ds.route.*</c> 仪表，按固定窗口（<see cref="RollbackGateHostedService"/> 周期）
/// 消费累计增量。
/// <para>
/// 修正计数（<c>horizon.sync.corrections</c>）由 ZoneShardGrain（Silo 进程）上报——网关进程内
/// 无写入者，故经 <see cref="ICorrectionCounterStore"/>（Redis 汇聚）读取运行总数并计算窗口增量：
/// 首次读取基线化（不把历史总量当窗口增量）；增量 &lt; 0（计数被重置）时置 0。未注入 store 时
/// 修正信号恒为 0（向后兼容，仅回退率 + 延迟双信号）。
/// </para>
/// </summary>
public sealed class RollbackWindowSampler : IRollbackWindowSampler
{
    private readonly ILogger<RollbackWindowSampler> _logger;
    private readonly ICorrectionCounterStore? _correctionStore;
    private readonly object _lock = new();
    private readonly MeterListener _listener = new();

    private bool _watching;
    private long _attempts;
    private long _fallbacks;
    private double _latencySumMs;
    private long _latencyCount;

    /// <summary>上次读到的修正计数运行总数（null = 尚未基线化；首次读取把总量记为基线、窗口增量为 0）。</summary>
    private long? _lastCorrectionsTotal;

    /// <param name="logger">日志器。</param>
    /// <param name="correctionStore">跨进程修正计数存储（Redis 汇聚；null = 不启用修正率信号）。</param>
    public RollbackWindowSampler(ILogger<RollbackWindowSampler> logger, ICorrectionCounterStore? correctionStore = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _correctionStore = correctionStore;

        _listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            lock (_lock)
            {
                switch (instrument.Name)
                {
                    case "horizon.sync.ds.route.attempts": _attempts += value; break;
                    case "horizon.sync.ds.route.fallback": _fallbacks += value; break;
                }
            }
        });
        _listener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
        {
            lock (_lock)
            {
                if (instrument.Name == "horizon.sync.ds.route.latency.ms")
                {
                    _latencySumMs += value;
                    _latencyCount++;
                }
            }
        });
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == SyncMetrics.Meter.Name)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
    }

    /// <summary>当前累计路由尝试数（诊断/测试）。</summary>
    public long TotalAttempts { get { lock (_lock) return _attempts; } }

    public void EnsureWatching()
    {
        lock (_lock)
        {
            if (_watching) return;
            _watching = true;
            // 清空累计值：从「开监听」到「首个窗口」之间的仪表写入不计入首个窗口，
            // 避免把启用瞬间的瞬时流量/历史残留混入窗口判定。
            _attempts = _fallbacks = 0;
            _latencySumMs = 0;
            _latencyCount = 0;
            // 修正计数基线重置：启用/热切瞬间重新基线化，避免把离线期间的累计修正量当成首窗口增量。
            _lastCorrectionsTotal = null;
            _listener.Start();
            _logger.LogDebug("RollbackWindowSampler：开始监听 horizon.sync.ds.route.* 仪表。");
        }
    }

    public async Task<RollbackWindow> SampleWindowAsync()
    {
        RollbackWindow window;
        lock (_lock)
        {
            window = new RollbackWindow(_attempts, _fallbacks, 0, _latencySumMs, _latencyCount);
            _attempts = _fallbacks = 0;
            _latencySumMs = 0;
            _latencyCount = 0;
        }

        if (_correctionStore is null) return window;

        // 跨进程修正增量：读运行总数 → 与上次基线求差（首次读取基线化，增量 0）。
        long total = 0;
        try
        {
            total = await _correctionStore.GetCorrectionsTotalAsync();
        }
        catch (Exception ex)
        {
            // 读失败（store 实现一般吞异常返回 0，此为兜底）：本窗口修正信号置 0，
            // 不更新基线——下次成功读取仍能算出差量，不丢信号。
            _logger.LogWarning(ex, "RollbackWindowSampler：读取跨进程修正计数失败（本窗口修正信号置 0）。");
            return window;
        }

        var delta = _lastCorrectionsTotal is null ? 0 : Math.Max(0, total - _lastCorrectionsTotal.Value);
        _lastCorrectionsTotal = total;
        return window with { Corrections = delta };
    }

    public void Stop()
    {
        lock (_lock)
        {
            _watching = false;
            _listener.Dispose();
        }
    }
}
