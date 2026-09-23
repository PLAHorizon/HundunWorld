using System;
using System.Threading.Tasks;
using Horizon.Core.Security;
using Horizon.Strategy.Storage.Redis;
using Microsoft.Extensions.Logging;

namespace Horizon.WebApi.Security
{
    /// <summary>
    /// 通行证级共享 IM 授权令牌存储。
    /// <para>
    /// 统一多端（PC / 移动端）令牌：同一通行证在任意客户端首次登录（基础验证通过）时，
    /// 服务端签发一张专属共享令牌并缓存到 Redis；之后任意客户端以该通行证登录时复用同一令牌。
    /// 当该通行证在所有客户端退出（会话集合清空）或令牌超过指定过期时限后，令牌失效。
    /// </para>
    /// <para>
    /// 共享令牌不绑定机器 ID（多端 machineId 不同），IM 网关须关闭
    /// <c>Gateway:ValidateTokenMachineId</c> 校验；令牌身份仍以加密载荷中的 PassportId 为权威。
    /// </para>
    /// <remarks>
    /// Redis 键布局：
    /// - <c>hundunworld:im:passport:token:{passportId}</c>：共享令牌（带 TTL，到期自动失效）；
    /// - <c>hundunworld:im:passport:sessions:{passportId}</c>：当前登录会话的机器 ID 集合（清空即全端退出）。
    /// </remarks>
    /// </summary>
    public class PassportImTokenStore
    {
        /// <summary>共享令牌键前缀。</summary>
        public const string TokenKeyPrefix = "hundunworld:im:passport:token:";

        /// <summary>登录会话集合键前缀。</summary>
        public const string SessionsKeyPrefix = "hundunworld:im:passport:sessions:";

        private readonly RedisCache _redisCache;
        private readonly UserAuthTokenProvider _authTokenProvider;
        private readonly ILogger<PassportImTokenStore> _logger;
        private readonly TimeSpan _tokenExpiry;

        /// <summary>共享令牌有效期（小时）：令牌内嵌有效期与 Redis TTL 共用，保证两者同步过期。</summary>
        private int TokenExpiryHours => (int)_tokenExpiry.TotalHours;

