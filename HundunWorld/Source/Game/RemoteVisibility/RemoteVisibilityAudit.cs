using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Horizon.Game.ECS.Arch.Systems;
using HundunWorld.Game.Network;
using HundunWorld.Game.RemoteVisibility.Contracts;

namespace HundunWorld.Game.RemoteVisibility;

/// <summary>
/// 可见性审计：核对"应可见远程实体集合"（SnapshotApplySystem 导出）与"实际呈现集合"
/// （FlaxActorSyncSystem 导出），缺失触发补建并输出限频告警（spec 5.4.1 规则 1/2、4.4.3）。
/// </summary>
public sealed class RemoteVisibilityAudit : IRemoteVisibilityAudit
{
    private readonly Func<IReadOnlyCollection<ulong>> _getExpectedVisible;
    private readonly Func<IReadOnlyCollection<ulong>> _getPresented;
    private readonly Action? _reconcileCallback;

    /// <summary>分环节计数提供者（SnapshotApplySystem 统计字段，可空）。</summary>
    private readonly Func<VisibilityDiagnosticsSnapshot>? _diagnosticsProvider;

    /// <summary>当前已触发补建的缺失实体集合（去重，避免补建风暴）。</summary>
    private readonly HashSet<ulong> _pendingRebuild = new();

    /// <summary>上次核对时间（Stopwatch ticks，用于周期限频）。</summary>
    private long _lastCheckTicks;

    /// <summary>上次告警输出时间（Stopwatch ticks，同批每秒 ≤1 条）。</summary>
    private long _lastAlertTicks;

    /// <summary>核对次数（观测）。</summary>
    public long ReconciliationCount { get; private set; }

    /// <summary>累计缺失告警次数（观测）。</summary>
    public long MissingAlertCount => _missingAlertCount;
    private long _missingAlertCount;

    /// <summary>当前是否暂停告警（断线冻结期）。</summary>
    public bool Paused { get; set; }

