using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>
/// 角色在线状态存储接口（Redis 实现）。<br/>
/// 使用独立 Redis Key + TTL 管理角色在线状态，与 Orleans GrainStorage 分离（双轨制）。<br/>
/// Key 设计：character:presence:{characterId} → Hash { gatewayId, connectionId, lastHeartbeat }<br/>
/// TTL = 90 秒（心跳间隔 30 秒 × 3 倍容错），过期自动清理，避免服务器崩溃后残留 IsOnline=true。
/// </summary>
public interface ICharacterPresenceStore
{
    /// <summary>
    /// 角色上线：设置 presence key + TTL。<br/>
    /// 在 EnterGameAsync 中调用，记录角色所在网关和连接。
    /// </summary>
    /// <param name="characterId">角色 ID</param>
    /// <param name="gatewayId">网关标识（用于多网关部署时定位角色）</param>
    /// <param name="connectionId">连接标识（用于断线清理）</param>
    /// <returns>true=设置成功；false=设置失败（Redis 不可用时降级）</returns>
    Task<bool> SetOnlineAsync(long characterId, string gatewayId, string connectionId);

    /// <summary>
    /// 角色下线：删除 presence key。<br/>
    /// 在 GoOfflineAsync / DespawnImmediatelyAsync 中调用。
    /// </summary>
    /// <param name="characterId">角色 ID</param>
    /// <returns>true=删除成功或 key 不存在；false=删除失败</returns>
    Task<bool> SetOfflineAsync(long characterId);

    /// <summary>
    /// 心跳更新：刷新 TTL 和 LastHeartbeat 字段。<br/>
    /// 由客户端心跳消息触发（每 30 秒一次），保持在线状态不过期。
    /// </summary>
    /// <param name="characterId">角色 ID</param>
    /// <returns>true=续期成功；false=续期失败或 key 不存在</returns>
    Task<bool> RefreshHeartbeatAsync(long characterId);

    /// <summary>
    /// 查询单个角色在线状态。
    /// </summary>
    /// <param name="characterId">角色 ID</param>
    /// <returns>true=在线；false=离线或 Redis 不可用</returns>
    Task<bool> IsOnlineAsync(long characterId);

    /// <summary>
    /// 批量查询在线状态（减少 Redis 往返）。
    /// </summary>
    /// <param name="characterIds">角色 ID 列表</param>
    /// <returns>characterId → 是否在线 的字典</returns>
    Task<Dictionary<long, bool>> BatchIsOnlineAsync(IReadOnlyList<long> characterIds);

    /// <summary>
    /// 获取所有在线角色（用于广播和清理）。<br/>
    /// 使用 SCAN 遍历 character:presence:* 键，避免 KEYS 阻塞 Redis。
    /// </summary>
    /// <returns>在线角色 ID 列表</returns>
    Task<IReadOnlyList<long>> GetAllOnlineCharacterIdsAsync();

    /// <summary>
    /// 获取心跳过期的角色（用于清理）。<br/>
    /// 由 CharacterPresenceMonitorHostedService 每 10 秒调用一次，
    /// 检测超过阈值未心跳的角色，触发 DespawnImmediatelyAsync。
    /// </summary>
    /// <param name="heartbeatTimeout">心跳超时阈值（默认 90 秒）</param>
    /// <returns>过期角色列表（characterId, 最后心跳时间）</returns>
    Task<IReadOnlyList<(long characterId, DateTime lastHeartbeat)>> GetExpiredCharactersAsync(TimeSpan heartbeatTimeout);
}
