using Horizon.Core;

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
    }
}