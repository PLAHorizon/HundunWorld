using System;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core.Observability;
using Horizon.Game.Gateway.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Horizon.Game.Gateway.Services;

/// <summary>
/// 阶段 6.3 P2 — 自动回滚门判定宿主：每 <see cref="GatewayOptions.DsRollbackOptions.WindowSeconds"/>
/// 采样 <see cref="IRollbackWindowSampler"/> 的窗口增量，按「回退率、平均延迟或修正率连续超阈」触发
/// <see cref="IRollbackCoordinator.Arm"/>（数据面内存 override 强制回切 UseDS）；
/// 冷却到期后连续健康窗口达标 → <see cref="IRollbackCoordinator.Clear"/> 自愈恢复。
/// </summary>
/// <remarks>
/// <para>防振荡设计：单窗抖动不触发（连续坏窗口数）；恢复需要冷却 + 连续健康窗口数，双保险。</para>
/// <para>零流量防误报：窗口尝试数 &lt; MinAttemptsPerWindow 视为流量不足，不参与判定（坏/健康计数均不动）。</para>
/// <para>自愈循环语义：Arm 后数据面停止路由 → 回退率自然归零 → 冷却到期后窗口变健康 → 恢复；
/// 若根因仍在（DS 未恢复），恢复后首个坏窗口再次 Arm（刷新冷却）——门只在持续故障时保持闭合。</para>
/// <para>UseDS=false 时零副作用（数据面无尝试、无仪表写入；本服务仅空采样）。</para>
/// </remarks>
public sealed class RollbackGateHostedService : BackgroundService
{
    private readonly IRollbackCoordinator _coordinator;
    private readonly IRollbackWindowSampler _sampler;
    private readonly IOptionsMonitor<GatewayOptions> _options;
    private readonly ILogger<RollbackGateHostedService> _logger;

    private int _consecutiveBadWindows;
    private int _consecutiveHealthyWindows;

    /// <summary>最近一次窗口判定结果（诊断/测试）。</summary>
    public string LastDecision { get; private set; } = "startup";

    /// <summary>最近一次采样的窗口（诊断/测试）。</summary>
    public RollbackWindow LastWindow { get; private set; }

    public RollbackGateHostedService(
        IRollbackCoordinator coordinator,
        IRollbackWindowSampler sampler,
        IOptionsMonitor<GatewayOptions> options,
        ILogger<RollbackGateHostedService> logger)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RollbackGateHostedService：启动（自动回滚门就绪）。");
        var interval = GetInterval(_options.CurrentValue);

        while (!stoppingToken.IsCancellationRequested)
        {
            // 幂等：启用前清空累计（采样器只在被调用时开始监听，false→true 热启动的首窗口干净）。
            _sampler.EnsureWatching();

            var window = await _sampler.SampleWindowAsync();
            LastWindow = window;
            Evaluate(window, _options.CurrentValue);

            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _sampler.Stop();
        _logger.LogInformation("RollbackGateHostedService：退出。");
    }

