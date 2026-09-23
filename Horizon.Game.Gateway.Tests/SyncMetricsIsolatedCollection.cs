using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 6.3 — 同步指标测试串行集合。
/// <para>
/// 原因：<c>SyncMetrics</c> 为**静态**仪表，MeterListener 捕获的是进程级测量流——\n若多个测试类并行记录同一仪表，测量事件会串扰进彼此的捕获，破坏「无 X 事件」类缺席断言。\n凡记录 <c>horizon.sync.ds.route.*</c> / <c>horizon.sync.corrections</c> 的测试类\n（DsRoutingMetricsTests / DsRoutingTests / DsSyncDataPlaneBreakerTests）均挂此集合，\n串行执行，各自捕获仅含本测试事件。
/// </summary>
[CollectionDefinition(SyncMetricsIsolatedCollection.Name, DisableParallelization = true)]
public class SyncMetricsIsolatedCollection
{
    public const string Name = "SyncMetricsIsolated";
}
