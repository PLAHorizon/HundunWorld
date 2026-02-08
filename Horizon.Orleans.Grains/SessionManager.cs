using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Core;
using Horizon.Core.Abstract.Enums;
using Microsoft.Extensions.Logging;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 会话管理服务 - Redis持久化实现
    /// Implements Redis-based session persistence for user sessions
    /// </summary>
    public class SessionManager
    {
        private readonly ILogger _logger;
        private const string SESSION_KEY_PREFIX = "SESSION:";
        private const string USER_SESSIONS_KEY_PREFIX = "USER:SESSIONS:";
        private const int DEFAULT_SESSION_TIMEOUT_MINUTES = 1440; // 24 hours

        public SessionManager(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 创建新会话
        /// </summary>
        public async Task<string> CreateSessionAsync(SessionInfo sessionInfo, int timeoutMinutes = DEFAULT_SESSION_TIMEOUT_MINUTES)
        {
            try
            {
                if (sessionInfo == null)
                {
                    throw new ArgumentNullException(nameof(sessionInfo));
                }

                // 生成唯一的会话ID
                if (string.IsNullOrEmpty(sessionInfo.SessionId))
                {
                    sessionInfo.SessionId = GenerateSessionId();
                }

                sessionInfo.CreateTime = DateTime.UtcNow;
                sessionInfo.LastActiveTime = DateTime.UtcNow;
                sessionInfo.IsActive = true;

                // 存储会话到Redis
                string sessionKey = GetSessionKey(sessionInfo.SessionId);
                await Cache.InsertAsync(sessionKey, sessionInfo, timeoutMinutes);

                // 将会话ID添加到用户的会话集合
                string userSessionsKey = GetUserSessionsKey(sessionInfo.PassportId);
                await Cache.AddItemToSetAsync(userSessionsKey, sessionInfo.SessionId);

                _logger.LogInformation("会话创建成功: SessionId={SessionId}, PassportId={PassportId}", 
                    sessionInfo.SessionId, sessionInfo.PassportId);

                return sessionInfo.SessionId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建会话失败: PassportId={PassportId}", sessionInfo?.PassportId);
                throw;
            }
        }

        /// <summary>
        /// 获取会话信息
        /// </summary>
        public async Task<SessionInfo> GetSessionAsync(string sessionId)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionId))
                {
                    return null;
                }

                string sessionKey = GetSessionKey(sessionId);
                var session = await Cache.GetAsync<SessionInfo>(sessionKey);

                if (session != null && session.IsActive)
                {
                    // 更新最后活跃时间
                    session.LastActiveTime = DateTime.UtcNow;
                    await Cache.InsertAsync(sessionKey, session, DEFAULT_SESSION_TIMEOUT_MINUTES);
                }

                return session;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取会话失败: SessionId={SessionId}", sessionId);
                return null;
            }
        }

        /// <summary>
        /// 更新会话信息
        /// </summary>
        public async Task<bool> UpdateSessionAsync(SessionInfo sessionInfo)
        {
            try
            {
                if (sessionInfo == null || string.IsNullOrEmpty(sessionInfo.SessionId))
                {
                    return false;
                }

                sessionInfo.LastActiveTime = DateTime.UtcNow;

                string sessionKey = GetSessionKey(sessionInfo.SessionId);
                return await Cache.InsertAsync(sessionKey, sessionInfo, DEFAULT_SESSION_TIMEOUT_MINUTES);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新会话失败: SessionId={SessionId}", sessionInfo?.SessionId);
                return false;
            }
        }

        /// <summary>
        /// 刷新会话过期时间
        /// </summary>
        public async Task<bool> RefreshSessionAsync(string sessionId, int timeoutMinutes = DEFAULT_SESSION_TIMEOUT_MINUTES)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionId))
                {
                    return false;
                }

                string sessionKey = GetSessionKey(sessionId);
                var session = await Cache.GetAsync<SessionInfo>(sessionKey);

                if (session != null && session.IsActive)
                {
                    session.LastActiveTime = DateTime.UtcNow;
                    return await Cache.InsertAsync(sessionKey, session, timeoutMinutes);
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刷新会话失败: SessionId={SessionId}", sessionId);
                return false;
            }
        }

        /// <summary>
        /// 终止会话
        /// </summary>
        public async Task<bool> TerminateSessionAsync(string sessionId)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionId))
                {
                    return false;
                }

                // 获取会话信息
                var session = await GetSessionAsync(sessionId);
                if (session == null)
                {
                    return false;
                }

                // 标记为非活跃
                session.IsActive = false;

                // 从用户会话集合中移除
                string userSessionsKey = GetUserSessionsKey(session.PassportId);
                await Cache.RemoveItemFromSetAsync(userSessionsKey, sessionId);

                // 删除会话
                string sessionKey = GetSessionKey(sessionId);
                await Cache.RemoveAsync(sessionKey);

                _logger.LogInformation("会话终止成功: SessionId={SessionId}, PassportId={PassportId}", 
                    sessionId, session.PassportId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "终止会话失败: SessionId={SessionId}", sessionId);
                return false;
            }
        }

        /// <summary>
        /// 获取用户的所有活跃会话
        /// </summary>
        public async Task<List<SessionInfo>> GetUserSessionsAsync(string passportId)
        {
            try
            {
                if (string.IsNullOrEmpty(passportId))
                {
                    return new List<SessionInfo>();
                }

                string userSessionsKey = GetUserSessionsKey(passportId);
                var sessionIds = await Cache.GetAllItemsFromSetAsync<string>(userSessionsKey);

                if (sessionIds == null || !sessionIds.Any())
                {
                    return new List<SessionInfo>();
                }

                var sessions = new List<SessionInfo>();
                foreach (var sessionId in sessionIds)
                {
                    var session = await GetSessionAsync(sessionId);
                    if (session != null && session.IsActive)
                    {
                        sessions.Add(session);
                    }
                }

                return sessions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户会话失败: PassportId={PassportId}", passportId);
                return new List<SessionInfo>();
            }
        }

        /// <summary>
        /// 终止用户的所有会话
        /// </summary>
        public async Task<bool> TerminateUserSessionsAsync(string passportId)
        {
            try
            {
                if (string.IsNullOrEmpty(passportId))
                {
                    return false;
                }

                var sessions = await GetUserSessionsAsync(passportId);
                foreach (var session in sessions)
                {
                    await TerminateSessionAsync(session.SessionId);
                }

                _logger.LogInformation("用户所有会话已终止: PassportId={PassportId}, Count={Count}", 
                    passportId, sessions.Count);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "终止用户所有会话失败: PassportId={PassportId}", passportId);
                return false;
            }
        }

        /// <summary>
        /// 验证会话是否有效
        /// </summary>
        public async Task<bool> ValidateSessionAsync(string sessionId)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionId))
                {
                    return false;
                }

                var session = await GetSessionAsync(sessionId);
                return session != null && session.IsActive;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证会话失败: SessionId={SessionId}", sessionId);
                return false;
            }
        }

        /// <summary>
        /// 清理过期会话（由后台任务调用）
        /// </summary>
        public async Task<int> CleanupExpiredSessionsAsync()
        {
            try
            {
                int cleanedCount = 0;
                // Redis会自动处理过期键，这里只是记录清理操作
                _logger.LogInformation("会话清理任务执行完成，清理数量: {Count}", cleanedCount);
                return cleanedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理过期会话失败");
                return 0;
            }
        }

        #region Private Helper Methods

        private string GetSessionKey(string sessionId)
        {
            return $"{SESSION_KEY_PREFIX}{sessionId}";
        }

        private string GetUserSessionsKey(string passportId)
        {
            return $"{USER_SESSIONS_KEY_PREFIX}{passportId}";
        }

        private string GenerateSessionId()
        {
            return $"{Guid.NewGuid():N}_{DateTime.UtcNow.Ticks}";
        }

        #endregion
    }
}
