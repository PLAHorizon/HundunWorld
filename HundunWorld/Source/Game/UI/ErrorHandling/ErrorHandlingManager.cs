using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using Game.Game.Network;
using Horizon.Game.Message.Enums;
using HundunWorld.Game.Network;
using HundunWorld.Game.UI.Components;

namespace HundunWorld.Game.UI.ErrorHandling
{
    /// <summary>
    /// 错误处理管理器
    /// 负责统一的错误处理、用户反馈和恢复策略
    /// </summary>
    public class ErrorHandlingManager : Script
    {
        private static ErrorHandlingManager _instance;
        private ToastManager _toastManager = new ToastManager();
        private List<ErrorInfo> _errorHistory = new List<ErrorInfo>();
        private const int MAX_ERROR_HISTORY = 100;

        // 错误重试配置
        private Dictionary<ErrorType, int> _maxRetryAttempts = new Dictionary<ErrorType, int>
        {
            { ErrorType.Network, 3 },
            { ErrorType.Server, 2 },
            { ErrorType.Authentication, 1 },
            { ErrorType.Validation, 0 },
            { ErrorType.Unknown, 1 }
        };

        // 事件
        public event Action<ErrorInfo> ErrorOccurred;
        public event Action<ErrorInfo> CriticalErrorOccurred;

