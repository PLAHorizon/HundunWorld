using Horizon.Core;
using Horizon.Core.Abstract;
using Horizon.Orleans.Grains;
using Microsoft.Extensions.Logging;
using Moq;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// SessionManager 单元测试
    /// 测试会话创建、获取、终止、验证和清理功能
    /// </summary>
    [Collection("CacheTests")]
    public class SessionManagerTests : IDisposable
    {
        private readonly SessionManager _sessionManager;
        private readonly Mock<ILogger<SessionManager>> _loggerMock;
        private readonly Mock<ICache> _cacheMock;
        private readonly Dictionary<string, object> _cacheStore;

        public SessionManagerTests()
        {
            _loggerMock = new Mock<ILogger<SessionManager>>();
            _cacheMock = new Mock<ICache>();
            _cacheStore = new Dictionary<string, object>();

            // 设置ICache mock到Cache.Current
            Cache.Current = _cacheMock.Object;

            // 设置基本的Cache mock行为
            SetupCacheMock();

            _sessionManager = new SessionManager(_loggerMock.Object);
        }

        public void Dispose()
        {
            _cacheStore.Clear();
        }

        private void SetupCacheMock()
        {
            // Mock InsertAsync(string key, object data, int cacheTime)
            _cacheMock.Setup(c => c.InsertAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<int>()))
                .ReturnsAsync((string key, object data, int time) =>
                {
                    _cacheStore[key] = data;
                    return true;
                });

            // Mock InsertAsync<SessionInfo>(string key, SessionInfo data, int cacheTime)
            _cacheMock.Setup(c => c.InsertAsync<SessionInfo>(It.IsAny<string>(), It.IsAny<SessionInfo>(), It.IsAny<int>()))
                .ReturnsAsync((string key, SessionInfo data, int time) =>
                {
                    _cacheStore[key] = data;
                    return true;
                });

            // Mock InsertAsync(string key, object data)
            _cacheMock.Setup(c => c.InsertAsync(It.IsAny<string>(), It.IsAny<object>()))
                .ReturnsAsync((string key, object data) =>
                {
                    _cacheStore[key] = data;
                    return true;
                });

            // Mock InsertAsync<SessionInfo>(string key, SessionInfo data)
            _cacheMock.Setup(c => c.InsertAsync<SessionInfo>(It.IsAny<string>(), It.IsAny<SessionInfo>()))
                .ReturnsAsync((string key, SessionInfo data) =>
                {
                    _cacheStore[key] = data;
                    return true;
                });

            // Mock GetAsync<T>
            _cacheMock.Setup(c => c.GetAsync<SessionInfo>(It.IsAny<string>()))
                .ReturnsAsync((string key) =>
                {
                    if (_cacheStore.TryGetValue(key, out var value) && value is SessionInfo session)
                        return session;
                    return null!;
                });

            // Mock RemoveAsync
            _cacheMock.Setup(c => c.RemoveAsync(It.IsAny<string>()))
                .Returns((string key) =>
                {
                    _cacheStore.Remove(key);
                    return Task.CompletedTask;
                });

            // Mock AddItemToSetAsync
            _cacheMock.Setup(c => c.AddItemToSetAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string setId, string item) =>
                {
                    if (!_cacheStore.ContainsKey(setId))
                        _cacheStore[setId] = new HashSet<string>();
                    ((HashSet<string>)_cacheStore[setId]).Add(item);
                    return Task.CompletedTask;
                });

            // Mock RemoveItemFromSetAsync
            _cacheMock.Setup(c => c.RemoveItemFromSetAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string setId, string item) =>
                {
                    if (_cacheStore.TryGetValue(setId, out var value) && value is HashSet<string> set)
                        set.Remove(item);
                    return Task.CompletedTask;
                });

            // Mock GetAllItemsFromSetAsync<string>
            _cacheMock.Setup(c => c.GetAllItemsFromSetAsync<string>(It.IsAny<string>()))
                .ReturnsAsync((string setId) =>
                {
                    if (_cacheStore.TryGetValue(setId, out var value) && value is HashSet<string> set)
                        return set;
                    return new HashSet<string>();
                });
        }

        #region CreateSessionAsync Tests

        [Fact]
        public async Task CreateSessionAsync_ValidSession_ReturnsSessionId()
        {
            // Arrange
            var sessionInfo = CreateTestSessionInfo();

            // Act
            var sessionId = await _sessionManager.CreateSessionAsync(sessionInfo);

            // Assert
            Assert.False(string.IsNullOrEmpty(sessionId));
            Assert.Equal(sessionInfo.SessionId, sessionId);
        }

        [Fact]
        public async Task CreateSessionAsync_NullSession_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => _sessionManager.CreateSessionAsync(null!));
        }

        [Fact]
        public async Task CreateSessionAsync_NoSessionId_GeneratesOne()
        {
            // Arrange
            var sessionInfo = CreateTestSessionInfo();
            sessionInfo.SessionId = null!;

            // Act
            var sessionId = await _sessionManager.CreateSessionAsync(sessionInfo);

            // Assert
            Assert.False(string.IsNullOrEmpty(sessionId));
        }

        [Fact]
        public async Task CreateSessionAsync_SetsCreateTimeAndActiveTime()
        {
            // Arrange
            var sessionInfo = CreateTestSessionInfo();
            var beforeCreate = DateTime.UtcNow;

            // Act
            await _sessionManager.CreateSessionAsync(sessionInfo);

            // Assert
            Assert.True(sessionInfo.CreateTime >= beforeCreate);
            Assert.True(sessionInfo.LastActiveTime >= beforeCreate);
            Assert.True(sessionInfo.IsActive);
        }

        [Fact]
        public async Task CreateSessionAsync_StoresSessionInCache()
        {
            // Arrange
            var sessionInfo = CreateTestSessionInfo();

            // Act
            var sessionId = await _sessionManager.CreateSessionAsync(sessionInfo);

            // Assert - 验证会话数据存储到缓存中
            Assert.True(_cacheStore.ContainsKey($"SESSION:{sessionId}"));
        }

        [Fact]
        public async Task CreateSessionAsync_AddsToUserSessionSet()
        {
            // Arrange
            var sessionInfo = CreateTestSessionInfo();

            // Act
            await _sessionManager.CreateSessionAsync(sessionInfo);

            // Assert - 验证AddItemToSetAsync被调用
            _cacheMock.Verify(c => c.AddItemToSetAsync(
                It.Is<string>(k => k.Contains(sessionInfo.PassportId)),
                It.IsAny<string>()), Times.Once);
        }

        #endregion

        #region GetSessionAsync Tests

        [Fact]
        public async Task GetSessionAsync_ExistingSession_ReturnsSession()
        {
            // Arrange
            var sessionInfo = CreateTestSessionInfo();
            var sessionId = await _sessionManager.CreateSessionAsync(sessionInfo);

            // Act
            var result = await _sessionManager.GetSessionAsync(sessionId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(sessionId, result.SessionId);
        }

        [Fact]
        public async Task GetSessionAsync_NullSessionId_ReturnsNull()
        {
            // Act
            var result = await _sessionManager.GetSessionAsync(null!);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetSessionAsync_EmptySessionId_ReturnsNull()
        {
            // Act
            var result = await _sessionManager.GetSessionAsync(string.Empty);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetSessionAsync_NonExistentSession_ReturnsNull()
        {
            // Act
            var result = await _sessionManager.GetSessionAsync("nonexistent_session_id");

            // Assert
            Assert.Null(result);
        }

        #endregion

        #region TerminateSessionAsync Tests

        [Fact]
        public async Task TerminateSessionAsync_ExistingSession_ReturnsTrue()
        {
            // Arrange
            var sessionInfo = CreateTestSessionInfo();
            var sessionId = await _sessionManager.CreateSessionAsync(sessionInfo);

            // Act
            var result = await _sessionManager.TerminateSessionAsync(sessionId);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task TerminateSessionAsync_NullSessionId_ReturnsFalse()
        {
            // Act
            var result = await _sessionManager.TerminateSessionAsync(null!);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task TerminateSessionAsync_NonExistentSession_ReturnsFalse()
        {
            // Act
            var result = await _sessionManager.TerminateSessionAsync("nonexistent_id");

            // Assert
            Assert.False(result);
        }

        #endregion

        #region ValidateSessionAsync Tests

        [Fact]
        public async Task ValidateSessionAsync_ActiveSession_ReturnsTrue()
        {
            // Arrange
            var sessionInfo = CreateTestSessionInfo();
            var sessionId = await _sessionManager.CreateSessionAsync(sessionInfo);

            // Act
            var result = await _sessionManager.ValidateSessionAsync(sessionId);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task ValidateSessionAsync_NullSessionId_ReturnsFalse()
        {
            // Act
            var result = await _sessionManager.ValidateSessionAsync(null!);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task ValidateSessionAsync_NonExistentSession_ReturnsFalse()
        {
            // Act
            var result = await _sessionManager.ValidateSessionAsync("nonexistent_id");

            // Assert
            Assert.False(result);
        }

        #endregion

        #region UpdateSessionAsync Tests

        [Fact]
        public async Task UpdateSessionAsync_ValidSession_ReturnsTrue()
        {
            // Arrange
            var sessionInfo = CreateTestSessionInfo();
            await _sessionManager.CreateSessionAsync(sessionInfo);

            // Act
            var result = await _sessionManager.UpdateSessionAsync(sessionInfo);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task UpdateSessionAsync_NullSession_ReturnsFalse()
        {
            // Act
            var result = await _sessionManager.UpdateSessionAsync(null!);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task UpdateSessionAsync_NoSessionId_ReturnsFalse()
        {
            // Arrange
            var sessionInfo = CreateTestSessionInfo();
            sessionInfo.SessionId = null!;

            // Act
            var result = await _sessionManager.UpdateSessionAsync(sessionInfo);

            // Assert
            Assert.False(result);
        }

        #endregion

        #region RefreshSessionAsync Tests

        [Fact]
        public async Task RefreshSessionAsync_ActiveSession_ReturnsTrue()
        {
            // Arrange
            var sessionInfo = CreateTestSessionInfo();
            var sessionId = await _sessionManager.CreateSessionAsync(sessionInfo);

            // Act
            var result = await _sessionManager.RefreshSessionAsync(sessionId);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task RefreshSessionAsync_NullSessionId_ReturnsFalse()
        {
            // Act
            var result = await _sessionManager.RefreshSessionAsync(null!);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task RefreshSessionAsync_EmptySessionId_ReturnsFalse()
        {
            // Act
            var result = await _sessionManager.RefreshSessionAsync(string.Empty);

            // Assert
            Assert.False(result);
        }

        #endregion

        #region GetUserSessionsAsync Tests

        [Fact]
        public async Task GetUserSessionsAsync_NullPassportId_ReturnsEmptyList()
        {
            // Act
            var result = await _sessionManager.GetUserSessionsAsync(null!);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetUserSessionsAsync_EmptyPassportId_ReturnsEmptyList()
        {
            // Act
            var result = await _sessionManager.GetUserSessionsAsync(string.Empty);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetUserSessionsAsync_NoSessions_ReturnsEmptyList()
        {
            // Act
            var result = await _sessionManager.GetUserSessionsAsync("unknown_passport");

            // Assert
            Assert.Empty(result);
        }

        #endregion

        #region TerminateUserSessionsAsync Tests

        [Fact]
        public async Task TerminateUserSessionsAsync_NullPassportId_ReturnsFalse()
        {
            // Act
            var result = await _sessionManager.TerminateUserSessionsAsync(null!);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task TerminateUserSessionsAsync_ValidPassportId_ReturnsTrue()
        {
            // Act
            var result = await _sessionManager.TerminateUserSessionsAsync("test_passport");

            // Assert
            Assert.True(result);
        }

        #endregion

        #region CleanupExpiredSessionsAsync Tests

        [Fact]
        public async Task CleanupExpiredSessionsAsync_ReturnsZero()
        {
            // Act (Redis自动处理过期，此方法目前只记录日志)
            var result = await _sessionManager.CleanupExpiredSessionsAsync();

            // Assert
            Assert.Equal(0, result);
        }

        #endregion

        #region Helper Methods

        private static SessionInfo CreateTestSessionInfo()
        {
            return new SessionInfo
            {
                SessionId = $"test_{Guid.NewGuid():N}",
                UserId = Guid.NewGuid(),
                PassportId = "TEST_PASSPORT_001",
                AppId = 1,
                IsActive = true,
                CreateTime = DateTime.UtcNow,
                LastActiveTime = DateTime.UtcNow,
                ClientIP = "127.0.0.1",
                PlatformId = "test_platform",
                DeviceId = "test_device"
            };
        }

        #endregion
    }
}