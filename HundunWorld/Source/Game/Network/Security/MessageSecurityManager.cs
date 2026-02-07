using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace HundunWorld.Game.Network.Security
{
    /// <summary>
    /// 消息验证和安全管理系统
    /// 负责消息完整性验证、防篡改、频率限制和安全审计
    /// </summary>
    public class MessageSecurityManager
    {
        private static MessageSecurityManager _instance;
        public static MessageSecurityManager Instance => _instance ??= new MessageSecurityManager();

        private readonly Dictionary<ulong, MessageFrequencyTracker> _frequencyTrackers;
        private readonly HashSet<string> _blacklistedSessions;
        private readonly Dictionary<string, int> _ipConnectionAttempts;
        private readonly List<SecurityRule> _securityRules;
        private readonly StringBuilder _securityLog;

        // 安全配置
        private const int MAX_MESSAGES_PER_SECOND = 50;
        private const int MAX_CONNECTION_ATTEMPTS_PER_IP = 10;
        private const int BLACKLIST_DURATION_MINUTES = 30;
        private const int SECURITY_LOG_MAX_LENGTH = 10000;

        private MessageSecurityManager()
        {
            _frequencyTrackers = new Dictionary<ulong, MessageFrequencyTracker>();
            _blacklistedSessions = new HashSet<string>();
            _ipConnectionAttempts = new Dictionary<string, int>();
            _securityRules = new List<SecurityRule>();
            _securityLog = new StringBuilder();

            InitializeSecurityRules();
        }

        /// <summary>
        /// 初始化安全规则
        /// </summary>
        private void InitializeSecurityRules()
        {
            // 敏感操作频率限制
            _securityRules.Add(new SecurityRule
            {
                RuleType = SecurityRuleType.RateLimit,
                MessageTypes = new HashSet<MessageType> 
                { 
                    MessageType.LoginRequest, 
                    MessageType.RegisterRequest,
                    MessageType.VerificationCodeRequest 
                },
                MaxFrequency = 5, // 每分钟最多5次
                TimeWindowSeconds = 60
            });

            // 大型消息大小限制
            _securityRules.Add(new SecurityRule
            {
                RuleType = SecurityRuleType.SizeLimit,
                MessageTypes = new HashSet<MessageType> { MessageType.ChatMessage },
                MaxSizeBytes = 1024 // 聊天消息限制1KB
            });

            // 敏感数据加密要求
            _securityRules.Add(new SecurityRule
            {
                RuleType = SecurityRuleType.EncryptionRequired,
                MessageTypes = new HashSet<MessageType> 
                { 
                    MessageType.LoginRequest, 
                    MessageType.RegisterRequest 
                }
            });
        }

        /// <summary>
        /// 验证消息安全性
        /// </summary>
        public MessageValidationResult ValidateMessage(HorizonMessagePacket message, string sessionId, string clientIp)
        {
            var result = new MessageValidationResult { IsValid = true };

            try
            {
                // 1. 基础验证
                if (!PerformBasicValidation(message, result))
                    return result;

                // 2. 会话验证
                if (!ValidateSession(sessionId, result))
                    return result;

                // 3. IP频率限制
                if (!ValidateIpFrequency(clientIp, result))
                    return result;

                // 4. 消息频率限制
                if (!ValidateMessageFrequency(sessionId, message.Header.MessageType, result))
                    return result;

                // 5. 应用安全规则
                if (!ApplySecurityRules(message, result))
                    return result;

                // 6. 数据完整性验证
                if (!ValidateDataIntegrity(message, result))
                    return result;

                LogSecurityEvent($"消息验证通过: {message.Header.MessageType} (会话: {sessionId})");
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.RejectionReason = $"安全验证异常: {ex.Message}";
                LogSecurityEvent($"消息验证异常: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 执行基础验证
        /// </summary>
        private bool PerformBasicValidation(HorizonMessagePacket message, MessageValidationResult result)
        {
            if (message?.Header == null)
            {
                result.IsValid = false;
                result.RejectionReason = "消息头为空";
                return false;
            }

            if (message.Header.Timestamp == 0)
            {
                result.IsValid = false;
                result.RejectionReason = "缺少时间戳";
                return false;
            }

            // 检查时间戳是否合理（防止重放攻击）
            var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var timeDifference = Math.Abs(currentTime - message.Header.Timestamp);
            if (timeDifference > 300) // 5分钟时间窗口
            {
                result.IsValid = false;
                result.RejectionReason = "时间戳超出允许范围";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 验证会话
        /// </summary>
        private bool ValidateSession(string sessionId, MessageValidationResult result)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                result.IsValid = false;
                result.RejectionReason = "无效的会话ID";
                return false;
            }

            if (_blacklistedSessions.Contains(sessionId))
            {
                result.IsValid = false;
                result.RejectionReason = "会话已被列入黑名单";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 验证IP频率
        /// </summary>
        private bool ValidateIpFrequency(string clientIp, MessageValidationResult result)
        {
            if (string.IsNullOrEmpty(clientIp))
                return true; // 如果没有IP信息，跳过验证

            if (!_ipConnectionAttempts.ContainsKey(clientIp))
            {
                _ipConnectionAttempts[clientIp] = 1;
            }
            else
            {
                _ipConnectionAttempts[clientIp]++;
            }

            if (_ipConnectionAttempts[clientIp] > MAX_CONNECTION_ATTEMPTS_PER_IP)
            {
                result.IsValid = false;
                result.RejectionReason = "IP连接尝试过于频繁";
                BlacklistSession($"IP_{clientIp}"); // 临时黑名单
                return false;
            }

            return true;
        }

        /// <summary>
        /// 验证消息频率
        /// </summary>
        private bool ValidateMessageFrequency(string sessionId, MessageType messageType, MessageValidationResult result)
        {
            if (!_frequencyTrackers.ContainsKey(GetSessionHash(sessionId)))
            {
                _frequencyTrackers[GetSessionHash(sessionId)] = new MessageFrequencyTracker();
            }

            var tracker = _frequencyTrackers[GetSessionHash(sessionId)];
            var currentTime = Time.GameTime;

            // 清理过期记录
            tracker.Messages.RemoveAll(m => currentTime - m.Timestamp > 1.0f);

            // 检查频率限制
            var recentMessages = tracker.Messages.FindAll(m => m.MessageType == messageType);
            if (recentMessages.Count > MAX_MESSAGES_PER_SECOND)
            {
                result.IsValid = false;
                result.RejectionReason = "消息发送过于频繁";
                return false;
            }

            // 记录当前消息
            tracker.Messages.Add(new MessageRecord
            {
                MessageType = messageType,
                Timestamp = currentTime
            });

            return true;
        }

        /// <summary>
        /// 应用安全规则
        /// </summary>
        private bool ApplySecurityRules(HorizonMessagePacket message, MessageValidationResult result)
        {
            foreach (var rule in _securityRules)
            {
                if (rule.MessageTypes.Contains(message.Header.MessageType))
                {
                    switch (rule.RuleType)
                    {
                        case SecurityRuleType.RateLimit:
                            // 频率限制已在ValidateMessageFrequency中处理
                            break;

                        case SecurityRuleType.SizeLimit:
                            var messageSize = GetMessageSize(message);
                            if (messageSize > rule.MaxSizeBytes)
                            {
                                result.IsValid = false;
                                result.RejectionReason = $"消息大小超出限制 ({messageSize} > {rule.MaxSizeBytes} bytes)";
                                return false;
                            }
                            break;

                        case SecurityRuleType.EncryptionRequired:
                            // 检查消息是否经过加密
                            if (!IsMessageEncrypted(message))
                            {
                                result.IsValid = false;
                                result.RejectionReason = "敏感消息必须加密传输";
                                return false;
                            }
                            break;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 验证数据完整性
        /// </summary>
        private bool ValidateDataIntegrity(HorizonMessagePacket message, MessageValidationResult result)
        {
            // 计算消息哈希
            var computedHash = ComputeMessageHash(message);
            
            // 验证哈希（如果有提供的话）
            if (!string.IsNullOrEmpty(message.Header.Hash) && 
                !computedHash.Equals(message.Header.Hash, StringComparison.OrdinalIgnoreCase))
            {
                result.IsValid = false;
                result.RejectionReason = "消息完整性验证失败";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 计算消息哈希
        /// </summary>
        private string ComputeMessageHash(HorizonMessagePacket message)
        {
            try
            {
                var data = Encoding.UTF8.GetBytes(message.Body.ToString());
                using var sha256 = SHA256.Create();
                var hashBytes = sha256.ComputeHash(data);
                return Convert.ToBase64String(hashBytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 检查消息是否加密
        /// </summary>
        private bool IsMessageEncrypted(HorizonMessagePacket message)
        {
            // 检查是否有加密标识
            return (message.Header.Flags & (uint)MessageFlags.Encrypted) != 0;
        }

        /// <summary>
        /// 获取消息大小
        /// </summary>
        private int GetMessageSize(HorizonMessagePacket message)
        {
            // 简化的大小计算
            return Encoding.UTF8.GetByteCount(message.Body.ToString());
        }

        /// <summary>
        /// 获取会话哈希
        /// </summary>
        private ulong GetSessionHash(string sessionId)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(sessionId));
            return BitConverter.ToUInt64(hashBytes, 0);
        }

        /// <summary>
        /// 将会话加入黑名单
        /// </summary>
        public void BlacklistSession(string sessionId)
        {
            _blacklistedSessions.Add(sessionId);
            LogSecurityEvent($"会话被列入黑名单: {sessionId}");

            // 设置定时移除（简化实现）
            // 在实际应用中应该使用定时器
        }

        /// <summary>
        /// 从黑名单中移除会话
        /// </summary>
        public void UnblacklistSession(string sessionId)
        {
            _blacklistedSessions.Remove(sessionId);
            LogSecurityEvent($"会话从黑名单中移除: {sessionId}");
        }

        /// <summary>
        /// 记录安全事件
        /// </summary>
        private void LogSecurityEvent(string message)
        {
            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n";
            _securityLog.Append(logEntry);

            // 限制日志长度
            if (_securityLog.Length > SECURITY_LOG_MAX_LENGTH)
            {
                _securityLog.Remove(0, _securityLog.Length - SECURITY_LOG_MAX_LENGTH / 2);
            }

            Debug.Log($"[Security] {message}");
        }

        /// <summary>
        /// 获取安全日志
        /// </summary>
        public string GetSecurityLog()
        {
            return _securityLog.ToString();
        }

        /// <summary>
        /// 清理过期的跟踪器
        /// </summary>
        public void CleanupExpiredTrackers()
        {
            var currentTime = Time.GameTime;
            var trackersToRemove = new List<ulong>();

            foreach (var kvp in _frequencyTrackers)
            {
                var tracker = kvp.Value;
                tracker.Messages.RemoveAll(m => currentTime - m.Timestamp > 60.0f); // 保留1分钟内的记录
                
                if (tracker.Messages.Count == 0)
                {
                    trackersToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in trackersToRemove)
            {
                _frequencyTrackers.Remove(key);
            }
        }

        /// <summary>
        /// 重置IP连接尝试计数
        /// </summary>
        public void ResetIpAttempts(string clientIp)
        {
            if (_ipConnectionAttempts.ContainsKey(clientIp))
            {
                _ipConnectionAttempts[clientIp] = 0;
            }
        }
    }

    /// <summary>
    /// 消息验证结果
    /// </summary>
    public class MessageValidationResult
    {
        public bool IsValid { get; set; }
        public string RejectionReason { get; set; }
        public SecurityThreatLevel ThreatLevel { get; set; } = SecurityThreatLevel.Low;
    }

    /// <summary>
    /// 消息频率跟踪器
    /// </summary>
    public class MessageFrequencyTracker
    {
        public List<MessageRecord> Messages { get; set; } = new List<MessageRecord>();
    }

    /// <summary>
    /// 消息记录
    /// </summary>
    public class MessageRecord
    {
        public MessageType MessageType { get; set; }
        public float Timestamp { get; set; }
    }

    /// <summary>
    /// 安全规则
    /// </summary>
    public class SecurityRule
    {
        public SecurityRuleType RuleType { get; set; }
        public HashSet<MessageType> MessageTypes { get; set; }
        public int MaxFrequency { get; set; }
        public int TimeWindowSeconds { get; set; }
        public int MaxSizeBytes { get; set; }
    }

    /// <summary>
    /// 安全规则类型
    /// </summary>
    public enum SecurityRuleType
    {
        RateLimit,          // 频率限制
        SizeLimit,          // 大小限制
        EncryptionRequired  // 必须加密
    }

    /// <summary>
    /// 安全威胁等级
    /// </summary>
    public enum SecurityThreatLevel
    {
        Low,    // 低威胁
        Medium, // 中等威胁
        High,   // 高威胁
        Critical // 严重威胁
    }
}