using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Horizon.Core.Security
{
    /// <summary>
    /// 用户鉴权令牌数据
    /// 包含用户登录时间、机器ID、PassportId及游戏角色Id的加密数据
    /// </summary>
    public class UserAuthTokenData
    {
        /// <summary>
        /// 用户通行证ID
        /// </summary>
        public string PassportId { get; set; } = "";

        /// <summary>
        /// 登录时间（UTC Unix毫秒）
        /// </summary>
        public long LoginTime { get; set; }

        /// <summary>
        /// 客户端机器唯一标识符（机器GUID，替代原来的IP地址以提升跨平台稳定性）
        /// </summary>
        public string MachineId { get; set; } = "";

        /// <summary>
        /// 游戏角色ID（角色进入游戏后设置，初始为0）
        /// </summary>
        public long CharacterId { get; set; }

        /// <summary>
        /// 令牌过期时间（UTC Unix毫秒）
        /// </summary>
        public long ExpiryTime { get; set; }
    }

    /// <summary>
    /// 鉴权令牌验证结果
    /// </summary>
    public class AuthTokenValidationResult
    {
        /// <summary>
        /// 是否验证通过
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string ErrorMessage { get; set; } = "";

        /// <summary>
        /// 解析后的令牌数据（验证通过时有值）
        /// </summary>
        public UserAuthTokenData? TokenData { get; set; }

        public static AuthTokenValidationResult Success(UserAuthTokenData data) =>
            new() { IsValid = true, TokenData = data };

        public static AuthTokenValidationResult Fail(string message) =>
            new() { IsValid = false, ErrorMessage = message };
    }

    /// <summary>
    /// 用户鉴权令牌提供器
    /// 使用AES-256-CBC加密 + HMAC-SHA256签名，确保令牌仅在网关层或服务端层能被解析和验证真伪
    /// 令牌绑定到客户端机器ID（而非IP地址），以在动态IP/NAT环境下保持稳定
    /// </summary>
    public class UserAuthTokenProvider
    {
        private readonly byte[] _encryptionKey;
        private readonly byte[] _hmacKey;
        private readonly ILogger<UserAuthTokenProvider> _logger;

        /// <summary>
        /// 默认令牌有效期（小时）
        /// </summary>
        private const int DefaultTokenExpiryHours = 24;

        /// <summary>
        /// HMAC签名长度（字节）
        /// </summary>
        private const int HmacLength = 32;

        /// <summary>
        /// AES IV长度（字节）
        /// </summary>
        private const int IvLength = 16;

        /// <summary>
        /// AES最小块大小（字节）
        /// </summary>
        private const int MinAesBlockSize = 16;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="secretKey">加密密钥（建议至少32个字符）</param>
        /// <param name="logger">日志记录器</param>
        public UserAuthTokenProvider(string secretKey, ILogger<UserAuthTokenProvider> logger)
        {
            if (string.IsNullOrWhiteSpace(secretKey))
            {
                throw new ArgumentException("鉴权令牌密钥不能为空", nameof(secretKey));
            }

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // 从密钥字符串派生加密密钥和HMAC密钥
            using var sha512 = SHA512.Create();
            var keyMaterial = sha512.ComputeHash(Encoding.UTF8.GetBytes(secretKey));
            _encryptionKey = new byte[32];
            _hmacKey = new byte[32];
            Array.Copy(keyMaterial, 0, _encryptionKey, 0, 32);
            Array.Copy(keyMaterial, 32, _hmacKey, 0, 32);
        }

        /// <summary>
        /// 生成用户鉴权令牌
        /// 将用户登录时间、机器ID、PassportId及游戏角色Id加密为令牌字符串
        /// </summary>
        /// <param name="passportId">用户通行证ID</param>
        /// <param name="machineId">客户端机器唯一标识符（由客户端通过 MachineIdentifier.GetMachineGuid() 获取后上传）</param>
        /// <param name="characterId">游戏角色ID（角色进入游戏前为0）</param>
        /// <param name="tokenExpiryHours">令牌有效期（小时），默认24小时</param>
        /// <returns>加密后的令牌字符串</returns>
        public string GenerateToken(string passportId, string machineId, long characterId = 0, int tokenExpiryHours = DefaultTokenExpiryHours)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(passportId))
                {
                    throw new ArgumentException("PassportId不能为空", nameof(passportId));
                }

                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var tokenData = new UserAuthTokenData
                {
                    PassportId = passportId,
                    LoginTime = now,
                    MachineId = machineId?.Trim() ?? "",
                    CharacterId = characterId,
                    ExpiryTime = now + (long)TimeSpan.FromHours(tokenExpiryHours).TotalMilliseconds
                };

                // 序列化令牌数据
                var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(tokenData);

                // AES-256-CBC加密
                using var aes = Aes.Create();
                aes.Key = _encryptionKey;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.GenerateIV();

                byte[] cipherText;
                using (var encryptor = aes.CreateEncryptor())
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    {
                        cs.Write(jsonBytes, 0, jsonBytes.Length);
                        cs.FlushFinalBlock();
                    }
                    cipherText = ms.ToArray();
                }

                // 计算HMAC-SHA256签名（对IV + 密文进行签名）
                var dataToSign = new byte[IvLength + cipherText.Length];
                Array.Copy(aes.IV, 0, dataToSign, 0, IvLength);
                Array.Copy(cipherText, 0, dataToSign, IvLength, cipherText.Length);

                byte[] hmac;
                using (var hmacSha256 = new HMACSHA256(_hmacKey))
                {
                    hmac = hmacSha256.ComputeHash(dataToSign);
                }

                // 组装令牌：IV + 密文 + HMAC
                var tokenBytes = new byte[IvLength + cipherText.Length + HmacLength];
                Array.Copy(aes.IV, 0, tokenBytes, 0, IvLength);
                Array.Copy(cipherText, 0, tokenBytes, IvLength, cipherText.Length);
                Array.Copy(hmac, 0, tokenBytes, IvLength + cipherText.Length, HmacLength);

                var token = Convert.ToBase64String(tokenBytes);
                _logger.LogDebug("已为用户 {PassportId} 生成鉴权令牌", passportId);
                return token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成鉴权令牌失败: PassportId={PassportId}", passportId);
                throw;
            }
        }

        /// <summary>
        /// 验证用户鉴权令牌
        /// 解密并验证令牌的真伪、有效期及用户身份
        /// </summary>
        /// <param name="token">令牌字符串</param>
        /// <param name="expectedPassportId">期望的PassportId（可选，传入时进行身份匹配验证）</param>
        /// <param name="expectedMachineId">期望的客户端机器ID（可选，传入时进行机器ID匹配验证）</param>
        /// <returns>验证结果</returns>
        public AuthTokenValidationResult ValidateToken(string token, string? expectedPassportId = null, string? expectedMachineId = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    return AuthTokenValidationResult.Fail("鉴权令牌为空");
                }

                // Base64解码
                byte[] tokenBytes;
                try
                {
                    tokenBytes = Convert.FromBase64String(token);
                }
                catch (FormatException)
                {
                    return AuthTokenValidationResult.Fail("鉴权令牌格式无效");
                }

                // 验证最小长度：IV(16) + 至少1块AES密文(MinAesBlockSize) + HMAC(32) = 64
                if (tokenBytes.Length < IvLength + MinAesBlockSize + HmacLength)
                {
                    return AuthTokenValidationResult.Fail("鉴权令牌长度无效");
                }

                // 提取各部分
                var iv = new byte[IvLength];
                var cipherTextLength = tokenBytes.Length - IvLength - HmacLength;
                var cipherText = new byte[cipherTextLength];
                var receivedHmac = new byte[HmacLength];

                Array.Copy(tokenBytes, 0, iv, 0, IvLength);
                Array.Copy(tokenBytes, IvLength, cipherText, 0, cipherTextLength);
                Array.Copy(tokenBytes, IvLength + cipherTextLength, receivedHmac, 0, HmacLength);

                // 验证HMAC签名
                var dataToVerify = new byte[IvLength + cipherTextLength];
                Array.Copy(iv, 0, dataToVerify, 0, IvLength);
                Array.Copy(cipherText, 0, dataToVerify, IvLength, cipherTextLength);

                byte[] computedHmac;
                using (var hmacSha256 = new HMACSHA256(_hmacKey))
                {
                    computedHmac = hmacSha256.ComputeHash(dataToVerify);
                }

                if (!CryptographicOperations.FixedTimeEquals(receivedHmac, computedHmac))
                {
                    _logger.LogWarning("鉴权令牌HMAC签名验证失败，可能存在篡改攻击");
                    return AuthTokenValidationResult.Fail("鉴权令牌签名验证失败");
                }

                // AES解密
                byte[] plainText;
                using (var aes = Aes.Create())
                {
                    aes.Key = _encryptionKey;
                    aes.IV = iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    using var decryptor = aes.CreateDecryptor();
                    using var ms = new MemoryStream(cipherText);
                    using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                    using var resultMs = new MemoryStream();
                    cs.CopyTo(resultMs);
                    plainText = resultMs.ToArray();
                }

                // 反序列化令牌数据
                var tokenData = JsonSerializer.Deserialize<UserAuthTokenData>(plainText);
                if (tokenData == null)
                {
                    return AuthTokenValidationResult.Fail("鉴权令牌数据解析失败");
                }

                // 验证有效期
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (now > tokenData.ExpiryTime)
                {
                    _logger.LogWarning("鉴权令牌已过期: PassportId={PassportId}, ExpiryTime={ExpiryTime}",
                        tokenData.PassportId, tokenData.ExpiryTime);
                    return AuthTokenValidationResult.Fail("鉴权令牌已过期");
                }

                // 验证PassportId匹配
                if (!string.IsNullOrEmpty(expectedPassportId) &&
                    !string.Equals(tokenData.PassportId, expectedPassportId, StringComparison.Ordinal))
                {
                    _logger.LogWarning("鉴权令牌PassportId不匹配: 期望={Expected}, 实际={Actual}",
                        expectedPassportId, tokenData.PassportId);
                    return AuthTokenValidationResult.Fail("鉴权令牌身份不匹配");
                }

                // 验证客户端机器ID匹配（不允许跳过机器ID校验）
                if (!string.IsNullOrEmpty(expectedMachineId))
                {
                    if (string.IsNullOrEmpty(tokenData.MachineId))
                    {
                        _logger.LogWarning("鉴权令牌缺少客户端机器ID: PassportId={PassportId}, Expected={Expected}",
                            tokenData.PassportId, expectedMachineId);
                        return AuthTokenValidationResult.Fail("鉴权令牌缺少客户端机器ID");
                    }

                    if (!string.Equals(tokenData.MachineId.Trim(), expectedMachineId.Trim(), StringComparison.Ordinal))
                    {
                        _logger.LogWarning("鉴权令牌客户端机器ID不匹配: 期望={Expected}, 实际={Actual}",
                            expectedMachineId, tokenData.MachineId);
                        return AuthTokenValidationResult.Fail("鉴权令牌客户端机器ID不匹配");
                    }
                }

                return AuthTokenValidationResult.Success(tokenData);
            }
            catch (CryptographicException ex)
            {
                _logger.LogWarning(ex, "鉴权令牌解密失败，令牌可能被篡改");
                return AuthTokenValidationResult.Fail("鉴权令牌解密失败");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证鉴权令牌时发生异常");
                return AuthTokenValidationResult.Fail("鉴权令牌验证异常");
            }
        }

        /// <summary>
        /// 解析并验证令牌（调用 ValidateToken，会检查有效期，但不验证 PassportId 或机器ID绑定）。
        /// 用于 Grain 层获取令牌中的元数据进行二次验证。
        /// </summary>
        /// <param name="token">令牌字符串</param>
        /// <returns>令牌数据，解析或验证失败返回null</returns>
        public UserAuthTokenData? ParseToken(string token)
        {
            var result = ValidateToken(token);
            return result.IsValid ? result.TokenData : null;
        }

        /// <summary>
        /// 仅解密令牌而不校验有效期，用于令牌过期后的刷新场景。
        /// 仍会验证HMAC签名、PassportId和MachineId匹配，但不检查ExpiryTime。
        /// 适用场景：TokenLogin 路径中，用户持有过期令牌时允许其解密出身份信息后签发新令牌。
        /// </summary>
        /// <param name="token">令牌字符串</param>
        /// <param name="expectedPassportId">期望的PassportId（可选）</param>
        /// <param name="expectedMachineId">期望的客户端机器ID（可选）</param>
        /// <returns>验证结果（不检查有效期）</returns>
        public AuthTokenValidationResult ValidateTokenWithoutExpiryCheck(string token, string? expectedPassportId = null, string? expectedMachineId = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    return AuthTokenValidationResult.Fail("鉴权令牌为空");
                }

                // Base64解码
                byte[] tokenBytes;
                try
                {
                    tokenBytes = Convert.FromBase64String(token);
                }
                catch (FormatException)
                {
                    return AuthTokenValidationResult.Fail("鉴权令牌格式无效");
                }

                // 验证最小长度
                if (tokenBytes.Length < IvLength + MinAesBlockSize + HmacLength)
                {
                    return AuthTokenValidationResult.Fail("鉴权令牌长度无效");
                }

                var iv = new byte[IvLength];
                var cipherTextLength = tokenBytes.Length - IvLength - HmacLength;
                var cipherText = new byte[cipherTextLength];
                var receivedHmac = new byte[HmacLength];

                Array.Copy(tokenBytes, 0, iv, 0, IvLength);
                Array.Copy(tokenBytes, IvLength, cipherText, 0, cipherTextLength);
                Array.Copy(tokenBytes, IvLength + cipherTextLength, receivedHmac, 0, HmacLength);

                // 验证HMAC签名
                var dataToVerify = new byte[IvLength + cipherTextLength];
                Array.Copy(iv, 0, dataToVerify, 0, IvLength);
                Array.Copy(cipherText, 0, dataToVerify, IvLength, cipherTextLength);

                byte[] computedHmac;
                using (var hmacSha256 = new HMACSHA256(_hmacKey))
                {
                    computedHmac = hmacSha256.ComputeHash(dataToVerify);
                }

                if (!CryptographicOperations.FixedTimeEquals(receivedHmac, computedHmac))
                {
                    _logger.LogWarning("鉴权令牌HMAC签名验证失败（跳过有效期模式），可能存在篡改攻击");
                    return AuthTokenValidationResult.Fail("鉴权令牌签名验证失败");
                }

                // AES解密
                byte[] plainText;
                using (var aes = Aes.Create())
                {
                    aes.Key = _encryptionKey;
                    aes.IV = iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    using var decryptor = aes.CreateDecryptor();
                    using var ms = new MemoryStream(cipherText);
                    using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                    using var resultMs = new MemoryStream();
                    cs.CopyTo(resultMs);
                    plainText = resultMs.ToArray();
                }

                // 反序列化令牌数据
                var tokenData = JsonSerializer.Deserialize<UserAuthTokenData>(plainText);
                if (tokenData == null)
                {
                    return AuthTokenValidationResult.Fail("鉴权令牌数据解析失败");
                }

                // 验证PassportId匹配
                if (!string.IsNullOrEmpty(expectedPassportId) &&
                    !string.Equals(tokenData.PassportId, expectedPassportId, StringComparison.Ordinal))
                {
                    _logger.LogWarning("鉴权令牌PassportId不匹配（跳过有效期模式）: 期望={Expected}, 实际={Actual}",
                        expectedPassportId, tokenData.PassportId);
                    return AuthTokenValidationResult.Fail("鉴权令牌身份不匹配");
                }

                // 验证客户端机器ID匹配
                if (!string.IsNullOrEmpty(expectedMachineId))
                {
                    if (string.IsNullOrEmpty(tokenData.MachineId))
                    {
                        _logger.LogWarning("鉴权令牌缺少客户端机器ID（跳过有效期模式）: PassportId={PassportId}",
                            tokenData.PassportId);
                        return AuthTokenValidationResult.Fail("鉴权令牌缺少客户端机器ID");
                    }

                    if (!string.Equals(tokenData.MachineId.Trim(), expectedMachineId.Trim(), StringComparison.Ordinal))
                    {
                        _logger.LogWarning("鉴权令牌客户端机器ID不匹配（跳过有效期模式）: 期望={Expected}, 实际={Actual}",
                            expectedMachineId, tokenData.MachineId);
                        return AuthTokenValidationResult.Fail("鉴权令牌客户端机器ID不匹配");
                    }
                }

                // 注意：此处故意不检查 ExpiryTime，允许过期令牌通过
                _logger.LogInformation("鉴权令牌解密成功（跳过有效期检查）: PassportId={PassportId}, ExpiryTime={ExpiryTime}",
                    tokenData.PassportId, tokenData.ExpiryTime);

                return AuthTokenValidationResult.Success(tokenData);
            }
            catch (CryptographicException ex)
            {
                _logger.LogWarning(ex, "鉴权令牌解密失败（跳过有效期模式），令牌可能被篡改");
                return AuthTokenValidationResult.Fail("鉴权令牌解密失败");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解析鉴权令牌时发生异常（跳过有效期模式）");
                return AuthTokenValidationResult.Fail("鉴权令牌解析异常");
            }
        }
    }
}
