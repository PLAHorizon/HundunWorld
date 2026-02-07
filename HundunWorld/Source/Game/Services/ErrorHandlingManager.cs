using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlaxEngine;

namespace HundunWorld.Game.Services
{
    /// <summary>
    /// 服务层错误处理管理器
    /// 负责统一处理游戏中的各种错误
    /// </summary>
    public class ServiceErrorHandlingManager
    {
        private static ServiceErrorHandlingManager _instance;
        public static ServiceErrorHandlingManager Instance => _instance ??= new ServiceErrorHandlingManager();

        private Queue<ErrorMessage> _errorQueue = new Queue<ErrorMessage>();
        private bool _isShowingError = false;

        // 事件
        public event Action<ErrorMessage> ErrorOccurred;
        public event Action<string> GlobalError;

        private ServiceErrorHandlingManager()
        {
        }

        /// <summary>
        /// 处理错误
        /// </summary>
        public void HandleError(string message, UIErrorType type = UIErrorType.General, Exception exception = null)
        {
            var error = new ErrorMessage
            {
                Message = message,
                Type = type,
                Exception = exception,
                Timestamp = DateTime.Now
            };

            _errorQueue.Enqueue(error);
            ErrorOccurred?.Invoke(error);

            Debug.LogError($"错误 [{type}]: {message}");
            
            if (exception != null)
            {
                Debug.LogException(exception);
            }

            // 如果没有正在显示的错误，则立即处理
            if (!_isShowingError)
            {
                ProcessNextError();
            }
        }

        /// <summary>
        /// 处理全局错误
        /// </summary>
        public void HandleGlobalError(string message)
        {
            GlobalError?.Invoke(message);
            Debug.LogError($"全局错误: {message}");
        }

        /// <summary>
        /// 处理下一个错误
        /// </summary>
        private async void ProcessNextError()
        {
            if (_errorQueue.Count == 0)
            {
                _isShowingError = false;
                return;
            }

            _isShowingError = true;
            var error = _errorQueue.Dequeue();

            try
            {
                // 显示错误UI
                await ShowErrorUI(error);
            }
            catch (Exception ex)
            {
                Debug.LogError($"显示错误UI时发生异常: {ex.Message}");
            }

            // 处理完当前错误后，延迟一段时间再处理下一个
            await Task.Delay(1000);
            ProcessNextError();
        }

        /// <summary>
        /// 显示错误UI
        /// </summary>
        private async Task ShowErrorUI(ErrorMessage error)
        {
            // 这里应该调用实际的UI系统来显示错误
            // 暂时使用Debug输出模拟
            Debug.Log($"显示错误对话框: {error.Message} (类型: {error.Type})");
            
            // 模拟UI显示时间
            await Task.Delay(2000);
        }

        /// <summary>
        /// 清除所有待处理的错误
        /// </summary>
        public void ClearAllErrors()
        {
            _errorQueue.Clear();
            _isShowingError = false;
            Debug.Log("已清除所有待处理的错误");
        }

        /// <summary>
        /// 获取待处理错误数量
        /// </summary>
        public int GetPendingErrorCount()
        {
            return _errorQueue.Count;
        }
    }

    /// <summary>
    /// 错误信息类
    /// </summary>
    public class ErrorMessage
    {
        public string Message { get; set; }
        public UIErrorType Type { get; set; }
        public Exception Exception { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// UI错误类型枚举
    /// </summary>
    public enum UIErrorType
    {
        General,        // 一般错误
        Network,        // 网络错误
        Authentication, // 认证错误
        Character,      // 角色相关错误
        Combat,         // 战斗相关错误
        UI,            // UI相关错误
        Resource,       // 资源相关错误
        System          // 系统错误
    }
}