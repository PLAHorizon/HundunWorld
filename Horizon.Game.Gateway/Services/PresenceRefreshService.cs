using System.Collections.Concurrent;
using Horizon.Game.Core.Sim.Server;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.Gateway.Services;

/// <summary>
/// Presence TTL 兜底刷新服务（从 GameNetworkServer 提取）。<br/>
/// 修复 BUG（心跳 TTL 反复过低）：客户端只发送 InputPacket 不发送 Heartbeat 消息，
/// 导致 Redis presence TTL 在 90 秒后过期。<br/>
/// 本服务在收到已绑定角色的数据时，每 30 秒刷新一次 presence TTL，
/// 确保只要客户端在发送输入，角色在线状态就不会过期。<br/>
/// 不阻塞消息处理（fire-and-forget + 异常吞并）。
/// </summary>
public sealed class PresenceRefreshService
{
    private readonly ICharacterPresenceStore _presenceStore;
    private readonly ILogger<PresenceRefreshService> _logger;

    /// <summary>
    /// 每角色上次兜底刷新 presence 的时间。<br/>
    /// 限制刷新频率为每 30 秒一次（避免每帧 InputPacket 都刷新 Redis）。
    /// </summary>
    private readonly ConcurrentDictionary<long, DateTime> _lastRefreshByCharacter = new();

    /// <summary>presence TTL 兜底刷新的最小间隔（秒）。</summary>
    private const int RefreshIntervalSeconds = 30;

    public PresenceRefreshService(
        ICharacterPresenceStore presenceStore,
        ILogger<PresenceRefreshService> logger)
    {
        _presenceStore = presenceStore ?? throw new ArgumentNullException(nameof(presenceStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 兜底刷新角色 presence TTL（fire-and-forget，不阻塞调用方）。<br/>
    /// 频率限制：每 30 秒最多刷新一次。异常被吞并（仅 Debug 日志），
    /// 避免 Redis 故障影响消息处理主流程。
    /// </summary>
    /// <param name="characterId">角色 ID。</param>
    public void TryRefreshInBackground(long characterId)
    {
        // 频率限制：每 30 秒最多刷新一次，避免每帧 InputPacket 都刷新 Redis
        var now = DateTime.UtcNow;
        if (_lastRefreshByCharacter.TryGetValue(characterId, out var lastRefresh))
        {
            if ((now - lastRefresh).TotalSeconds < RefreshIntervalSeconds)
                return;
        }

        // 使用 AddOrUpdate 原子更新刷新时间戳，防止多 worker 竞态。
        // 注意：此处先更新再刷新。即使刷新失败，下次也将等待 30 秒后重试，
        // 避免 Redis 瞬时故障导致高频重试打满 Redis 连接。
        // 竞态修复：更新前先检查 TryGetValue，再用 AddOrUpdate 确保原子性。
        _lastRefreshByCharacter.AddOrUpdate(characterId, now, (_, _) => now);

        // fire-and-forget：不阻塞 OnDataReceived 主流程
        _ = Task.Run(async () =>
        {
            try
            {
                var refreshed = await _presenceStore.RefreshHeartbeatAsync(characterId).ConfigureAwait(false);
                if (!refreshed)
                {
                    // presence key 不存在（可能角色已下线或 Redis 故障）。
                    // 不重建 presence（与 HeartbeatHandler 一致：避免已下线角色在 Redis 中"复活"）。
                    _logger.LogDebug(
                        "presence TTL 兜底刷新返回 false，可能角色已下线或 Redis 故障。CharacterId={CharacterId}",
                        characterId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "presence TTL 兜底刷新异常（不影响消息处理）。CharacterId={CharacterId}",
                    characterId);
            }
        });
    }

    /// <summary>
    /// 移除角色的刷新时间记录（连接清理时调用，避免内存泄漏）。
    /// </summary>
    /// <param name="characterId">角色 ID。</param>
    public void RemoveCharacter(long characterId)
    {
        _lastRefreshByCharacter.TryRemove(characterId, out _);
    }
}
