using System;
using FlaxEngine;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.UI
{
    /// <summary>
    /// 场景切换管理器 - 兼容层
    /// [已废弃] 请使用 GameSceneManager 代替
    /// 此类保留仅为向后兼容，内部委托给 GameSceneManager 处理
    /// </summary>
    [Obsolete("SceneTransitionManager已废弃，请使用GameSceneManager代替")]
    public class SceneTransitionManager : Script
    {
        #region 单例模式

        private static SceneTransitionManager _instance;
        public static SceneTransitionManager Instance => _instance;

        #endregion

        #region 状态

        private SceneType _currentScene = SceneType.Start;
        private SceneType _previousScene = SceneType.Start;

        #endregion

        #region 事件

        /// <summary>场景切换开始事件</summary>
        public event Action<SceneType, SceneType> TransitionStarted;

        /// <summary>场景切换完成事件</summary>
        public event Action<SceneType, SceneType> TransitionCompleted;

        /// <summary>场景切换失败事件</summary>
        public event Action<SceneType, SceneType, string> TransitionFailed;

        #endregion

        #region 生命周期

        public override void OnAwake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }

            _instance = this;
            Actor.SetStaticFlag(StaticFlags.FullyStatic, true);

            Debug.Log("[SceneTransitionManager] 初始化完成（兼容层）");
        }
        
        public override void OnStart()
        {
            // 延迟到 OnStart 中订阅事件，确保 GameSceneManager 已经初始化
            var gameSceneManager = GameSceneManager.Instance ?? GameSceneManager.GetOrCreate();
            if (gameSceneManager != null)
            {
                gameSceneManager.TransitionStarted += OnGameSceneTransitionStarted;
                gameSceneManager.TransitionCompleted += OnGameSceneTransitionCompleted;
                gameSceneManager.TransitionFailed += OnGameSceneTransitionFailed;
                Debug.Log("[SceneTransitionManager] 已订阅GameSceneManager事件");
            }
            else
            {
                Debug.LogWarning("[SceneTransitionManager] GameSceneManager不可用，无法订阅事件");
            }
        }

        public override void OnDestroy()
        {
            var gameSceneManager = GameSceneManager.Instance;
            if (gameSceneManager != null)
            {
                gameSceneManager.TransitionStarted -= OnGameSceneTransitionStarted;
                gameSceneManager.TransitionCompleted -= OnGameSceneTransitionCompleted;
                gameSceneManager.TransitionFailed -= OnGameSceneTransitionFailed;
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>
        /// 获取或创建SceneTransitionManager实例
        /// </summary>
        [Obsolete("请使用GameSceneManager.GetOrCreate()代替")]
        public static SceneTransitionManager GetOrCreateInstance()
        {
            if (_instance != null)
                return _instance;

            var existingActor = Level.FindActor("SceneTransitionManager");
            if (existingActor != null)
            {
                _instance = existingActor.GetScript<SceneTransitionManager>();
                if (_instance != null)
                    return _instance;
            }

            var gameObject = new EmptyActor();
            gameObject.Name = "SceneTransitionManager";
            gameObject.SetStaticFlag(StaticFlags.FullyStatic, true);
            Level.SpawnActor(gameObject);
            _instance = gameObject.AddScript<SceneTransitionManager>();

            Debug.Log("[SceneTransitionManager] 自动创建实例（兼容层）");
            return _instance;
        }

        #endregion

        #region 场景切换

        /// <summary>
        /// 切换到指定场景 - 委托给 GameSceneManager
        /// </summary>
        [Obsolete("请使用GameSceneManager.TransitionTo()代替")]
        public bool TransitionToScene(SceneType targetScene, bool saveCurrentState = false)
        {
            var gameSceneManager = GameSceneManager.GetOrCreate();
            if (gameSceneManager == null)
            {
                Debug.LogError("[SceneTransitionManager] GameSceneManager不可用");
                TransitionFailed?.Invoke(_currentScene, targetScene, "GameSceneManager不可用");
                return false;
            }

            return gameSceneManager.TransitionTo(targetScene);
        }

        #endregion

        #region GameSceneManager事件处理

        private void OnGameSceneTransitionStarted(SceneType from, SceneType to)
        {
            _previousScene = from;
            TransitionStarted?.Invoke(from, to);
        }

        private void OnGameSceneTransitionCompleted(SceneType from, SceneType to)
        {
            _currentScene = to;
            _previousScene = from;
            TransitionCompleted?.Invoke(from, to);
        }

        private void OnGameSceneTransitionFailed(SceneType to, string error)
        {
            TransitionFailed?.Invoke(_currentScene, to, error);
        }

        #endregion

        #region 公共属性

        public SceneType CurrentScene => GameSceneManager.Instance?.CurrentSceneType ?? _currentScene;
        public SceneType PreviousScene => _previousScene;
        public bool IsTransitioning => GameSceneManager.Instance?.IsTransitioning ?? false;

        #endregion
    }
}
