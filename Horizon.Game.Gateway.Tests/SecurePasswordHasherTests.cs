using Horizon.Core;
using System.Text;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// SecurePasswordHasher 单元测试
    /// 测试密码哈希、验证、强度检查和迁移功能
    /// </summary>
    public class SecurePasswordHasherTests
    {
        #region HashPassword Tests

        [Fact]
        public void HashPassword_ValidPassword_ReturnsHashAndSalt()
        {
            // Arrange
            string password = "TestPassword123!";

            // Act
            var (hash, salt) = SecurePasswordHasher.HashPassword(password);

            // Assert
            Assert.False(string.IsNullOrEmpty(hash));
            Assert.False(string.IsNullOrEmpty(salt));
        }

        [Fact]
        public void HashPassword_SamePassword_ReturnsDifferentHashes()
        {
            // Arrange
            string password = "TestPassword123!";

            // Act
            var (hash1, salt1) = SecurePasswordHasher.HashPassword(password);
            var (hash2, salt2) = SecurePasswordHasher.HashPassword(password);

            // Assert - 不同的盐值应产生不同的哈希
            Assert.NotEqual(salt1, salt2);
            Assert.NotEqual(hash1, hash2);
        }

        [Fact]
        public void HashPassword_NullPassword_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => SecurePasswordHasher.HashPassword(null!));
        }

        [Fact]
        public void HashPassword_EmptyPassword_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => SecurePasswordHasher.HashPassword(string.Empty));
        }

        [Fact]
        public void HashPassword_ReturnsBase64EncodedStrings()
        {
            // Arrange
            string password = "TestPassword123!";

            // Act
            var (hash, salt) = SecurePasswordHasher.HashPassword(password);

            // Assert - 验证是有效的Base64字符串
            byte[] hashBytes = Convert.FromBase64String(hash);
            byte[] saltBytes = Convert.FromBase64String(salt);
            Assert.Equal(64, hashBytes.Length); // 512 bits = 64 bytes
            Assert.Equal(32, saltBytes.Length); // 256 bits = 32 bytes
        }

        #endregion

        #region VerifyPassword Tests

        [Fact]
        public void VerifyPassword_CorrectPassword_ReturnsTrue()
        {
            // Arrange
            string password = "TestPassword123!";
            var (hash, salt) = SecurePasswordHasher.HashPassword(password);

            // Act
            bool result = SecurePasswordHasher.VerifyPassword(password, hash, salt);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void VerifyPassword_WrongPassword_ReturnsFalse()
        {
            // Arrange
            string password = "TestPassword123!";
            var (hash, salt) = SecurePasswordHasher.HashPassword(password);

            // Act
            bool result = SecurePasswordHasher.VerifyPassword("WrongPassword456!", hash, salt);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void VerifyPassword_NullPassword_ReturnsFalse()
        {
            // Arrange
            var (hash, salt) = SecurePasswordHasher.HashPassword("TestPassword123!");

            // Act
            bool result = SecurePasswordHasher.VerifyPassword(null!, hash, salt);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void VerifyPassword_EmptyPassword_ReturnsFalse()
        {
            // Arrange
            var (hash, salt) = SecurePasswordHasher.HashPassword("TestPassword123!");

            // Act
            bool result = SecurePasswordHasher.VerifyPassword(string.Empty, hash, salt);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void VerifyPassword_NullHash_ReturnsFalse()
        {
            // Act
            bool result = SecurePasswordHasher.VerifyPassword("TestPassword123!", null!, "somesalt");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void VerifyPassword_NullSalt_ReturnsFalse()
        {
            // Act
            bool result = SecurePasswordHasher.VerifyPassword("TestPassword123!", "somehash", null!);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void VerifyPassword_InvalidBase64Hash_ReturnsFalse()
        {
            // Act
            bool result = SecurePasswordHasher.VerifyPassword("TestPassword123!", "not-valid-base64!!!", "dGVzdA==");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void VerifyPassword_CaseSensitive_ReturnsFalse()
        {
            // Arrange
            string password = "TestPassword123!";
            var (hash, salt) = SecurePasswordHasher.HashPassword(password);

            // Act - 密码应区分大小写
            bool result = SecurePasswordHasher.VerifyPassword("testpassword123!", hash, salt);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void VerifyPassword_UnicodePassword_WorksCorrectly()
        {
            // Arrange
            string password = "混沌世界Test123!";
            var (hash, salt) = SecurePasswordHasher.HashPassword(password);

            // Act
            bool result = SecurePasswordHasher.VerifyPassword(password, hash, salt);

            // Assert
            Assert.True(result);
        }

        #endregion

        #region IsPasswordStrong Tests

        [Fact]
        public void IsPasswordStrong_NullPassword_ReturnsFalse()
        {
            Assert.False(SecurePasswordHasher.IsPasswordStrong(null!));
        }

        [Fact]
        public void IsPasswordStrong_EmptyPassword_ReturnsFalse()
        {
            Assert.False(SecurePasswordHasher.IsPasswordStrong(string.Empty));
        }

        [Fact]
        public void IsPasswordStrong_TooShort_ReturnsFalse()
        {
            Assert.False(SecurePasswordHasher.IsPasswordStrong("Ab1!"));
        }

        [Fact]
        public void IsPasswordStrong_OnlyLowercase_ReturnsFalse()
        {
            Assert.False(SecurePasswordHasher.IsPasswordStrong("abcdefghijklmnop"));
        }

        [Fact]
        public void IsPasswordStrong_UpperLowerDigit_ReturnsTrue()
        {
            // 至少3种复杂性: 大写+小写+数字
            Assert.True(SecurePasswordHasher.IsPasswordStrong("TestPass123"));
        }

        [Fact]
        public void IsPasswordStrong_UpperLowerSpecial_ReturnsTrue()
        {
            // 至少3种复杂性: 大写+小写+特殊字符
            Assert.True(SecurePasswordHasher.IsPasswordStrong("TestPass!!"));
        }

        [Fact]
        public void IsPasswordStrong_LowerDigitSpecial_ReturnsTrue()
        {
            // 至少3种复杂性: 小写+数字+特殊字符
            Assert.True(SecurePasswordHasher.IsPasswordStrong("testpass1!"));
        }

        [Fact]
        public void IsPasswordStrong_OnlyUpperAndLower_ReturnsFalse()
        {
            // 只有2种复杂性: 大写+小写
            Assert.False(SecurePasswordHasher.IsPasswordStrong("TestPassword"));
        }

        [Fact]
        public void IsPasswordStrong_ExactlyEightChars_WithComplexity_ReturnsTrue()
        {
            Assert.True(SecurePasswordHasher.IsPasswordStrong("Test12!a"));
        }

        #endregion

        #region Password Encoding Tests - Base64 Compatibility

        [Fact]
        public void HashPassword_WithBase64EncodedInput_WorksCorrectly()
        {
            // Arrange
            string plainPassword = "TestPassword123!";
            string base64Password = Convert.ToBase64String(Encoding.UTF8.GetBytes(plainPassword));
            
            // Decode back to plaintext (simulating what the system does)
            string decodedPassword = Encoding.UTF8.GetString(Convert.FromBase64String(base64Password));

            // Act - Hash the decoded password
            var (hash, salt) = SecurePasswordHasher.HashPassword(decodedPassword);

            // Assert - Verify with the original plaintext password
            bool isValid = SecurePasswordHasher.VerifyPassword(plainPassword, hash, salt);
            Assert.True(isValid);
        }

        [Fact]
        public void VerifyPassword_WithBase64InputAndOutput_WorksCorrectly()
        {
            // Arrange - Simulate registration flow
            string plainPassword = "MySecure123!";
            string base64Password = Convert.ToBase64String(Encoding.UTF8.GetBytes(plainPassword));
            string decodedPassword = Encoding.UTF8.GetString(Convert.FromBase64String(base64Password));
            
            var (hash, salt) = SecurePasswordHasher.HashPassword(decodedPassword);

            // Act - Simulate login flow
            string loginBase64Password = Convert.ToBase64String(Encoding.UTF8.GetBytes(plainPassword));
            string loginDecodedPassword = Encoding.UTF8.GetString(Convert.FromBase64String(loginBase64Password));
            bool isValid = SecurePasswordHasher.VerifyPassword(loginDecodedPassword, hash, salt);

            // Assert
            Assert.True(isValid);
        }

        [Fact]
        public void HashPassword_ConsistentResultsWithBase64Workflow()
        {
            // Arrange
            string password = "ComplexPass1!";
            
            // Simulate encoding/decoding workflow
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(password));
            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));

            // Act
            var (hash1, salt1) = SecurePasswordHasher.HashPassword(password);
            var (hash2, salt2) = SecurePasswordHasher.HashPassword(decoded);

            // Assert - Both should be able to verify the same password
            Assert.True(SecurePasswordHasher.VerifyPassword(password, hash1, salt1));
            Assert.True(SecurePasswordHasher.VerifyPassword(decoded, hash2, salt2));
            Assert.True(SecurePasswordHasher.VerifyPassword(password, hash2, salt2));
            Assert.True(SecurePasswordHasher.VerifyPassword(decoded, hash1, salt1));
        }

        [Fact]
        public void VerifyPassword_WithChineseCharacters_WorksCorrectly()
        {
            // Arrange
            string password = "中文密码Test123!";
            string base64Password = Convert.ToBase64String(Encoding.UTF8.GetBytes(password));
            string decodedPassword = Encoding.UTF8.GetString(Convert.FromBase64String(base64Password));
            
            var (hash, salt) = SecurePasswordHasher.HashPassword(decodedPassword);

            // Act
            bool isValid = SecurePasswordHasher.VerifyPassword(password, hash, salt);

            // Assert
            Assert.True(isValid);
        }

        #endregion
    }
}