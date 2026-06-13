using Horizon.Game.Core.Interfaces;
using Horizon.Strategy.Storage.Redis;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Services
{
    public class CharacterFingerprintService : ICharacterFingerprintService
    {
        private readonly Lazy<RedisCache> _redisCacheLazy;
        private readonly ILogger<CharacterFingerprintService> _logger;
        private readonly string _gatewayId;
        private readonly TimeSpan _fingerprintExpiry = TimeSpan.FromHours(24);
        private const int MaxRetryAttempts = 3;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);

        private RedisCache RedisCache => _redisCacheLazy.Value;

        public CharacterFingerprintService(RedisCache redisCache, ILogger<CharacterFingerprintService> logger, string gatewayId = "")
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _gatewayId = gatewayId ?? string.Empty;
            _redisCacheLazy = new Lazy<RedisCache>(() => redisCache);
        }

        public CharacterFingerprintService(string connectionString, ILogger<CharacterFingerprintService> logger, string gatewayId = "")
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _gatewayId = gatewayId ?? string.Empty;
            _redisCacheLazy = new Lazy<RedisCache>(() =>
            {
                try
                {
                    var cache = new RedisCache(connectionString);
                    _logger.LogInformation("CharacterFingerprintService Redis 延迟初始化成功");
                    return cache;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "CharacterFingerprintService Redis 延迟初始化失败");
                    throw;
                }
            });
        }

        public async Task<bool> TryAcquireAsync(long userId, long characterId, string gatewayId, string connectionId)
        {
            var fingerprintKey = GetFingerprintKey(characterId);
            var connectionSetKey = GetConnectionSetKey(connectionId);
            var lockKey = $"lock:character:fingerprint:{characterId}";
            var effectiveGatewayId = string.IsNullOrEmpty(gatewayId) ? _gatewayId : gatewayId;

            for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
            {
                try
                {
                    using var redisLock = await RedisCache.AcquireLockAsync(lockKey, LockTimeout);

                    var existing = await RedisCache.GetAsync(fingerprintKey);
                    if (!string.IsNullOrEmpty(existing))
                    {
                        var existingFingerprint = JsonSerializer.Deserialize<CharacterFingerprint>(existing);
                        if (existingFingerprint != null && existingFingerprint.ConnectionId != connectionId)
                        {
                            _logger.LogWarning(
                                "角色 {CharacterId} 已被另一会话占用: Gateway={GatewayId}, Connection={ConnectionId}",
                                characterId, existingFingerprint.GatewayId, existingFingerprint.ConnectionId);
                            return false;
                        }
                    }

                    var fingerprint = new CharacterFingerprint
                    {
                        UserId = userId,
                        CharacterId = characterId,
                        GatewayId = effectiveGatewayId,
                        ConnectionId = connectionId,
                        CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };

                    var json = JsonSerializer.Serialize(fingerprint);
                    await RedisCache.SetAsync(fingerprintKey, json, _fingerprintExpiry);
                    await RedisCache.AddItemToSetAsync(connectionSetKey, characterId.ToString());

                    _logger.LogInformation(
                        "角色指纹创建成功: CharacterId={CharacterId}, ConnectionId={ConnectionId}",
                        characterId, connectionId);
                    return true;
                }
                catch (TimeoutException) when (attempt < MaxRetryAttempts)
                {
                    _logger.LogWarning("获取角色指纹锁超时，第 {Attempt} 次重试: CharacterId={CharacterId}", attempt, characterId);
                    await Task.Delay(RetryDelay);
                }
                catch (Exception ex) when (attempt < MaxRetryAttempts)
                {
                    _logger.LogWarning(ex, "创建角色指纹失败，第 {Attempt} 次重试: CharacterId={CharacterId}", attempt, characterId);
                    await Task.Delay(RetryDelay);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "创建角色指纹失败，已达最大重试次数: CharacterId={CharacterId}", characterId);
                    return false;
                }
            }

            return false;
        }

        public async Task<bool> ReleaseAsync(long characterId)
        {
            var fingerprintKey = GetFingerprintKey(characterId);

            for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
            {
                try
                {
                    var existing = await RedisCache.GetAsync(fingerprintKey);
                    if (!string.IsNullOrEmpty(existing))
                    {
                        var fingerprint = JsonSerializer.Deserialize<CharacterFingerprint>(existing);
                        if (fingerprint != null && !string.IsNullOrEmpty(fingerprint.ConnectionId))
                        {
                            var connectionSetKey = GetConnectionSetKey(fingerprint.ConnectionId);
                            await RedisCache.RemoveItemFromSetAsync(connectionSetKey, characterId.ToString());
                        }
                    }

                    await RedisCache.RemoveAsync(fingerprintKey);

                    _logger.LogInformation("角色指纹已释放: CharacterId={CharacterId}", characterId);
                    return true;
                }
                catch (RedisConnectionException ex)
                {
                    _logger.LogWarning(ex, "Redis 不可用，跳过释放角色指纹: CharacterId={CharacterId}", characterId);
                    return false;
                }
                catch (Exception ex) when (attempt < MaxRetryAttempts)
                {
                    _logger.LogWarning(ex, "释放角色指纹失败，第 {Attempt} 次重试: CharacterId={CharacterId}", attempt, characterId);
                    await Task.Delay(RetryDelay);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "释放角色指纹失败，已达最大重试次数: CharacterId={CharacterId}", characterId);
                    return false;
                }
            }

            return false;
        }

        public async Task ReleaseByConnectionAsync(string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId)) return;

            var connectionSetKey = GetConnectionSetKey(connectionId);

            for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
            {
                try
                {
                    var characterIds = await RedisCache.GetAllItemsFromSetAsync(connectionSetKey);

                    foreach (var charIdStr in characterIds)
                    {
                        if (long.TryParse(charIdStr, out var characterId))
                        {
                            var fingerprintKey = GetFingerprintKey(characterId);
                            await RedisCache.RemoveAsync(fingerprintKey);
                            _logger.LogDebug("断线清理角色指纹: CharacterId={CharacterId}, ConnectionId={ConnectionId}", characterId, connectionId);
                        }
                    }

                    await RedisCache.RemoveAsync(connectionSetKey);

                    _logger.LogInformation("连接断开，已清理所有角色指纹: ConnectionId={ConnectionId}, Count={Count}", connectionId, characterIds.Count);
                    return;
                }
                catch (RedisConnectionException ex)
                {
                    _logger.LogWarning(ex, "Redis 不可用，跳过断线角色指纹清理: ConnectionId={ConnectionId}", connectionId);
                    return;
                }
                catch (Exception ex) when (attempt < MaxRetryAttempts)
                {
                    _logger.LogWarning(ex, "断线清理角色指纹失败，第 {Attempt} 次重试: ConnectionId={ConnectionId}", attempt, connectionId);
                    await Task.Delay(RetryDelay);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "断线清理角色指纹失败: ConnectionId={ConnectionId}", connectionId);
                    return;
                }
            }
        }

        public async Task<bool> IsOnlineAsync(long characterId)
        {
            try
            {
                return await RedisCache.ExistsAsync(GetFingerprintKey(characterId));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "查询角色在线状态失败: CharacterId={CharacterId}", characterId);
                return false;
            }
        }

        private static string GetFingerprintKey(long characterId) => $"character:fingerprint:{characterId}";
        private static string GetConnectionSetKey(string connectionId) => $"connection:characters:{connectionId}";
    }

    public class CharacterFingerprint
    {
        public long UserId { get; set; }
        public long CharacterId { get; set; }
        public string GatewayId { get; set; } = string.Empty;
        public string ConnectionId { get; set; } = string.Empty;
        public long CreatedAt { get; set; }
    }
}
