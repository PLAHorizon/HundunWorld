using System;
using System.Collections.Generic;
using System.Linq;
using FlaxEngine;
using HundunWorld.Game.UI.Events;
using HundunWorld.Game.UI.States;

using Horizon.Game.Message.Network;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.UI.Core
{
    /// <summary>
    /// 统一状态管理器 - 重构版本
    /// 负责管理UI的全局状态、场景状态和状态变更
    /// 遵循单一职责原则，专注于状态管理
    /// </summary>
    public class UnifiedStateManager : Script
    {
        private static UnifiedStateManager _instance;
        private static readonly object _lock = new object();

        // 核心状态
        private UIState _currentState;
        private Dictionary<SceneType, SceneState> _sceneStates = new Dictionary<SceneType, SceneState>();
        private TransitionState _currentTransition;

        // 事件总线
        private UIEventBus _eventBus;

        // 状态验证器
        private readonly List<IStateValidator> _validators = new List<IStateValidator>();

        // 状态监听器
        private readonly List<IStateListener> _listeners = new List<IStateListener>();

        // 配置
        public bool EnableAutomaticSnapshots { get; set; } = true;
        public int MaxStateHistoryCount { get; set; } = 50;
        public bool LogStateChanges { get; set; } = true;

        /// <summary>
        /// 单例实例
        /// </summary>
        public static UnifiedStateManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            var gameObject = Level.FindActor("UnifiedStateManager") ?? new EmptyActor();
                            gameObject.Name = "UnifiedStateManager";
                            _instance = gameObject.GetScript<UnifiedStateManager>() ?? gameObject.AddScript<UnifiedStateManager>();
                            _instance.OnAwake();
                            Engine.RequestingExit += () => { _instance = null; };
                        }
                    }
                }
                return _instance;
            }
        }

        #region 生命周期

        public override void OnAwake()
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

            InitializeStateManager();
        }

        public override void OnStart()
        {
            FlaxEngine.Debug.Log("统一状态管理器初始化完成");
        }

        public override void OnDestroy()
        {
            CleanupStateManager();
            
            if (_instance == this)
            {
                _instance = null;
            }
        }

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化状态管理器
        /// </summary>
        private void InitializeStateManager()
        {
            // 初始化事件总线
            _eventBus = UIEventBus.Instance;

            // 初始化状态
            _currentState = new UIState();
           
            
            // 初始化所有场景状态
            InitializeAllSceneStates();

            // 注册内置验证器
            RegisterBuiltInValidators();

            FlaxEngine.Debug.Log("统一状态管理器初始化完成");
        }

        /// <summary>
        /// 初始化所有场景状态
        /// </summary>
        private void InitializeAllSceneStates()
        {
            foreach (SceneType sceneType in Enum.GetValues(typeof(SceneType)))
            {
                var sceneState = new SceneState
                {
                    SceneType = sceneType,
                    LifecycleState = SceneLifecycleState.Uninitialized
                };

                // 设置场景特定配置
                ConfigureSceneState(sceneState);
                
                _sceneStates[sceneType] = sceneState;
            }
        }

        /// <summary>
        /// 配置场景状态
        /// </summary>
        /// <param name="sceneState">场景状态</param>
        private void ConfigureSceneState(SceneState sceneState)
        {
            switch (sceneState.SceneType)
            {
                case SceneType.Login:
                    sceneState.CanBeCached = true;
                    sceneState.Priority = 10;
                    break;
                    
                case SceneType.CharacterSelection:
                    sceneState.RequiredPermissions.Add("authenticated");
                    sceneState.CanBeCached = true;
                    sceneState.Priority = 8;
                    break;
                    
                case SceneType.GameWorld:
                    sceneState.RequiredPermissions.Add("authenticated");
                    sceneState.RequiredPermissions.Add("character_selected");
                    sceneState.CanBeCached = false;
                    sceneState.Priority = 5;
                    break;
            }
        }

        /// <summary>
        /// 注册内置验证器
        /// </summary>
        private void RegisterBuiltInValidators()
        {
            // 添加权限验证器
            _validators.Add(new PermissionValidator());
            
            // 添加状态一致性验证器
            _validators.Add(new StateConsistencyValidator());
        }

        /// <summary>
        /// 清理状态管理器
        /// </summary>
        private void CleanupStateManager()
        {
            _validators.Clear();
            _listeners.Clear();
            FlaxEngine.Debug.Log("统一状态管理器资源已清理");
        }

        #endregion

        #region 状态访问

        /// <summary>
        /// 获取当前UI状态
        /// </summary>
        /// <returns>当前UI状态的副本</returns>
        public UIState GetCurrentState()
        {
            return _currentState?.Clone();
        }

        /// <summary>
        /// 获取指定场景状态
        /// </summary>
        /// <param name="sceneType">场景类型</param>
        /// <returns>场景状态的副本</returns>
        public SceneState GetSceneState(SceneType sceneType)
        {
            return _sceneStates.TryGetValue(sceneType, out var state) ? state.Clone() : null;
        }

        /// <summary>
        /// 获取当前切换状态
        /// </summary>
        /// <returns>当前切换状态的副本</returns>
        public TransitionState GetCurrentTransition()
        {
            return _currentTransition?.Clone();
        }

        /// <summary>
        /// 获取所有场景状态
        /// </summary>
        /// <returns>所有场景状态的副本</returns>
        public Dictionary<SceneType, SceneState> GetAllSceneStates()
        {
            var result = new Dictionary<SceneType, SceneState>();
            foreach (var kvp in _sceneStates)
            {
                result[kvp.Key] = kvp.Value.Clone();
            }
            return result;
        }

        #endregion

        #region 状态变更

        /// <summary>
        /// 开始场景切换
        /// </summary>
        /// <param name="toScene">目标场景</param>
        /// <param name="parameters">切换参数</param>
        /// <param name="forced">是否强制切换</param>
        /// <returns>切换状态</returns>
        public TransitionState BeginSceneTransition(SceneType toScene, Dictionary<string, object> parameters = null, bool forced = false)
        {
            if (_currentState.IsTransitioning && !forced)
            {
                FlaxEngine.Debug.LogWarning("已有场景切换在进行中，无法开始新的切换");
                return null;
            }

            var fromScene = _currentState.CurrentScene;

            // 创建切换状态
            _currentTransition = new TransitionState
            {
                TransitionId = Guid.NewGuid().ToString(),
                FromScene = fromScene,
                ToScene = toScene,
                IsForced = forced,
                Parameters = parameters ?? new Dictionary<string, object>()
            };

            // 验证切换条件
            if (!forced && !ValidateTransition(_currentTransition))
            {
                _currentTransition.SetError("切换条件验证失败");
                return _currentTransition;
            }

            // 更新状态
            var oldState = _currentState.Clone();
            _currentState.IsTransitioning = true;
            _currentState.TransitionId = _currentTransition.TransitionId;
            _currentState.IncrementVersion();

            // 发布事件
            _eventBus.Publish(new SceneTransitionStartedEvent(fromScene, toScene, _currentTransition));
            _eventBus.Publish(new StateChangedEvent(oldState, _currentState, "开始场景切换"));

            if (LogStateChanges)
            {
                FlaxEngine.Debug.Log($"开始场景切换: {fromScene} -> {toScene}, ID: {_currentTransition.TransitionId}");
            }

            return _currentTransition;
        }

        /// <summary>
        /// 完成场景切换
        /// </summary>
        /// <param name="success">是否成功</param>
        /// <param name="errorMessage">错误信息</param>
        public void CompleteSceneTransition(bool success = true, string errorMessage = "")
        {
            if (_currentTransition == null || !_currentState.IsTransitioning)
            {
                FlaxEngine.Debug.LogWarning("没有正在进行的场景切换");
                return;
            }

            var oldState = _currentState.Clone();

            if (success)
            {
                // 成功完成切换
                _currentState.PreviousScene = _currentState.CurrentScene;
                _currentState.CurrentScene = _currentTransition.ToScene;
                _currentTransition.Complete();

                // 更新场景状态
                UpdateSceneStatesAfterTransition();
            }
            else
            {
                // 切换失败
                _currentTransition.SetError(errorMessage);
            }

            // 重置切换状态
            _currentState.IsTransitioning = false;
            _currentState.TransitionId = "";
            _currentState.IncrementVersion();

            // 发布事件
            _eventBus.Publish(new SceneTransitionCompletedEvent(
                _currentTransition.FromScene, 
                _currentTransition.ToScene, 
                _currentTransition, 
                success));
            _eventBus.Publish(new StateChangedEvent(oldState, _currentState, success ? "场景切换成功" : "场景切换失败"));

            if (LogStateChanges)
            {
                FlaxEngine.Debug.Log($"场景切换{(success ? "成功" : "失败")}: {_currentTransition.GenerateReport()}");
            }

            _currentTransition = null;
        }

        /// <summary>
        /// 更新切换进度
        /// </summary>
        /// <param name="progress">进度值 (0.0 - 1.0)</param>
        /// <param name="phase">当前阶段</param>
        public void UpdateTransitionProgress(float progress, TransitionPhase? phase = null)
        {
            if (_currentTransition == null) return;

            if (phase.HasValue)
            {
                _currentTransition.SetPhase(phase.Value);
            }
            else
            {
                _currentTransition.UpdateProgress(progress);
            }

            // 发布进度事件
            _eventBus.Publish(new SceneTransitionProgressEvent(
                _currentTransition.FromScene,
                _currentTransition.ToScene,
                _currentTransition,
                _currentTransition.Progress));
        }

        /// <summary>
        /// 更新用户会话
        /// </summary>
        /// <param name="username">用户名</param>
        /// <param name="userId">用户ID</param>
        /// <param name="accessToken">访问令牌</param>
        /// <param name="refreshToken">刷新令牌</param>
        public void UpdateUserSession(string username, ulong userId, string accessToken, string refreshToken = "")
        {
            var oldSession = _currentState.UserSession?.Clone();
            var oldState = _currentState.Clone();

            _currentState.UserSession.Username = username;
            _currentState.UserSession.UserId = userId;
            _currentState.UserSession.AccessToken = accessToken;
            _currentState.UserSession.RefreshToken = refreshToken;
            _currentState.IncrementVersion();

            // 发布事件
            _eventBus.Publish(new UserSessionChangedEvent(oldSession, _currentState.UserSession));
            _eventBus.Publish(new StateChangedEvent(oldState, _currentState, "用户会话更新"));

            if (LogStateChanges)
            {
                FlaxEngine.Debug.Log($"用户会话已更新: {username} (ID: {userId})");
            }
        }

        /// <summary>
        /// 清除用户会话
        /// </summary>
        public void ClearUserSession()
        {
            var oldSession = _currentState.UserSession?.Clone();
            var oldState = _currentState.Clone();

            _currentState.UserSession.Clear();
            _currentState.SelectedCharacter = null;
            _currentState.Characters.Clear();
            _currentState.IncrementVersion();

            // 发布事件
            _eventBus.Publish(new UserSessionChangedEvent(oldSession, _currentState.UserSession));
            _eventBus.Publish(new SelectedCharacterChangedEvent(_currentState.SelectedCharacter, null));
            _eventBus.Publish(new CharacterListUpdatedEvent(new List<CharacterInfo>()));
            _eventBus.Publish(new StateChangedEvent(oldState, _currentState, "用户会话清除"));

            if (LogStateChanges)
            {
                FlaxEngine.Debug.Log("用户会话已清除");
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 验证场景切换
        /// </summary>
        /// <param name="transition">切换状态</param>
        /// <returns>是否有效</returns>
        private bool ValidateTransition(TransitionState transition)
        {
            foreach (var validator in _validators)
            {
                if (!validator.ValidateTransition(transition, _currentState, _sceneStates))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 切换完成后更新场景状态
        /// </summary>
        private void UpdateSceneStatesAfterTransition()
        {
            var fromScene = _currentTransition.FromScene;
            var toScene = _currentTransition.ToScene;

            // 更新源场景状态
            if (_sceneStates.TryGetValue(fromScene, out var fromState))
            {
                fromState.SetLifecycleState(SceneLifecycleState.Hidden);
            }

            // 更新目标场景状态
            if (_sceneStates.TryGetValue(toScene, out var toState))
            {
                toState.SetLifecycleState(SceneLifecycleState.Active);
            }
        }

        #endregion
    }

    #region 验证器接口和实现

    /// <summary>
    /// 状态验证器接口
    /// </summary>
    public interface IStateValidator
    {
        bool ValidateTransition(TransitionState transition, UIState currentState, Dictionary<SceneType, SceneState> sceneStates);
    }

    /// <summary>
    /// 权限验证器
    /// </summary>
    public class PermissionValidator : IStateValidator
    {
        public bool ValidateTransition(TransitionState transition, UIState currentState, Dictionary<SceneType, SceneState> sceneStates)
        {
            if (!sceneStates.TryGetValue(transition.ToScene, out var targetScene))
            {
                return false;
            }

            // 这里可以实现具体的权限检查逻辑
            // 例如检查用户是否已认证、是否有访问特定场景的权限等
            
            return true; // 暂时返回true，实际项目中需要实现具体逻辑
        }
    }

    /// <summary>
    /// 状态一致性验证器
    /// </summary>
    public class StateConsistencyValidator : IStateValidator
    {
        public bool ValidateTransition(TransitionState transition, UIState currentState, Dictionary<SceneType, SceneState> sceneStates)
        {
            // 检查状态一致性，例如：
            // 1. 是否存在循环切换
            // 2. 目标场景是否可达
            // 3. 必要的前置条件是否满足
            
            return true; // 暂时返回true，实际项目中需要实现具体逻辑
        }
    }

    /// <summary>
    /// 状态监听器接口
    /// </summary>
    public interface IStateListener
    {
        void OnStateChanged(UIState oldState, UIState newState);
        void OnSceneStateChanged(SceneType sceneType, SceneState oldState, SceneState newState);
    }

    #endregion
}