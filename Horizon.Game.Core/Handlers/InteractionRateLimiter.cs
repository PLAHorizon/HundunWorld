using System;
using System.Linq;

namespace Horizon.Game.Core.Handlers;

/// <summary>
/// 阶段 5.2 — per-key 最小间隔速率限制（横切服务，从 <see cref="SyncInteractionHandler"/> 提取）。
/// 线程安全（锁内仅字典操作），支持周期性清理超过 1 秒未活动的条目以控制内存占用。
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// 判定 key（如 interactorId）是否允许本次请求通过。线程安全。
    /// </summary>
    /// <param name="key">限流键（按此维度独立计数）。</param>
    /// <returns>true 允许通过；false 被限流（距上次请求小于最小间隔）。</returns>
    bool Allow(long key);
}

/// <summary>
/// <see cref="IRateLimiter"/> 的会话态实现：固定窗口近似（最小间隔毫秒），
/// 底层复用 <see cref="SyncSessionState"/> 的速率限制字典与锁。
/// </summary>
public sealed class SessionInteractionRateLimiter : IRateLimiter
{
    /// <summary>交互意图速率限制：每个 interactorId 每秒最多请求数。</summary>
    public const int InteractionRateLimitPerSecond = 10;

    /// <summary>两次请求之间的最小间隔（毫秒）= 1000 / <see cref="InteractionRateLimitPerSecond"/>。</summary>
    public const double InteractionRateLimitIntervalMs = 1000.0 / InteractionRateLimitPerSecond;

    private readonly SyncSessionState _sessionState;

    public SessionInteractionRateLimiter(SyncSessionState sessionState)
    {
        _sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
    }

    /// <inheritdoc />
    public bool Allow(long key)
    {
        var now = DateTime.UtcNow;

        lock (_sessionState.RateLimitLock)
        {
            // 周期性清理：移除超过 1 秒未活动的条目
            if ((now - _sessionState.LastRateLimitCleanup).TotalSeconds > 1)
            {
                _sessionState.LastRateLimitCleanup = now;
                var cutoff = now.AddSeconds(-1);
                var staleKeys = _sessionState.InteractionLastRequestTime
                    .Where(kvp => kvp.Value < cutoff)
                    .Select(kvp => kvp.Key)
                    .ToList();
                foreach (var staleKey in staleKeys)
                {
                    _sessionState.InteractionLastRequestTime.Remove(staleKey);
                }
            }

            // 速率限制：检查距上次请求的时间间隔
            if (_sessionState.InteractionLastRequestTime.TryGetValue(key, out var lastTime))
            {
                var elapsedMs = (now - lastTime).TotalMilliseconds;
                if (elapsedMs < InteractionRateLimitIntervalMs)
                {
                    return false;
                }
            }

            _sessionState.InteractionLastRequestTime[key] = now;
            return true;
        }
    }
}