        /// <summary>
        /// 构造共享令牌存储。
        /// </summary>
        /// <param name="redisConnectionString">Redis 连接字符串（与网关注册中心共用）。</param>
        /// <param name="authTokenProvider">令牌签发器（与 Game / IM 网关共享同一密钥方案）。</param>
        /// <param name="tokenExpiryHours">共享令牌过期时限（小时）。</param>
        /// <param name="logger">日志记录器。</param>
        public PassportImTokenStore(
            string redisConnectionString,
            UserAuthTokenProvider authTokenProvider,
            int tokenExpiryHours,
            ILogger<PassportImTokenStore> logger)
        {
            if (string.IsNullOrWhiteSpace(redisConnectionString))
            {
                throw new ArgumentException("Redis 连接字符串不能为空", nameof(redisConnectionString));
            }

            _redisCache = new RedisCache(redisConnectionString);
            _authTokenProvider = authTokenProvider ?? throw new ArgumentNullException(nameof(authTokenProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _tokenExpiry = TimeSpan.FromHours(Math.Max(1, tokenExpiryHours));
        }

        /// <summary>
        /// 获取或创建通行证级共享 IM 授权令牌。
        /// <para>
        /// 命中缓存则直接复用（多端拿到同一令牌）；未命中则签发新令牌并写入 Redis（带过期时限）。
        /// <paramref name="machineId"/> 仅用于登记登录会话（令牌本身不绑定机器）。
        /// Redis 不可用时降级为直接签发临时令牌，保证登录流程可用。
        /// </para>
        /// </summary>
        /// <param name="passportId">通行证 ID。</param>
        /// <param name="machineId">客户端机器 ID（可为空，仅用于会话登记）。</param>
        /// <returns>共享令牌；passportId 无效时返回空串。</returns>
        public async Task<string> GetOrCreateAsync(string passportId, string machineId)
        {
            if (string.IsNullOrWhiteSpace(passportId))
            {
                return string.Empty;
            }

            var tokenKey = TokenKeyPrefix + passportId.Trim();
            try
            {
                var cached = await _redisCache.GetAsync(tokenKey).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(cached))
                {
                    // 缓存命中仍须校验令牌有效性：历史版本签发的令牌内嵌有效期（默认24h）
                    // 可能早于 Redis TTL（默认168h）先行过期；直接复用会把过期令牌发给客户端，
                    // 导致 IM 网关拒绝服务（“鉴权令牌已过期”）。
                    var cachedValidation = _authTokenProvider.ValidateToken(cached);
                    if (cachedValidation.IsValid)
                    {
                        await RegisterSessionAsync(passportId, machineId).ConfigureAwait(false);
                        return cached;
                    }

                    _logger.LogInformation(
                        "共享IM令牌缓存已失效（{Reason}），重新签发: PassportId={PassportId}",
                        cachedValidation.ErrorMessage, passportId);
                }

                // 共享令牌不绑定机器：machineId 传空，多端复用；
                // 令牌内嵌有效期与 Redis TTL 对齐（tokenExpiryHours），避免缓存期内令牌先行过期。
                var token = _authTokenProvider.GenerateToken(passportId.Trim(), string.Empty, 0, TokenExpiryHours);
                await _redisCache.SetAsync(tokenKey, token, _tokenExpiry).ConfigureAwait(false);
                await RegisterSessionAsync(passportId, machineId).ConfigureAwait(false);
                return token;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "共享IM令牌存储访问失败，降级为直接签发临时令牌: PassportId={PassportId}",
                    passportId);
                return _authTokenProvider.GenerateToken(passportId.Trim(), string.Empty, 0, TokenExpiryHours);
            }
        }

        /// <summary>
        /// 客户端退出登录：注销其会话；该通行证全部客户端退出后删除共享令牌（令牌立即失效，下次登录重新签发）。
        /// </summary>
        /// <param name="passportId">通行证 ID。</param>
        /// <param name="machineId">退出的客户端机器 ID。</param>
        public async Task ReleaseAsync(string passportId, string machineId)
        {
            if (string.IsNullOrWhiteSpace(passportId))
            {
                return;
            }

            try
            {
                var sessionsKey = SessionsKeyPrefix + passportId.Trim();
                if (!string.IsNullOrWhiteSpace(machineId))
                {
                    await _redisCache.RemoveItemFromSetAsync(sessionsKey, machineId.Trim()).ConfigureAwait(false);
                }

                var remaining = await _redisCache.GetAllItemsFromSetAsync(sessionsKey).ConfigureAwait(false);
                if (remaining == null || remaining.Count == 0)
                {
                    // 全端退出：令牌失效并清理会话键
                    await _redisCache.RemoveAsync(TokenKeyPrefix + passportId.Trim()).ConfigureAwait(false);
                    await _redisCache.RemoveAsync(sessionsKey).ConfigureAwait(false);
                    _logger.LogInformation("通行证 {PassportId} 已在所有客户端退出，共享IM令牌已失效", passportId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "注销共享IM令牌会话失败: PassportId={PassportId}", passportId);
            }
        }

        private async Task RegisterSessionAsync(string passportId, string machineId)
        {
            if (string.IsNullOrWhiteSpace(machineId))
            {
                return;
            }

            var sessionsKey = SessionsKeyPrefix + passportId.Trim();
            await _redisCache.AddItemToSetAsync(sessionsKey, machineId.Trim()).ConfigureAwait(false);
            // 会话集合与共享令牌同 TTL：客户端崩溃未登出时残留会话随令牌一起过期，不阻塞后续全端退出判定
            await _redisCache.ExpireEntryInAsync(sessionsKey, _tokenExpiry).ConfigureAwait(false);
        }
    }
}
