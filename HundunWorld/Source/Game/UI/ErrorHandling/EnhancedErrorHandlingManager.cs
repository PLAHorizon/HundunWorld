using FlaxEngine;
using Horizon.Game.Message.Enums;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HundunWorld.Game.UI.ErrorHandling
{
    /// <summary>
    /// 增强的错误处理管理器
    /// 提供错误处理、回退机制和恢复策略
    /// </summary>
    public class EnhancedErrorHandlingManager
    {
        private static EnhancedErrorHandlingManager _instance;
        public static EnhancedErrorHandlingManager Instance => _instance ??= new EnhancedErrorHandlingManager();

        // 状态历史栈，用于回退
        private readonly Stack<SceneStateSnapshot> _stateHistory = new Stack<SceneStateSnapshot>();
        private readonly Dictionary<SceneType, Func<Task<bool>>> _recoveryStrategies = new Dictionary<SceneType, Func<Task<bool>>>();
        
        // 事件
        public event Action<ErrorInfo> ErrorOccurred;
        public event Action<string> RecoveryAttempted;
        public event Action<SceneStateSnapshot> StateRestored;

        private EnhancedErrorHandlingManager()
        {
            InitializeRecoveryStrategies();
        }

        /// <summary>
        /// 保存当前状态快照
        /// </summary>
        /// <param name="currentScene">当前场景</param>
        /// <param name="userSession">用户会话</param>
        /// <param name="additionalData">额外数据</param>
        public void SaveStateSnapshot(SceneType currentScene, UserSession userSession, object additionalData = null)
        {
            var snapshot = new SceneStateSnapshot
            {
                Scene = currentScene,
                Timestamp = DateTime.UtcNow,
                UserSession = userSession != null ? new UserSession
                {
                    Username = userSession.Username,
                    UserId = userSession.UserId,
                    AccessToken = userSession.AccessToken,
                    RefreshToken = userSession.RefreshToken
                } : new UserSession(),
                AdditionalData = additionalData
            };

            _stateHistory.Push(snapshot);
            
            // 限制历史记录数量
            while (_stateHistory.Count > 10)
            {
                var temp = new Stack<SceneStateSnapshot>();
                for (int i = 0; i < 10; i++)
                {
                    if (_stateHistory.Count > 0)
                        temp.Push(_stateHistory.Pop());
                }
                _stateHistory.Clear();
                while (temp.Count > 0)
                {
                    _stateHistory.Push(temp.Pop());
                }
            }

            FlaxEngine.Debug.Log($"状态快照已保存: {currentScene} at {snapshot.Timestamp}");
        }

        /// <summary>
        /// 处理错误并尝试恢复
        /// </summary>
        /// <param name="error">错误信息</param>
        /// <param name="currentScene">当前场景</param>
        /// <returns>是否成功恢复</returns>
        public async Task<bool> HandleErrorAndRecover(ErrorInfo error, SceneType currentScene)
        {
            FlaxEngine.Debug.LogError($"处理错误: {error.Message} (类型: {error.Type}, 严重性: {error.Severity})");
            
            // 触发错误事件
            ErrorOccurred?.Invoke(error);

            // 根据错误严重性决定恢复策略
            switch (error.Severity)
            {
                case ErrorSeverity.Critical:
                    return await HandleCriticalError(error, currentScene);
                    
                case ErrorSeverity.Error:
                    return await HandleError(error, currentScene);
                    
                case ErrorSeverity.Warning:
                    return await HandleWarning(error, currentScene);
                    
                default:
                    return true; // 忽略信息级别的错误
            }
        }

        /// <summary>
        /// 处理严重错误
        /// </summary>
        private async Task<bool> HandleCriticalError(ErrorInfo error, SceneType currentScene)
        {
            FlaxEngine.Debug.LogError($"严重错误，尝试回退到安全状态: {error.Message}");
            
            // 尝试回退到上一个稳定状态
            if (await TryRestorePreviousState())
            {
                return true;
            }
            
            // 如果回退失败，强制回到登录界面
            var uiStateManager = UIStateManager.Instance;
            if (uiStateManager != null)
            {
                uiStateManager.ForceTransitionToScene(SceneType.Login, "严重错误恢复");
                uiStateManager.ClearUserSession();
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// 处理一般错误
        /// </summary>
        public async Task<bool> HandleError(ErrorInfo error, SceneType currentScene)
        {
            FlaxEngine.Debug.LogWarning($"一般错误，尝试场景级恢复: {error.Message}");
            
            // 尝试当前场景的恢复策略
            if (_recoveryStrategies.ContainsKey(currentScene))
            {
                try
                {
                    RecoveryAttempted?.Invoke($"尝试恢复场景: {currentScene}");
                    bool success = await _recoveryStrategies[currentScene]();
                    if (success)
                    {
                        FlaxEngine.Debug.Log($"场景恢复成功: {currentScene}");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"恢复策略执行失败: {ex.Message}");
                }
            }
            
            // 如果场景恢复失败，尝试回退
            return await TryRestorePreviousState();
        }

        /// <summary>
        /// 处理警告
        /// </summary>
        private async Task<bool> HandleWarning(ErrorInfo error, SceneType currentScene)
        {
            FlaxEngine.Debug.LogWarning($"警告级错误，记录但继续: {error.Message}");
            
            // 警告级别错误通常不需要恢复，只需记录
            return true;
        }

        /// <summary>
        /// 尝试恢复到上一个状态
        /// </summary>
        private async Task<bool> TryRestorePreviousState()
        {
            if (_stateHistory.Count == 0)
            {
                FlaxEngine.Debug.LogWarning("没有可用的历史状态进行回退");
                return false;
            }

            var previousState = _stateHistory.Pop();
            var uiStateManager = UIStateManager.Instance;
            
            if (uiStateManager != null)
            {
                try
                {
                    // 恢复用户会话
                    if (previousState.UserSession.IsAuthenticated)
                    {
                        uiStateManager.UpdateUserSession(
                            previousState.UserSession.Username,
                            previousState.UserSession.UserId,
                            previousState.UserSession.AccessToken,
                            previousState.UserSession.RefreshToken
                        );
                    }
                    else
                    {
                        uiStateManager.ClearUserSession();
                    }

                    // 强制转换到之前的场景
                    uiStateManager.ForceTransitionToScene(previousState.Scene, "错误恢复");
                    
                    FlaxEngine.Debug.Log($"成功回退到状态: {previousState.Scene} ({previousState.Timestamp})");
                    StateRestored?.Invoke(previousState);
                    
                    return true;
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"状态恢复失败: {ex.Message}");
                }
            }
            
            return false;
        }

        /// <summary>
        /// 初始化各场景的恢复策略
        /// </summary>
        private void InitializeRecoveryStrategies()
        {
            // 登录场景恢复策略
            _recoveryStrategies[SceneType.Login] = async () =>
            {
                // 清理登录状态，重置表单
                FlaxEngine.Debug.Log("执行登录场景恢复策略");
                // 这里可以添加具体的登录界面重置逻辑
                return true;
            };

            // 角色选择场景恢复策略
            _recoveryStrategies[SceneType.CharacterSelection] = async () =>
            {
                // 重新加载角色列表
                FlaxEngine.Debug.Log("执行角色选择场景恢复策略");
                // 这里可以添加重新获取角色列表的逻辑
                return true;
            };

            // 游戏世界场景恢复策略
            _recoveryStrategies[SceneType.GameWorld] = async () =>
            {
                // 尝试重新连接游戏服务器
                FlaxEngine.Debug.Log("执行游戏世界场景恢复策略");
                // 这里可以添加重连游戏服务器的逻辑
                return true;
            };
        }

        /// <summary>
        /// 清理状态历史
        /// </summary>
        public void ClearStateHistory()
        {
            _stateHistory.Clear();
            FlaxEngine.Debug.Log("状态历史已清理");
        }

        /// <summary>
        /// 获取状态历史数量
        /// </summary>
        public int GetStateHistoryCount()
        {
            return _stateHistory.Count;
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
                Source = source,
                Timestamp = DateTime.UtcNow
            };
            
            _ = HandleErrorAndRecover(errorInfo, SceneType.Login);
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
                Code = code,
                Source = "Authentication",
                Timestamp = DateTime.UtcNow
            };
            
            _ = HandleErrorAndRecover(errorInfo, SceneType.Login);
        }
    }

    

    
}