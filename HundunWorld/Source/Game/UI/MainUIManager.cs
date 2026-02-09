using FlaxEngine;
using Horizon.Game.Message.Enums;
using HundunWorld.Game.UI.Authentication;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI
{
    /// <summary>
    /// 主UI管理器
    /// 统一管理所有UI界面的显示、隐藏和切换
    /// </summary>
    public class MainUIManager : Script
    {
        private static MainUIManager _instance;
        public static MainUIManager Instance => _instance;

        // UI界面组件
        private AuthenticationUI _authenticationUI;
        
        // UI状态管理
        private UIStateManager _stateManager;
        private Dictionary<SceneType, Script> _uiComponents = new Dictionary<SceneType, Script>();
        
        // 当前活动的UI
        private Script _currentActiveUI;

        // 活跃的效果图标
        private readonly Dictionary<(ulong TargetId, int EffectId), EffectIconEntry> _activeEffectIcons = new();

        public override void OnStart()
        {
            InitializeInstance();
            InitializeStateManager();
            InitializeUIComponents();
            SubscribeEvents();
            
            FlaxEngine.Debug.Log("主UI管理器初始化完成");
        }

        /// <summary>
        /// 初始化单例实例
        /// </summary>
        private void InitializeInstance()
        {
            if (_instance == null)
            {
                _instance = this;
                // 确保跨场景持久化
                Actor.SetStaticFlag(StaticFlags.FullyStatic, true);
            }
            else if (_instance != this)
            {
                // 销毁多余的实例
                Destroy(Actor);
                return;
            }
        }

        /// <summary>
        /// 初始化状态管理器
        /// </summary>
        private void InitializeStateManager()
        {
            _stateManager = UIStateManager.Instance;
        }

        /// <summary>
        /// 初始化UI组件
        /// </summary>
        private void InitializeUIComponents()
        {
            try
            {
                // 创建认证UI
                var authActor = new EmptyActor();
                authActor.Name = "AuthenticationUI";
                authActor.Parent = Actor;
                _authenticationUI = authActor.AddScript<AuthenticationUI>();
                _uiComponents[SceneType.Login] = _authenticationUI;
                _uiComponents[SceneType.Register] = _authenticationUI; // 认证UI处理登录和注册

                FlaxEngine.Debug.Log($"UI组件初始化完成，共创建{_uiComponents.Count}个UI组件");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"初始化UI组件时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 订阅事件
        /// </summary>
        private void SubscribeEvents()
        {
            if (_stateManager != null)
            {
                _stateManager.SceneChanged += OnSceneChanged;
                _stateManager.LoadingStateChanged += OnLoadingStateChanged;
                FlaxEngine.Debug.Log("已订阅状态管理器事件");
            }
        }

        /// <summary>
        /// 场景切换事件处理
        /// </summary>
        private void OnSceneChanged(SceneType previousScene, SceneType newScene)
        {
            FlaxEngine.Debug.Log($"主UI管理器处理场景切换: {previousScene} -> {newScene}");
            
            try
            {
                // 隐藏之前的UI
                HidePreviousUI(previousScene);
                
                // 显示新的UI
                ShowNewUI(newScene);
                
                // 更新当前活动UI
                UpdateCurrentActiveUI(newScene);
                
                FlaxEngine.Debug.Log($"场景切换处理完成: {newScene}");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"处理场景切换时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 隐藏之前的UI
        /// </summary>
        private void HidePreviousUI(SceneType previousScene)
        {
            if (previousScene == SceneType. Start)
                return; // 初始状态无需隐藏
                
            if (_uiComponents.TryGetValue(previousScene, out var previousUI) && previousUI != null)
            {
                try
                {
                    // 调用相应UI的隐藏方法
                    switch (previousScene)
                    {
                        case SceneType.Login:
                        case SceneType.Register :
                            if (_authenticationUI != null)
                                _authenticationUI.HideAuthenticationUI();
                            break;
                        default:
                            // 对于其他UI组件，它们通过事件监听自动处理显示/隐藏
                            break;
                    }
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"隐藏UI {previousScene} 时出错: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 显示新的UI
        /// </summary>
        private void ShowNewUI(SceneType newScene)
        {
            if (_uiComponents.TryGetValue(newScene, out var newUI) && newUI != null)
            {
                try
                {
                    // 调用相应UI的显示方法
                    switch (newScene)
                    {
                        case SceneType.Login:
                        case SceneType.Register :
                            if (_authenticationUI != null)
                                _authenticationUI.ShowAuthenticationUI();
                            break;
                        default:
                            // 其他UI组件通过事件监听自动处理
                            break;
                    }
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"显示UI {newScene} 时出错: {ex.Message}");
                }
            }
            else
            {
                FlaxEngine.Debug.LogWarning($"未找到场景 {newScene} 对应的UI组件");
            }
        }

        /// <summary>
        /// 更新当前活动UI
        /// </summary>
        private void UpdateCurrentActiveUI(SceneType newScene)
        {
            if (_uiComponents.TryGetValue(newScene, out var newActiveUI))
            {
                _currentActiveUI = newActiveUI;
            }
        }

        /// <summary>
        /// 加载状态变化事件处理
        /// </summary>
        private void OnLoadingStateChanged(bool isLoading)
        {
            // 可以在这里实现全局的加载状态显示
            FlaxEngine.Debug.Log($"全局加载状态变化: {isLoading}");
        }

        /// <summary>
        /// 强制刷新所有UI状态
        /// </summary>
        public void RefreshAllUI()
        {
            try
            {
                FlaxEngine.Debug.Log("刷新所有UI状态");
                
                var currentScene = _stateManager.CurrentScene;
                OnSceneChanged(SceneType.Start, currentScene);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"刷新UI状态时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取指定场景的UI组件
        /// </summary>
        public T GetUIComponent<T>(SceneType sceneType) where T : Script
        {
            if (_uiComponents.TryGetValue(sceneType, out var component))
            {
                return component as T;
            }
            return null;
        }

        /// <summary>
        /// 检查UI组件是否可用
        /// </summary>
        public bool IsUIComponentAvailable(SceneType sceneType)
        {
            return _uiComponents.ContainsKey(sceneType) && _uiComponents[sceneType] != null;
        }

        /// <summary>
        /// 重新初始化UI组件（用于解决编辑器播放模式问题）
        /// </summary>
        public void ReinitializeUIComponents()
        {
            try
            {
                FlaxEngine.Debug.Log("重新初始化UI组件");
                
                // 清理现有组件
                _uiComponents.Clear();
                _currentActiveUI = null;
                
                // 重新初始化
                InitializeUIComponents();
                
                // 刷新UI状态
                RefreshAllUI();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"重新初始化UI组件时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示效果图标
        /// </summary>
        public void ShowEffectIcon(ulong targetId, int effectId, string effectName, float duration)
        {
            try
            {
                FlaxEngine.Debug.Log($"显示效果图标: 目标{targetId}, 效果{effectId}({effectName}), 持续时间{duration}秒");

                var key = (targetId, effectId);
                if (_activeEffectIcons.ContainsKey(key))
                {
                    // 刷新已有效果的持续时间
                    var existing = _activeEffectIcons[key];
                    existing.RemainingDuration = duration;
                    existing.EffectName = effectName;
                }
                else
                {
                    // 添加新效果图标记录
                    _activeEffectIcons[key] = new EffectIconEntry
                    {
                        TargetId = targetId,
                        EffectId = effectId,
                        EffectName = effectName,
                        RemainingDuration = duration,
                        TotalDuration = duration
                    };
                    FlaxEngine.Debug.Log($"新增效果图标: {effectName} (ID:{effectId}), 持续{duration}秒");
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"显示效果图标时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 移除效果图标
        /// </summary>
        public void RemoveEffectIcon(ulong targetId, int effectId)
        {
            var key = (targetId, effectId);
            if (_activeEffectIcons.Remove(key))
            {
                FlaxEngine.Debug.Log($"移除效果图标: 目标{targetId}, 效果{effectId}");
            }
        }

        /// <summary>
        /// 处理Buff显示消息
        /// </summary>
        public void HandleBuffDisplayMessage(Horizon.Game.Message.Network.BuffDisplayMessage message)
        {
            if (message == null) return;

            try
            {
                switch (message.Operation)
                {
                    case Horizon.Game.Message.Network.BuffOperation.Add:
                    case Horizon.Game.Message.Network.BuffOperation.Refresh:
                    case Horizon.Game.Message.Network.BuffOperation.Stack:
                        ShowEffectIcon(message.TargetId, message.EffectId, message.EffectName, message.Duration);
                        break;
                    case Horizon.Game.Message.Network.BuffOperation.Remove:
                        RemoveEffectIcon(message.TargetId, message.EffectId);
                        break;
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"处理Buff显示消息时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取目标实体上的活跃效果数量
        /// </summary>
        public int GetActiveEffectCount(ulong targetId)
        {
            int count = 0;
            foreach (var entry in _activeEffectIcons.Values)
            {
                if (entry.TargetId == targetId)
                    count++;
            }
            return count;
        }

        public override void OnDestroy()
        {
            // 取消事件订阅
            if (_stateManager != null)
            {
                _stateManager.SceneChanged -= OnSceneChanged;
                _stateManager.LoadingStateChanged -= OnLoadingStateChanged;
            }

            // 清理组件引用
            _uiComponents.Clear();
            _currentActiveUI = null;

            // 清空单例引用
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }

    /// <summary>
    /// 效果图标条目
    /// </summary>
    public class EffectIconEntry
    {
        public ulong TargetId { get; set; }
        public int EffectId { get; set; }
        public string EffectName { get; set; } = "";
        public float RemainingDuration { get; set; }
        public float TotalDuration { get; set; }
    }
}