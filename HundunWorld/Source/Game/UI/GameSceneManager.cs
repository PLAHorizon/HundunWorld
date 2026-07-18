using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlaxEngine;
using FlaxEngine.GUI;
using Horizon.Game.Message.Enums;
using HundunWorld.Game.UI.Character;

namespace HundunWorld.Game.UI
{
    /// <summary>
    /// 游戏场景管理器 - 统一管理所有场景切换逻辑
    /// 
    /// 设计原则：
    /// 1. RootScene作为根场景始终保持加载（包含网络、状态管理等核心组件）
    /// 2. 其他场景作为子场景动态加载/卸载（附加加载，不替换RootScene）
    /// 3. 使用过渡场景 + 淡入淡出效果实现平滑切换
    /// 
    /// 切换流程：
    /// 1. 淡出（屏幕变黑）
    /// 2. 加载过渡场景
    /// 3. 淡入（显示过渡场景）
    /// 4. 卸载旧场景 + 加载新场景
    /// 5. 淡出（过渡场景变黑）
    /// 6. 卸载过渡场景
    /// 7. 淡入（显示新场景）
    /// </summary>
    public class GameSceneManager : Script
    {
        #region 单例

        private static GameSceneManager _instance;
        public static GameSceneManager Instance => _instance;

        public static GameSceneManager GetOrCreate()
        {
            if (_instance != null)
                return _instance;

            for (int i = 0; i < Level.ScenesCount; i++)
            {
                var scene = Level.GetScene(i);
                if (scene != null)
                {
                    var scripts = scene.GetScripts<GameSceneManager>();
                    if (scripts != null && scripts.Length > 0)
                    {
                        _instance = scripts[0];
                        Debug.Log($"[GameSceneManager] 从场景 {scene.Name} 找到实例");
                        return _instance;
                    }
                }
            }

            var existingActor = Level.FindActor("GameSceneManager");
            if (existingActor != null)
            {
                _instance = existingActor.GetScript<GameSceneManager>();
                if (_instance != null)
                    return _instance;
            }

            Debug.Log("[GameSceneManager] 未找到现有实例，创建新实例...");
            var actor = new EmptyActor { Name = "GameSceneManager" };
            actor.SetStaticFlag(StaticFlags.FullyStatic, true);
            Level.SpawnActor(actor);
            _instance = actor.AddScript<GameSceneManager>();
            return _instance;
        }

        #endregion

        #region 场景配置

        public static readonly Dictionary<SceneType, string> ScenePaths = new Dictionary<SceneType, string>
        {
            { SceneType.Start, "Content/Maps/RootScene.scene" },
            { SceneType.Login, "Content/Maps/Login.scene" },
            { SceneType.CharacterSelection, "Content/Maps/Character.scene" },
            { SceneType.CharacterCreation, "Content/Maps/Character.scene" },
            { SceneType.GameWorld, "Content/Maps/World.scene" }
        };

        private const string RootSceneName = "RootScene";
        private static readonly string RootScenePath = "Content/Maps/RootScene.scene";
        private static readonly string TransitionScenePath = "Content/Maps/TransitionScene.scene";
        private const string TransitionSceneName = "TransitionScene";

        #endregion

        #region 配置

        [Serialize]
        public bool EnableTransitionEffect = false;

        #endregion

        #region 状态

        private SceneType _currentSceneType = SceneType.Start;
        private SceneType? _pendingSceneType;
        private SceneType _previousSceneType = SceneType.Start;
        private FlaxEngine.Scene _rootScene;
        private FlaxEngine.Scene _currentSubScene;
        private FlaxEngine.Scene _transitionScene;
        private Guid _transitionSceneId = Guid.Empty;
        private Guid _targetSceneId = Guid.Empty;
        private bool _isTransitioning;
        private string _pendingTargetPath;
        private TransitionState _transitionState = TransitionState.Idle;
        private bool _waitingForTransitionUnload = false;
        private int _staleTransitionRetryCount = 0;
        private float _staleTransitionCheckTimer = 0f;
        private float _unloadTimeoutTimer = 0f;
        private const float UnloadTimeout = 5.0f;

        private float _currentLoadingProgress = 0f;
        private float _targetLoadingProgress = 0f;
        private float _loadingTimer = 0f;
        private const float MinLoadingDuration = 1.5f;
        private const float MaxLoadingDuration = 20.0f;
        private float _waitTimerAfterLoaded = 0f;
        private float _diagLogTimer = 0f;
        private const float MinWaitTime = 1.0f;
        private HundunWorld.Game.UI.Components.RoundedProgressBar _transitionProgressBar;
        private string _pendingTargetSceneName; // 记录目标场景的文件名（不含扩展名）

        /// <summary>
        /// 过渡状态机
        /// </summary>
        private enum TransitionState
        {
            Idle,                    // 空闲
            FadingOutToTransition,   // 淡出到过渡场景
            LoadingTransitionScene,  // 加载过渡场景
            FadingInTransition,      // 淡入显示过渡场景
            UnloadingOldScene,       // 卸载旧场景
            LoadingTargetScene,      // 加载目标场景
            FadingOutFromTransition, // 淡出过渡场景
            UnloadingTransition,     // 卸载过渡场景
            FadingInTarget,          // 淡入显示目标场景
            Completing               // 完成中
        }

        public SceneType CurrentSceneType => _currentSceneType;
        public bool IsTransitioning => _isTransitioning;

        #endregion

        #region 事件

        public event Action<SceneType, SceneType> TransitionStarted;
        public event Action<SceneType, SceneType> TransitionCompleted;
        public event Action<SceneType, string> TransitionFailed;
        public event Action<float> LoadProgress;

        #endregion

        #region 生命周期

        public override void OnStart()
        {
            base.OnStart();
            LoadProgress += GameSceneManager_LoadProgress;
        }

        /// <summary>
        /// 更新场景加载进度
        /// </summary>
        /// <param name="obj"></param>
        private void GameSceneManager_LoadProgress(float obj)
        {
            // 更新 UI
            if (_transitionProgressBar != null)
            {
                // ProgressBar.Value 范围是 0-1，不是 0-100
                _transitionProgressBar.Value = MathF.Max(_transitionProgressBar.Value, _currentLoadingProgress);
            }
        }

