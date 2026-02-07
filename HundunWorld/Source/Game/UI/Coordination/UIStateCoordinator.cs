using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlaxEngine;
using HundunWorld.Game.UI.Core;
using HundunWorld.Game.UI.Controllers;
using Horizon.Game.Message.Enums;
using HundunWorld.Game.UI.Events;
using Game.UI.Controllers;

namespace HundunWorld.Game.UI.Coordination
{
    /// <summary>
    /// UI状态协调器
    /// 统一协调管理所有UI状态系统，包括传统状态管理器和统一状态管理器
    /// 确保状态一致性并提供统一的API接口
    /// </summary>
    public class UIStateCoordinator : Script
    {
        private static UIStateCoordinator _instance;
        private static readonly object _lock = new object();
        
        // 状态管理器实例
        private UIStateManager _legacyStateManager;      // 传统的UI状态管理器
        private UnifiedStateManager _unifiedStateManager; // 统一状态管理器
        private UISwitchController _switchController;     // 切换控制器
        private UIEventBus _eventBus;                     // 事件总线
        
        // 协调状态
        private bool _isCoordinating = false;
        private SceneType _coordinatedScene = SceneType.Start;
        private Dictionary<SceneType, SceneCoordinationInfo> _sceneCoordinationMap;
        
        // 事件
        public event Action<SceneType, SceneType> CoordinatedSceneChanged;
        public event Action<string> CoordinationError;
        
