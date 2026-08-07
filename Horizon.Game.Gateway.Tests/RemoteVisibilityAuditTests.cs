using System;
using System.Collections.Generic;
using HundunWorld.Game.Network;
using HundunWorld.Game.RemoteVisibility;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 可见性审计（RemoteVisibilityAudit）单元测试：覆盖 spec 5.4.1 规则 1/2、5.4.3 异常 2。
/// </summary>
public class RemoteVisibilityAuditTests : IDisposable
{
    public RemoteVisibilityAuditTests()
    {
        ClientSyncMetrics.Reset();
    }

    public void Dispose()
    {
        ClientSyncMetrics.Reset();
    }

    private static (RemoteVisibilityAudit Audit, List<ulong> Rebuilt) Build(
        HashSet<ulong> expected,
        HashSet<ulong> presented,
        bool paused = false)
    {
        var rebuilt = new List<ulong>();
        var audit = new RemoteVisibilityAudit(
            getExpectedVisible: () => new List<ulong>(expected),
            getPresented: () => new List<ulong>(presented),
            reconcileCallback: () => rebuilt.Add(9999))
        {
            Paused = paused,
        };
        return (audit, rebuilt);
    }

    [Fact]
    public void GetMissingPresentations_ExpectedMinusPresented()
    {
        var expected = new HashSet<ulong> { 1001, 1002, 1003 };
        var presented = new HashSet<ulong> { 1001 };
        var (audit, _) = Build(expected, presented);

        var missing = audit.GetMissingPresentations();

        Assert.Equal(2, missing.Count);
        Assert.Contains(1002UL, missing);
        Assert.Contains(1003UL, missing);
    }

    [Fact]
    public void RunReconciliation_AllPresented_NoAlert()
    {
        var expected = new HashSet<ulong> { 1001 };
        var presented = new HashSet<ulong> { 1001 };
        var (audit, rebuilt) = Build(expected, presented);

        audit.RunReconciliation();

        Assert.Empty(rebuilt);
        Assert.Equal(0, audit.MissingAlertCount);
        Assert.Equal(0, ClientSyncMetrics.MissingPresentationCount);
    }

    [Fact]
    public void RunReconciliation_MissingTriggersRebuildAndAlert()
    {
        var expected = new HashSet<ulong> { 1001, 1002 };
        var presented = new HashSet<ulong> { 1001 };
        var (audit, rebuilt) = Build(expected, presented);

        audit.RunReconciliation();

        Assert.Single(rebuilt);          // 触发一次补建回调
        Assert.Equal(1, audit.MissingAlertCount);
        Assert.Equal(1, ClientSyncMetrics.MissingPresentationCount);
    }

    [Fact]
    public void RunReconciliation_MissingDedup_NoRebuildStorm()
    {
        var expected = new HashSet<ulong> { 1001, 1002 };
        var presented = new HashSet<ulong> { 1001 };
        var (audit, rebuilt) = Build(expected, presented);

        // 同一批缺失：第二次核对不重复触发补建（去重）。
        audit.RunReconciliation();
        audit.RunReconciliation();

        Assert.Single(rebuilt);          // 仅触发一次补建
    }

    [Fact]
    public void RunReconciliation_Paused_NoAlertNoRebuild()
    {
        var expected = new HashSet<ulong> { 1001, 1002 };
        var presented = new HashSet<ulong> { 1001 };
        var (audit, rebuilt) = Build(expected, presented, paused: true);

        audit.RunReconciliation();

        Assert.Empty(rebuilt);
        Assert.Equal(0, audit.MissingAlertCount);
        Assert.Equal(1, ClientSyncMetrics.MissingPresentationCount); // 计数仍记录，但不告警
    }

    [Fact]
    public void RunReconciliation_DataSourceThrows_Isolated()
    {
        // 应可见数据源抛异常 → 跳过本次核对，不扩散（spec 5.4.1 规则 4）。
        var audit = new RemoteVisibilityAudit(
            getExpectedVisible: () => throw new InvalidOperationException("数据源异常"),
            getPresented: () => new List<ulong>());

        audit.RunReconciliation(); // 不应抛出

        Assert.Equal(1, audit.ReconciliationCount);
    }

    [Fact]
    public void GetDiagnosticsSnapshot_CountsExpectedAndPresented()
    {
        var expected = new HashSet<ulong> { 1001, 1002 };
        var presented = new HashSet<ulong> { 1001 };
        var (audit, _) = Build(expected, presented);

        var diag = audit.GetDiagnosticsSnapshot();

        Assert.Equal(2, diag.ExpectedVisibleCount);
        Assert.Equal(1, diag.PresentedCount);
        Assert.Equal(1, diag.MissingCount);
    }

    [Fact]
    public void ReconcileCallbackThrows_IsIsolated()
    {
        var expected = new HashSet<ulong> { 1001 };
        var presented = new HashSet<ulong>();
        var audit = new RemoteVisibilityAudit(
            getExpectedVisible: () => new List<ulong>(expected),
            getPresented: () => new List<ulong>(presented),
            reconcileCallback: () => throw new InvalidOperationException("补建异常"));

        audit.RunReconciliation(); // 不应抛出

        Assert.Equal(1, audit.MissingAlertCount);
    }
}