    /// <summary>
    /// 初始化可见性审计。
    /// </summary>
    /// <param name="getExpectedVisible">应可见远程实体集合提供者（SnapshotApplySystem.GetRemoteEntityIds）。</param>
    /// <param name="getPresented">实际呈现实体集合提供者（FlaxActorSyncSystem.GetPresentedEntityIds）。</param>
    /// <param name="reconcileCallback">补建回调（FlaxActorSyncSystem 公开补建入口，可空）。</param>
    /// <param name="diagnosticsProvider">分环节计数提供者（可空，默认从 ClientSyncMetrics 汇总）。</param>
    public RemoteVisibilityAudit(
        Func<IReadOnlyCollection<ulong>> getExpectedVisible,
        Func<IReadOnlyCollection<ulong>> getPresented,
        Action? reconcileCallback = null,
        Func<VisibilityDiagnosticsSnapshot>? diagnosticsProvider = null)
    {
        _getExpectedVisible = getExpectedVisible ?? throw new ArgumentNullException(nameof(getExpectedVisible));
        _getPresented = getPresented ?? throw new ArgumentNullException(nameof(getPresented));
        _reconcileCallback = reconcileCallback;
        _diagnosticsProvider = diagnosticsProvider;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<ulong> ExpectedVisibleEntityIds => SafeCollect(_getExpectedVisible);

    /// <inheritdoc />
    public IReadOnlyCollection<ulong> PresentedEntityIds => SafeCollect(_getPresented);

    /// <inheritdoc />
    public IReadOnlyCollection<ulong> GetMissingPresentations()
    {
        try
        {
            var expected = new HashSet<ulong>(SafeCollect(_getExpectedVisible));
            var presented = SafeCollect(_getPresented);
            expected.ExceptWith(presented);
            return expected.ToArray();
        }
        catch (Exception ex)
        {
            // 数据源异常 → 跳过本次核对，不影响观测链路（spec 5.4.1 规则 4 不扩散）。
            System.Diagnostics.Debug.WriteLine($"[RemoteVisibilityAudit] 计算缺失集合异常: {ex.Message}");
            return Array.Empty<ulong>();
        }
    }

    /// <inheritdoc />
    public void RunReconciliation()
    {
        try
        {
            ReconciliationCount++;

            var missing = GetMissingPresentations();
            var expectedCount = SafeCollect(_getExpectedVisible).Count;
            var presentedCount = SafeCollect(_getPresented).Count;

            if (missing.Count == 0)
            {
                _pendingRebuild.Clear();
                UpdateMetrics(expectedCount, presentedCount, 0, 0);
                return;
            }

            if (Paused)
            {
                // 断线冻结期：仅记录计数，不告警不补建（避免误报）。
                UpdateMetrics(expectedCount, presentedCount, missing.Count, 0);
                return;
            }

            UpdateMetrics(expectedCount, presentedCount, missing.Count, 0);

            // 去重：仅对新增缺失实体触发补建。
            var newMissing = missing.Where(id => !_pendingRebuild.Contains(id)).ToArray();
            if (newMissing.Length == 0)
            {
                return;
            }

            foreach (var id in newMissing)
            {
                _pendingRebuild.Add(id);
            }

            // 限频：同批缺失告警每秒 ≤1 条（spec 4.4.3）。
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            var elapsedSinceAlert = (now - _lastAlertTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
            if (elapsedSinceAlert >= 1.0 || _lastAlertTicks == 0)
            {
                _lastAlertTicks = now;
                _missingAlertCount++;
                UpdateMetrics(expectedCount, presentedCount, missing.Count, 1);
                System.Diagnostics.Debug.WriteLine(
                    $"[RemoteVisibilityAudit] 缺失呈现告警: MissingCount={missing.Count}, 新触发补建={newMissing.Length}, 首批={string.Join(",", newMissing.Take(5))}");
            }

            // 触发补建（去重限频由 FlaxActorSyncSystem 既有机制叠加防护）。
            try
            {
                _reconcileCallback?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RemoteVisibilityAudit] 补建回调异常被隔离: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            // 本次核对异常不扩散（spec 5.4.1 规则 4）。
            System.Diagnostics.Debug.WriteLine($"[RemoteVisibilityAudit] 核对异常被隔离: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public VisibilityDiagnosticsSnapshot GetDiagnosticsSnapshot()
    {
        // 优先使用注入的分环节计数提供者（SnapshotApplySystem 统计字段，spec 4.4.1）。
        if (_diagnosticsProvider != null)
        {
            try
            {
                return _diagnosticsProvider();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RemoteVisibilityAudit] 分环节诊断提供者异常被隔离: {ex.Message}");
            }
        }

        var expectedCount = SafeCollect(_getExpectedVisible).Count;
        var presentedCount = SafeCollect(_getPresented).Count;
        var missing = GetMissingPresentations();

        return new VisibilityDiagnosticsSnapshot(
            SnapshotsReceived: ClientSyncMetrics.SnapshotsReceived,
            SnapshotsApplied: 0,
            DeltasApplied: 0,
            SpawnsApplied: 0,
            UpdatesApplied: 0,
            DespawnsApplied: 0,
            ActorsPresented: presentedCount,
            ExpectedVisibleCount: expectedCount,
            PresentedCount: presentedCount,
            MissingCount: missing.Count);
    }

    private static IReadOnlyCollection<ulong> SafeCollect(Func<IReadOnlyCollection<ulong>> source)
    {
        try
        {
            return source() ?? Array.Empty<ulong>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RemoteVisibilityAudit] 数据源读取异常被隔离: {ex.Message}");
            return Array.Empty<ulong>();
        }
    }

    private static void UpdateMetrics(int expected, int presented, int missingCount, int alertDelta)
    {
        ClientSyncMetrics.UpdateVisibilityCounters(expected, presented, missingCount, alertDelta);
    }
}