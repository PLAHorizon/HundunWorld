using System;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Gateway.Configuration;
using Horizon.Game.Gateway.Monitoring;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Horizon.Game.Gateway.Services;

/// <summary>
/// 阶段 2.5 — DSRegistry 路由表自愈维护服务。<br/>
/// 周期调用 <see cref="DSRegistry.PruneStale"/> 摘除心跳过期的故障 DS（防止路由黑洞），
/// 并上报存活 DS 数 / 累计摘除数指标（<see cref="GatewayMetrics"/>），
/// 使「DS 注册 → 心跳续约 → 过期摘除 → 指标可见」形成自愈闭环。
/// </summary>
/// <remarks>
/// <para>
/// 灰度关（<see cref="GatewayOptions.UseDS"/>=false）时 <see cref="ExecuteAsync"/> 直接返回，
/// 不启动循环——保持老路径零副作用（与 <see cref="SyncDispatcherHostedService"/> 一致）。
/// </para>
/// <para>
/// 时钟通过 <see cref="DSRegistry"/> 的注入时钟驱动（注册表内部判定过期），
/// 本服务只负责节奏（<see cref="PruneInterval"/>）与指标上报；<c>pruneInterval</c> 可注入供测试加速。
/// </para>
/// <para>
/// 存活数指标用 UpDownCounter 增量上报（代码库既有 gauge 惯例，见 <see cref="GatewayMetrics.ActiveConnections"/>），
/// 由本服务在单线程循环内维护 delta，避免跨线程竞争。
/// </para>
/// </remarks>
public sealed class DSRegistryMaintenanceHostedService : BackgroundService
{
    /// <summary>默认修剪间隔（秒）。心跳 30s / 过期 90s，15s 周期保证故障 DS 在过期后 ≤15s 内被摘除。</summary>
    private const int DefaultPruneIntervalSeconds = 15;

    private readonly DSRegistry _registry;
    private readonly IOptionsMonitor<GatewayOptions> _options;
    private readonly ILogger<DSRegistryMaintenanceHostedService> _logger;
    private readonly TimeSpan _pruneInterval;
    private readonly IDSnapshotStore? _snapshotStore;

    private long _lastAliveCount;
    private long _totalPruned;

    /// <summary>最近一次上报的存活 DS 数（诊断/测试）。</summary>
    public long LastAliveCount => Interlocked.Read(ref _lastAliveCount);

    /// <summary>累计摘除的过期 DS 数（诊断/测试）。</summary>
    public long TotalPruned => Interlocked.Read(ref _totalPruned);

    public DSRegistryMaintenanceHostedService(
        DSRegistry registry,
        IOptionsMonitor<GatewayOptions> options,
        ILogger<DSRegistryMaintenanceHostedService> logger,
        TimeSpan? pruneInterval = null,
        IDSnapshotStore? snapshotStore = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pruneInterval = pruneInterval ?? TimeSpan.FromSeconds(DefaultPruneIntervalSeconds);
        _snapshotStore = snapshotStore;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "DSRegistryMaintenanceHostedService：启动（热切换感知：每 {Interval}s 检查 UseDS 开关）。",
            _pruneInterval.TotalSeconds);

        var enabledEver = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            // 运行时可感知开关（阶段 6.3 热切换）：UseDS=false 时空闲轮询（不修剪/不持久化/不触碰注册表），
            // 置 true 后自动开始维护——支持不重启网关热启动/热停，无需 OnChange 复杂接线。
            if (!_options.CurrentValue.UseDS)
            {
                if (enabledEver)
                {
                    _logger.LogInformation(
                        "DSRegistryMaintenanceHostedService：UseDS 已关闭，停止 DS 路由表维护（老路径）。");
                }
                enabledEver = false;
                try
                {
                    await Task.Delay(_pruneInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                continue;
            }

            if (!enabledEver)
            {
                _logger.LogInformation(
                    "DSRegistryMaintenanceHostedService：UseDS 已开启，启动 DS 路由表自愈维护，修剪间隔 {Interval}s。",
                    _pruneInterval.TotalSeconds);
                enabledEver = true;

                // 进入启用态时从持久化快照恢复（消除「等 DS 下一轮心跳」的恢复空窗；
                // 重启后首启与运行时 false→true 热启动共用此路径）。
                // 恢复失败/无快照 → 空注册表，DS 心跳照常重建（与无持久化时行为一致）。
                if (_snapshotStore is not null)
                {
                    try
                    {
                        var restored = await _snapshotStore.RestoreSnapshotAsync();
                        if (restored is { Count: > 0 })
                        {
                            _registry.RestoreSnapshot(restored);
                            _logger.LogInformation(
                                "DSRegistryMaintenanceHostedService：已从持久化快照恢复 {Count} 个 DS 条目（重启空窗消除）。",
                                restored.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "DSRegistryMaintenanceHostedService：快照恢复失败，回退为空注册表（DS 心跳将自动重建）。");
                    }
                }
            }

            try
            {
                var pruned = _registry.PruneStale();
                var alive = _registry.CountAlive();

                // 存活数为 gauge：按与上次的差量上报，保持 UpDownCounter 与真实值一致。
                var previous = Interlocked.Exchange(ref _lastAliveCount, alive);
                var delta = alive - previous;
                if (delta != 0)
                {
                    GatewayMetrics.ActiveDsServers.Add(delta);
                }

                if (pruned > 0)
                {
                    GatewayMetrics.DsServersPrunedTotal.Add(pruned);
                    Interlocked.Add(ref _totalPruned, pruned);
                    _logger.LogWarning(
                        "DSRegistryMaintenanceHostedService：摘除 {Pruned} 个心跳过期的 DS。当前存活 {Alive} 个。",
                        pruned, alive);
                }
                else if (alive != previous)
                {
                    _logger.LogDebug(
                        "DSRegistryMaintenanceHostedService：DS 存活数变化 {Previous}→{Alive}。",
                        previous, alive);
                }

                // 持久化当前快照（best-effort：失败仅记日志，内存路由不受影响，下轮重试）。
                if (_snapshotStore is not null)
                {
                    try
                    {
                        await _snapshotStore.SaveSnapshotAsync(_registry.Snapshot());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "DSRegistryMaintenanceHostedService：快照持久化失败（下轮维护周期重试）。");
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // 维护循环不允许因单次异常退出：记录后继续下一轮（自愈闭环的兜底）。
                _logger.LogError(ex, "DSRegistryMaintenanceHostedService：单轮维护异常，{Delay}s 后重试。",
                    _pruneInterval.TotalSeconds);
            }

            try
            {
                await Task.Delay(_pruneInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation(
            "DSRegistryMaintenanceHostedService：退出。累计摘除 {Pruned} 个过期 DS，最后存活 {Alive} 个。",
            TotalPruned, LastAliveCount);
    }
}
