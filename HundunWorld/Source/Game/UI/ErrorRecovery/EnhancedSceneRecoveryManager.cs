using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.UI.StateValidation;

namespace HundunWorld.Game.UI.ErrorRecovery
{
    /// <summary>
    /// 强化错误恢复管理器 - 专门处理场景切换失败后的恢复
    /// 解决场景切换后无法再次切换的Bug
    /// </summary>
    public class EnhancedSceneRecoveryManager
    {
        private static EnhancedSceneRecoveryManager _instance;
        public static EnhancedSceneRecoveryManager Instance => _instance ??= new EnhancedSceneRecoveryManager();

        // 恢复策略
        private readonly Dictionary<RecoveryScenario, IRecoveryStrategy> _recoveryStrategies;
        
        // 状态快照
        private readonly Stack<SceneStateSnapshot> _stateSnapshots = new Stack<SceneStateSnapshot>();
        private const int MAX_SNAPSHOTS = 5;

        // 恢复统计
        private int _totalRecoveryAttempts = 0;
        private int _successfulRecoveries = 0;

        public EnhancedSceneRecoveryManager()
        {
            _recoveryStrategies = new Dictionary<RecoveryScenario, IRecoveryStrategy>
            {
                [RecoveryScenario.StateCorruption] = new StateCorruptionRecovery(),
                [RecoveryScenario.EventHandlerFailure] = new EventHandlerRecovery(),
                [RecoveryScenario.CircularTransition] = new CircularTransitionRecovery(),
                [RecoveryScenario.PermissionDenied] = new PermissionRecovery(),
                [RecoveryScenario.UnexpectedException] = new ExceptionRecovery()
            };
        }

       

        /// <summary>
        /// 创建状态快照
        /// </summary>
        /// <param name="stateManager">状态管理器</param>
        public void CreateStateSnapshot(UIStateManager stateManager)
        {
            if (stateManager == null) return;

            try
            {
                var snapshot = new SceneStateSnapshot
                {
                    CurrentScene = stateManager.CurrentScene,
                    UserSession = new UserSession
                    {
                        Username = stateManager.UserSession.Username,
                        UserId = stateManager.UserSession.UserId,
                        AccessToken = stateManager.UserSession.AccessToken,
                        RefreshToken = stateManager.UserSession.RefreshToken
                    },
                    SelectedCharacter = stateManager.SelectedCharacter,
                    CharacterList = new List<CharacterInfo>(stateManager.CharacterList),
                    IsLoading = stateManager.IsLoading,
                    ErrorMessage = stateManager.ErrorMessage,
                    Timestamp = DateTime.UtcNow
                };

                _stateSnapshots.Push(snapshot);

                // 限制快照数量
                while (_stateSnapshots.Count > MAX_SNAPSHOTS)
                {
                    var temp = new Stack<SceneStateSnapshot>();
                    for (int i = 0; i < MAX_SNAPSHOTS; i++)
                    {
                        if (_stateSnapshots.Count > 0)
                            temp.Push(_stateSnapshots.Pop());
                    }
                    _stateSnapshots.Clear();
                    while (temp.Count > 0)
                    {
                        _stateSnapshots.Push(temp.Pop());
                    }
                }

                FlaxEngine.Debug.Log($"状态快照已创建: {snapshot.CurrentScene} at {snapshot.Timestamp}");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"创建状态快照失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理场景切换错误并尝试恢复
        /// </summary>
        /// <param name="scenario">错误场景</param>
        /// <param name="fromScene">源场景</param>
        /// <param name="toScene">目标场景</param>
        /// <param name="errorMessage">错误信息</param>
        /// <param name="stateManager">状态管理器</param>
        /// <returns>恢复是否成功</returns>
        public async Task<RecoveryResult> HandleTransitionErrorAsync(
            RecoveryScenario scenario,
            SceneType fromScene,
            SceneType toScene,
            string errorMessage,
            UIStateManager stateManager)
        {
            _totalRecoveryAttempts++;
            
            FlaxEngine.Debug.LogWarning($"开始错误恢复: {scenario}, {fromScene} -> {toScene}, 错误: {errorMessage}");

            try
            {
                // 记录错误到验证器
                SceneTransitionValidator.RecordTransitionError(fromScene, toScene, errorMessage);

                // 选择恢复策略
                if (!_recoveryStrategies.TryGetValue(scenario, out var strategy))
                {
                    strategy = _recoveryStrategies[RecoveryScenario.UnexpectedException];
                }

                // 执行恢复
                var context = new RecoveryContext
                {
                    FromScene = fromScene,
                    ToScene = toScene,
                    ErrorMessage = errorMessage,
                    StateManager = stateManager,
                    AvailableSnapshots = new List<SceneStateSnapshot>(_stateSnapshots)
                };

                var result = await strategy.RecoverAsync(context);

                if (result.IsSuccess)
                {
                    _successfulRecoveries++;
                    FlaxEngine.Debug.Log($"错误恢复成功: {result.RecoveryAction}");
                }
                else
                {
                    FlaxEngine.Debug.LogError($"错误恢复失败: {result.ErrorMessage}");
                }

                return result;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"错误恢复过程中发生异常: {ex.Message}");
                return new RecoveryResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"恢复过程异常: {ex.Message}",
                    RecoveryAction = "恢复失败"
                };
            }
        }

