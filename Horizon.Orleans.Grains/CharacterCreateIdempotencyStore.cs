using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 角色创建协议级幂等存储
    /// 以 (userId, gameId, idempotencyKey) 为键记录已创建的角色ID，客户端在途断线重试携带相同
    /// 幂等键时直接返回首次创建的角色，避免重复创建。
    /// Redis 为持久去重（Orleans Redis 存储提供程序注入的 <see cref="IConnectionMultiplexer"/>）；
    /// 未注入（如测试环境）时降级为进程内存表，覆盖客户端重试窗口内的去重。
    /// </summary>
    public sealed class CharacterCreateIdempotencyStore
    {
        private static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(30);

        private readonly IConnectionMultiplexer? _redis;
        private readonly ConcurrentDictionary<string, long> _memoryMap = new ConcurrentDictionary<string, long>();

        public CharacterCreateIdempotencyStore(IConnectionMultiplexer? redis = null)
        {
            _redis = redis;
        }

        /// <summary>
        /// 尝试获取已记录的角色ID；未记录返回 null。
        /// </summary>
        public async Task<long?> TryGetAsync(long userId, int gameId, string idempotencyKey)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey)) return null;

            if (_redis != null)
            {
                var key = BuildKey(userId, gameId, idempotencyKey);
                var value = await _redis.GetDatabase().StringGetAsync(key).ConfigureAwait(false);
                return value.HasValue && long.TryParse(value.ToString(), out var characterId) ? characterId : (long?)null;
            }

            return _memoryMap.TryGetValue(BuildKey(userId, gameId, idempotencyKey), out var cachedId) ? cachedId : (long?)null;
        }

        /// <summary>
        /// 记录幂等键到已创建的角色ID。
        /// </summary>
        public async Task RecordAsync(long userId, int gameId, string idempotencyKey, long characterId)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey)) return;

            if (_redis != null)
            {
                var key = BuildKey(userId, gameId, idempotencyKey);
                await _redis.GetDatabase().StringSetAsync(key, characterId.ToString(), EntryTtl).ConfigureAwait(false);
                return;
            }

            _memoryMap[BuildKey(userId, gameId, idempotencyKey)] = characterId;
        }

        private static string BuildKey(long userId, int gameId, string idempotencyKey)
        {
            return $"char:create:uid:{userId}:gid:{gameId}:{idempotencyKey}";
        }
    }
}
