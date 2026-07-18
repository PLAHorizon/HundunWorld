using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Horizon.Game.Core.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Horizon.Strategy.Storage.Redis
{
    /// <summary>
    /// 角色指纹 Redis 存储（Silo 端使用，复用 <see cref="RedisConnection"/> 单例）。<br/>
    /// 实现 <see cref="ICharacterFingerprintService"/> 接口，与 Gateway 端的
    /// <c>CharacterFingerprintService</c> 行为一致，操作同一组 Redis Key：
    /// <list type="bullet">
    /// <item><c>character:fingerprint:{characterId}</c> → JSON 串（TTL 5min）</item>
    /// <item><c>connection:characters:{connectionId}</c> → Set（记录该连接上的所有 characterId）</item>
    /// </list>
    /// 修复 BUG：CharacterGrain.GoOfflineAsync 需要 Silo 端注入 ICharacterFingerprintService
    /// 来清理 fingerprint key，但 Silo 不引用 Horizon.Game.Gateway，无法使用
    /// CharacterFingerprintService。本类放在 Horizon.Strategy.Storage.Redis 中，
    /// Silo 已引用此项目，可直接注册使用。
    /// </summary>
    public class RedisCharacterFingerprintStore : ICharacterFingerprintService
    {
        private const string FingerprintKeyPrefix = "character:fingerprint:";
        private const string ConnectionSetKeyPrefix = "connection:characters:";
        private const string LockKeyPrefix = "lock:character:fingerprint:";
        private static readonly TimeSpan DefaultFingerprintExpiry = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);

        private readonly RedisConnection _redisConnection;
        private readonly int _defaultDb;
        private readonly ILogger<RedisCharacterFingerprintStore>? _logger;

        public RedisCharacterFingerprintStore(
            RedisConnection redisConnection,
            ILogger<RedisCharacterFingerprintStore>? logger = null,
            int defaultDb = -1)
        {
            _redisConnection = redisConnection ?? throw new ArgumentNullException(nameof(redisConnection));
            _logger = logger;
            _defaultDb = defaultDb;
        }

        /// <inheritdoc />
        public async Task<bool> TryAcquireAsync(long userId, long characterId, string gatewayId, string connectionId)
        {
            try
            {
                var fingerprintKey = GetFingerprintKey(characterId);
                var database = await _redisConnection.GetDatabaseAsync(_defaultDb);

                // 简单的 SET NX EX 语义：若 key 已存在且未过期，则拒绝
                var existing = await database.StringGetAsync(fingerprintKey);
                if (existing.HasValue)
                {
                    var existingFingerprint = JsonSerializer.Deserialize<CharacterFingerprint>(existing.ToString());
                    if (existingFingerprint != null)
                    {
                        var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(existingFingerprint.CreatedAt);
                        var age = DateTimeOffset.UtcNow - createdAt;
                        if (age <= DefaultFingerprintExpiry && existingFingerprint.ConnectionId != connectionId)
                        {
                            _logger?.LogWarning(
                                "角色 {CharacterId} 已被另一会话占用: Gateway={GatewayId}, Connection={ConnectionId}",
                                characterId, existingFingerprint.GatewayId, existingFingerprint.ConnectionId);
                            return false;
                        }
                        // 过期或同连接，允许抢占
                    }
                }

                var fingerprint = new CharacterFingerprint
                {
                    UserId = userId,
                    CharacterId = characterId,
                    GatewayId = gatewayId ?? string.Empty,
                    ConnectionId = connectionId ?? string.Empty,
                    CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                var json = JsonSerializer.Serialize(fingerprint);
                await database.StringSetAsync(fingerprintKey, json, DefaultFingerprintExpiry);

                if (!string.IsNullOrEmpty(connectionId))
                {
                    var connectionSetKey = GetConnectionSetKey(connectionId);
                    await database.SetAddAsync(connectionSetKey, characterId.ToString(CultureInfo.InvariantCulture));
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "TryAcquireAsync 失败: CharacterId={CharacterId}", characterId);
                return false;
            }
        }

        /// <inheritdoc />
        public async Task<bool> ReleaseAsync(long characterId)
        {
            try
            {
                var fingerprintKey = GetFingerprintKey(characterId);
                var database = await _redisConnection.GetDatabaseAsync(_defaultDb);

                // 尝试从 fingerprint 中读出 connectionId，以便同步清理 connection Set
                var existing = await database.StringGetAsync(fingerprintKey);
                if (existing.HasValue)
                {
                    var fingerprint = JsonSerializer.Deserialize<CharacterFingerprint>(existing.ToString());
                    if (fingerprint != null && !string.IsNullOrEmpty(fingerprint.ConnectionId))
                    {
                        var connectionSetKey = GetConnectionSetKey(fingerprint.ConnectionId);
                        await database.SetRemoveAsync(connectionSetKey, characterId.ToString(CultureInfo.InvariantCulture));
                    }
                }

                await database.KeyDeleteAsync(fingerprintKey);
                _logger?.LogDebug("角色指纹已释放: CharacterId={CharacterId}", characterId);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "ReleaseAsync 失败: CharacterId={CharacterId}", characterId);
                return false;
            }
        }

        /// <inheritdoc />
        public async Task<bool> RefreshAsync(long characterId)
        {
            try
            {
                var fingerprintKey = GetFingerprintKey(characterId);
                var database = await _redisConnection.GetDatabaseAsync(_defaultDb);

                var existing = await database.StringGetAsync(fingerprintKey);
                if (!existing.HasValue)
                {
                    _logger?.LogWarning("刷新角色指纹失败: 指纹不存在 CharacterId={CharacterId}", characterId);
                    return false;
                }

                var fingerprint = JsonSerializer.Deserialize<CharacterFingerprint>(existing.ToString());
                if (fingerprint == null) return false;

                fingerprint.CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var json = JsonSerializer.Serialize(fingerprint);
                await database.StringSetAsync(fingerprintKey, json, DefaultFingerprintExpiry);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "RefreshAsync 失败: CharacterId={CharacterId}", characterId);
                return false;
            }
        }

        /// <inheritdoc />
        public async Task ReleaseByConnectionAsync(string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId)) return;

            try
            {
                var connectionSetKey = GetConnectionSetKey(connectionId);
                var database = await _redisConnection.GetDatabaseAsync(_defaultDb);

                var characterIds = await database.SetMembersAsync(connectionSetKey);
                foreach (var charIdStr in characterIds)
                {
                    if (long.TryParse(charIdStr.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var characterId))
                    {
                        await database.KeyDeleteAsync(GetFingerprintKey(characterId));
                    }
                }

                await database.KeyDeleteAsync(connectionSetKey);
                _logger?.LogInformation("连接断开，已清理所有角色指纹: ConnectionId={ConnectionId}, Count={Count}",
                    connectionId, characterIds.Length);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "ReleaseByConnectionAsync 失败: ConnectionId={ConnectionId}", connectionId);
            }
        }

        /// <inheritdoc />
        public async Task<bool> IsOnlineAsync(long characterId)
        {
            try
            {
                var fingerprintKey = GetFingerprintKey(characterId);
                var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
                return await database.KeyExistsAsync(fingerprintKey);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "IsOnlineAsync 失败: CharacterId={CharacterId}", characterId);
                return false;
            }
        }

        private static string GetFingerprintKey(long characterId) => FingerprintKeyPrefix + characterId.ToString(CultureInfo.InvariantCulture);
        private static string GetConnectionSetKey(string connectionId) => ConnectionSetKeyPrefix + connectionId;
    }

    /// <summary>
    /// 角色指纹数据（与 Gateway 端 CharacterFingerprintService.CharacterFingerprint 保持一致）。<br/>
    /// 注意：两端序列化的 JSON 必须兼容，否则会互相读不出来。
    /// </summary>
    public class CharacterFingerprint
    {
        public long UserId { get; set; }
        public long CharacterId { get; set; }
        public string GatewayId { get; set; } = string.Empty;
        public string ConnectionId { get; set; } = string.Empty;
        public long CreatedAt { get; set; }
    }
}
