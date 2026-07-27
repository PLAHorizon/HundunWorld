using System;
using System.Globalization;
using System.Threading.Tasks;
using Horizon.Game.Core.Sim.Server;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Horizon.Strategy.Storage.Redis
{
    /// <summary>
    /// 角色位置 Redis 永久存储（双轨制架构的实现端）。<br/>
    /// 与 Orleans GrainStorage 分离，使用独立 Redis Key 永久存储角色最后位置：<br/>
    /// - Key: <c>character:position:{characterId}</c><br/>
    /// - Value: Hash { x, y, z, yaw, updatedAt }<br/>
    /// - TTL = 无（永久存储），服务器重启后激活 Grain 时从此处恢复位置
    /// <para>
    /// 降级策略：所有 Redis 操作 try-catch，失败时记录日志并返回安全默认值（false/null），
    /// 不向调用方抛异常，避免 Redis 不可用时影响主业务流程。
    /// 调用方（CharacterGrain）应同时维护内存缓存（CharacterState.LastPosition*）作为兜底。
    /// </para>
    /// </summary>
    public class RedisCharacterPositionStore : ICharacterPositionStore
    {
        private const string KeyPrefix = "character:position:";
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

                // 永久存储：不设置 TTL
                await database.HashSetAsync(key, entries);

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

        private static string BuildKey(long characterId) =>
            KeyPrefix + characterId.ToString(CultureInfo.InvariantCulture);
    }
}