        public override void OnUpdate()
        {
            UpdateLoadingProgress();
            UpdateTransitionUnloadTimeout();
            UpdateStaleTransitionCleanup();
            
            // 核心修复：TransitionScene 清理看门狗
            ForceUnloadTransitionSceneIfStale();
            
            // 核心修复：超时强制完成切换
            // 如果在 LoadingTargetScene 状态超过 3 秒，直接强制完成
            if (_isTransitioning && _transitionState == TransitionState.LoadingTargetScene)
            {
                _forceCompleteTimer += Time.DeltaTime;
                if (_forceCompleteTimer >= 3.0f)
                {
                    Debug.LogWarning($"[GameSceneManager] 加载超时3秒，强制完成切换");
                    LogSceneList("强制完成前");
                    
                    // 确保目标场景已加载
                    if (_currentSubScene == null)
                    {
                        for (int i = 0; i < Level.ScenesCount; i++)
                        {
                            var scene = Level.GetScene(i);
                            if (scene != null && scene != _rootScene && !IsTransitionScene(scene))
                            {
                                _currentSubScene = scene;
                                Debug.Log($"[GameSceneManager] 超时强制: 找到目标场景 {scene.Name}");
                                break;
                            }
                        }
                    }
                    
                    // 直接完成切换，跳过所有中间步骤
                    CompleteTransition();
                    _forceCompleteTimer = 0f;
                }
            }
            else
            {
                _forceCompleteTimer = 0f;
            }
        }
        
        private float _forceCompleteTimer = 0f;

        /// <summary>
        /// 看门狗：无条件清理残留的 TransitionScene
        /// 不依赖任何状态机逻辑，只要切换完成就检查
        /// </summary>
        private void ForceUnloadTransitionSceneIfStale()
        {
            // 只在非切换状态时检查
            if (_isTransitioning) return;
            if (_transitionState != TransitionState.Idle && _transitionState != TransitionState.Completing) return;

            for (int i = Level.ScenesCount - 1; i >= 0; i--)
            {
                var scene = Level.GetScene(i);
                if (scene != null && IsTransitionScene(scene))
                {
                    Debug.LogWarning($"[GameSceneManager] 看门狗: 发现残留过渡场景 {scene.Name}，强制卸载");
                    DestroyAllSceneActors(scene);
                    SafeUnloadScene(scene);
                    _transitionScene = null;
                }
            }
        }

        /// <summary>
        /// 处理过渡场景残留清理：等待销毁操作完成后再次检查并卸载
        /// </summary>
        private void UpdateStaleTransitionCleanup()
        {
            if (_staleTransitionCheckTimer <= 0f) return;

            _staleTransitionCheckTimer -= Time.DeltaTime;
            if (_staleTransitionCheckTimer > 0f) return;

            // 等待了 1 帧，再次检查
            for (int i = Level.ScenesCount - 1; i >= 0; i--)
            {
                var scene = Level.GetScene(i);
                if (scene != null && IsTransitionScene(scene))
                {
                    _staleTransitionRetryCount++;
                    if (_staleTransitionRetryCount <= 5)
                    {
                        Debug.LogWarning($"[GameSceneManager] 过渡场景仍在(重试{_staleTransitionRetryCount}/5)，再次卸载");
                        
                        // 先销毁所有Actor再卸载
                        DestroyAllSceneActors(scene);
                        
                        // 等待一帧后卸载
                        Scripting.InvokeOnUpdate(() =>
                        {
                            for (int j = 0; j < Level.ScenesCount; j++)
                            {
                                var s = Level.GetScene(j);
                                if (s != null && IsTransitionScene(s))
                                {
                                    Debug.Log($"[GameSceneManager] 执行过渡场景卸载(重试): {s.Name}");
                                    SafeUnloadScene(s);
                                    break;
                                }
                            }
                        });
                        
                        _staleTransitionCheckTimer = 2.0f;
                        return;
                    }
                    else
                    {
                        Debug.LogError($"[GameSceneManager] 过渡场景连续卸载5次失败，放弃");
                        break;
                    }
                }
            }
            
            // 场景已不存在，清理完成
            _staleTransitionRetryCount = 0;
            _staleTransitionCheckTimer = 0f;
        }

        /// <summary>
        /// 清理残留的过渡场景：如果切换已完成但过渡场景仍在，强制卸载
        /// </summary>
        private void CleanupStaleTransitionScene()
        {
            // 只在空闲状态检查（说明切换已完成或未开始）
            if (_transitionState != TransitionState.Idle) return;
            if (_isTransitioning) return;

            for (int i = Level.ScenesCount - 1; i >= 0; i--)
            {
                var scene = Level.GetScene(i);
                if (scene != null && IsTransitionScene(scene))
                {
                    // 先销毁过渡场景内的所有Actor
                    var children = scene.Children;
                    if (children != null)
                    {
                        for (int j = children.Length - 1; j >= 0; j--)
                        {
                            var child = children[j];
                            if (child != null && child != null)
                            {
                                Debug.Log($"[GameSceneManager] CleanupStale: 销毁过渡场景Actor {child.Name}");
                                Actor.Destroy(child);
                            }
                        }
                    }
                    
                    Debug.LogWarning($"[GameSceneManager] CleanupStaleTransitionScene 发现残留过渡场景: {scene.Name}，强制卸载");
                    LogSceneList("CleanupStaleTransitionScene发现残留");
                    SafeUnloadScene(scene);
                    
                    // 等待一帧后再次检查，如果还在则重试（最多重试5次）
                    _staleTransitionRetryCount = 0;
                    _staleTransitionCheckTimer = 1.0f;
                    return;
                }
            }
            
            // 没有残留场景
            _staleTransitionRetryCount = 0;
            _staleTransitionCheckTimer = 0f;
        }

        /// <summary>
        /// 过渡场景卸载超时保护：若异步卸载超时未完成，强制进入 Step7
        /// </summary>
        private void UpdateTransitionUnloadTimeout()
        {
            if (!_waitingForTransitionUnload) return;

            _unloadTimeoutTimer += Time.DeltaTime;
            if (_unloadTimeoutTimer >= UnloadTimeout)
            {
                Debug.LogWarning($"[GameSceneManager] 过渡场景卸载超时 ({UnloadTimeout}s)，强制进入 Step7");
                _waitingForTransitionUnload = false;
                _unloadTimeoutTimer = 0f;

                // 最后尝试确保过渡场景被卸载
                for (int i = 0; i < Level.ScenesCount; i++)
                {
                    var scene = Level.GetScene(i);
                    if (scene != null && IsTransitionScene(scene))
                    {
                        Debug.LogWarning($"[GameSceneManager] 超时后强制卸载过渡场景: {scene.Name}");
                        SafeUnloadScene(scene);
                        break;
                    }
                }

                Step7_FadeInTarget();
            }
        }

