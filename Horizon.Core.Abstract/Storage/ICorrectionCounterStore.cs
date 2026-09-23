using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Game.Core.Observability;

/// <summary>
/// 跨进程修正计数存储（阶段 6.3 P2 — 自动回滚门修正率信号 / P2⑨ 灰度对比面板）。
/// <para>
/// Silo 进程（<see cref="CorrectionPublisher"/>）把 <c>horizon.sync.corrections</c> 仪表增量
/// 累加写入（<see cref="IncrementCorrectionsAsync"/> 总数 / <see cref="IncrementCorrectionsByReasonAsync"/>
/// 按 reason）；网关进程（RollbackWindowSampler / GrayComparisonHostedService）按窗口读取运行总数
/// 并自行计算增量（<see cref="GetCorrectionsTotalAsync"/> / <see cref="GetCorrectionsByReasonAsync"/>）——
/// 读侧不重置，多网关各自算 delta，天然无相互干扰。
/// </para>
/// <para>
/// 当前实现为 Redis（总数键 <c>cluster:{{clusterId}}:sync:corrections:total</c> + 按 reason 哈希键
/// <c>cluster:{{clusterId}}:sync:corrections:reasons</c>，见 Horizon.Strategy.Storage.Redis.
/// RedisCorrectionCounterStore）；OTLP 为单向推送、网关无法回读，故选 Redis 作汇聚通道。测试用内存实现。
/// </para>
/// </summary>
public interface ICorrectionCounterStore
{
    /// <summary>读取修正计数运行总数（网关窗口增量计算用；键不存在视为 0）。</summary>
    Task<long> GetCorrectionsTotalAsync();

    /// <summary>把增量累加进运行总数（Silo 发布用；多 Silo 并发 INCRBY 原子安全）。</summary>
    Task IncrementCorrectionsAsync(long delta);

    /// <summary>读取按 reason 维度的修正计数运行总数（P2⑨ 灰度对比面板用；键不存在视为空）。</summary>
    Task<IReadOnlyDictionary<string, long>> GetCorrectionsByReasonAsync();

    /// <summary>按 reason 维度把增量累加进运行总数（Silo 发布用；多 Silo 并发 HINCRBY 原子安全）。</summary>
    Task IncrementCorrectionsByReasonAsync(IReadOnlyDictionary<string, long> deltas);
}
