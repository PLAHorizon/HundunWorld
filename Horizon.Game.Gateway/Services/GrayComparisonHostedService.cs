using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core.Observability;
using Horizon.Game.Gateway.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Horizon.Game.Gateway.Services;

/// <summary>灰度对比面板单窗口快照（按维度增量）。</summary>
/// <param name="AttemptsByResult">本窗口路由尝试增量（按 result：success=DS 段进量，其余=回退分类）。</param>
/// <param name="FallbackByReason">本窗口 DS 回退 Orleans 增量（按 reason，Orleans 段兜底量）。</param>
/// <param name="CorrectionsByReason">本窗口服务端修正增量（按 reason，跨进程自 Silo 经 Redis 汇聚）。</param>
public sealed record GrayWindowSnapshot(
    IReadOnlyDictionary<string, long> AttemptsByResult,
    IReadOnlyDictionary<string, long> FallbackByReason,
    IReadOnlyDictionary<string, long> CorrectionsByReason);

/// <summary>
/// 阶段 6.3 P2⑨ — 灰度对比面板：每窗口（与自动回滚门对齐，<c>DsRollback:WindowSeconds</c>）把
/// Orleans 段 vs DS 段的对比信号聚合并输出为 Prometheus gauge 集：
/// <list type="bullet">
///   <item><c>horizon.sync.gray.window.attempts</c>{result} —— 本窗口 DS 路由尝试增量（success=DS 段实际进量）；</item>
///   <item><c>horizon.sync.gray.window.fallback</c>{reason} —— 本窗口 DS 回退 Orleans 增量（Orleans 段兜底量）；</item>
///   <item><c>horizon.sync.gray.window.corrections</c>{reason} —— 本窗口服务端修正增量（跨进程自 Silo 经 Redis）。</item>
/// </list>
/// 与自动回滚门（回退率/延迟/修正率判定 + 回切执行）解耦——面板是纯可观测输出，恒注册运行
/// （UseDS=false 时 attempts/fallback 恒 0，修正信号仍展示；零副作用）。
/// </summary>
public sealed class GrayComparisonHostedService : BackgroundService
{
    private readonly IOptionsMonitor<GatewayOptions> _options;
    private readonly ICorrectionCounterStore? _correctionStore;
    private readonly ILogger<GrayComparisonHostedService> _logger;
    private readonly TimeSpan _window;
    private readonly MeterListener _listener = new();
    private readonly object _lock = new();
    private readonly Dictionary<string, long> _attemptsByResult = new();
    private readonly Dictionary<string, long> _fallbackByReason = new();
    private bool _listening;

    /// <summary>上次读到的按 reason 修正计数基线（null = 尚未基线化；首次读取把总量记为基线、窗口增量为 0）。</summary>
    private IReadOnlyDictionary<string, long>? _lastCorrectionsByReason;