        /// <summary>
        /// 强制恢复到安全状态
        /// </summary>
        /// <param name="stateManager">状态管理器</param>
        /// <returns>恢复是否成功</returns>
        public bool ForceRecoveryToSafeState(UIStateManager stateManager)
        {
            try
            {
                FlaxEngine.Debug.LogWarning("执行强制恢复到安全状态");

                // 清除错误状态
                stateManager.ClearError();
                stateManager.SetLoadingState(false);

                // 判断安全的目标状态
                SceneType safeScene;
                if (stateManager.UserSession?.IsAuthenticated == true)
                {
                    safeScene = SceneType.CharacterSelection; // 已登录用户回到角色选择
                }
                else
                {
                    safeScene = SceneType.Login; // 未登录用户回到登录界面
                }

                // 强制切换到安全状态
                 stateManager.ForceTransitionToScene(safeScene, "强制错误恢复");

                return true;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"强制恢复失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取恢复统计信息
        /// </summary>
        public RecoveryStatistics GetStatistics()
        {
            return new RecoveryStatistics
            {
                TotalAttempts = _totalRecoveryAttempts,
                SuccessfulRecoveries = _successfulRecoveries,
                SuccessRate = _totalRecoveryAttempts > 0 ? (double)_successfulRecoveries / _totalRecoveryAttempts : 0,
                AvailableSnapshots = _stateSnapshots.Count
            };
        }

        /// <summary>
        /// 清理过期快照
        /// </summary>
        public void CleanupExpiredSnapshots()
        {
            var expiredTime = DateTime.UtcNow.AddMinutes(-30); // 30分钟过期
            var tempStack = new Stack<SceneStateSnapshot>();

            while (_stateSnapshots.Count > 0)
            {
                var snapshot = _stateSnapshots.Pop();
                if (snapshot.Timestamp > expiredTime)
                {
                    tempStack.Push(snapshot);
                }
            }

            while (tempStack.Count > 0)
            {
                _stateSnapshots.Push(tempStack.Pop());
            }
        }
    }

    #region 恢复策略接口和实现

    /// <summary>
    /// 恢复策略接口
    /// </summary>
    public interface IRecoveryStrategy
    {
        Task<RecoveryResult> RecoverAsync(RecoveryContext context);
    }

    /// <summary>
    /// 状态损坏恢复策略
    /// </summary>
    public class StateCorruptionRecovery : IRecoveryStrategy
    {
        public async Task<RecoveryResult> RecoverAsync(RecoveryContext context)
        {
            if (context.AvailableSnapshots.Count > 0)
            {
                var latestSnapshot = context.AvailableSnapshots[0];
                
                // 恢复到最近的快照状态
                context.StateManager.UpdateUserSession(
                    latestSnapshot.UserSession.Username,
                    latestSnapshot.UserSession.UserId,
                    latestSnapshot.UserSession.AccessToken,
                    latestSnapshot.UserSession.RefreshToken);

                if (latestSnapshot.SelectedCharacter != null)
                {
                    context.StateManager.SetSelectedCharacter(latestSnapshot.SelectedCharacter);
                }

                context.StateManager.UpdateCharacterList(latestSnapshot.CharacterList);
                
                var success = context.StateManager.TransitionToScene(latestSnapshot.CurrentScene, false);

                return new RecoveryResult
                {
                    IsSuccess = success,
                    RecoveryAction = $"恢复到快照状态: {latestSnapshot.CurrentScene}",
                    ErrorMessage = success ? "" : "恢复到快照状态失败"
                };
            }

            return new RecoveryResult
            {
                IsSuccess = false,
                RecoveryAction = "状态损坏恢复",
                ErrorMessage = "没有可用的状态快照"
            };
        }
    }

    /// <summary>
    /// 事件处理器失败恢复策略
    /// </summary>
    public class EventHandlerRecovery : IRecoveryStrategy
    {
        public async Task<RecoveryResult> RecoverAsync(RecoveryContext context)
        {
            // 尝试重新订阅事件或重置事件处理器
            // 这里可以添加具体的事件处理器重置逻辑
            
            // 简单恢复：尝试重新切换到目标场景
            await Task.Delay(100); // 短暂延迟

            var success = context.StateManager.TransitionToScene(context.ToScene, false);

            return new RecoveryResult
            {
                IsSuccess = success,
                RecoveryAction = "重试场景切换",
                ErrorMessage = success ? "" : "重试场景切换失败"
            };
        }
    }

    /// <summary>
    /// 循环切换恢复策略
    /// </summary>
    public class CircularTransitionRecovery : IRecoveryStrategy
    {
        public async Task<RecoveryResult> RecoverAsync(RecoveryContext context)
        {
            // 打破循环：强制切换到安全状态
            var safeScene = context.StateManager.UserSession?.IsAuthenticated == true 
                ? SceneType.CharacterSelection 
                : SceneType.Login;

            context.StateManager.ForceTransitionToScene(safeScene, "打破循环切换");
            var success = context.StateManager.CurrentScene != safeScene;
            return new RecoveryResult
            {
                IsSuccess =success ,
                RecoveryAction = $"打破循环，切换到安全状态: {safeScene}",
                ErrorMessage = success ? "" : "打破循环失败"
            };
        }
    }

    /// <summary>
    /// 权限拒绝恢复策略
    /// </summary>
    public class PermissionRecovery : IRecoveryStrategy
    {
        public async Task<RecoveryResult> RecoverAsync(RecoveryContext context)
        {
            // 权限不足时，回到适当的状态
            SceneType fallbackScene;
            
            if (context.StateManager.UserSession?.IsAuthenticated != true)
            {
                fallbackScene = SceneType.Login;
            }
            else
            {
                fallbackScene = SceneType.CharacterSelection;
            }

            var success = context.StateManager.TransitionToScene(fallbackScene, false);

            return new RecoveryResult
            {
                IsSuccess = success,
                RecoveryAction = $"权限不足，回退到: {fallbackScene}",
                ErrorMessage = success ? "" : "权限恢复失败"
            };
        }
    }

    /// <summary>
    /// 异常恢复策略
    /// </summary>
    public class ExceptionRecovery : IRecoveryStrategy
    {
        public async Task<RecoveryResult> RecoverAsync(RecoveryContext context)
        {
            // 通用异常恢复：重置状态并切换到安全场景
            context.StateManager.ClearError();
            context.StateManager.SetLoadingState(false);

            var safeScene = SceneType.Login;
            var success = context.StateManager.CurrentScene != safeScene;

            return new RecoveryResult
            {
                IsSuccess = success,
                RecoveryAction = $"异常恢复，重置到: {safeScene}",
                ErrorMessage = success ? "" : "异常恢复失败"
            };
        }
    }

    #endregion

    #region 辅助类

    /// <summary>
    /// 恢复场景枚举
    /// </summary>
    public enum RecoveryScenario
    {
        StateCorruption,      // 状态损坏
        EventHandlerFailure,  // 事件处理器失败
        CircularTransition,   // 循环切换
        PermissionDenied,     // 权限拒绝
        UnexpectedException   // 意外异常
    }

    /// <summary>
    /// 恢复上下文
    /// </summary>
    public class RecoveryContext
    {
        public SceneType FromScene { get; set; }
        public SceneType ToScene { get; set; }
        public string ErrorMessage { get; set; }
        public UIStateManager StateManager { get; set; }
        public List<SceneStateSnapshot> AvailableSnapshots { get; set; }
    }

    /// <summary>
    /// 恢复结果
    /// </summary>
    public class RecoveryResult
    {
        public bool IsSuccess { get; set; }
        public string RecoveryAction { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// 状态快照
    /// </summary>
    public class SceneStateSnapshot
    {
        public SceneType CurrentScene { get; set; }
        public UserSession UserSession { get; set; }
        public CharacterInfo SelectedCharacter { get; set; }
        public List<CharacterInfo> CharacterList { get; set; }
        public bool IsLoading { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// 恢复统计信息
    /// </summary>
    public class RecoveryStatistics
    {
        public int TotalAttempts { get; set; }
        public int SuccessfulRecoveries { get; set; }
        public double SuccessRate { get; set; }
        public int AvailableSnapshots { get; set; }
    }

    #endregion
}