        public static UIStateCoordinator Instance
        {
            get
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        var actor = Level.FindActor("UIStateCoordinator") ?? new EmptyActor();
                        actor.Name = "UIStateCoordinator";
                        actor.SetStaticFlag(StaticFlags.FullyStatic, true);
                        _instance = actor.GetScript<UIStateCoordinator>() ?? actor.AddScript<UIStateCoordinator>();
                    }
                    return _instance;
                }
            }
        }
        
        public bool IsCoordinating => _isCoordinating;
        public SceneType CurrentScene => _coordinatedScene;
        
        #region 场景协调信息
        
        public class SceneCoordinationInfo
        {
            public SceneType SceneType { get; set; }
            public bool RequiresUnifiedState { get; set; } = true;
            public bool RequiresLegacyState { get; set; } = false;
            public List<SceneType> AllowedTransitions { get; set; } = new List<SceneType>();
            public Dictionary<string, object> SceneData { get; set; } = new Dictionary<string, object>();
        }
        
        #endregion
        
        #region 生命周期
        
        public override void OnEnable()
        {
            lock (_lock)
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
                
                InitializeCoordinator();
            }
        }
        
        public override void OnStart()
        {
            Debug.Log("[UIStateCoordinator] UI状态协调器初始化完成");
        }
        
        public override void OnDestroy()
        {
            CleanupCoordinator();
            
            if (_instance == this)
            {
                _instance = null;
            }
        }
        
        #endregion
        
        #region 初始化和清理
        
        /// <summary>
        /// 初始化协调器
        /// </summary>
        private void InitializeCoordinator()
        {
            try
            {
                // 获取各状态管理器实例
                _legacyStateManager = UIStateManager.Instance;
                _unifiedStateManager = UnifiedStateManager.Instance;
                _switchController = UISwitchController.Instance;
                _eventBus = UIEventBus.Instance;
                
                // 初始化场景协调映射
                InitializeSceneCoordinationMap();
                
                // 订阅事件
                SubscribeToEvents();
                
                _isCoordinating = true;
                Debug.Log("[UIStateCoordinator] 协调器初始化成功");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIStateCoordinator] 初始化失败: {ex.Message}");
                CoordinationError?.Invoke($"协调器初始化失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 初始化场景协调映射
        /// </summary>
        private void InitializeSceneCoordinationMap()
        {
            _sceneCoordinationMap = new Dictionary<SceneType, SceneCoordinationInfo>
            {
                [SceneType.Start] = new SceneCoordinationInfo
                {
                    SceneType = SceneType.Start,
                    RequiresUnifiedState = false,
                    RequiresLegacyState = true,
                    AllowedTransitions = new List<SceneType> { SceneType.Login, SceneType.Register, SceneType.CharacterSelection }
                },
                
                [SceneType.Login] = new SceneCoordinationInfo
                {
                    SceneType = SceneType.Login,
                    RequiresUnifiedState = true,
                    RequiresLegacyState = true,
                    AllowedTransitions = new List<SceneType> { SceneType.Register, SceneType.CharacterSelection, SceneType.Start }
                },
                
                [SceneType.Register] = new SceneCoordinationInfo
                {
                    SceneType = SceneType.Register,
                    RequiresUnifiedState = true,
                    RequiresLegacyState = true,
                    AllowedTransitions = new List<SceneType> { SceneType.Login, SceneType.CharacterSelection }
                },
                
                [SceneType.CharacterSelection] = new SceneCoordinationInfo
                {
                    SceneType = SceneType.CharacterSelection,
                    RequiresUnifiedState = true,
                    RequiresLegacyState = true,
                    AllowedTransitions = new List<SceneType> { SceneType.Login, SceneType.CharacterCreation, SceneType.GameWorld }
                },
                
                [SceneType.CharacterCreation] = new SceneCoordinationInfo
                {
                    SceneType = SceneType.CharacterCreation,
                    RequiresUnifiedState = true,
                    RequiresLegacyState = false,
                    AllowedTransitions = new List<SceneType> { SceneType.CharacterSelection }
                },
                
                [SceneType.GameWorld] = new SceneCoordinationInfo
                {
                    SceneType = SceneType.GameWorld,
                    RequiresUnifiedState = true,
                    RequiresLegacyState = false,
                    AllowedTransitions = new List<SceneType> { SceneType.CharacterSelection, SceneType.Settings }
                },
                
                [SceneType.Settings] = new SceneCoordinationInfo
                {
                    SceneType = SceneType.Settings,
                    RequiresUnifiedState = true,
                    RequiresLegacyState = false,
                    AllowedTransitions = new List<SceneType> { SceneType.GameWorld }
                }
            };
        }
        
        /// <summary>
        /// 订阅事件
        /// </summary>
        private void SubscribeToEvents()
        {
            // 订阅传统状态管理器事件
            if (_legacyStateManager != null)
            {
                _legacyStateManager.SceneChanged += OnLegacySceneChanged;
                _legacyStateManager.ErrorOccurred += OnLegacyError;
            }
            
            // 订阅统一状态管理器事件
            if (_unifiedStateManager != null)
            {
                _eventBus.Subscribe<SceneStateChangedEvent>(OnUnifiedSceneChanged);
                _eventBus.Subscribe<SceneTransitionCompletedEvent>(OnTransitionCompleted);
            }
        }
        
        /// <summary>
        /// 清理协调器
        /// </summary>
        private void CleanupCoordinator()
        {
            _isCoordinating = false;
            
            // 取消订阅事件
            if (_legacyStateManager != null)
            {
                _legacyStateManager.SceneChanged -= OnLegacySceneChanged;
                _legacyStateManager.ErrorOccurred -= OnLegacyError;
            }
            
            if (_eventBus != null)
            {
                _eventBus.UnsubscribeAll("UIStateCoordinator");
            }
            
            _sceneCoordinationMap?.Clear();
            Debug.Log("[UIStateCoordinator] 协调器已清理");
        }
        
        #endregion
        
        #region 场景切换协调
        
        /// <summary>
        /// 协调场景切换
        /// </summary>
        public async Task<bool> CoordinateSceneSwitchAsync(SceneType targetScene, bool useAnimation = true)
        {
            if (!_isCoordinating)
            {
                Debug.LogWarning("[UIStateCoordinator] 协调器未激活");
                return false;
            }
            
            var currentScene = _coordinatedScene;
            
            // 验证切换合法性
            if (!ValidateSceneTransition(currentScene, targetScene))
            {
                var errorMsg = $"非法的场景切换: {currentScene} -> {targetScene}";
                Debug.LogWarning($"[UIStateCoordinator] {errorMsg}");
                CoordinationError?.Invoke(errorMsg);
                return false;
            }
            
            try
            {
                Debug.Log($"[UIStateCoordinator] 开始协调场景切换: {currentScene} -> {targetScene}");
                
                // 获取场景协调信息
                var coordinationInfo = _sceneCoordinationMap[targetScene];
                
                // 执行协调切换
                var success = await ExecuteCoordinatedSwitch(currentScene, targetScene, coordinationInfo, useAnimation);
                
                if (success)
                {
                    _coordinatedScene = targetScene;
                    CoordinatedSceneChanged?.Invoke(currentScene, targetScene);
                    Debug.Log($"[UIStateCoordinator] 场景切换协调成功: {targetScene}");
                }
                else
                {
                    Debug.LogWarning($"[UIStateCoordinator] 场景切换协调失败: {targetScene}");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIStateCoordinator] 场景切换协调异常: {ex.Message}");
                CoordinationError?.Invoke($"场景切换异常: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 验证场景切换合法性
        /// </summary>
        private bool ValidateSceneTransition(SceneType fromScene, SceneType toScene)
        {
            if (fromScene == toScene) return true;
            
            if (_sceneCoordinationMap.TryGetValue(toScene, out var info))
            {
                return info.AllowedTransitions.Contains(fromScene);
            }
            
            return false;
        }
        
        /// <summary>
        /// 执行协调切换
        /// </summary>
        private async Task<bool> ExecuteCoordinatedSwitch(SceneType fromScene, SceneType toScene, 
            SceneCoordinationInfo info, bool useAnimation)
        {
            // 根据场景需求决定使用哪个状态管理器
            
            if (info.RequiresUnifiedState && _unifiedStateManager != null)
            {
                // 使用统一状态管理器进行切换
                return await ExecuteUnifiedStateSwitch(fromScene, toScene, useAnimation);
            }
            else if (info.RequiresLegacyState && _legacyStateManager != null)
            {
                // 使用传统状态管理器进行切换
                return ExecuteLegacyStateSwitch(fromScene, toScene);
            }
            else if (_switchController != null)
            {
                // 使用切换控制器
                return await ExecuteSwitchControllerSwitch(fromScene, toScene, useAnimation);
            }
            
            Debug.LogWarning($"[UIStateCoordinator] 无法找到合适的切换方式: {toScene}");
            return false;
        }
        
        /// <summary>
        /// 执行统一状态管理器切换
        /// </summary>
        private async Task<bool> ExecuteUnifiedStateSwitch(SceneType fromScene, SceneType toScene, bool useAnimation)
        {
            try
            {
                var request = new SwitchRequest
                {
                    TargetScene = toScene,
                    EnableAnimation = useAnimation,
                    CreateSnapshot = true
                };
                
                var result = await _switchController.RequestSceneSwitchAsync(toScene);
                return result.IsSuccess;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIStateCoordinator] 统一状态切换失败: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 执行传统状态管理器切换
        /// </summary>
        private bool ExecuteLegacyStateSwitch(SceneType fromScene, SceneType toScene)
        {
            try
            {
                return _legacyStateManager.TransitionToScene(toScene);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIStateCoordinator] 传统状态切换失败: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 执行切换控制器切换
        /// </summary>
        private async Task<bool> ExecuteSwitchControllerSwitch(SceneType fromScene, SceneType toScene, bool useAnimation)
        {
            try
            {
                var request = new SwitchRequest
                {
                    TargetScene = toScene,
                    EnableAnimation = useAnimation,
                    CreateSnapshot = true
                };
                
                var result = await _switchController.RequestSceneSwitchAsync(toScene);
                return result.IsSuccess;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIStateCoordinator] 切换控制器切换失败: {ex.Message}");
                return false;
            }
        }
        
        #endregion
        
        #region 事件处理
        
        /// <summary>
        /// 传统状态管理器场景变更事件
        /// </summary>
        private void OnLegacySceneChanged(SceneType previousScene, SceneType newScene)
        {
            if (_isCoordinating && newScene != _coordinatedScene)
            {
                Debug.Log($"[UIStateCoordinator] 检测到传统状态变更: {previousScene} -> {newScene}");
                _coordinatedScene = newScene;
                CoordinatedSceneChanged?.Invoke(previousScene, newScene);
            }
        }
        
        /// <summary>
        /// 统一状态管理器场景变更事件
        /// </summary>
        private void OnUnifiedSceneChanged(SceneStateChangedEvent eventData)
        {
            if (_isCoordinating && eventData.NewState.SceneType != _coordinatedScene)
            {
                Debug.Log($"[UIStateCoordinator] 检测到统一状态变更: {_coordinatedScene} -> {eventData.NewState.SceneType}");
                var previousScene = _coordinatedScene;
                _coordinatedScene = eventData.NewState.SceneType;
                CoordinatedSceneChanged?.Invoke(previousScene, eventData.NewState.SceneType);
            }
        }
        
        /// <summary>
        /// 切换完成事件
        /// </summary>
        private void OnTransitionCompleted(SceneTransitionCompletedEvent eventData)
        {
            Debug.Log($"[UIStateCoordinator] 切换完成: {eventData.FromScene} -> {eventData.ToScene}");
        }
        
        /// <summary>
        /// 传统状态管理器错误事件
        /// </summary>
        private void OnLegacyError(string errorMessage)
        {
            Debug.LogError($"[UIStateCoordinator] 传统状态管理器错误: {errorMessage}");
            CoordinationError?.Invoke($"传统状态错误: {errorMessage}");
        }
        
        #endregion
        
        #region 状态查询和管理
        
        /// <summary>
        /// 获取场景协调信息
        /// </summary>
        public SceneCoordinationInfo GetSceneCoordinationInfo(SceneType sceneType)
        {
            return _sceneCoordinationMap.TryGetValue(sceneType, out var info) ? info : null;
        }
        
        /// <summary>
        /// 获取当前协调状态
        /// </summary>
        public Dictionary<string, object> GetCoordinationStatus()
        {
            return new Dictionary<string, object>
            {
                ["IsCoordinating"] = _isCoordinating,
                ["CurrentScene"] = _coordinatedScene,
                ["HasLegacyManager"] = _legacyStateManager != null,
                ["HasUnifiedManager"] = _unifiedStateManager != null,
                ["HasSwitchController"] = _switchController != null,
                ["SceneCoordinationMapCount"] = _sceneCoordinationMap?.Count ?? 0
            };
        }
        
        /// <summary>
        /// 强制同步状态
        /// </summary>
        public void ForceStateSynchronization()
        {
            if (_legacyStateManager != null && _unifiedStateManager != null)
            {
                var legacyScene = _legacyStateManager.CurrentScene;
                var unifiedState = _unifiedStateManager.GetCurrentState();
                
                if (legacyScene != unifiedState?.CurrentScene)
                {
                    Debug.LogWarning($"[UIStateCoordinator] 检测到状态不一致，强制同步");
                    _coordinatedScene = legacyScene;
                }
            }
        }
        
        #endregion
    }
}