        private void UpdateLoadingProgress()
        {
            // 在目标场景加载、旧场景卸载或过渡场景淡入期间都更新进度
            if (_transitionState != TransitionState.LoadingTargetScene && 
                _transitionState != TransitionState.UnloadingOldScene &&
                _transitionState != TransitionState.FadingInTransition)
            {
                // 重置进度条引用和计时器
                if (_transitionState == TransitionState.Idle)
                {
                    _transitionProgressBar = null;
                    _loadingTimer = 0f;
                }
                return;
            }

            // 如果在 UnloadingOldScene 状态但目标场景已加载完成，推进到 LoadingTargetScene
            if (_transitionState == TransitionState.UnloadingOldScene && _currentSubScene != null && !IsTransitionScene(_currentSubScene))
            {
                Debug.Log("[GameSceneManager] 旧场景卸载期间目标场景已加载，推进到 LoadingTargetScene");
                _transitionState = TransitionState.LoadingTargetScene;
                _loadingTimer = 0f;
            }

            // 查找进度条
            if (_transitionProgressBar == null)
            {
                FindTransitionProgressBar();
            }

            _loadingTimer += Time.DeltaTime;

            // 1. 计算基于时间的进度
            // 正常情况下至少需要 MinLoadingDuration 秒跑完
            float timeProgress = _loadingTimer / MinLoadingDuration;

            // 2. 主动轮询查找目标场景（不依赖 OnSceneLoaded 事件）
            if (_transitionState == TransitionState.LoadingTargetScene && _currentSubScene == null)
            {
                for (int i = 0; i < Level.ScenesCount; i++)
                {
                    var scene = Level.GetScene(i);
                    if (scene != null && scene != _rootScene && !IsTransitionScene(scene))
                    {
                        _currentSubScene = scene;
                        Debug.Log($"[GameSceneManager] 轮询发现目标场景: {scene.Name}");
                        break;
                    }
                }
            }

            // 3. 模拟/同步加载状态
            if (_transitionState == TransitionState.LoadingTargetScene && _currentSubScene != null)
            {
                // 已加载完成
                // 目标是 1.0，但我们希望进度条至少在 MinLoadingDuration 内是“读条”状态
                // 所以我们让 target 跟着 timeProgress 走，直到 1.0
                _targetLoadingProgress = Mathf.Min(timeProgress, 1.0f);
            }
            else
            {
                // 仍在加载中（卸载旧场景、淡入过渡场景、或加载新场景中）
                // 进度条平滑增长，受到 MinLoadingDuration 限制，且在未完成前最高到 95%
                if (timeProgress < 0.95f)
                {
                    _targetLoadingProgress = timeProgress;
                }
                else
                {
                    // 超过了最短时间还没完成，慢慢向 99% 靠拢，直到 MaxLoadingDuration
                    float extraTimeProgress = (_loadingTimer - MinLoadingDuration) / (MaxLoadingDuration - MinLoadingDuration);
                    _targetLoadingProgress = Mathf.Lerp(0.95f, 0.99f, extraTimeProgress);
                }
            }

            // 平滑过渡当前显示的进度值
            _currentLoadingProgress = Mathf.MoveTowards(_currentLoadingProgress, _targetLoadingProgress, Time.DeltaTime * 2.0f);

            // 4. 检查是否完成
            // 条件：进度条到1.0 + 目标场景已加载 + 满足最短时长
            bool targetSceneLoaded = _currentSubScene != null;

            // 周期性诊断日志（每秒1次）
            if (_transitionState == TransitionState.LoadingTargetScene && _loadingTimer > 0)
            {
                _diagLogTimer += Time.DeltaTime;
                if (_diagLogTimer >= 1.0f)
                {
                    _diagLogTimer = 0f;
                    Debug.Log($"[GameSceneManager] 加载进度诊断: progress={_currentLoadingProgress:F2}, " +
                        $"targetProgress={_targetLoadingProgress:F2}, timer={_loadingTimer:F2}s/{MinLoadingDuration}s, " +
                        $"targetLoaded={targetSceneLoaded}, subScene={_currentSubScene?.Name ?? "null"}, " +
                        $"waitAfterLoaded={_waitTimerAfterLoaded:F2}s/{MinWaitTime}s");
                }
            }

            if (_currentLoadingProgress >= 1.0f && _transitionState == TransitionState.LoadingTargetScene && targetSceneLoaded && _loadingTimer >= MinLoadingDuration)
            {
                _currentLoadingProgress = 1.0f;
                _waitTimerAfterLoaded += Time.DeltaTime;

                if (_waitTimerAfterLoaded >= MinWaitTime)
                {
                    Debug.Log($"[GameSceneManager] 加载时长: {_loadingTimer:F2}s, 满足最短时长 {MinLoadingDuration}s, 开始淡出");
                    _waitTimerAfterLoaded = 0f;
                    _loadingTimer = 0f;
                    _diagLogTimer = 0f;
                    Step5_FadeOutFromTransition();
                }
            }

          

            LoadProgress?.Invoke(_currentLoadingProgress);
        }

