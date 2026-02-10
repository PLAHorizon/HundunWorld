using System;
using System.Security.Cryptography;
using System.Text;

namespace Horizon.Core
{
    /// <summary>
    /// 安全密码哈希工具类
    /// 使用 PBKDF2 (Password-Based Key Derivation Function 2) 和 HMACSHA512 进行密码哈希
    /// 符合 OWASP 密码存储最佳实践
    /// </summary>
    public static class SecurePasswordHasher
    {
        // PBKDF2 迭代次数 - OWASP 推荐至少 210,000 次用于 HMACSHA512
        private const int Iterations = 210000;
        
        // 盐值长度（字节）
        private const int SaltSize = 32; // 256 bits
        
        // 哈希长度（字节）
        private const int HashSize = 64; // 512 bits

        /// <summary>
        /// 生成密码哈希和盐值
        /// </summary>
        /// <param name="password">明文密码</param>
        /// <returns>包含哈希值和盐值的元组</returns>
        public static (string hash, string salt) HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentNullException(nameof(password), "密码不能为空");
            }

            // 生成随机盐值
            byte[] salt = GenerateSalt();
            
            // 生成哈希
            byte[] hash = HashPasswordWithSalt(password, salt);
            
            // 返回 Base64 编码的哈希值和盐值
            return (
                Convert.ToBase64String(hash),
                Convert.ToBase64String(salt)
            );
        }

        /// <summary>
        /// 验证密码是否匹配
        /// </summary>
        /// <param name="password">用户输入的明文密码</param>
        /// <param name="storedHash">存储的哈希值（Base64）</param>
        /// <param name="storedSalt">存储的盐值（Base64）</param>
        /// <returns>密码是否匹配</returns>
        public static bool VerifyPassword(string password, string storedHash, string storedSalt)
        {
            if (string.IsNullOrEmpty(password))
            {
                return false;
            }

            if (string.IsNullOrEmpty(storedHash) || string.IsNullOrEmpty(storedSalt))
            {
                return false;
            }

            try
            {
                // 解码存储的盐值
                byte[] salt = Convert.FromBase64String(storedSalt);
                
                // 使用相同的盐值计算输入密码的哈希
                byte[] hash = HashPasswordWithSalt(password, salt);
                
                // 解码存储的哈希
                byte[] storedHashBytes = Convert.FromBase64String(storedHash);
                
                // 使用时间常量比较防止时序攻击
                return CryptographicOperations.FixedTimeEquals(hash, storedHashBytes);
            }
            catch
            {
                // 如果解码失败或发生其他错误，返回 false
                return false;
            }
        }

        /// <summary>
        /// 生成随机盐值
        /// </summary>
        /// <returns>盐值字节数组</returns>
        private static byte[] GenerateSalt()
        {
            byte[] salt = new byte[SaltSize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }
            return salt;
        }

        /// <summary>
        /// 使用 PBKDF2 和指定盐值生成密码哈希
        /// </summary>
        /// <param name="password">明文密码</param>
        /// <param name="salt">盐值</param>
        /// <returns>哈希值字节数组</returns>
        private static byte[] HashPasswordWithSalt(string password, byte[] salt)
        {
            // 使用现代的静态方法 Pbkdf2 (推荐用于 .NET 6+)
            // 这比使用 Rfc2898DeriveBytes 构造函数更高效，且不需要 IDisposable
            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
            return Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes,
                salt,
                Iterations,
                HashAlgorithmName.SHA512,
                HashSize);
        }

        /// <summary>
        /// 为旧系统提供的迁移方法：从明文密码生成哈希
        /// 注意：此方法仅用于数据迁移，不应在新代码中使用
        /// </summary>
        /// <param name="plaintextPassword">明文密码</param>
        /// <param name="passportId">通行证ID（用于向后兼容）</param>
        /// <returns>包含哈希值和盐值的元组</returns>
        [Obsolete("此方法仅用于从旧系统迁移数据，不应在新代码中使用")]
        public static (string hash, string salt) MigrateFromPlaintext(string plaintextPassword, string passportId)
        {
            // 使用旧的加密方法获取加密后的密码
            string encryptedPassword = PassportHelper.SetPasportPassword(passportId, plaintextPassword);
            
            // 对加密后的密码进行安全哈希
            return HashPassword(encryptedPassword);
        }

        /// <summary>
        /// 验证密码强度
        /// </summary>
        /// <param name="password">密码</param>
        /// <returns>是否满足强度要求</returns>
        public static bool IsPasswordStrong(string password)
        {
            if (string.IsNullOrEmpty(password))
                return false;

            // 最小长度 8 个字符
            if (password.Length < 8)
                return false;

            bool hasUpper = false;
            bool hasLower = false;
            bool hasDigit = false;
            bool hasSpecial = false;

            foreach (char c in password)
            {
                if (char.IsUpper(c)) hasUpper = true;
                else if (char.IsLower(c)) hasLower = true;
                else if (char.IsDigit(c)) hasDigit = true;
                else if (!char.IsLetterOrDigit(c)) hasSpecial = true;
            }

            // 至少包含大写、小写、数字中的三种
            int complexity = (hasUpper ? 1 : 0) + (hasLower ? 1 : 0) + 
                           (hasDigit ? 1 : 0) + (hasSpecial ? 1 : 0);

            return complexity >= 3;
        }
    }
}