    /// <param name="options">网关配置监视器（窗口时长读取 <c>DsRollback:WindowSeconds</c>，缺省 30s，与回滚门对齐）。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="correctionStore">跨进程修正计数存储（Redis 汇聚；null = 修正信号缺位，面板仅输出路由对比）。</param>
    public GrayComparisonHostedService(
        IOptionsMonitor<GatewayOptions> options,
        ILogger<GrayComparisonHostedService> logger,
        ICorrectionCounterStore? correctionStore = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _correctionStore = correctionStore;
        _window = TimeSpan.FromSeconds(Math.Max(5, options.CurrentValue.DsRollback?.WindowSeconds ?? 30));

        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            lock (_lock)
            {
                switch (instrument.Name)
                {
                    case "horizon.sync.ds.route.attempts":
                        Accumulate(tags, "result", value, _attemptsByResult);
                        break;
                    case "horizon.sync.ds.route.fallback":
                        Accumulate(tags, "reason", value, _fallbackByReason);
                        break;
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

    private static void Accumulate(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key, long value, Dictionary<string, long> dict)
    {
        string? dim = null;
        foreach (var t in tags)
        {
            if (t.Key == key) { dim = t.Value?.ToString(); break; }
        }
        var k = dim ?? "<none>";
        dict.TryGetValue(k, out var cur);
        dict[k] = cur + value;
    }

    /// <summary>开始监听仪表（幂等；从「开监听」起的增量才计入窗口）。</summary>
    public void StartListening()
    {
        lock (_lock)
        {
            if (_listening) return;
            _listening = true;
            _attemptsByResult.Clear();
            _fallbackByReason.Clear();
            // 修正计数基线重置：启用瞬间重新基线化，避免把离线期间的累计修正量当成首窗口增量。
            _lastCorrectionsByReason = null;
            _listener.Start();
            _logger.LogDebug("GrayComparisonHostedService：开始监听 horizon.sync.ds.route.* 仪表。");
        }
    }

    /// <summary>停止监听（测试/关闭用）。</summary>
    public void StopListening()
    {
        lock (_lock)
        {
            if (!_listening) return;
            _listening = false;
            _listener.Dispose();
        }
    }

    /// <summary>
    /// 采样一个窗口：消费仪表累计增量（attempts 按 result / fallback 按 reason）并跨进程读取
    /// 按 reason 修正计数增量（首次基线化；读失败置空且不更新基线——下次成功仍能算出差量）。
    /// </summary>
    public async Task<GrayWindowSnapshot> SampleWindowAsync()
    {
        Dictionary<string, long> attempts;
        Dictionary<string, long> fallback;
        lock (_lock)
        {
            attempts = new Dictionary<string, long>(_attemptsByResult);
            fallback = new Dictionary<string, long>(_fallbackByReason);
            _attemptsByResult.Clear();
            _fallbackByReason.Clear();
        }

        if (_correctionStore is null)
        {
            return new GrayWindowSnapshot(attempts, fallback, new Dictionary<string, long>());
        }

        IReadOnlyDictionary<string, long> current;
        try
        {
            current = await _correctionStore.GetCorrectionsByReasonAsync();
        }
        catch (Exception ex)
        {
            // 读失败：本窗口修正信号置空，不更新基线（下次成功读取仍能算出差量，不丢信号）。
            _logger.LogWarning(ex, "GrayComparisonHostedService：读取按 reason 修正计数失败（本窗口修正信号置空）。");
            return new GrayWindowSnapshot(attempts, fallback, new Dictionary<string, long>());
        }

        var corrections = new Dictionary<string, long>();
        if (_lastCorrectionsByReason is { } baseline)
        {
            foreach (var kv in current)
            {
                if (baseline.TryGetValue(kv.Key, out var prev))
                {
                    var delta = Math.Max(0, kv.Value - prev);
                    if (delta > 0) corrections[kv.Key] = delta;
                }
                else if (kv.Value > 0)
                {
                    // 基线中不存在的 reason（新出现）：首次出现记全量（该 reason 此前无历史）。
                    corrections[kv.Key] = kv.Value;
                }
            }
        }
        _lastCorrectionsByReason = new Dictionary<string, long>(current);
        return new GrayWindowSnapshot(attempts, fallback, corrections);
    }

    /// <summary>把窗口快照写入面板 gauge（每窗口覆盖上次值；Prometheus 展示最近窗口增量）。</summary>
    public static void RecordWindow(GrayWindowSnapshot window)
    {
        foreach (var kv in window.AttemptsByResult)
        {
            SyncMetrics.GrayWindowAttempts.Record(kv.Value, new KeyValuePair<string, object?>("result", kv.Key));
        }
        foreach (var kv in window.FallbackByReason)
        {
            SyncMetrics.GrayWindowFallback.Record(kv.Value, new KeyValuePair<string, object?>("reason", kv.Key));
        }
        foreach (var kv in window.CorrectionsByReason)
        {
            SyncMetrics.GrayWindowCorrections.Record(kv.Value, new KeyValuePair<string, object?>("reason", kv.Key));
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        StartListening();
        _logger.LogInformation("GrayComparisonHostedService：启动（灰度对比面板，窗口 {Window}）。", _window);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_window, stoppingToken);
                RecordWindow(await SampleWindowAsync());
            }
            catch (OperationCanceledException)
            {
                break; // 正常退出
            }
            catch (Exception ex)
            {
                // 单窗失败不中断服务（下一窗口继续）。
                _logger.LogError(ex, "GrayComparisonHostedService：窗口采样/输出失败（本窗口跳过）。");
            }
        }

        StopListening();
        _logger.LogInformation("GrayComparisonHostedService：退出。");
    }
}