        /// <summary>
        /// 递归查找进度条控件
        /// </summary>
        private bool FindProgressBarRecursive(ContainerControl parent, ref ProgressBar foundBar)
        {
            foreach (var child in parent.Children)
            {
                if (child is ProgressBar bar)
                {
                    foundBar = bar;
                    return true;
                }
                // 如果子控件也是 ContainerControl，递归查找
                if (child is ContainerControl container)
                {
                    if (FindProgressBarRecursive(container, ref foundBar))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void FindTransitionProgressBar()
        {
            if (_transitionScene == null) return;

            // 1. 确保过渡场景的所有 Canvas 都在高层级（999），仅低于淡入淡出遮罩（1000）
            var canvases = _transitionScene.GetChildren<UICanvas>();
           
            // 2. 递归查找进度条控件
            foreach (var uiControl in canvases)
            {
                uiControl.Order = 999;
                
                ProgressBar foundBar = null;
                if (FindProgressBarRecursive(uiControl.GUI, ref foundBar) && foundBar != null)
                {
                    // 替换为圆角进度条
                    var roundedBar = new HundunWorld.Game.UI.Components.RoundedProgressBar
                    {
                        Bounds = foundBar.Bounds,
                        AnchorPreset = foundBar.AnchorPreset,
                        Offsets = foundBar.Offsets,
                        BackgroundColor = foundBar.BackgroundColor,
                        BarColor = foundBar.BarColor,
                        Value = foundBar.Value,
                        SmoothingScale = foundBar.SmoothingScale,
                        CornerRadius = 10f,
                        BarMargin = foundBar.BarMargin
                    };
                    var parent = foundBar.Parent;
                    var index = foundBar.IndexInParent;
                    foundBar.Parent = null;
                    parent.AddChild(roundedBar);
                    roundedBar.IndexInParent = index;
                    
                    _transitionProgressBar = roundedBar;
                    Debug.Log("[GameSceneManager] 从 Canvas GUI 中找到过渡场景进度条并应用圆角样式");
                    return;
                }
            }
            
            Debug.LogWarning("[GameSceneManager] 未找到过渡场景进度条");
        }

        public override void OnAwake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }

            _instance = this;
            Actor.SetStaticFlag(StaticFlags.FullyStatic, true);

            Level.SceneLoaded += OnSceneLoaded;
            Level.SceneUnloading += OnSceneUnloading;
            Level.SceneUnloaded += OnSceneUnloaded;

            FindRootScene();
            Debug.Log("[GameSceneManager] 初始化完成");
        }

        public override void OnDestroy()
        {
            Level.SceneLoaded -= OnSceneLoaded;
            Level.SceneUnloading -= OnSceneUnloading;
            Level.SceneUnloaded -= OnSceneUnloaded;

            if (_instance == this)
                _instance = null;
        }

        private void FindRootScene()
        {
            _rootScene = Actor?.Scene;
            if (_rootScene != null)
            {
                Debug.Log($"[GameSceneManager] 找到RootScene(脚本所在场景): {_rootScene.Name}");
                return;
            }
            
            Debug.LogError("[GameSceneManager] 无法找到RootScene！GameSceneManager 必须挂载在 RootScene 上");
        }

        #endregion

        #region 场景切换

        public bool TransitionTo(SceneType targetScene)
        {
            Debug.Log($"[GameSceneManager] TransitionTo: {_currentSceneType} -> {targetScene}");

            if (_isTransitioning)
            {
                Debug.LogWarning($"[GameSceneManager] 场景正在切换中，忽略请求: {targetScene}");
                return false;
            }

            if (_currentSceneType == targetScene)
            {
                Debug.Log($"[GameSceneManager] 已在目标场景: {targetScene}");
                return true;
            }

            if (!ScenePaths.TryGetValue(targetScene, out var targetPath))
            {
                Debug.LogError($"[GameSceneManager] 未找到场景配置: {targetScene}");
                TransitionFailed?.Invoke(targetScene, "未找到场景配置");
                return false;
            }

            // 确保 _currentSubScene 被正确追踪（可能在 GameSceneManager 初始化前就加载了）
            if (_currentSubScene == null)
            {
                for (int i = 0; i < Level.ScenesCount; i++)
                {
                    var scene = Level.GetScene(i);
                    if (scene != null && scene != _rootScene && !IsTransitionScene(scene))
                    {
                        _currentSubScene = scene;
                        Debug.Log($"[GameSceneManager] 追踪到当前子场景: {scene.Name}");
                        break;
                    }
                }
            }

            _isTransitioning = true;
            _pendingSceneType = targetScene;
            _previousSceneType = _currentSceneType;
            _pendingTargetPath = targetPath;

            TransitionStarted?.Invoke(_previousSceneType, targetScene);

            // 开始切换流程
            StartTransitionSequence();
            return true;
        }

        /// <summary>
        /// 开始切换序列
        /// </summary>
        private void StartTransitionSequence()
        {
            Debug.Log("[GameSceneManager] === 开始场景切换序列 ===");

            if (EnableTransitionEffect)
            {
                // 步骤1: 淡出（屏幕变黑）
                _transitionState = TransitionState.FadingOutToTransition;
                Debug.Log("[GameSceneManager] 步骤1: 淡出屏幕");

                var effect = SceneTransitionEffect.GetOrCreate();
                if (effect != null)
                {
                    effect.StartFadeOut(() =>
                    {
                        // 步骤2: 加载过渡场景
                        Step2_LoadTransitionScene();
                    });
                }
                else
                {
                    Step2_LoadTransitionScene();
                }
            }
            else
            {
                // 无过渡效果，直接加载目标场景
                DirectLoadTargetScene();
            }
        }

        /// <summary>
        /// 步骤2: 加载过渡场景
        /// </summary>
        private void Step2_LoadTransitionScene()
        {
            _transitionState = TransitionState.LoadingTransitionScene;
            Debug.Log("[GameSceneManager] 步骤2: 加载过渡场景");

            // 检查过渡场景是否已加载
            for (int i = 0; i < Level.ScenesCount; i++)
            {
                var existingScene = Level.GetScene(i);
                if (existingScene != null && IsTransitionScene(existingScene))
                {
                    _transitionScene = existingScene;
                    Debug.Log($"[GameSceneManager] 过渡场景已存在: {existingScene.Name}");
                    Step3_FadeInTransition();
                    return;
                }
            }

            // 使用 GetAssetInfo 获取 ID，避免同步加载 Asset 导致的阻塞
            if (Content.GetAssetInfo(TransitionScenePath, out var assetInfo))
            {
                _transitionSceneId = assetInfo.ID;
                Debug.Log($"[GameSceneManager] 开始异步加载过渡场景, ID: {_transitionSceneId}");
                Level.LoadSceneAsync(_transitionSceneId);
            }
            else
            {
                Debug.LogError($"[GameSceneManager] 无法找到过渡场景资产: {TransitionScenePath}");
                DirectLoadTargetScene();
            }

            // 等待 OnSceneLoaded 事件触发 Step3
        }

        /// <summary>
        /// 检查场景是否是过渡场景
        /// </summary>
        private bool IsTransitionScene(FlaxEngine.Scene scene)
        {
            if (scene == null) return false;

            // 检查名称匹配
            if (scene.Name == TransitionSceneName) return true;
            if (scene.Name == "TransitionScene") return true;

            return false;
        }

        /// <summary>
        /// 步骤3: 淡入显示过渡场景
        /// </summary>
        private void Step3_FadeInTransition()
        {
            _transitionState = TransitionState.FadingInTransition;
            Debug.Log("[GameSceneManager] 步骤3: 淡入过渡场景");

            var effect = SceneTransitionEffect.Instance;
            if (effect != null)
            {
                effect.StartFadeIn(() =>
                {
                    Step4_UnloadOldAndLoadNew();
                });
            }
            else
            {
                Step4_UnloadOldAndLoadNew();
            }
        }

        /// <summary>
        /// 步骤4: 卸载旧场景，加载新场景
        /// </summary>
        private void Step4_UnloadOldAndLoadNew()
        {
            _transitionState = TransitionState.UnloadingOldScene;
            Debug.Log("[GameSceneManager] 步骤4: 卸载旧场景，加载新场景");

            LoadProgress?.Invoke(0.3f);

            // 卸载当前子场景（绝不能卸载RootScene）
            if (_currentSubScene != null && _currentSubScene != _rootScene && _currentSubScene != _transitionScene)
            {
                Debug.Log($"[GameSceneManager] 卸载旧场景: {_currentSubScene.Name}");
                var sceneToUnload = _currentSubScene;
                _currentSubScene = null;
                SafeUnloadScene(sceneToUnload);
            }
            else if (_currentSubScene == _rootScene)
            {
                Debug.LogError($"[GameSceneManager] 严重错误：_currentSubScene 指向RootScene {_rootScene?.Name}，跳过卸载！");
                _currentSubScene = null;
            }
            else
            {
                Debug.Log($"[GameSceneManager] 无旧场景需要卸载 (_currentSubScene={_currentSubScene?.Name ?? "null"})");
            }

            // 延迟一帧后加载新场景
            Scripting.InvokeOnUpdate(() =>
            {
                _transitionState = TransitionState.LoadingTargetScene;
                _loadingTimer = 0f; // 重置加载计时器，从目标场景加载开始计时
                _currentLoadingProgress = 0f;
                _targetLoadingProgress = 0f;
                _waitTimerAfterLoaded = 0f;
                Debug.Log($"[GameSceneManager] Step4延迟帧: 状态已切换为LoadingTargetScene, 计时器已重置, _currentSubScene={_currentSubScene?.Name ?? "null"}");
                LoadProgress?.Invoke(0.5f);

                // 检查目标是否是RootScene
                if (_pendingTargetPath == RootScenePath)
                {
                    // 目标是RootScene，不需要加载，但需要展示进度条过程
                    _currentSubScene = _rootScene;
                    _currentLoadingProgress = 0f;
                    _targetLoadingProgress = 0f;
                    _loadingTimer = 0f;
                    _waitTimerAfterLoaded = 0f;
                    Debug.Log("[GameSceneManager] 目标是RootScene，直接进入进度条最短时间流程");
                }
                else
                {
                    // 加载目标场景
                    LoadTargetScene(_pendingTargetPath);
                }
            });
        }

        /// <summary>
        /// 加载目标场景
        /// </summary>
        private void LoadTargetScene(string scenePath)
        {
            Debug.Log($"[GameSceneManager] 加载目标场景: {scenePath}");

            if (string.IsNullOrEmpty(scenePath))
            {
                Debug.LogError("[GameSceneManager] 场景路径为空！");
                HandleTransitionError("场景路径为空");
                return;
            }

            // 记录目标场景的文件名（不含扩展名）
            _pendingTargetSceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);

            // 检查场景是否已加载
            for (int i = 0; i < Level.ScenesCount; i++)
            {
                var existingScene = Level.GetScene(i);
                if (existingScene != null && existingScene.Name == _pendingTargetSceneName)
                {
                    Debug.Log($"[GameSceneManager] 目标场景已存在: {existingScene.Name}");
                    _currentSubScene = existingScene;
                    _targetSceneId = Guid.Empty;
                    Step5_FadeOutFromTransition();
                    return;
                }
            }

            // 使用 GetAssetInfo 获取 ID，避免同步加载 Asset 导致的阻塞
            if (Content.GetAssetInfo(scenePath, out var assetInfo))
            {
                _targetSceneId = assetInfo.ID;
                Debug.Log($"[GameSceneManager] 开始异步加载目标场景, ID: {_targetSceneId}, 文件名: {_pendingTargetSceneName}");
                Level.LoadSceneAsync(_targetSceneId);
            }
            else
            {
                Debug.LogError($"[GameSceneManager] 无法找到目标场景资产: {scenePath}");
                HandleTransitionError("无法找到目标场景");
                return;
            }

            // 重置进度状态
            _currentLoadingProgress = 0f;
            _targetLoadingProgress = 0f;
            _loadingTimer = 0f;
            _waitTimerAfterLoaded = 0f;

            // 等待 UpdateLoadingProgress 处理进度和跳转
        }