    private static TimeSpan GetInterval(GatewayOptions opts)
    {
        var seconds = opts.DsRollback?.WindowSeconds ?? 30;
        if (seconds < 1) seconds = 30;
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>测试入口：直接驱动单窗口判定（避免后台循环时序依赖；生产路径为 <see cref="ExecuteAsync"/> 的周期调用）。</summary>
    public void EvaluateWindowForTest(RollbackWindow window, GatewayOptions opts) => Evaluate(window, opts);

    private void Evaluate(RollbackWindow window, GatewayOptions opts)
    {
        var rollback = opts.DsRollback ?? new GatewayOptions.DsRollbackOptions();
        var minAttempts = Math.Max(1, rollback.MinAttemptsPerWindow);
        var fallbackThreshold = Math.Clamp(rollback.FallbackRateThreshold, 0.0, 1.0);
        var latencyThreshold = Math.Max(0, rollback.LatencyMsThreshold);
        var correctionsThreshold = Math.Max(0, rollback.CorrectionsPerWindowThreshold);
        var badNeeded = Math.Max(1, rollback.ConsecutiveBadWindowsToTrigger);
        var healthyNeeded = Math.Max(1, rollback.ConsecutiveHealthyWindowsToRecover);

        // 流量不足：回退/延迟信号需要足够路由尝试；修正信号以自身阈值判流量（修正本身就是流量，
        // 与网关路由量无关——Silo 权威模拟对所有会话产生修正）。不参与判定的窗口坏/健康计数均不动。
        if (window.Attempts < minAttempts && (correctionsThreshold <= 0 || window.Corrections < correctionsThreshold))
        {
            LastDecision = $"insufficient_traffic({window.Attempts}<{minAttempts}, corrections {window.Corrections}<{correctionsThreshold})";
            return;
        }

        var isBad = window.FallbackRate > fallbackThreshold
            || window.MeanLatencyMs > latencyThreshold
            || (correctionsThreshold > 0 && window.Corrections >= correctionsThreshold);
        if (isBad)
        {
            // 坏窗口：重置恢复计数；若 override 已 arm 且冷却已过（评估恢复期），立即重 Arm（根因复发 → 刷新冷却）。
            _consecutiveHealthyWindows = 0;
            if (_coordinator.IsOverrideArmed && !_coordinator.IsRollbackActive)
            {
                _coordinator.Arm();
                SyncMetrics.RollbacksTriggered.Add(1);
                _logger.LogWarning(
                    "RollbackGateHostedService：恢复评估期再次出现坏窗口（回退率 {Rate:P0}，延迟 {Latency:F0}ms，修正 {Corrections}），重新回切（刷新冷却）。",
                    window.FallbackRate, window.MeanLatencyMs, window.Corrections);
                LastDecision = $"relapse({window.FallbackRate:P0} fallback, {window.MeanLatencyMs:F0}ms, corrections {window.Corrections})";
                return;
            }

            _consecutiveBadWindows++;
            LastDecision = $"bad({window.FallbackRate:P0} fallback, {window.MeanLatencyMs:F0}ms, corrections {window.Corrections}, {_consecutiveBadWindows}/{badNeeded})";

            if (_consecutiveBadWindows >= badNeeded)
            {
                _coordinator.Arm();
                SyncMetrics.RollbacksTriggered.Add(1);
                _logger.LogWarning(
                    "RollbackGateHostedService：连续 {N} 个坏窗口（回退率 {Rate:P0}，延迟 {Latency:F0}ms，修正 {Corrections}），自动回切 UseDS（DS 通道临时摘除，冷却后自愈探测）。",
                    _consecutiveBadWindows, window.FallbackRate, window.MeanLatencyMs, window.Corrections);
                _consecutiveBadWindows = 0;
            }
            return;
        }

        // 健康窗口：坏计数归零；恢复计数仅在「override 已 arm 且冷却已过」的评估恢复期推进
        // （冷却期内 IsRollbackActive=true，健康窗口不计数——避免冷却尚未到期就提前恢复）。
        _consecutiveBadWindows = 0;
        if (!_coordinator.IsOverrideArmed || _coordinator.IsRollbackActive)
        {
            LastDecision = $"healthy({window.FallbackRate:P0} fallback, {window.MeanLatencyMs:F0}ms, corrections {window.Corrections})";
            return;
        }

        _consecutiveHealthyWindows++;
        LastDecision = $"recovering({_consecutiveHealthyWindows}/{healthyNeeded})";
        if (_consecutiveHealthyWindows >= healthyNeeded)
        {
            _coordinator.Clear();
            _consecutiveHealthyWindows = 0;
            _logger.LogInformation(
                "RollbackGateHostedService：连续 {N} 个健康窗口，DS 通道自愈恢复（override 解除）。",
                healthyNeeded);
        }
    }
}
