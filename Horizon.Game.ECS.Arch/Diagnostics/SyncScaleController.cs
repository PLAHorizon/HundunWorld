using System;
using System.Collections.Generic;

namespace Horizon.Game.ECS.Arch.Diagnostics;

/// <summary>
/// 客户端规模档位控制器（spec 5.5.1.3 / 5.5.1.4，超大规模容量治理）。
/// </summary>
/// <remarks>
/// <para>
/// 档位语义（按同屏远程实体数）：
/// </para>
/// <list type="bullet">
///   <item><b>Tier0</b>：≤ 20，全帧平滑呈现。</item>
///   <item><b>Tier1</b>：≤ 100。</item>
///   <item><b>Tier2</b>：≤ 1000。</item>
///   <item><b>Tier3</b>：≤ 5000。</item>
///   <item><b>OverLimit</b>：&gt; 5000，需对最远实体降级。</item>
/// </list>
/// <para>
/// <b>最远优先降级策略</b>：同屏实体数超当前档位阈值时，按与本地玩家距离排序选取最远实体
/// "暂停平滑推进或降低更新优先级"，但<b>不得从订阅集无声丢失</b>（spec 5.5.1.3）。
/// 档位回落（<see cref="Restore"/>）后原降级实体恢复全帧平滑呈现；恢复时偏移按既有 3 档传送策略处理覆盖，无闪跳。
/// </para>
/// <para>
/// <b>诊断</b>：档位切换触发 <see cref="TierChanged"/> 与 <see cref="ISyncDiagnosticsSink.OnScaleTierChanged"/>，
/// 单实体降级触发 <see cref="ISyncDiagnosticsSink.OnScaleDegrade"/>（含距离与原因）。
/// </para>
/// </remarks>
public sealed class SyncScaleController
{
    /// <summary>档位切换通知（参数：切换前档位、切换后档位）。</summary>
    public event Action<SyncScaleTier, SyncScaleTier>? TierChanged;

    private SyncScaleTier _currentTier = SyncScaleTier.Tier0;
    private int[] _tierThresholds = { 20, 100, 1000, 5000 };
    private readonly HashSet<ulong> _degradedEntityIds = new();
    private readonly List<(ulong EntityId, float Distance)> _sortBuffer = new();
    private ISyncDiagnosticsSink? _diagnostics;
    private int _lastDegradeDiagCount = -1;

    /// <summary>当前档位。</summary>
    public SyncScaleTier CurrentTier => _currentTier;

    /// <summary>档位阈值（实体数），必须严格递增且全部 &gt; 0。</summary>
    public int[] TierThresholds
    {
        get => _tierThresholds;
        set
        {
            if (IsValidThresholds(value))
                _tierThresholds = value;
        }
    }

    /// <summary>诊断事件汇（可选），由 ECSUpdateDriver 注入。</summary>
    public ISyncDiagnosticsSink? Diagnostics
    {
        get => _diagnostics;
        set => _diagnostics = value;
    }

    /// <summary>当前被降级的实体 ID 集合（只读视图，供 InterpolationSystem 使用）。</summary>
    public IReadOnlyCollection<ulong> DegradedEntityIds => _degradedEntityIds;

    /// <summary>同屏远程实体数变化时调用（每帧由 FlaxActorSyncSystem 汇报）。</summary>
    /// <param name="count">当前同屏远程实体数。</param>
    public void OnRemoteEntityCountChanged(int count)
    {
        var tier = ClassifyTier(count);
        if (tier == _currentTier)
        {
            return;
        }

        var from = _currentTier;
        _currentTier = tier;
        TierChanged?.Invoke(from, tier);
        _diagnostics?.OnScaleTierChanged(count, from, tier);
    }

    /// <summary>
    /// 超档位时按距离排序选取最远实体进行降级（暂停插值推进，不移除订阅、不销毁 Actor）。
    /// 距离由上层 <c>FlaxActorSyncSystem</c> 计算（其持有实体位置与本地玩家位置）。
    /// </summary>
    /// <param name="farEntities">同屏远程实体（EntityId 与距本地玩家距离）。</param>
    public void ApplyDegradeTo(IEnumerable<(ulong EntityId, float Distance)> farEntities)
    {
        if (farEntities is null) return;

        _sortBuffer.Clear();
        foreach (var item in farEntities)
        {
            _sortBuffer.Add(item);
        }

        // 按距离降序：最远实体优先降级。
        _sortBuffer.Sort(static (a, b) => b.Distance.CompareTo(a.Distance));

        // 计算应降级的实体数：超出当前档位阈值的部分。
        var cap = GetCapForTier(_currentTier);
        var overCount = Math.Max(0, _sortBuffer.Count - cap);

        _degradedEntityIds.Clear();
        for (int i = 0; i < Math.Min(overCount, _sortBuffer.Count); i++)
        {
            _degradedEntityIds.Add(_sortBuffer[i].EntityId);
        }

        // 诊断：仅当本次降级集合规模变化时输出（持续过程限频为一条启动事件）。
        if (_sortBuffer.Count != _lastDegradeDiagCount)
        {
            for (int i = 0; i < Math.Min(overCount, _sortBuffer.Count); i++)
            {
                _diagnostics?.OnScaleDegrade(_sortBuffer[i].EntityId, _sortBuffer[i].Distance, "ScaleOverLimit");
            }
            _lastDegradeDiagCount = _sortBuffer.Count;
        }
    }

    /// <summary>
    /// 档位回落时恢复原降级实体（全帧平滑呈现，无闪跳——偏移按既有 3 档传送处理覆盖）。
    /// </summary>
    /// <param name="restoredEntities">恢复全帧平滑呈现的实体 ID 集合。</param>
    public void Restore(IEnumerable<ulong> restoredEntities)
    {
        if (restoredEntities is null) return;
        foreach (var id in restoredEntities)
        {
            _degradedEntityIds.Remove(id);
        }
    }

    /// <summary>清空全部降级标记（档位大幅回落时调用）。</summary>
    public void ClearDegraded()
    {
        _degradedEntityIds.Clear();
    }

    private SyncScaleTier ClassifyTier(int count)
    {
        if (count <= _tierThresholds[0]) return SyncScaleTier.Tier0;
        for (int i = 1; i < _tierThresholds.Length; i++)
        {
            if (count <= _tierThresholds[i]) return (SyncScaleTier)i;
        }
        return SyncScaleTier.OverLimit;
    }

    private int GetCapForTier(SyncScaleTier tier)
    {
        var idx = (int)tier;
        if (idx < 0 || idx >= _tierThresholds.Length) return _tierThresholds[_tierThresholds.Length - 1];
        return _tierThresholds[idx];
    }

    private static bool IsValidThresholds(int[]? thresholds)
    {
        if (thresholds is null || thresholds.Length == 0) return false;
        for (int i = 0; i < thresholds.Length; i++)
        {
            if (thresholds[i] <= 0) return false;
            if (i > 0 && thresholds[i] <= thresholds[i - 1]) return false;
        }
        return true;
    }
}