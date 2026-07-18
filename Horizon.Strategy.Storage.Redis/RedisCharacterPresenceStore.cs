using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.Core.Sim.Server;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Horizon.Strategy.Storage.Redis
{
    /// <summary>
    /// 角色在线状态 Redis 存储（双轨制架构的实现端）。<br/>
    /// 与 Orleans GrainStorage 分离，使用独立 Redis Key + TTL 管理在线状态：<br/>
    /// - Key: <c>character:presence:{characterId}</c><br/>
    /// - Value: Hash { gatewayId, connectionId, lastHeartbeat }<br/>
    /// - TTL = 90 秒（心跳间隔 30 秒 × 3 倍容错）<br/>
    /// <para>
    /// 降级策略：所有 Redis 操作 try-catch，失败时记录日志并返回安全默认值（false/空列表），
    /// 不向调用方抛异常，避免 Redis 不可用时影响主业务流程。
    /// 调用方（CharacterGrain/GameNetworkServer）应同时维护内存状态作为兜底。
    /// </para>
    /// </summary>
    public class RedisCharacterPresenceStore : ICharacterPresenceStore
    {
        private const string KeyPrefix = "character:presence:";
        private const string FieldGatewayId = "gatewayId";
        private const string FieldConnectionId = "connectionId";
        private const string FieldLastHeartbeat = "lastHeartbeat";

        /// <summary>
        /// presence key 默认 TTL（秒）。<br/>
        /// 设计依据：客户端心跳间隔 30 秒 × 3 倍容错 = 90 秒，
        /// 即允许连续 3 次心跳丢失后才判定离线。
        /// </summary>
        public const int DefaultPresenceTtlSeconds = 90;

        private readonly RedisConnection _redisConnection;
        private readonly int _defaultDb;
        private readonly int _presenceTtlSeconds;
        private readonly ILogger<RedisCharacterPresenceStore>? _logger;

        /// <summary>
        /// 构造函数。<br/>
        /// 通常由 DI 容器注入 <see cref="RedisConnection"/>（单例）。
        /// </summary>
        /// <param name="redisConnection">Redis 连接管理器</param>
        /// <param name="logger">日志器</param>
        /// <param name="defaultDb">默认数据库索引（-1 表示使用默认数据库）</param>
        /// <param name="presenceTtlSeconds">presence key TTL（秒），默认 90</param>
        public RedisCharacterPresenceStore(
            RedisConnection redisConnection,
            ILogger<RedisCharacterPresenceStore>? logger = null,
            int defaultDb = -1,
            int presenceTtlSeconds = DefaultPresenceTtlSeconds)
        {
            _redisConnection = redisConnection ?? throw new ArgumentNullException(nameof(redisConnection));
            _logger = logger;
            _defaultDb = defaultDb;
            _presenceTtlSeconds = presenceTtlSeconds > 0 ? presenceTtlSeconds : DefaultPresenceTtlSeconds;
        }

        /// <inheritdoc />
        public async Task<bool> SetOnlineAsync(long characterId, string gatewayId, string connectionId)
        {
            try
            {
                var key = BuildKey(characterId);
                var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
                var nowTicks = DateTime.UtcNow.Ticks;
                var ttl = TimeSpan.FromSeconds(_presenceTtlSeconds);

                // 使用 Hash 存储元信息 + 设置 TTL
                var entries = new HashEntry[]
                {
                    new(FieldGatewayId, gatewayId ?? string.Empty),
                    new(FieldConnectionId, connectionId ?? string.Empty),
                    new(FieldLastHeartbeat, nowTicks.ToString(CultureInfo.InvariantCulture))
                };

                await database.HashSetAsync(key, entries);
                await database.KeyExpireAsync(key, ttl);

                _logger?.LogDebug(
                    "Character {CharacterId} set online: gateway={GatewayId}, conn={ConnectionId}, ttl={Ttl}s",
                    characterId, gatewayId, connectionId, _presenceTtlSeconds);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "Redis SetOnlineAsync failed for character {CharacterId}, degraded to in-memory state",
                    characterId);
                return false;
            }
        }

        /// <inheritdoc />
        public async Task<bool> SetOfflineAsync(long characterId)
        {
            try
            {
                var key = BuildKey(characterId);
                var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
                var deleted = await database.KeyDeleteAsync(key);

                _logger?.LogDebug(
                    "Character {CharacterId} set offline, key deleted={Deleted}", characterId, deleted);
                return true; // 无论 key 是否存在都视为下线成功
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "Redis SetOfflineAsync failed for character {CharacterId}, degraded to in-memory state",
                    characterId);
                return false;
            }
        }

        /// <inheritdoc />
        public async Task<bool> RefreshHeartbeatAsync(long characterId)
        {
            try
            {
                var key = BuildKey(characterId);
                var database = await _redisConnection.GetDatabaseAsync(_defaultDb);

                // 先检查 key 是否存在（角色必须已上线才能续期）
                var exists = await database.KeyExistsAsync(key);
                if (!exists)
                {
                    _logger?.LogWarning(
                        "RefreshHeartbeatAsync: character {CharacterId} presence key not found, possible stale heartbeat",
                        characterId);
                    return false;
                }

                var nowTicks = DateTime.UtcNow.Ticks;
                var ttl = TimeSpan.FromSeconds(_presenceTtlSeconds);

                // 刷新 lastHeartbeat 字段 + 重置 TTL
                await database.HashSetAsync(key, FieldLastHeartbeat,
                    nowTicks.ToString(CultureInfo.InvariantCulture));
                await database.KeyExpireAsync(key, ttl);

                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "Redis RefreshHeartbeatAsync failed for character {CharacterId}, degraded to in-memory state",
                    characterId);
                return false;
            }
        }

        /// <inheritdoc />
        public async Task<bool> IsOnlineAsync(long characterId)
        {
            try
            {
                var key = BuildKey(characterId);
                var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
                return await database.KeyExistsAsync(key);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "Redis IsOnlineAsync failed for character {CharacterId}, returning false (degraded)",
                    characterId);
                return false; // 降级：Redis 不可用时认为离线，避免误判在线导致逻辑错误
            }
        }

        /// <inheritdoc />
        public async Task<Dictionary<long, bool>> BatchIsOnlineAsync(IReadOnlyList<long> characterIds)
        {
            var result = new Dictionary<long, bool>(characterIds.Count);
            if (characterIds.Count == 0) return result;

            try
            {
                var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
                // KeyExistsAsync(RedisKey[]) 返回 long（存在总数），无法逐个判断。
                // 改为逐个查询以保证正确性。批量场景下角色数通常很少（<100），性能可接受。
                foreach (var id in characterIds)
                {
                    var key = BuildKey(id);
                    result[id] = await database.KeyExistsAsync(key);
                }
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "Redis BatchIsOnlineAsync failed for {Count} characters, returning all-offline (degraded)",
                    characterIds.Count);
                // 降级：全部返回 false
                foreach (var id in characterIds)
                {
                    result[id] = false;
                }
                return result;
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<long>> GetAllOnlineCharacterIdsAsync()
        {
            var result = new List<long>();
            try
            {
                var endpoints = _redisConnection.GetEndPoints();
                var pattern = KeyPrefix + "*";

                foreach (var endpoint in endpoints)
                {
                    var server = _redisConnection.GetServer(endpoint);
                    if (server.IsReplica) continue; // 只扫描主节点，避免重复

                    foreach (var redisKey in server.Keys(_defaultDb, pattern))
                    {
                        if (TryParseCharacterId(redisKey, out var characterId))
                        {
                            result.Add(characterId);
                        }
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "Redis GetAllOnlineCharacterIdsAsync failed, returning empty list (degraded)");
                return result; // 降级：返回空列表
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<(long characterId, DateTime lastHeartbeat)>> GetExpiredCharactersAsync(
            TimeSpan heartbeatTimeout)
        {
            var result = new List<(long, DateTime)>();
            try
            {
                var endpoints = _redisConnection.GetEndPoints();
                var pattern = KeyPrefix + "*";
                var thresholdTicks = DateTime.UtcNow.Subtract(heartbeatTimeout).Ticks;

                foreach (var endpoint in endpoints)
                {
                    var server = _redisConnection.GetServer(endpoint);
                    if (server.IsReplica) continue;

                    foreach (var redisKey in server.Keys(_defaultDb, pattern))
                    {
                        if (!TryParseCharacterId(redisKey, out var characterId)) continue;

                        // 检查 TTL，已过期的不会出现在扫描结果中，但仍可能因时钟漂移残留极短时间
                        // 重点检查 lastHeartbeat 字段是否超过阈值
                        var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
                        var lastHeartbeatValue = await database.HashGetAsync(redisKey, FieldLastHeartbeat);
                        if (!lastHeartbeatValue.HasValue) continue;

                        // RedisValue 隐式转换为 string/ReadOnlySpan<byte> 会导致 TryParse 二义性，显式 ToString
                        if (!long.TryParse(lastHeartbeatValue.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var lastHeartbeatTicks)) continue;
                        if (lastHeartbeatTicks < thresholdTicks)
                        {
                            result.Add((characterId, new DateTime(lastHeartbeatTicks, DateTimeKind.Utc)));
                        }
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "Redis GetExpiredCharactersAsync failed, returning empty list (degraded)");
                return result;
            }
        }

        private static string BuildKey(long characterId) => KeyPrefix + characterId.ToString(CultureInfo.InvariantCulture);

        private static bool TryParseCharacterId(RedisKey redisKey, out long characterId)
        {
            var keyStr = redisKey.ToString();
            if (keyStr.StartsWith(KeyPrefix, StringComparison.Ordinal))
            {
                var idStr = keyStr.AsSpan(KeyPrefix.Length);
                if (long.TryParse(idStr, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                {
                    characterId = id;
                    return true;
                }
            }
            characterId = 0;
            return false;
        }
    }
}
