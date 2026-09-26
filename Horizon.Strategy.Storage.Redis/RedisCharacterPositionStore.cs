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
    /// 角色位置 Redis 缓存存储（双轨制架构的实现端）。<br/>
    /// 与 Orleans GrainStorage 分离，使用独立 Redis Key 存储角色最后位置：<br/>
    /// - Key: <c>character:position:{characterId}</c><br/>
    /// - Value: Hash { x, y, z, yaw, updatedAt }<br/>
    /// - 索引: Sorted Set <c>character:position:index</c>（member=characterId, score=updatedAtTicks），
    ///   归档服务据此枚举"超过 24h 无新位置更新"的条目，落库 SQL Server 后经
    ///   <see cref="MarkPositionArchivedAsync"/> 清除缓存。<br/>
    /// 角色进入游戏时先命中本缓存，未命中由调用方回退读 SQL Server 归档。
    /// <para>
    /// 降级策略：所有 Redis 操作 try-catch，失败时记录日志并返回安全默认值（false/null/空集合），
    /// 不向调用方抛异常，避免 Redis 不可用时影响主业务流程。
    /// 调用方（CharacterGrain）应同时维护内存缓存（CharacterState.LastPosition*）作为兜底。
    /// </para>
    /// </summary>
    public class RedisCharacterPositionStore : ICharacterPositionStore
    {
        private const string KeyPrefix = "character:position:";
        private const string UpdateIndexKey = "character:position:index";
        private const string FieldX = "x";
        private const string FieldY = "y";
        private const string FieldZ = "z";
        private const string FieldYaw = "yaw";
        private const string FieldUpdatedAt = "updatedAt";

        private readonly RedisConnection _redisConnection;
        private readonly int _defaultDb;
        private readonly ILogger<RedisCharacterPositionStore>? _logger;

        /// <summary>
        /// 构造函数。<br/>
        /// 通常由 DI 容器注入 <see cref="RedisConnection"/>（单例）。
        /// </summary>
        /// <param name="redisConnection">Redis 连接管理器</param>
        /// <param name="logger">日志器</param>
        /// <param name="defaultDb">默认数据库索引（-1 表示使用默认数据库）</param>
        public RedisCharacterPositionStore(
            RedisConnection redisConnection,
            ILogger<RedisCharacterPositionStore>? logger = null,
            int defaultDb = -1)
        {
            _redisConnection = redisConnection ?? throw new ArgumentNullException(nameof(redisConnection));
            _logger = logger;
            _defaultDb = defaultDb;
        }

        /// <inheritdoc />
        public async Task<bool> SavePositionAsync(long characterId, float x, float y, float z, float yaw)
        {
            try
            {
                var key = BuildKey(characterId);
                var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
                var nowTicks = DateTime.UtcNow.Ticks;

                // float 字段使用 "R"（Round-Trip）格式化，保证精度无损
                // updatedAt 存储 Ticks（long），用于诊断
                var entries = new HashEntry[]
                {
                    new(FieldX, x.ToString("R", CultureInfo.InvariantCulture)),
                    new(FieldY, y.ToString("R", CultureInfo.InvariantCulture)),
                    new(FieldZ, z.ToString("R", CultureInfo.InvariantCulture)),
                    new(FieldYaw, yaw.ToString("R", CultureInfo.InvariantCulture)),
                    new(FieldUpdatedAt, nowTicks.ToString(CultureInfo.InvariantCulture))
                };

                // 永久存储：不设置 TTL（24h 无更新由归档服务落库 SQL 后清除）
                await database.HashSetAsync(key, entries);

                // 维护更新索引：score=本次写入时间 ticks，归档清扫按"最后更新时间"枚举
                await database.SortedSetAddAsync(UpdateIndexKey, characterId.ToString(CultureInfo.InvariantCulture), nowTicks);

                _logger?.LogDebug(
                    "Character {CharacterId} position saved: x={X}, y={Y}, z={Z}, yaw={Yaw}",
                    characterId, x, y, z, yaw);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "Redis SavePositionAsync failed for character {CharacterId}, degraded to in-memory state",
                    characterId);
                return false;
            }
        }

        /// <inheritdoc />
        public async Task<(float X, float Y, float Z, float Yaw)?> GetPositionAsync(long characterId)
        {
            try
            {
                var key = BuildKey(characterId);
                var database = await _redisConnection.GetDatabaseAsync(_defaultDb);

                var entries = await database.HashGetAsync(key, new RedisValue[]
                {
                    FieldX, FieldY, FieldZ, FieldYaw
                });

                // 任一字段缺失即视为无数据
                if (!entries[0].HasValue || !entries[1].HasValue ||
                    !entries[2].HasValue || !entries[3].HasValue)
                {
                    return null;
                }

                // 使用 "R" 格式存储，用 float.Parse 还原精度
                var x = float.Parse(entries[0].ToString(), CultureInfo.InvariantCulture);
                var y = float.Parse(entries[1].ToString(), CultureInfo.InvariantCulture);
                var z = float.Parse(entries[2].ToString(), CultureInfo.InvariantCulture);
                var yaw = float.Parse(entries[3].ToString(), CultureInfo.InvariantCulture);

                return (x, y, z, yaw);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "Redis GetPositionAsync failed for character {CharacterId}, returning null (degraded)",
                    characterId);
                return null; // 降级：返回 null，调用方回退到内存缓存
            }
        }

        /// <inheritdoc />
        public async Task<bool> ClearPositionAsync(long characterId)
        {
            try
            {
                var key = BuildKey(characterId);
                var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
                await database.KeyDeleteAsync(key);
                await database.SortedSetRemoveAsync(UpdateIndexKey, characterId.ToString(CultureInfo.InvariantCulture));

                _logger?.LogDebug(
                    "Character {CharacterId} position cleared", characterId);
                return true; // 无论 key 是否存在都视为清除成功
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "Redis ClearPositionAsync failed for character {CharacterId}, degraded to in-memory state",
                    characterId);
                return false;
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<StaleCharacterPosition>> GetStalePositionsAsync(long cutoffUtcTicks, int maxCount)
        {
            if (maxCount <= 0) return Array.Empty<StaleCharacterPosition>();
            try
            {
                var database = await _redisConnection.GetDatabaseAsync(_defaultDb);

                // 索引 score 落后于 Hash updatedAt 时（历史键未走新 Save 路径）会先得到一次
                // "偏旧"的枚举——下面逐个用 Hash 内真实 updatedAt 复核，不满足的顺手把索引校准。
                var members = await database.SortedSetRangeByScoreWithScoresAsync(
                    UpdateIndexKey, double.NegativeInfinity, cutoffUtcTicks,
                    Exclude.None, Order.Ascending, 0, maxCount);

                var results = new List<StaleCharacterPosition>(members.Length);
                foreach (var member in members)
                {
                    if (!long.TryParse(member.Element.ToString(), out var characterId))
                    {
                        await database.SortedSetRemoveAsync(UpdateIndexKey, member.Element);
                        continue;
                    }

                    var entries = await database.HashGetAsync(BuildKey(characterId), new RedisValue[]
                    {
                        FieldX, FieldY, FieldZ, FieldYaw, FieldUpdatedAt
                    });

                    if (!entries[0].HasValue || !entries[1].HasValue ||
                        !entries[2].HasValue || !entries[3].HasValue || !entries[4].HasValue)
                    {
                        // Hash 已不存在（角色删除/人工清理）：移除孤儿索引成员
                        await database.SortedSetRemoveAsync(UpdateIndexKey, characterId.ToString(CultureInfo.InvariantCulture));
                        continue;
                    }

                    var updatedAtTicks = long.Parse(entries[4].ToString(), CultureInfo.InvariantCulture);
                    if (updatedAtTicks > cutoffUtcTicks)
                    {
                        // Hash 实际比索引更新（索引 score 待校准）：跳过本轮并校准 score
                        await database.SortedSetAddAsync(UpdateIndexKey, characterId.ToString(CultureInfo.InvariantCulture), updatedAtTicks);
                        continue;
                    }

                    results.Add(new StaleCharacterPosition(
                        characterId,
                        float.Parse(entries[0].ToString(), CultureInfo.InvariantCulture),
                        float.Parse(entries[1].ToString(), CultureInfo.InvariantCulture),
                        float.Parse(entries[2].ToString(), CultureInfo.InvariantCulture),
                        float.Parse(entries[3].ToString(), CultureInfo.InvariantCulture),
                        updatedAtTicks));

                    if (results.Count >= maxCount) break;
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Redis GetStalePositionsAsync failed, returning empty batch");
                return Array.Empty<StaleCharacterPosition>();
            }
        }

        /// <inheritdoc />
        public async Task<bool> MarkPositionArchivedAsync(long characterId)
        {
            try
            {
                var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
                var idMember = characterId.ToString(CultureInfo.InvariantCulture);
                await database.KeyDeleteAsync(BuildKey(characterId));
                await database.SortedSetRemoveAsync(UpdateIndexKey, idMember);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Redis MarkPositionArchivedAsync failed for character {CharacterId}", characterId);
                return false;
            }
        }

        /// <inheritdoc />
        public async Task<int> BackfillPositionIndexAsync()
        {
            // 历史遗留 Key（索引机制之前写入）不会自动进索引，用 SCAN 一次性补录，
            // ZADD NX 保留 Hash 内的真实 updatedAt 作为 score，绝不覆盖已有索引成员。
            try
            {
                var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
                int added = 0;
                foreach (var endpoint in _redisConnection.GetEndPoints())
                {
                    var server = _redisConnection.GetServer(endpoint);
                    if (!server.IsReplica)
                    {
                        // KeysAsync 内部使用 SCAN，非阻塞
                        foreach (var redisKey in server.Keys(_defaultDb < 0 ? 0 : _defaultDb, KeyPrefix + "*", pageSize: 500))
                        {
                            var idText = redisKey.ToString().Substring(KeyPrefix.Length);
                            if (!long.TryParse(idText, out var characterId)) continue;

                            var updated = await database.HashGetAsync(redisKey, FieldUpdatedAt);
                            if (!updated.HasValue || !long.TryParse(updated.ToString(), out var ticks)) continue;

                            bool ok = await database.SortedSetAddAsync(
                                UpdateIndexKey, characterId.ToString(CultureInfo.InvariantCulture), ticks, When.NotExists);
                            if (ok) added++;
                        }
                    }
                }
                _logger?.LogInformation("Redis 位置索引回填完成，补录 {Added} 个角色", added);
                return added;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Redis BackfillPositionIndexAsync failed（回填失败仅延迟旧键归档，不影响读写）");
                return 0;
            }
        }

        private static string BuildKey(long characterId) =>
            KeyPrefix + characterId.ToString(CultureInfo.InvariantCulture);
    }
}