        /// <summary>
        /// 步骤5: 淡出过渡场景
        /// </summary>
        private void Step5_FadeOutFromTransition()
        {
            _transitionState = TransitionState.FadingOutFromTransition;
            Debug.Log("[GameSceneManager] 步骤5: 淡出过渡场景");
            LogSceneList("Step5开始前");

            LoadProgress?.Invoke(0.8f);

            var effect = SceneTransitionEffect.Instance;
            if (effect != null)
            {
                Debug.Log("[GameSceneManager] Step5: 开始淡出，完成后将调用 Step6");
                effect.StartFadeOut(() =>
                {
                    Debug.Log("[GameSceneManager] Step5淡出完成，调用 Step6");
                    Step6_UnloadTransition();
                });
            }
            else
            {
                Debug.LogWarning("[GameSceneManager] Step5: SceneTransitionEffect 实例为null，直接调用 Step6");
                Step6_UnloadTransition();
            }
        }

        /// <summary>
        /// 步骤6: 卸载过渡场景
        /// </summary>
        private void Step6_UnloadTransition()
        {
            _transitionState = TransitionState.UnloadingTransition;
            Debug.Log("[GameSceneManager] 步骤6: 卸载过渡场景");
            LogSceneList("Step6开始前");

            // 卸载前重置进度条
            if (_transitionProgressBar != null)
            {
                _transitionProgressBar.Value = 0f;
                _transitionProgressBar = null;
            }
            _currentLoadingProgress = 0f;
            _targetLoadingProgress = 0f;
            _loadingTimer = 0f;

            // 如果引用为空，尝试从场景列表中查找
            if (_transitionScene == null)
            {
                for (int i = 0; i < Level.ScenesCount; i++)
                {
                    var scene = Level.GetScene(i);
                    if (scene != null && IsTransitionScene(scene))
                    {
                        _transitionScene = scene;
                        Debug.Log($"[GameSceneManager] 从场景列表中找到过渡场景: {scene.Name}");
                        break;
                    }
                }
            }

            if (_transitionScene != null)
            {
                Debug.Log($"[GameSceneManager] 卸载过渡场景: {_transitionScene.Name}");
                var sceneToUnload = _transitionScene;
                _transitionScene = null;

                // 先销毁过渡场景内的动态Actor（如PreviewCamera、UICharacter等）
                // 这些Actor可能是旧代码在构造时错误生成的，会阻止场景卸载
                DestroyTransitionSceneActors(sceneToUnload);

                // 设置等待标志，由 OnSceneUnloaded 回调触发 Step7
                _waitingForTransitionUnload = true;
                _unloadTimeoutTimer = 0f;
                SafeUnloadScene(sceneToUnload);
            }
            else
            {
                Debug.LogWarning("[GameSceneManager] 过渡场景引用为空，跳过卸载");
                Step7_FadeInTarget();
            }
        }

        /// <summary>
        /// 步骤7: 淡入显示目标场景
        /// </summary>
        private void Step7_FadeInTarget()
        {
            _transitionState = TransitionState.FadingInTarget;
            Debug.Log("[GameSceneManager] 步骤7: 淡入目标场景");

            LoadProgress?.Invoke(1.0f);

            var effect = SceneTransitionEffect.Instance;
            if (effect != null)
            {
                effect.StartFadeIn(() =>
                {
                    CompleteTransition();
                });
            }
            else
            {
                CompleteTransition();
            }
        }

