using System;
using System.Collections.Generic;

namespace HundunWorld.Game.RemoteVisibility.Contracts;

/// <summary>
/// 可见性诊断快照：接收/解码/应用/呈现四环节计数与差异定位（spec 4.4.1、5.4.1 规则 3）。
/// </summary>
public readonly record struct VisibilityDiagnosticsSnapshot(
    long SnapshotsReceived,
    long SnapshotsApplied,
    long DeltasApplied,
    long SpawnsApplied,
    long UpdatesApplied,
    long DespawnsApplied,
    long ActorsPresented,
    int ExpectedVisibleCount,
    int PresentedCount,
    int MissingCount);

/// <summary>
/// 可见性审计：核对"应可见远程实体集合"与"实际呈现集合"，缺失触发补建与告警。
/// </summary>
public interface IRemoteVisibilityAudit
{
    /// <summary>应可见远程实体集合（SnapshotApplySystem 导出，只读）。</summary>
    IReadOnlyCollection<ulong> ExpectedVisibleEntityIds { get; }

    /// <summary>实际呈现实体集合（FlaxActorSyncSystem 导出，只读）。</summary>
    IReadOnlyCollection<ulong> PresentedEntityIds { get; }

    /// <summary>当前缺失呈现的实体集合（应可见 − 实际呈现）。</summary>
    IReadOnlyCollection<ulong> GetMissingPresentations();

    /// <summary>执行一次核对：不一致时触发补建回调并输出告警（内部去重限频）。</summary>
    void RunReconciliation();

    /// <summary>分环节诊断快照。</summary>
    VisibilityDiagnosticsSnapshot GetDiagnosticsSnapshot();

    /// <summary>暂停告警（断线冻结期避免误报）。</summary>
    bool Paused { get; set; }
}