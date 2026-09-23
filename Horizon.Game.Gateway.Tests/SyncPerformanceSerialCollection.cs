using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 性能/预算断言测试的串行集合。
/// <para>
/// 这些测试以墙钟/CPU 预算为断言依据（单 tick ≤16.7ms、带宽 <X 等），
/// xUnit 默认并行运行不同类会互相抢占 CPU 导致预算超限的**随机失败**（基线即存在）。
/// 归入同一 Collection 串行执行，消除并行 CPU 竞争，使性能门禁稳定可回归。
/// </para>
/// </summary>
[CollectionDefinition("SyncPerformanceSerial", DisableParallelization = true)]
public class SyncPerformanceSerialCollection
{
}