        /// <summary>
        /// 完成切换
        /// </summary>
        private void CompleteTransition()
        {
            _transitionState = TransitionState.Completing;
            Debug.Log("[GameSceneManager] === 场景切换完成 ===");

            // 强制清理残留的过渡场景（不依赖 OnSceneUnloaded 回调）
            ForceCleanupTransitionScene();

            if (_transitionProgressBar != null)
            {
                _transitionProgressBar.Value = 0f;
                _transitionProgressBar = null;
            }
            _currentLoadingProgress = 0f;
            _targetLoadingProgress = 0f;
            _loadingTimer = 0f;
            _waitTimerAfterLoaded = 0f;

            var previousScene = _previousSceneType;
            var targetScene = _pendingSceneType ?? SceneType.Start;

            _currentSceneType = targetScene;
            _isTransitioning = false;
            _pendingSceneType = null;
            _pendingTargetPath = null;
            _transitionSceneId = Guid.Empty;
            _targetSceneId = Guid.Empty;
            _transitionState = TransitionState.Idle;
            _waitingForTransitionUnload = false;
            _unloadTimeoutTimer = 0f;

            // 同步 UIStateManager 状态，确保 UIStateManager 的 CurrentScene 与 GameSceneManager 一致
            var stateManager = UIStateManager.Instance;
            if (stateManager != null)
            {
                stateManager.TransitionToScene(targetScene, false);
            }

            // 输出当前场景列表用于诊断
            LogSceneList("切换完成后");

            Debug.Log($"[GameSceneManager] 切换完成: {previousScene} -> {targetScene}");
            TransitionCompleted?.Invoke(previousScene, targetScene);
        }

        /// <summary>
        /// 强制清理残留的过渡场景
        /// 先销毁过渡场景内所有Actor，再卸载场景本身
        /// </summary>
        private void ForceCleanupTransitionScene()
        {
            _waitingForTransitionUnload = false;
            _unloadTimeoutTimer = 0f;

            for (int i = Level.ScenesCount - 1; i >= 0; i--)
            {
                var scene = Level.GetScene(i);
                if (scene != null && IsTransitionScene(scene))
                {
                    Debug.LogWarning($"[GameSceneManager] ForceCleanup: 发现残留过渡场景 {scene.Name}，立即清理");
                    LogSceneList("ForceCleanup前");
                    
                    // 销毁场景内所有子Actor
                    DestroyAllSceneActors(scene);
                    
                    // 立即卸载场景（不延迟）
                    Debug.Log($"[GameSceneManager] ForceCleanup: 卸载过渡场景 {scene.Name}");
                    SafeUnloadScene(scene);
                    _transitionScene = null;
                }
            }
        }

        /// <summary>
        /// 销毁场景内的所有Actor（递归）
        /// </summary>
        private void DestroyAllSceneActors(FlaxEngine.Scene scene)
        {
            if (scene == null) return;
            
            var children = scene.Children;
            if (children == null || children.Length == 0) return;
            
            for (int j = children.Length - 1; j >= 0; j--)
            {
                var child = children[j];
                if (child != null)
                {
                    Debug.Log($"[GameSceneManager] 销毁过渡场景Actor: {child.Name} ({child.TypeName})");
                    Actor.Destroy(child);
                }
            }
        }

        /// <summary>
        /// 销毁过渡场景内的动态Actor（PreviewCamera、UICharacter等）
        /// 这些Actor是UI预览组件错误生成的，会阻止场景正常卸载
        /// </summary>
        private void DestroyTransitionSceneActors(FlaxEngine.Scene scene)
        {
            if (scene == null) return;
            
            string[] dynamicActorNames = { "PreviewCamera", "UICharacterCameraRoot", "UICharacter", "StaticModelPreview", "AnimatedModelPreview" };
            
            var children = scene.Children;
            if (children == null) return;
            
            for (int i = children.Length - 1; i >= 0; i--)
            {
                var child = children[i];
                if (child == null) continue;
                
                bool isDynamic = false;
                foreach (var name in dynamicActorNames)
                {
                    if (child.Name == name)
                    {
                        isDynamic = true;
                        break;
                    }
                }
                
                if (isDynamic)
                {
                    Debug.Log($"[GameSceneManager] 销毁过渡场景内动态Actor: {child.Name}");
                    Actor.Destroy(child);
                }
            }
        }

        /// <summary>
        /// 输出当前场景列表用于诊断
        /// </summary>
        private void LogSceneList(string context)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"[GameSceneManager] 场景列表({context}): [");
            for (int i = 0; i < Level.ScenesCount; i++)
            {
                if (i > 0) sb.Append(", ");
                var scene = Level.GetScene(i);
                sb.Append(scene?.Name ?? "null");
            }
            sb.Append("]");
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// 修复：安全的场景卸载方法。统一防御 RootScene 被误卸载。
        /// 所有需要卸载场景的地方都应调用此方法，而不是直接调用 Level.UnloadSceneAsync。
        /// </summary>
        private void SafeUnloadScene(FlaxEngine.Scene scene)
        {
            if (scene == null)
            {
                Debug.LogWarning("[GameSceneManager] SafeUnloadScene: 场景为 null，跳过卸载");
                return;
            }

            if (scene == _rootScene)
            {
                Debug.LogError($"[GameSceneManager] SafeUnloadScene: 拒绝卸载 RootScene！scene.Name={scene.Name}, 调用栈:\n{System.Environment.StackTrace}");
                return;
            }

            Debug.Log($"[GameSceneManager] SafeUnloadScene: 卸载场景 {scene.Name}");
            Level.UnloadSceneAsync(scene);
        }

        /// <summary>
        /// 直接加载目标场景（无过渡效果）
        /// </summary>
        private void DirectLoadTargetScene()
        {
            Debug.Log("[GameSceneManager] 直接加载目标场景（无过渡效果）");

            // 卸载当前子场景
            if (_currentSubScene != null && _currentSubScene != _rootScene)
            {
                var sceneToUnload = _currentSubScene;
                _currentSubScene = null;
                SafeUnloadScene(sceneToUnload);
            }

            if (_pendingTargetPath == RootScenePath)
            {
                CompleteTransition();
            }
            else
            {
                var sceneAsset = HundunWorld.Game.HundunWorldGame.LoadContentWithFallback<SceneAsset>(_pendingTargetPath);
                if (sceneAsset != null)
                {
                    _targetSceneId = sceneAsset.ID;
                    _transitionState = TransitionState.LoadingTargetScene;
                    Debug.Log($"[GameSceneManager] 直接模式: 开始异步加载目标场景, ID: {_targetSceneId}");
                   // Level.LoadSceneAsync(_targetSceneId);
                    Task.Factory.StartNew(async () => Level.LoadSceneAsync(_targetSceneId));
                    // 等待 OnSceneLoaded 触发 CompleteTransition
                }
                else
                {
                    HandleTransitionError($"无法加载目标场景: {_pendingTargetPath}");
                }
            }
        }