        public static ErrorHandlingManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var gameObject = Level.FindActor("ErrorHandlingManager") ?? new EmptyActor();
                    gameObject.Name = "ErrorHandlingManager";
                    _instance =  gameObject.AddScript<ErrorHandlingManager>();
                    
                }
                return _instance;
            }
        }

        public override void OnAwake()
        {
            if (_instance == null)
            {
                _instance = this;
                Actor.SetStaticFlag(StaticFlags.FullyStatic, true);
            }
            else if (_instance != this)
            {
                Destroy(Actor);
                return;
            }
        }

        public override void OnStart()
        {
            
            FlaxEngine.Debug.Log("错误处理管理器初始化完成");
        }

        /// <summary>
        /// 处理错误
        /// </summary>
        public void HandleError(ErrorInfo errorInfo)
        {
            try
            {
                // 记录错误
                LogError(errorInfo);

                // 触发事件
                ErrorOccurred?.Invoke(errorInfo);

                if (errorInfo.Severity == ErrorSeverity.Critical)
                {
                    CriticalErrorOccurred?.Invoke(errorInfo);
                }

                // 显示用户反馈
                ShowUserFeedback(errorInfo);

                // 执行恢复策略
                ExecuteRecoveryStrategy(errorInfo);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"错误处理器自身发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 快速处理错误的便捷方法
        /// </summary>
        public void HandleError(string message, ErrorType type = ErrorType.Unknown, ErrorSeverity severity = ErrorSeverity.Error, string source = "")
        {
            var errorInfo = new ErrorInfo(type, severity, message, source: source);
            HandleError(errorInfo);
        }

        /// <summary>
        /// 记录错误到历史记录
        /// </summary>
        private void LogError(ErrorInfo errorInfo)
        {
            _errorHistory.Add(errorInfo);

            // 限制历史记录数量
            if (_errorHistory.Count > MAX_ERROR_HISTORY)
            {
                _errorHistory.RemoveAt(0);
            }

            // 输出到控制台
            if (errorInfo.Severity == ErrorSeverity.Critical || errorInfo.Severity == ErrorSeverity.Error)
                EnhancedLogging.LogError($"[{errorInfo.Type}] {errorInfo.Message} (来源: {errorInfo.Source})");
            else
                EnhancedLogging.LogWarning($"[{errorInfo.Type}] {errorInfo.Message} (来源: {errorInfo.Source})");

            if (!string.IsNullOrEmpty(errorInfo.Details))
            {
                FlaxEngine.Debug.Log($"错误详情: {errorInfo.Details}");
            }
        }

        /// <summary>
        /// 显示用户反馈
        /// </summary>
        private void ShowUserFeedback(ErrorInfo errorInfo)
        {
            ToastType toastType;
            string displayMessage = GetUserFriendlyMessage(errorInfo);

            switch (errorInfo.Severity)
            {
                case ErrorSeverity.Info:
                    toastType = ToastType.Info;
                    break;
                case ErrorSeverity.Warning:
                    toastType = ToastType.Warning;
                    break;
                case ErrorSeverity.Error:
                    toastType = ToastType.Error;
                    break;
                case ErrorSeverity.Critical:
                    toastType = ToastType.Error;
                    // 严重错误显示更长时间
                    _toastManager.ShowToast(displayMessage, toastType,10f);
                    return;
                default:
                    toastType = ToastType.Error;
                    break;
            }

            _toastManager.ShowToast(displayMessage, toastType);
        }

        /// <summary>
        /// 获取用户友好的错误消息
        /// </summary>
        private string GetUserFriendlyMessage(ErrorInfo errorInfo)
        {
            switch (errorInfo.Type)
            {
                case ErrorType.Network:
                    return "网络连接出现问题，请检查网络设置";
                case ErrorType.Authentication:
                    return "用户名或密码错误，请重新输入";
                case ErrorType.Validation:
                    return errorInfo.Message; // 验证错误通常已经是用户友好的
                case ErrorType.System:
                    return "系统错误，请稍后重试";
                default:
                    return errorInfo.Message;
            }
        }

        /// <summary>
        /// 执行恢复策略
        /// </summary>
        private void ExecuteRecoveryStrategy(ErrorInfo errorInfo)
        {
            switch (errorInfo.Type)
            {
                case ErrorType.Network:
                    HandleNetworkError(errorInfo);
                    break;
                case ErrorType.Authentication:
                    HandleAuthenticationError(errorInfo);
                    break;
                case ErrorType.System:
                    HandleServerError(errorInfo);
                    break;
                case ErrorType.Validation:
                    // 验证错误通常不需要自动恢复
                    break;
            }
        }

        private void HandleNetworkError(ErrorInfo errorInfo)
        {
            // 网络错误恢复策略
            FlaxEngine.Debug.Log("执行网络错误恢复策略");

            // 可以在这里实现：
            // 1. 自动重连
            // 2. 切换到离线模式
            // 3. 提示用户检查网络
        }

        private void HandleAuthenticationError(ErrorInfo errorInfo)
        {
            // 认证错误恢复策略
            FlaxEngine.Debug.Log("执行认证错误恢复策略");

            // 可以在这里实现：
            // 1. 清除本地认证信息
            // 2. 返回登录界面
            // 3. 提示重新登录
        }

        private void HandleServerError(ErrorInfo errorInfo)
        {
            // 服务器错误恢复策略
            FlaxEngine.Debug.Log("执行服务器错误恢复策略");

            // 可以在这里实现：
            // 1. 自动重试请求
            // 2. 切换到备用服务器
            // 3. 启用降级模式
        }

        #region 公共接口

        /// <summary>
        /// 处理网络错误
        /// </summary>
        public void HandleNetworkError(string message, string details = "")
        {
            var errorInfo = new ErrorInfo
            {
                Type = ErrorType.Network,
                Severity = ErrorSeverity.Error,
                Message = message,
                Source = "Network"
            };
            HandleError(errorInfo);
        }

        /// <summary>
        /// 处理认证错误
        /// </summary>
        public void HandleAuthenticationError(string message, string code = "")
        {
            var errorInfo = new ErrorInfo
            {
                Type = ErrorType.Authentication,
                Severity = ErrorSeverity.Error,
                Message = message,
                Source = "Authentication"
            };
            HandleError(errorInfo);
        }

        /// <summary>
        /// 处理验证错误
        /// </summary>
        public void HandleValidationError(string message, string source = "")
        {
            var errorInfo = new ErrorInfo
            {
                Type = ErrorType.Validation,
                Severity = ErrorSeverity.Warning,
                Message = message,
                Source = source
            };
            HandleError(errorInfo);
        }

        /// <summary>
        /// 处理系统错误
        /// </summary>
        public void HandleServerError(string message, string code = "", string details = "")
        {
            var errorInfo = new ErrorInfo
            {
                Type = ErrorType.System,
                Severity = ErrorSeverity.Error,
                Message = message,
                Source = "Server"
            };
            HandleError(errorInfo);
        }

        /// <summary>
        /// 处理严重错误
        /// </summary>
        public void HandleCriticalError(string message, string details = "")
        {
            var errorInfo = new ErrorInfo
            {
                Type = ErrorType.Unknown,
                Severity = ErrorSeverity.Critical,
                Message = message,
                Source = "System"
            };
            HandleError(errorInfo);
        }

        /// <summary>
        /// 获取错误历史记录
        /// </summary>
        public List<ErrorInfo> GetErrorHistory()
        {
            return new List<ErrorInfo>(_errorHistory);
        }

        /// <summary>
        /// 清除错误历史记录
        /// </summary>
        public void ClearErrorHistory()
        {
            _errorHistory.Clear();
        }

        /// <summary>
        /// 获取最近的错误
        /// </summary>
        public ErrorInfo GetLastError()
        {
            return _errorHistory.Count > 0 ? _errorHistory[_errorHistory.Count - 1] : null;
        }

        #endregion
    }
}