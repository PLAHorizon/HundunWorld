using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Services;

/// <summary>阶段 6.3 P2 — 自动回滚门窗口采样器接口（可注入 fake 供判定宿主测试）。</summary>
public interface IRollbackWindowSampler
{
    /// <summary>启动/保持监听（幂等）。启用前先清除累计值，避免把空闲期数据带入首个窗口。</summary>
    void EnsureWatching();

    /// <summary>
    /// 消费自上次采样以来的累计增量并重置，返回窗口样本（窗口期内无流量时 Attempts=0）。
    /// 异步：修正计数跨进程读取（Redis）发生在采样点，首窗口基线化（不把历史总量当窗口增量）。
    /// </summary>
    Task<RollbackWindow> SampleWindowAsync();

    /// <summary>停止监听并清空累计值。</summary>
    void Stop();
}

/// <summary>一个采样窗口的路由/延迟/修正增量数据。</summary>
public readonly record struct RollbackWindow(
    long Attempts,
    long Fallbacks,
    long Corrections,
    double LatencySumMs,
    long LatencyCount)
{
    /// <summary>窗口回退率（fallback/attempts；无尝试时为 0，由 MinAttemptsPerWindow 兜底判流量不足）。</summary>
    public double FallbackRate => Attempts > 0 ? (double)Fallbacks / Attempts : 0;

    /// <summary>窗口平均转发延迟（毫秒；无延迟样本时为 0）。</summary>
    public double MeanLatencyMs => LatencyCount > 0 ? LatencySumMs / LatencyCount : 0;
}