        /// <summary>
        /// 处理切换错误
        /// </summary>
        private void HandleTransitionError(string message)
        {
            Debug.LogError($"[GameSceneManager] 切换错误: {message}");

            // 清理过渡场景
            if (_transitionScene != null)
            {
                SafeUnloadScene(_transitionScene);
                _transitionScene = null;
            }

            _isTransitioning = false;
            _transitionState = TransitionState.Idle;
            _pendingSceneType = null;
            _waitingForTransitionUnload = false;
            _unloadTimeoutTimer = 0f;

            if (_transitionProgressBar != null)
            {
                _transitionProgressBar.Value = 0f;
                _transitionProgressBar = null;
            }
            _currentLoadingProgress = 0f;
            _targetLoadingProgress = 0f;
            _loadingTimer = 0f;
            _waitTimerAfterLoaded = 0f;

            SceneTransitionEffect.Instance?.HideImmediate();
            TransitionFailed?.Invoke(_pendingSceneType ?? SceneType.Start, message);
        }

        #endregion

        #region 场景事件处理

        private void OnSceneLoaded(FlaxEngine.Scene scene, Guid sceneId)
        {
            Debug.Log($"[GameSceneManager] 场景已加载: {scene?.Name ?? "null"} ({sceneId}), 当前状态: {_transitionState}");
            LogSceneList($"OnSceneLoaded后:{scene?.Name}");

            // 清理多余的 AudioListener，确保全局只有一个
            CleanupDuplicateAudioListeners();

            if (sceneId == _transitionSceneId || (scene != null && IsTransitionScene(scene)))
            {
                _transitionScene = scene;
                _transitionSceneId = Guid.Empty; // 重置
                Debug.Log($"[GameSceneManager] 过渡场景加载完成: {scene?.Name ?? "Scene"}");

                if (_transitionState == TransitionState.LoadingTransitionScene)
                {
                    Scripting.InvokeOnUpdate(Step3_FadeInTransition);
                }
                return;
            }

            // 关键修复：使用更宽松的条件匹配目标场景
            // 只要不是RootScene且不是过渡场景，就认为是目标子场景
            if (scene != null && scene != _rootScene && !IsTransitionScene(scene))
            {
                _currentSubScene = scene;
                Debug.Log($"[GameSceneManager] OnSceneLoaded: _currentSubScene 设为 {scene.Name}, sceneId={sceneId}, _targetSceneId={_targetSceneId}, 当前状态: {_transitionState}");

                if (sceneId == _targetSceneId)
                {
                    _targetSceneId = Guid.Empty;
                    Debug.Log($"[GameSceneManager] 目标场景加载完成 (ID匹配): {scene?.Name ?? "Scene"}");
                }
                else
                {
                    Debug.Log($"[GameSceneManager] 目标场景加载完成 (Name匹配): {scene?.Name ?? "Scene"}");
                }

                InitializeCharacterSceneIfNeeded(scene);

                if (_transitionState == TransitionState.LoadingTargetScene && !EnableTransitionEffect)
                {
                    Scripting.InvokeOnUpdate(CompleteTransition);
                }
                else if (_transitionState == TransitionState.UnloadingOldScene || _transitionState == TransitionState.LoadingTargetScene)
                {
                    // 目标场景在旧场景卸载期间或加载期间就完成了，确保状态正确
                    // UpdateLoadingProgress 会在下一帧检测到 _currentSubScene != null 并推进进度
                    Debug.Log($"[GameSceneManager] 目标场景在状态 {_transitionState} 期间加载完成，等待 UpdateLoadingProgress 推进");
                }
                else if (!_isTransitioning)
                {
                    Debug.Log($"[GameSceneManager] 非切换模式场景加载: {scene?.Name ?? "Scene"}");
                }
                return;
            }

            if (scene == _rootScene)
            {
                _rootScene = scene;
                Debug.Log("[GameSceneManager] RootScene已加载");
                return;
            }
        }

        private void OnSceneUnloading(FlaxEngine.Scene scene, Guid sceneId)
        {
            if (scene == _rootScene)
            {
                // 修复：记录调用栈，帮助定位谁触发了 RootScene 卸载
                Debug.LogError($"[GameSceneManager] 严重错误：尝试卸载 RootScene！RootScene 在整个游戏生命周期中绝不能被卸载。调用栈:\n{System.Environment.StackTrace}");
            }
        }

        private void OnSceneUnloaded(FlaxEngine.Scene scene, Guid sceneId)
        {
            // 修复：RootScene 检查提前，确保重新加载逻辑一定被执行，避免被前面的 return 或异常跳过
            if (scene == _rootScene)
            {
                Debug.LogError("[GameSceneManager] 严重错误：RootScene 被卸载！尝试重新加载...");
                _rootScene = null;
                var rootSceneAsset = HundunWorld.Game.HundunWorldGame.LoadContentWithFallback<SceneAsset>(RootScenePath);
                if (rootSceneAsset != null)
                {
                    Debug.Log($"[GameSceneManager] 重新加载 RootScene, ID: {rootSceneAsset.ID}");
                    Task.Factory.StartNew(async()=> Level.LoadSceneAsync(rootSceneAsset.ID));
                }
                else
                {
                    Debug.LogError($"[GameSceneManager] 无法重新加载 RootScene: 资源加载失败, Path={RootScenePath}");
                }
                return;
            }

            Debug.Log($"[GameSceneManager] 场景已卸载: {scene?.Name ?? "null"}, 当前状态: {_transitionState}");
            LogSceneList($"OnSceneUnloaded后:{scene?.Name}");

            // 只有正在卸载旧场景时才清空 _currentSubScene
            // 防止旧场景的异步 OnSceneUnloaded 在新场景已加载后延迟触发，
            // 错误地清空已指向新场景的 _currentSubScene
            if (_currentSubScene == scene && _transitionState == TransitionState.UnloadingOldScene)
            {
                _currentSubScene = null;
                Debug.Log($"[GameSceneManager] 旧场景卸载完成，清空 _currentSubScene");
            }
            else if (_currentSubScene == scene && _transitionState != TransitionState.UnloadingOldScene)
            {
                Debug.LogWarning($"[GameSceneManager] 场景卸载事件延迟触发(状态已变为{_transitionState})，忽略对 _currentSubScene 的清空");
            }

            if (_transitionScene == scene)
            {
                _transitionScene = null;
            }

            // 过渡场景卸载完成，触发 Step7
            if (_waitingForTransitionUnload && IsTransitionScene(scene))
            {
                _waitingForTransitionUnload = false;
                _unloadTimeoutTimer = 0f;
                Debug.Log($"[GameSceneManager] 过渡场景卸载完成，进入 Step7");
                Scripting.InvokeOnUpdate(Step7_FadeInTarget);
            }
        }

