using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlaxEngine;
using HundunWorld.Game.UI.Core;
using HundunWorld.Game.UI.Events;
using HundunWorld.Game.UI.States;

using HundunWorld.Game.UI.Authentication;
using HundunWorld.Game.UI.Character;
using HundunWorld.Game.UI.GameMain;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.UI.Controllers
{
    /// <summary>
    /// 场景组件信息
    /// </summary>
    public class SceneComponentInfo
    {
        public SceneType SceneType { get; set; }
        public Script UIComponent { get; set; }
        public bool IsLoaded { get; set; }
        public bool IsActive { get; set; }
        public DateTime LastActivated { get; set; }
        public Dictionary<string, object> ComponentData { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// 场景控制器
    /// 负责管理UI场景的生命周期，包括场景组件的创建、激活、隐藏和销毁
    /// 处理场景间的数据传递和业务逻辑验证
    /// </summary>
    public class SceneController : Script
    {
        // 核心管理器
        private UnifiedStateManager _stateManager;
        private UIEventBus _eventBus;

        // 场景组件映射
        private readonly Dictionary<SceneType, SceneComponentInfo> _sceneComponents = 
            new Dictionary<SceneType, SceneComponentInfo>();

        // 场景数据传递
        private readonly Dictionary<string, object> _sharedData = new Dictionary<string, object>();

        // 场景加载策略
        private readonly Dictionary<SceneType, SceneLoadStrategy> _loadStrategies = 
            new Dictionary<SceneType, SceneLoadStrategy>();

        // 配置参数
        public bool EnableLazyLoading { get; set; } = true;
        public bool EnablePreloading { get; set; } = true;
        public bool LogSceneOperations { get; set; } = true;
        public int ComponentCacheSize { get; set; } = 5;

        #region 生命周期

        public override void OnStart()
        {
            InitializeController();
            FlaxEngine.Debug.Log("场景控制器初始化完成");
        }

        public override void OnDestroy()
        {
            CleanupController();
        }

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化控制器
        /// </summary>
        private void InitializeController()
        {
            // 获取核心管理器
            _stateManager = UnifiedStateManager.Instance;
            _eventBus = UIEventBus.Instance;

            // 初始化场景组件映射
            InitializeSceneMapping();

            // 设置加载策略
            SetupLoadStrategies();

            // 订阅事件
            SubscribeToEvents();

            // 预加载关键场景
            if (EnablePreloading)
            {
                PreloadCriticalScenes();
            }
        }

        /// <summary>
        /// 初始化场景组件映射
        /// </summary>
        private void InitializeSceneMapping()
        {
            // 为每个场景类型创建组件信息
            foreach (SceneType sceneType in Enum.GetValues(typeof(SceneType)))
            {
                _sceneComponents[sceneType] = new SceneComponentInfo
                {
                    SceneType = sceneType,
                    UIComponent = null,
                    IsLoaded = false,
                    IsActive = false
                };
            }

            if (LogSceneOperations)
            {
                FlaxEngine.Debug.Log($"初始化场景映射完成，共 {_sceneComponents.Count} 个场景");
            }
        }

        /// <summary>
        /// 设置加载策略
        /// </summary>
        private void SetupLoadStrategies()
        {
            // 登录场景 - 立即加载
            _loadStrategies[SceneType.Login] = SceneLoadStrategy.Immediate;
            _loadStrategies[SceneType.Register] = SceneLoadStrategy.Immediate;

            // 角色相关场景 - 懒加载
            _loadStrategies[SceneType.CharacterSelection] = SceneLoadStrategy.Lazy;
            _loadStrategies[SceneType.CharacterCreation] = SceneLoadStrategy.Lazy;

            // 游戏世界 - 预加载
            _loadStrategies[SceneType.GameWorld] = SceneLoadStrategy.Preload;

            // 其他场景 - 按需加载
            _loadStrategies[SceneType.Start] = SceneLoadStrategy.OnDemand;
        }

        /// <summary>
        /// 订阅事件
        /// </summary>
        private void SubscribeToEvents()
        {
            _eventBus.Subscribe<SceneTransitionStartedEvent>(OnSceneTransitionStarted, subscriberName: "SceneController");
            _eventBus.Subscribe<StateChangedEvent>(OnStateChanged, subscriberName: "SceneController");
        }

        /// <summary>
        /// 预加载关键场景
        /// </summary>
        private void PreloadCriticalScenes()
        {
            foreach (var kvp in _loadStrategies)
            {
                if (kvp.Value == SceneLoadStrategy.Preload || kvp.Value == SceneLoadStrategy.Immediate)
                {
                    LoadSceneComponent(kvp.Key);
                }
            }
        }

        /// <summary>
        /// 清理控制器
        /// </summary>
        private void CleanupController()
        {
            _eventBus?.UnsubscribeAll("SceneController");

            // 清理所有场景组件
            foreach (var componentInfo in _sceneComponents.Values)
            {
                if (componentInfo.UIComponent != null)
                {
                    UnloadSceneComponent(componentInfo.SceneType);
                }
            }

            _sceneComponents.Clear();
            _sharedData.Clear();

            FlaxEngine.Debug.Log("场景控制器资源已清理");
        }

        #endregion

        #region 场景切换

        /// <summary>
        /// 切换场景
        /// </summary>
        /// <param name="fromScene">源场景</param>
        /// <param name="toScene">目标场景</param>
        /// <returns>是否成功</returns>
        public bool SwitchScene(SceneType fromScene, SceneType toScene)
        {
            try
            {
                if (LogSceneOperations)
                {
                    FlaxEngine.Debug.Log($"场景切换: {fromScene} -> {toScene}");
                }

                // 隐藏源场景
                if (fromScene != SceneType.Start)
                {
                    HideScene(fromScene);
                }

                // 加载并显示目标场景
                LoadSceneComponent(toScene);
                ShowScene(toScene);

                // 更新场景状态
                UpdateSceneStates(fromScene, toScene);

                // 清理不需要的场景
                CleanupUnusedScenes();

                return true;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"场景切换失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 显示场景
        /// </summary>
        /// <param name="sceneType">场景类型</param>
        public void ShowScene(SceneType sceneType)
        {
            if (!_sceneComponents.TryGetValue(sceneType, out var componentInfo))
            {
                FlaxEngine.Debug.LogError($"未找到场景组件: {sceneType}");
                return;
            }

            // 确保组件已加载
            if (!componentInfo.IsLoaded)
            {
                LoadSceneComponent(sceneType);
            }

            // 激活组件
            if (componentInfo.UIComponent != null)
            {
                ActivateSceneComponent(componentInfo);
                
                if (LogSceneOperations)
                {
                    FlaxEngine.Debug.Log($"显示场景: {sceneType}");
                }
            }
        }

        /// <summary>
        /// 隐藏场景
        /// </summary>
        /// <param name="sceneType">场景类型</param>
        public void HideScene(SceneType sceneType)
        {
            if (!_sceneComponents.TryGetValue(sceneType, out var componentInfo))
            {
                return;
            }

            if (componentInfo.IsActive && componentInfo.UIComponent != null)
            {
                DeactivateSceneComponent(componentInfo);
                
                if (LogSceneOperations)
                {
                    FlaxEngine.Debug.Log($"隐藏场景: {sceneType}");
                }
            }
        }

        #endregion

        #region 组件管理

        /// <summary>
        /// 加载场景组件
        /// </summary>
        /// <param name="sceneType">场景类型</param>
        /// <returns>是否成功加载</returns>
        public bool LoadSceneComponent(SceneType sceneType)
        {
            if (!_sceneComponents.TryGetValue(sceneType, out var componentInfo))
            {
                FlaxEngine.Debug.LogError($"未找到场景配置: {sceneType}");
                return false;
            }

            // 如果已加载，直接返回
            if (componentInfo.IsLoaded && componentInfo.UIComponent != null)
            {
                return true;
            }

            try
            {
                // 创建UI组件
                var uiComponent = CreateUIComponent(sceneType);
                if (uiComponent == null)
                {
                    FlaxEngine.Debug.LogError($"创建UI组件失败: {sceneType}");
                    return false;
                }

                componentInfo.UIComponent = uiComponent;
                componentInfo.IsLoaded = true;

                // 初始化组件
                InitializeUIComponent(componentInfo);

                if (LogSceneOperations)
                {
                    FlaxEngine.Debug.Log($"加载场景组件: {sceneType}");
                }

                return true;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"加载场景组件失败 {sceneType}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 卸载场景组件
        /// </summary>
        /// <param name="sceneType">场景类型</param>
        public void UnloadSceneComponent(SceneType sceneType)
        {
            if (!_sceneComponents.TryGetValue(sceneType, out var componentInfo))
            {
                return;
            }

            if (componentInfo.UIComponent != null)
            {
                // 先隐藏组件
                if (componentInfo.IsActive)
                {
                    DeactivateSceneComponent(componentInfo);
                }

                // 销毁组件
                Destroy(componentInfo.UIComponent);
                componentInfo.UIComponent = null;
                componentInfo.IsLoaded = false;

                if (LogSceneOperations)
                {
                    FlaxEngine.Debug.Log($"卸载场景组件: {sceneType}");
                }
            }
        }

        /// <summary>
        /// 创建UI组件
        /// </summary>
        /// <param name="sceneType">场景类型</param>
        /// <returns>UI组件</returns>
        private Script CreateUIComponent(SceneType sceneType)
        {
            var componentActor = new EmptyActor();
            componentActor.Name = $"{sceneType}Component";
            componentActor.Parent = Actor;

            Script component = null;

            switch (sceneType)
            {
                case SceneType.Login:
                case SceneType.Register:
                    component = componentActor.AddScript<AuthenticationUI>();
                    break;

                case SceneType.CharacterSelection:
                case SceneType.CharacterCreation:
                    var existingController = Actor.GetScript<CharacterSceneController>();
                    if (existingController == null)
                    {
                        existingController = Level.FindScript<CharacterSceneController>();
                    }
                    if (existingController != null)
                    {
                        component = existingController;
                    }
                    else
                    {
                        component = componentActor.AddScript<CharacterSceneController>();
                    }
                    break;

                case SceneType.GameWorld:
                    component = componentActor.AddScript<GameMainUI>();
                    break;

                default:
                    FlaxEngine.Debug.LogWarning($"未知的场景类型: {sceneType}");
                    break;
            }

            return component;
        }

        /// <summary>
        /// 初始化UI组件
        /// </summary>
        /// <param name="componentInfo">组件信息</param>
        private void InitializeUIComponent(SceneComponentInfo componentInfo)
        {
            // 设置组件初始状态
            var sceneState = _stateManager.GetSceneState(componentInfo.SceneType);
            if (sceneState != null)
            {
                sceneState.SetLifecycleState(SceneLifecycleState.Ready);
            }

            // 初始化组件特定数据
            switch (componentInfo.SceneType)
            {
                case SceneType.CharacterSelection:
                    InitializeCharacterSelectionData(componentInfo);
                    break;

                case SceneType.GameWorld:
                    InitializeGameWorldData(componentInfo);
                    break;
            }

            // 发布组件初始化事件
            _eventBus.Publish(new SceneStateChangedEvent(
                componentInfo.SceneType, 
                null, 
                sceneState));
        }

        /// <summary>
        /// 激活场景组件
        /// </summary>
        /// <param name="componentInfo">组件信息</param>
        private void ActivateSceneComponent(SceneComponentInfo componentInfo)
        {
            if (componentInfo.UIComponent == null) return;

            try
            {
                // 调用组件特定的显示方法
                switch (componentInfo.SceneType)
                {
                    case SceneType.Login:
                    case SceneType.Register:
                        if (componentInfo.UIComponent is AuthenticationUI authUI)
                        {
                            authUI.ShowAuthenticationUI();
                        }
                        break;

                    case SceneType.GameWorld:
                        if (componentInfo.UIComponent is GameMainUI gameUI)
                        {
                            gameUI.ShowGameMainUI();
                        }
                        break;
                }

                componentInfo.IsActive = true;
                componentInfo.LastActivated = DateTime.UtcNow;

                // 更新场景状态
                var sceneState = _stateManager.GetSceneState(componentInfo.SceneType);
                if (sceneState != null)
                {
                    sceneState.SetLifecycleState(SceneLifecycleState.Active);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"激活场景组件失败 {componentInfo.SceneType}: {ex.Message}");
            }
        }

        /// <summary>
        /// 停用场景组件
        /// </summary>
        /// <param name="componentInfo">组件信息</param>
        private void DeactivateSceneComponent(SceneComponentInfo componentInfo)
        {
            if (componentInfo.UIComponent == null) return;

            try
            {
                // 调用组件特定的隐藏方法
                switch (componentInfo.SceneType)
                {
                    case SceneType.Login:
                    case SceneType.Register:
                        if (componentInfo.UIComponent is AuthenticationUI authUI)
                        {
                            authUI.HideAuthenticationUI();
                        }
                        break;

                    case SceneType.GameWorld:
                        if (componentInfo.UIComponent is GameMainUI gameUI)
                        {
                            gameUI.HideGameMainUI();
                        }
                        break;
                }

                componentInfo.IsActive = false;

                // 更新场景状态
                var sceneState = _stateManager.GetSceneState(componentInfo.SceneType);
                if (sceneState != null)
                {
                    sceneState.SetLifecycleState(SceneLifecycleState.Hidden);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"停用场景组件失败 {componentInfo.SceneType}: {ex.Message}");
            }
        }

        #endregion

        #region 数据管理

        /// <summary>
        /// 设置共享数据
        /// </summary>
        /// <param name="key">数据键</param>
        /// <param name="value">数据值</param>
        public void SetSharedData(string key, object value)
        {
            _sharedData[key] = value;
            
            if (LogSceneOperations)
            {
                FlaxEngine.Debug.Log($"设置共享数据: {key}");
            }
        }

        /// <summary>
        /// 获取共享数据
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="key">数据键</param>
        /// <returns>数据值</returns>
        public T GetSharedData<T>(string key)
        {
            if (_sharedData.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }
            return default(T);
        }

        /// <summary>
        /// 初始化角色选择数据
        /// </summary>
        /// <param name="componentInfo">组件信息</param>
        private void InitializeCharacterSelectionData(SceneComponentInfo componentInfo)
        {
            var currentState = _stateManager.GetCurrentState();
            if (currentState.UserSession.IsAuthenticated)
            {
                componentInfo.ComponentData["user_id"] = currentState.UserSession.UserId;
                componentInfo.ComponentData["characters"] = currentState.Characters;
            }
        }

        /// <summary>
        /// 初始化游戏世界数据
        /// </summary>
        /// <param name="componentInfo">组件信息</param>
        private void InitializeGameWorldData(SceneComponentInfo componentInfo)
        {
            var currentState = _stateManager.GetCurrentState();
            if (currentState.SelectedCharacter != null)
            {
                componentInfo.ComponentData["selected_character"] = currentState.SelectedCharacter;
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 更新场景状态
        /// </summary>
        /// <param name="fromScene">源场景</param>
        /// <param name="toScene">目标场景</param>
        private void UpdateSceneStates(SceneType fromScene, SceneType toScene)
        {
            // 更新源场景状态
            if (_sceneComponents.TryGetValue(fromScene, out var fromInfo))
            {
                var fromState = _stateManager.GetSceneState(fromScene);
                if (fromState != null)
                {
                    fromState.SetLifecycleState(SceneLifecycleState.Hidden);
                }
            }

            // 更新目标场景状态
            if (_sceneComponents.TryGetValue(toScene, out var toInfo))
            {
                var toState = _stateManager.GetSceneState(toScene);
                if (toState != null)
                {
                    toState.SetLifecycleState(SceneLifecycleState.Active);
                }
            }
        }

        /// <summary>
        /// 清理不使用的场景
        /// </summary>
        private void CleanupUnusedScenes()
        {
            var currentTime = DateTime.UtcNow;
            var componentsToUnload = new List<SceneType>();

            foreach (var kvp in _sceneComponents)
            {
                var componentInfo = kvp.Value;
                
                // 跳过当前活动的场景
                if (componentInfo.IsActive) continue;

                // 检查是否应该清理
                if (ShouldUnloadComponent(componentInfo, currentTime))
                {
                    componentsToUnload.Add(kvp.Key);
                }
            }

            // 卸载组件
            foreach (var sceneType in componentsToUnload)
            {
                UnloadSceneComponent(sceneType);
            }
        }

        /// <summary>
        /// 检查是否应该卸载组件
        /// </summary>
        /// <param name="componentInfo">组件信息</param>
        /// <param name="currentTime">当前时间</param>
        /// <returns>是否应该卸载</returns>
        private bool ShouldUnloadComponent(SceneComponentInfo componentInfo, DateTime currentTime)
        {
            // 检查缓存大小限制
            var loadedCount = 0;
            foreach (var info in _sceneComponents.Values)
            {
                if (info.IsLoaded) loadedCount++;
            }

            if (loadedCount <= ComponentCacheSize) return false;

            // 检查最后激活时间
            var timeSinceLastActivation = currentTime - componentInfo.LastActivated;
            return timeSinceLastActivation > TimeSpan.FromMinutes(10);
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 场景切换开始事件处理
        /// </summary>
        /// <param name="eventData">事件数据</param>
        private void OnSceneTransitionStarted(SceneTransitionStartedEvent eventData)
        {
            // 预加载目标场景
            if (_loadStrategies.TryGetValue(eventData.ToScene, out var strategy))
            {
                if (strategy == SceneLoadStrategy.Lazy || strategy == SceneLoadStrategy.OnDemand)
                {
                    LoadSceneComponent(eventData.ToScene);
                }
            }
        }

        /// <summary>
        /// 状态变更事件处理
        /// </summary>
        /// <param name="eventData">事件数据</param>
        private void OnStateChanged(StateChangedEvent eventData)
        {
            // 根据状态变更更新场景数据
            UpdateScenesWithNewState(eventData.NewState);
        }

        /// <summary>
        /// 根据新状态更新场景
        /// </summary>
        /// <param name="newState">新状态</param>
        private void UpdateScenesWithNewState(UIState newState)
        {
            // 更新用户会话相关的场景
            if (newState.UserSession.IsAuthenticated)
            {
                // 更新角色选择场景的数据
                if (_sceneComponents.TryGetValue(SceneType.CharacterSelection, out var charSelectionInfo))
                {
                    charSelectionInfo.ComponentData["user_session"] = newState.UserSession;
                    charSelectionInfo.ComponentData["characters"] = newState.Characters;
                }
            }

            // 更新选中角色相关的场景
            if (newState.SelectedCharacter != null)
            {
                if (_sceneComponents.TryGetValue(SceneType.GameWorld, out var gameWorldInfo))
                {
                    gameWorldInfo.ComponentData["selected_character"] = newState.SelectedCharacter;
                }
            }
        }

        #endregion
}
}