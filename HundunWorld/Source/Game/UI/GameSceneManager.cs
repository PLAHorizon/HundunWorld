using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlaxEngine;
using FlaxEngine.GUI;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.UI
{
    /// <summary>
    /// 游戏场景管理器 - 统一管理所有场景切换逻辑
    /// 
    /// 设计原则：
    /// 1. Start场景作为主场景始终保持加载（包含网络、状态管理等核心组件）
    /// 2. 其他场景作为子场景动态加载/卸载（附加加载，不替换主场景）
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
            { SceneType.Start, "Content/Maps/Main.scene" },
            { SceneType.Login, "Content/Maps/Login.scene" },
            { SceneType.CharacterSelection, "Content/Maps/Character.scene" },
            { SceneType.CharacterCreation, "Content/Maps/Character.scene" },
            { SceneType.GameWorld, "Content/Maps/World.scene" }
        };

        private const string MainSceneName = "Main";
        private static readonly string MainScenePath = "Content/Maps/Main.scene";
        private static readonly string TransitionScenePath = "Content/Maps/TransitionScene.scene";
        private const string TransitionSceneName = "TransitionScene";

        #endregion

        #region 配置

        [Serialize]
        public bool EnableTransitionEffect = true;

        #endregion

        #region 状态

        private SceneType _currentSceneType = SceneType.Start;
        private SceneType? _pendingSceneType;
        private SceneType _previousSceneType = SceneType.Start;
        private FlaxEngine.Scene _mainScene;
        private FlaxEngine.Scene _currentSubScene;
        private FlaxEngine.Scene _transitionScene;
        private Guid _transitionSceneId = Guid.Empty;
        private Guid _targetSceneId = Guid.Empty;
        private bool _isTransitioning;
        private string _pendingTargetPath;
        private TransitionState _transitionState = TransitionState.Idle;

        private float _currentLoadingProgress = 0f;
        private float _targetLoadingProgress = 0f;
        private float _loadingTimer = 0f;
        private const float MinLoadingDuration = 1.5f;
        private const float MaxLoadingDuration = 20.0f;
        private float _waitTimerAfterLoaded = 0f;
        private const float MinWaitTime = 1.0f;
        private HundunWorld.Game.UI.Components.RoundedProgressBar _transitionProgressBar;

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

            // 查找进度条
            if (_transitionProgressBar == null)
            {
                FindTransitionProgressBar();
            }

            _loadingTimer += Time.DeltaTime;

            // 1. 计算基于时间的进度
            // 正常情况下至少需要 MinLoadingDuration 秒跑完
            float timeProgress = _loadingTimer / MinLoadingDuration;

            // 2. 模拟/同步加载状态
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

            // 3. 检查是否完成
            if (_currentLoadingProgress >= 1.0f && _transitionState == TransitionState.LoadingTargetScene && _currentSubScene != null && _loadingTimer >= MinLoadingDuration)
            {
                _currentLoadingProgress = 1.0f;
                _waitTimerAfterLoaded += Time.DeltaTime;

                if (_waitTimerAfterLoaded >= MinWaitTime)
                {
                    Debug.Log($"[GameSceneManager] 加载时长: {_loadingTimer:F2}s, 满足最短时长 {MinLoadingDuration}s, 开始淡出");
                    _waitTimerAfterLoaded = 0f;
                    _loadingTimer = 0f;
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

            FindMainScene();
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

        private void FindMainScene()
        {
            for (int i = 0; i < Level.ScenesCount; i++)
            {
                var scene = Level.GetScene(i);
                if (scene != null && scene.Name == MainSceneName)
                {
                    _mainScene = scene;
                    Debug.Log($"[GameSceneManager] 找到主场景: {_mainScene.Name}");
                    break;
                }
            }
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

            // 卸载当前子场景
            if (_currentSubScene != null && _currentSubScene != _mainScene && _currentSubScene != _transitionScene)
            {
                Debug.Log($"[GameSceneManager] 卸载旧场景: {_currentSubScene.Name}");
                var sceneToUnload = _currentSubScene;
                _currentSubScene = null;
                Level.UnloadSceneAsync(sceneToUnload);
            }

            // 延迟一帧后加载新场景
            Scripting.InvokeOnUpdate(() =>
            {
                _transitionState = TransitionState.LoadingTargetScene;
                LoadProgress?.Invoke(0.5f);

                // 检查目标是否是主场景
                if (_pendingTargetPath == MainScenePath)
                {
                    // 目标是主场景，不需要加载，但需要展示进度条过程
                    _currentSubScene = _mainScene;
                    _currentLoadingProgress = 0f;
                    _targetLoadingProgress = 0f;
                    _loadingTimer = 0f;
                    _waitTimerAfterLoaded = 0f;
                    Debug.Log("[GameSceneManager] 目标是主场景，直接进入进度条最短时间流程");
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

            // 检查场景是否已加载
            var sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
            for (int i = 0; i < Level.ScenesCount; i++)
            {
                var existingScene = Level.GetScene(i);
                if (existingScene != null && existingScene.Name == sceneName)
                {
                    Debug.Log($"[GameSceneManager] 目标场景已存在: {existingScene.Name}");
                    _currentSubScene = existingScene;
                    Step5_FadeOutFromTransition();
                    return;
                }
            }

            // 使用 GetAssetInfo 获取 ID，避免同步加载 Asset 导致的阻塞
            if (Content.GetAssetInfo(scenePath, out var assetInfo))
            {
                _targetSceneId = assetInfo.ID;
                Debug.Log($"[GameSceneManager] 开始异步加载目标场景, ID: {_targetSceneId}");
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

            LoadProgress?.Invoke(0.8f);

            var effect = SceneTransitionEffect.Instance;
            if (effect != null)
            {
                effect.StartFadeOut(() =>
                {
                    Step6_UnloadTransition();
                });
            }
            else
            {
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
                Level.UnloadSceneAsync(sceneToUnload);

                // 延迟执行步骤7
                Scripting.InvokeOnUpdate(() =>
                {
                    Step7_FadeInTarget();
                });
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

            Debug.Log($"[GameSceneManager] 切换完成: {previousScene} -> {targetScene}");
            TransitionCompleted?.Invoke(previousScene, targetScene);
        }

        /// <summary>
        /// 直接加载目标场景（无过渡效果）
        /// </summary>
        private void DirectLoadTargetScene()
        {
            Debug.Log("[GameSceneManager] 直接加载目标场景（无过渡效果）");

            // 卸载当前子场景
            if (_currentSubScene != null && _currentSubScene != _mainScene)
            {
                var sceneToUnload = _currentSubScene;
                _currentSubScene = null;
                Level.UnloadSceneAsync(sceneToUnload);
            }

            if (_pendingTargetPath == MainScenePath)
            {
                CompleteTransition();
            }
            else
            {
                var sceneAsset = Content.Load<SceneAsset>(_pendingTargetPath);
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
                    HandleTransitionError("无法加载目标场景");
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
                Level.UnloadSceneAsync(_transitionScene);
                _transitionScene = null;
            }

            _isTransitioning = false;
            _transitionState = TransitionState.Idle;
            _pendingSceneType = null;

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

            if (sceneId == _targetSceneId || (scene != null && scene.Name != MainSceneName))
            {
                _currentSubScene = scene;

                if (sceneId == _targetSceneId)
                {
                    _targetSceneId = Guid.Empty; // 重置
                    Debug.Log($"[GameSceneManager] 目标场景加载完成: {scene?.Name ?? "Scene"}");
                }

                // 不再直接调用 Step5，交给 UpdateLoadingProgress 处理
                /*
                if (_transitionState == TransitionState.LoadingTargetScene)
                {
                    if (EnableTransitionEffect)
                        Scripting.InvokeOnUpdate(Step5_FadeOutFromTransition);
                    else
                        Scripting.InvokeOnUpdate(CompleteTransition);
                }
                */

                if (_transitionState == TransitionState.LoadingTargetScene && !EnableTransitionEffect)
                {
                    Scripting.InvokeOnUpdate(CompleteTransition);
                }
                else if (!_isTransitioning)
                {
                    Debug.Log($"[GameSceneManager] 非切换模式场景加载: {scene?.Name ?? "Scene"}");
                }
                return;
            }

            if (scene?.Name == MainSceneName)
            {
                _mainScene = scene;
                Debug.Log("[GameSceneManager] 主场景已加载");
                return;
            }
        }

        private void OnSceneUnloading(FlaxEngine.Scene scene, Guid sceneId)
        {
            if (scene.Name == MainSceneName)
            {
                Debug.LogError("[GameSceneManager] 警告：尝试卸载主场景！");
            }
        }

        private void OnSceneUnloaded(FlaxEngine.Scene scene, Guid sceneId)
        {
            Debug.Log($"[GameSceneManager] 场景已卸载: {scene?.Name ?? "null"}");

            if (_currentSubScene == scene)
            {
                _currentSubScene = null;
            }

            if (_transitionScene == scene)
            {
                _transitionScene = null;
            }

            if (scene?.Name == MainSceneName)
            {
                Debug.LogError("[GameSceneManager] 严重错误：主场景被卸载！尝试重新加载...");
                _mainScene = null;
                var mainSceneAsset = Content.Load<SceneAsset>(MainScenePath);
                if (mainSceneAsset != null)
                {
                  Task.Factory.StartNew(async()=>  Level.LoadSceneAsync(mainSceneAsset.ID));
                }
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
            if (_transitionScene != null)
            {
                Level.UnloadSceneAsync(_transitionScene);
                _transitionScene = null;
            }

            if (_currentSubScene != null && _currentSubScene != _mainScene)
            {
                Level.UnloadSceneAsync(_currentSubScene);
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
        /// 清理多余的 AudioListener，防止报错 "Unsupported amount of the audio listeners!"
        /// </summary>
        private void CleanupDuplicateAudioListeners()
        {
            var listeners = Level.GetActors<AudioListener>();
            if (listeners != null && listeners.Length > 1)
            {
                Debug.Log($"[GameSceneManager] 检测到 {listeners.Length} 个 AudioListener，正在清理多余项...");

                // 优先保留主场景中的监听器
                AudioListener mainListener = null;
                foreach (var listener in listeners)
                {
                    if (listener.Scene != null && listener.Scene.Name == MainSceneName)
                    {
                        mainListener = listener;
                        break;
                    }
                }

                // 如果没找到主场景的，就保留第一个
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