        #endregion

        #region 辅助方法

        public string GetScenePath(SceneType sceneType)
        {
            return ScenePaths.TryGetValue(sceneType, out var path) ? path : "";
        }

        public bool IsSceneLoaded(SceneType sceneType)
        {
            if (!ScenePaths.TryGetValue(sceneType, out var path))
                return false;

            var sceneName = System.IO.Path.GetFileNameWithoutExtension(path);
            for (int i = 0; i < Level.ScenesCount; i++)
            {
                var scene = Level.GetScene(i);
                if (scene != null && scene.Name == sceneName)
                    return true;
            }

            return false;
        }

        public void Reset()
        {
            _waitingForTransitionUnload = false;
            _unloadTimeoutTimer = 0f;

            if (_transitionScene != null)
            {
                SafeUnloadScene(_transitionScene);
                _transitionScene = null;
            }

            // 确保场景列表中没有残留的过渡场景
            for (int i = Level.ScenesCount - 1; i >= 0; i--)
            {
                var scene = Level.GetScene(i);
                if (scene != null && IsTransitionScene(scene))
                {
                    Debug.Log($"[GameSceneManager] Reset: 卸载残留过渡场景 {scene.Name}");
                    SafeUnloadScene(scene);
                }
            }

            if (_currentSubScene != null && _currentSubScene != _rootScene)
            {
                SafeUnloadScene(_currentSubScene);
                _currentSubScene = null;
            }

            _currentSceneType = SceneType.Start;
            _isTransitioning = false;
            _transitionState = TransitionState.Idle;
            _pendingSceneType = null;
            _pendingTargetPath = null;
            _transitionSceneId = Guid.Empty;
            _targetSceneId = Guid.Empty;

            if (_transitionProgressBar != null)
            {
                _transitionProgressBar.Value = 0f;
                _transitionProgressBar = null;
            }
            _currentLoadingProgress = 0f;
            _targetLoadingProgress = 0f;
            _loadingTimer = 0f;
            _waitTimerAfterLoaded = 0f;

            SceneTransitionEffect.Instance?.HideImmediate();
            Debug.Log("[GameSceneManager] 已重置到初始状态");
        }

        /// <summary>
        /// 初始化角色场景控制器
        /// 注意：SceneController.CreateUIComponent() 也可能创建 CharacterSceneController，
        /// 因此在创建前必须使用 Level.FindScript 全局检查，避免重复创建
        /// </summary>
        private void InitializeCharacterSceneIfNeeded(FlaxEngine.Scene scene)
        {
            if (scene == null) return;

            var sceneName = scene.Name ?? "";
            if (sceneName != "Character") return;

            Debug.Log($"[GameSceneManager] InitializeCharacterSceneIfNeeded: 开始初始化 Character 场景");

            // 确保场景中有完整的 Actor 结构（Camera、LightingRoot、AtmosphereRoot、GroundShadow）
            EnsureSceneActor(scene, "Camera", () =>
            {
                var cam = new Camera { Name = "Camera" };
                cam.LocalPosition = new Vector3(0, 150, -300);
                Level.SpawnActor(cam, scene);
            });

            EnsureSceneActor(scene, "LightingRoot", () =>
            {
                var root = new EmptyActor { Name = "LightingRoot" };
                Level.SpawnActor(root, scene);
            });

            EnsureSceneActor(scene, "AtmosphereRoot", () =>
            {
                var root = new EmptyActor { Name = "AtmosphereRoot" };
                Level.SpawnActor(root, scene);
            });

            EnsureSceneActor(scene, "GroundShadow", () =>
            {
                var root = new EmptyActor { Name = "GroundShadow" };
                Level.SpawnActor(root, scene);
            });

            // 确保 CharacterSceneRoot 存在
            var characterRoot = scene.FindActor("CharacterSceneRoot");
            if (characterRoot == null)
            {
                characterRoot = new EmptyActor { Name = "CharacterSceneRoot" };
                Level.SpawnActor(characterRoot, scene);
                Debug.Log("[GameSceneManager] 创建 CharacterSceneRoot");
            }

            // 检查 CharacterSceneRoot 上是否已有控制器（可能由场景文件直接挂载）
            var existingOnRoot = characterRoot.GetScript<CharacterSceneController>();
            if (existingOnRoot != null)
            {
                Debug.Log("[GameSceneManager] CharacterSceneController 已挂载在 CharacterSceneRoot 上，跳过创建");
                return;
            }

            // 检查全局是否有（SceneController 可能在其他 Actor 上创建了）
            var existingGlobal = Level.FindScript<CharacterSceneController>();
            if (existingGlobal != null)
            {
                Debug.Log($"[GameSceneManager] CharacterSceneController 已在 {existingGlobal.Actor?.Name ?? "unknown"} 上存在，跳过创建");
                return;
            }

            // 创建并挂载
            var controller = characterRoot.AddScript<CharacterSceneController>();
            if (controller != null)
            {
                Debug.Log("[GameSceneManager] CharacterSceneController 已创建并挂载到 CharacterSceneRoot");
            }
            else
            {
                Debug.LogError("[GameSceneManager] 创建 CharacterSceneController 失败");
            }
        }

        private void EnsureSceneActor(FlaxEngine.Scene scene, string actorName, System.Action createAction)
        {
            if (scene.FindActor(actorName) == null)
            {
                createAction?.Invoke();
                Debug.Log($"[GameSceneManager] 创建场景 Actor: {actorName}");
            }
        }

        private void CleanupDuplicateAudioListeners()
        {
            var listeners = Level.GetActors<AudioListener>();
            if (listeners != null && listeners.Length > 1)
            {
                Debug.Log($"[GameSceneManager] 检测到 {listeners.Length} 个 AudioListener，正在清理多余项...");

                // 优先保留RootScene中的监听器
                AudioListener mainListener = null;
                foreach (var listener in listeners)
                {
                    if (listener.Scene != null && listener.Scene == _rootScene)
                    {
                        mainListener = listener;
                        break;
                    }
                }

                // 如果没找到RootScene的，就保留第一个
                if (mainListener == null)
                    mainListener = listeners[0];

                // 销毁其他所有监听器
                foreach (var listener in listeners)
                {
                    if (listener != mainListener)
                    {
                        Debug.Log($"[GameSceneManager] 销毁多余的 AudioListener: {listener.Name} (来自场景: {listener.Scene?.Name ?? "未知"})");
                        Destroy(listener);
                    }
                }
            }
        }

        #endregion
    }
}
