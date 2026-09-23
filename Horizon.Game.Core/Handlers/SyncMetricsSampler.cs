using System;
using Horizon.Game.Core.Observability;
using Horizon.Game.Message.Sync.Server;

namespace Horizon.Game.Core.Handlers;

/// <summary>
/// 阶段 5.2 — 同步链路指标采样（横切服务，从各流处理器提取）。
/// 统一承载 InputLag / ReconnectDecision / AoiChunkCount 的上报与过滤逻辑，
/// 使流处理器不再直接依赖静态 <see cref="SyncMetrics"/> 仪表（可单测、可替换采样后端）。
/// </summary>
public interface IMetricsSampler
{
    /// <summary>
    /// 记录输入滞后（客户端 input 到服务器 consume 的滞后，毫秒）。
    /// 仅记录有效值（0 &lt; lag &lt; 60s），异常值（负数/超时）静默跳过。
    /// </summary>
    void RecordInputLag(long inputClientTick);

    /// <summary>记录重连恢复决策（按 decision 维度）。</summary>
    void RecordReconnectDecision(ResumeDecision decision);

    /// <summary>记录 AOI 订阅 chunk 数。</summary>
    void RecordAoiChunkCount(long chunkCount);
}

/// <summary>
/// <see cref="IMetricsSampler"/> 的默认实现：包装 <see cref="SyncMetrics"/> 静态仪表。
/// </summary>
public sealed class SyncMetricsSampler : IMetricsSampler
{
    /// <summary>
    /// 判断输入滞后值是否应被记录（纯函数，供确定性单测）。
    /// </summary>
    public static bool ShouldRecordInputLag(long inputClientTick)
    {
        var lagMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - inputClientTick;
        return lagMs > 0 && lagMs < 60_000; // 过滤异常值（负数或超过60秒）
    }

    /// <inheritdoc />
    public void RecordInputLag(long inputClientTick)
    {
        if (!ShouldRecordInputLag(inputClientTick)) return;
        SyncMetrics.InputLagMs.Record(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - inputClientTick);
    }

    /// <inheritdoc />
    public void RecordReconnectDecision(ResumeDecision decision)
    {
        SyncMetrics.ReconnectDecisions.Add(1, new System.Collections.Generic.KeyValuePair<string, object?>("decision", decision.ToString()));
    }

    /// <inheritdoc />
    public void RecordAoiChunkCount(long chunkCount)
    {
        SyncMetrics.AoiChunkCount.Record(chunkCount);
    }
}
