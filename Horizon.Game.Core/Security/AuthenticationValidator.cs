using Horizon.Core.Security;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Horizon.Game.Core.Security
{
    /// <summary>
    /// 认证验证器
    /// 提供用户认证相关的安全验证功能
    /// </summary>
    public class AuthenticationValidator
    {
        private readonly ILogger<AuthenticationValidator> _logger;

        // 通行证昵称验证正则（编译以提升重复调用性能）
        private static readonly Regex _nickNameAllDigitsRegex = new(@"^\d+$", RegexOptions.Compiled);
        private static readonly Regex _nickNameValidCharsRegex = new(@"^[\u4e00-\u9fa5a-zA-Z0-9_]+$", RegexOptions.Compiled);
        
        // 常用弱密码列表
        private readonly HashSet<string> _weakPasswords = new()
        {
            "123456", "password", "123456789", "12345678", "12345", "1234567", "1234567890",
            "qwerty", "abc123", "111111", "dragon", "1234", "monkey", "letmein", "trustno1",
            "sunshine", "iloveyou", "princess", "football", "123123", "welcome", "solo"
        };

        private readonly SensitiveWordFilter _sensitiveWordFilter = new();

        public AuthenticationValidator(ILogger<AuthenticationValidator> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 验证账户名
        /// </summary>
        public ValidationResult ValidateAccountName(string accountName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(accountName))
                {
                    return ValidationResult.Failure("账户名不能为空");
                }

                // 长度检查
                if (accountName.Length < 3 || accountName.Length > 20)
                {
                    return ValidationResult.Failure("账户名长度必须在3-20个字符之间");
                }

                // 字符检查（只允许字母、数字、下划线）
                if (!Regex.IsMatch(accountName, @"^[a-zA-Z0-9_]+$"))
                {
                    return ValidationResult.Failure("账户名只能包含字母、数字和下划线");
                }

                // 不能以数字或下划线开头
                if (char.IsDigit(accountName[0]) || accountName[0] == '_')
                {
                    return ValidationResult.Failure("账户名不能以数字或下划线开头");
                }

                // 敏感词检查
                if (ContainsSensitiveWords(accountName))
                {
                    return ValidationResult.Failure("账户名包含不允许的词汇");
                }

                return ValidationResult.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证账户名时发生异常: {AccountName}", accountName);
                return ValidationResult.Failure("账户名验证失败");
            }
        }

        /// <summary>
        /// 验证密码强度
        /// </summary>
        public ValidationResult ValidatePassword(string password)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(password))
                {
                    return ValidationResult.Failure("密码不能为空");
                }

                // 长度检查
                if (password.Length < 6 || password.Length > 20)
                {
                    return ValidationResult.Failure("密码长度必须在6-20个字符之间");
                }

                // 弱密码检查
                if (_weakPasswords.Contains(password.ToLower()))
                {
                    return ValidationResult.Failure("密码过于简单，请使用更强的密码");
                }

                // 密码复杂度检查
                int complexity = 0;
                if (Regex.IsMatch(password, @"[a-z]")) complexity++; // 小写字母
                if (Regex.IsMatch(password, @"[A-Z]")) complexity++; // 大写字母
                if (Regex.IsMatch(password, @"[0-9]")) complexity++; // 数字
                if (Regex.IsMatch(password, @"[^a-zA-Z0-9]")) complexity++; // 特殊字符

                if (complexity < 2)
                {
                    return ValidationResult.Failure("密码必须包含至少两种类型的字符（大写字母、小写字母、数字、特殊字符）");
                }

                return ValidationResult.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证密码时发生异常");
                return ValidationResult.Failure("密码验证失败");
            }
        }

        /// <summary>
        /// 验证角色名
        /// </summary>
        public ValidationResult ValidateCharacterName(string characterName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(characterName))
                {
                    return ValidationResult.Failure("角色名不能为空");
                }

                // 移除前后空格
                characterName = characterName.Trim();

                // 长度检查
                if (characterName.Length < 2 || characterName.Length > 12)
                {
                    return ValidationResult.Failure("角色名长度必须在2-12个字符之间");
                }

                // 不允许纯数字
                if (Regex.IsMatch(characterName, @"^\d+$"))
                {
                    return ValidationResult.Failure("角色名不能为纯数字");
                }

                // 不允许特殊字符（只允许中文、英文、数字）
                if (!Regex.IsMatch(characterName, @"^[\u4e00-\u9fa5a-zA-Z0-9]+$"))
                {
                    return ValidationResult.Failure("角色名只能包含中文、英文和数字");
                }

                // 敏感词检查
                if (ContainsSensitiveWords(characterName))
                {
                    return ValidationResult.Failure("角色名包含不允许的词汇");
                }

                return ValidationResult.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证角色名时发生异常: {CharacterName}", characterName);
                return ValidationResult.Failure("角色名验证失败");
            }
        }

        /// <summary>
        /// 验证邮箱格式
        /// </summary>
        public ValidationResult ValidateEmail(string email)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(email))
                {
                    return ValidationResult.Failure("邮箱不能为空");
                }

                // 邮箱格式验证
                const string emailPattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";
                if (!Regex.IsMatch(email, emailPattern))
                {
                    return ValidationResult.Failure("邮箱格式不正确");
                }

                // 长度检查
                if (email.Length > 100)
                {
                    return ValidationResult.Failure("邮箱地址过长");
                }

                return ValidationResult.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证邮箱时发生异常: {Email}", email);
                return ValidationResult.Failure("邮箱验证失败");
            }
        }

        /// <summary>
        /// 验证手机号格式（中国大陆）
        /// </summary>
        public ValidationResult ValidatePhoneNumber(string phoneNumber)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(phoneNumber))
                {
                    return ValidationResult.Failure("手机号不能为空");
                }

                // 中国大陆手机号验证
                const string phonePattern = @"^1[3-9]\d{9}$";
                if (!Regex.IsMatch(phoneNumber, phonePattern))
                {
                    return ValidationResult.Failure("手机号格式不正确");
                }

                return ValidationResult.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证手机号时发生异常: {PhoneNumber}", phoneNumber);
                return ValidationResult.Failure("手机号验证失败");
            }
        }

        /// <summary>
        /// 验证客户端版本
        /// </summary>
        public ValidationResult ValidateClientVersion(string clientVersion, string requiredVersion = "1.0.0")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(clientVersion))
                {
                    return ValidationResult.Failure("客户端版本信息缺失");
                }

                // 简单的版本比较（这里可以实现更复杂的版本比较逻辑）
                if (!Version.TryParse(clientVersion, out var clientVer))
                {
                    return ValidationResult.Failure("客户端版本格式错误");
                }

                if (!Version.TryParse(requiredVersion, out var requiredVer))
                {
                    _logger.LogWarning("服务端配置的要求版本格式错误: {RequiredVersion}", requiredVersion);
                    return ValidationResult.Success(); // 配置错误时允许通过
                }

                if (clientVer < requiredVer)
                {
                    return ValidationResult.Failure($"客户端版本过低，需要版本 {requiredVersion} 或更高");
                }

                return ValidationResult.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证客户端版本时发生异常: {ClientVersion}", clientVersion);
                return ValidationResult.Failure("客户端版本验证失败");
            }
        }

        /// <summary>
        /// 检查是否包含敏感词
        /// </summary>
        private bool ContainsSensitiveWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return _sensitiveWordFilter.ContainsSensitiveWord(text);
        }

        /// <summary>
        /// 验证通行证昵称
        /// 昵称允许中文、英文字母、数字，长度2-16个字符
        /// </summary>
        public ValidationResult ValidatePassportNickName(string nickName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(nickName))
                {
                    return ValidationResult.Failure("通行证昵称不能为空");
                }

                // 移除前后空格后再判断长度
                nickName = nickName.Trim();

                // 长度检查（按字符数，中文占一个字符）
                if (nickName.Length < 2 || nickName.Length > 16)
                {
                    return ValidationResult.Failure("通行证昵称长度必须在2-16个字符之间");
                }

                // 不允许纯数字
                if (_nickNameAllDigitsRegex.IsMatch(nickName))
                {
                    return ValidationResult.Failure("通行证昵称不能为纯数字");
                }

                // 只允许中文、英文字母、数字和下划线
                if (!_nickNameValidCharsRegex.IsMatch(nickName))
                {
                    return ValidationResult.Failure("通行证昵称只能包含中文、英文字母、数字和下划线");
                }

                // 敏感词检查
                if (ContainsSensitiveWords(nickName))
                {
                    return ValidationResult.Failure("通行证昵称包含不允许的词汇");
                }

                return ValidationResult.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证通行证昵称时发生异常: {NickName}", nickName);
                return ValidationResult.Failure("通行证昵称验证失败");
            }
        }

        /// <summary>
        /// 批量验证用户注册信息
        /// </summary>
        /// <param name="nickName">通行证昵称（必填）</param>
        /// <param name="password">密码（必填）</param>
        /// <param name="email">安全邮箱（可选，用于找回密码、实名认证、注销通行证等）</param>
        /// <param name="phoneNumber">手机号（可选）</param>
        public Task<ValidationResult> ValidateUserRegistrationAsync(
            string nickName, 
            string password, 
            string email, 
            string phoneNumber = null)
        {
            try
            {
                // 必填项：通行证昵称
                var nickNameValidation = ValidatePassportNickName(nickName);
                if (!nickNameValidation.IsValid)
                {
                    return Task.FromResult(nickNameValidation);
                }

                // 必填项：密码
                var passwordValidation = ValidatePassword(password);
                if (!passwordValidation.IsValid)
                {
                    return Task.FromResult(passwordValidation);
                }

                // 可选项：安全邮箱（提供时才验证格式）
                if (!string.IsNullOrWhiteSpace(email))
                {
                    var emailValidation = ValidateEmail(email);
                    if (!emailValidation.IsValid)
                    {
                        return Task.FromResult(emailValidation);
                    }
                }

                // 可选项：手机号（提供时才验证格式）
                if (!string.IsNullOrWhiteSpace(phoneNumber))
                {
                    var phoneValidation = ValidatePhoneNumber(phoneNumber);
                    if (!phoneValidation.IsValid)
                    {
                        return Task.FromResult(phoneValidation);
                    }
                }

                return Task.FromResult(ValidationResult.Success());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量验证用户注册信息时发生异常");
                return Task.FromResult(ValidationResult.Failure("用户信息验证失败"));
            }
        }
    }

    /// <summary>
    /// 验证结果
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; private set; }
        public string ErrorMessage { get; private set; }
        public string ErrorCode { get; private set; }

        private ValidationResult(bool isValid, string errorMessage = null, string errorCode = null)
        {
            IsValid = isValid;
            ErrorMessage = errorMessage;
            ErrorCode = errorCode;
        }

        public static ValidationResult Success()
        {
            return new ValidationResult(true);
        }

        public static ValidationResult Failure(string errorMessage, string errorCode = null)
        {
            return new ValidationResult(false, errorMessage, errorCode);
        }
    }
}
