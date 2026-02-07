using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace Horizon.Game.Core.Security
{
    /// <summary>
    /// 安全管理器
    /// 提供加密、会话管理、防攻击等安全功能
    /// </summary>
    public class SecurityManager
    {
        private readonly ILogger<SecurityManager> _logger;
        private readonly IDistributedCache _cache;
        
        // 用于存储失败的登录尝试
        private readonly ConcurrentDictionary<string, LoginAttemptInfo> _loginAttempts = new();
        
        // 配置常量
        private const int MaxLoginAttempts = 5;
        private const int LoginAttemptsWindowMinutes = 15;
        private const int SessionTimeoutHours = 24;

        public SecurityManager(ILogger<SecurityManager> logger, IDistributedCache cache = null)
        {
            _logger = logger;
            _cache = cache;
        }

        /// <summary>
        /// 生成安全的会话令牌
        /// </summary>
        public string GenerateSessionToken()
        {
            try
            {
                using var rng = RandomNumberGenerator.Create();
                byte[] tokenBytes = new byte[32];
                rng.GetBytes(tokenBytes);
                
                // 添加时间戳防止重复
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var combinedBytes = Encoding.UTF8.GetBytes($"{Convert.ToBase64String(tokenBytes)}_{timestamp}");
                
                return Convert.ToBase64String(combinedBytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成会话令牌时发生异常");
                // 降级处理：使用Guid
                return Guid.NewGuid().ToString("N");
            }
        }

        /// <summary>
        /// 验证会话令牌
        /// </summary>
        public async Task<bool> ValidateSessionTokenAsync(string sessionToken, string userId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sessionToken) || string.IsNullOrWhiteSpace(userId))
                {
                    return false;
                }

                if (_cache == null)
                {
                    _logger.LogWarning("缓存服务不可用，跳过会话验证");
                    return true; // 如果缓存不可用，暂时允许通过
                }

                var cacheKey = $"session_{sessionToken}";
                var cachedUserId = await _cache.GetStringAsync(cacheKey);
                
                return cachedUserId == userId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证会话令牌时发生异常: {SessionToken}", sessionToken);
                return false;
            }
        }

        /// <summary>
        /// 存储会话令牌
        /// </summary>
        public async Task<bool> StoreSessionTokenAsync(string sessionToken, string userId)
        {
            try
            {
                if (_cache == null)
                {
                    _logger.LogWarning("缓存服务不可用，无法存储会话");
                    return true; // 如果缓存不可用，返回成功但记录警告
                }

                var cacheKey = $"session_{sessionToken}";
                var options = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(SessionTimeoutHours)
                };

                await _cache.SetStringAsync(cacheKey, userId, options);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "存储会话令牌时发生异常: {SessionToken}", sessionToken);
                return false;
            }
        }

        /// <summary>
        /// 移除会话令牌
        /// </summary>
        public async Task<bool> RemoveSessionTokenAsync(string sessionToken)
        {
            try
            {
                if (_cache == null)
                {
                    return true;
                }

                var cacheKey = $"session_{sessionToken}";
                await _cache.RemoveAsync(cacheKey);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "移除会话令牌时发生异常: {SessionToken}", sessionToken);
                return false;
            }
        }

        /// <summary>
        /// 检查登录尝试频率
        /// </summary>
        public bool CheckLoginAttempts(string identifier, string clientIP)
        {
            try
            {
                var key = $"{identifier}_{clientIP}";
                var now = DateTime.UtcNow;

                if (_loginAttempts.TryGetValue(key, out var attemptInfo))
                {
                    // 检查时间窗口
                    if (now - attemptInfo.FirstAttemptTime > TimeSpan.FromMinutes(LoginAttemptsWindowMinutes))
                    {
                        // 重置计数器
                        _loginAttempts.TryRemove(key, out _);
                        return true;
                    }

                    // 检查尝试次数
                    if (attemptInfo.AttemptCount >= MaxLoginAttempts)
                    {
                        _logger.LogWarning("登录尝试过于频繁: {Identifier}, IP: {ClientIP}, 尝试次数: {AttemptCount}", 
                            identifier, clientIP, attemptInfo.AttemptCount);
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查登录尝试频率时发生异常: {Identifier}, IP: {ClientIP}", identifier, clientIP);
                return true; // 异常时允许通过
            }
        }

        /// <summary>
        /// 记录失败的登录尝试
        /// </summary>
        public void RecordFailedLoginAttempt(string identifier, string clientIP)
        {
            try
            {
                var key = $"{identifier}_{clientIP}";
                var now = DateTime.UtcNow;

                _loginAttempts.AddOrUpdate(key, 
                    new LoginAttemptInfo 
                    { 
                        FirstAttemptTime = now, 
                        LastAttemptTime = now, 
                        AttemptCount = 1 
                    },
                    (k, existing) =>
                    {
                        existing.LastAttemptTime = now;
                        existing.AttemptCount++;
                        return existing;
                    });

                _logger.LogWarning("记录失败的登录尝试: {Identifier}, IP: {ClientIP}", identifier, clientIP);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "记录失败登录尝试时发生异常: {Identifier}, IP: {ClientIP}", identifier, clientIP);
            }
        }

        /// <summary>
        /// 清除成功登录后的尝试记录
        /// </summary>
        public void ClearLoginAttempts(string identifier, string clientIP)
        {
            try
            {
                var key = $"{identifier}_{clientIP}";
                _loginAttempts.TryRemove(key, out _);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清除登录尝试记录时发生异常: {Identifier}, IP: {ClientIP}", identifier, clientIP);
            }
        }

        /// <summary>
        /// 加密敏感数据
        /// </summary>
        public string EncryptSensitiveData(string data, string key = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(data))
                    return data;

                // 简单的Base64编码（在生产环境中应使用更强的加密）
                var bytes = Encoding.UTF8.GetBytes(data);
                return Convert.ToBase64String(bytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加密敏感数据时发生异常");
                return data; // 异常时返回原数据
            }
        }

        /// <summary>
        /// 解密敏感数据
        /// </summary>
        public string DecryptSensitiveData(string encryptedData, string key = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(encryptedData))
                    return encryptedData;

                // 简单的Base64解码
                var bytes = Convert.FromBase64String(encryptedData);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解密敏感数据时发生异常");
                return encryptedData; // 异常时返回原数据
            }
        }

        /// <summary>
        /// 生成密码哈希
        /// </summary>
        public string HashPassword(string password, string salt)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(salt))
                    return string.Empty;

                using var sha256 = SHA256.Create();
                var saltedPassword = $"{salt}{password}{salt}";
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(saltedPassword));
                return Convert.ToBase64String(hashedBytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成密码哈希时发生异常");
                return string.Empty;
            }
        }

        /// <summary>
        /// 验证密码哈希
        /// </summary>
        public bool VerifyPassword(string password, string salt, string hashedPassword)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(password) || 
                    string.IsNullOrWhiteSpace(salt) || 
                    string.IsNullOrWhiteSpace(hashedPassword))
                    return false;

                var computedHash = HashPassword(password, salt);
                return computedHash == hashedPassword;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证密码哈希时发生异常");
                return false;
            }
        }

        /// <summary>
        /// 验证客户端IP是否在允许的范围内
        /// </summary>
        public bool IsAllowedIP(string clientIP)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(clientIP))
                    return false;

                // 这里可以实现IP白名单/黑名单逻辑
                // 暂时允许所有IP
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证客户端IP时发生异常: {ClientIP}", clientIP);
                return true; // 异常时允许通过
            }
        }

        /// <summary>
        /// 清理过期的登录尝试记录
        /// </summary>
        public void CleanupExpiredLoginAttempts()
        {
            try
            {
                var now = DateTime.UtcNow;
                var expiredKeys = new List<string>();

                foreach (var kvp in _loginAttempts)
                {
                    if (now - kvp.Value.FirstAttemptTime > TimeSpan.FromMinutes(LoginAttemptsWindowMinutes))
                    {
                        expiredKeys.Add(kvp.Key);
                    }
                }

                foreach (var key in expiredKeys)
                {
                    _loginAttempts.TryRemove(key, out _);
                }

                if (expiredKeys.Count > 0)
                {
                    _logger.LogInformation("清理了 {Count} 个过期的登录尝试记录", expiredKeys.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理过期登录尝试记录时发生异常");
            }
        }
    }

    /// <summary>
    /// 登录尝试信息
    /// </summary>
    public class LoginAttemptInfo
    {
        public DateTime FirstAttemptTime { get; set; }
        public DateTime LastAttemptTime { get; set; }
        public int AttemptCount { get; set; }
    }
}