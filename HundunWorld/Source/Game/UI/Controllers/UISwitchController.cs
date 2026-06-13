using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlaxEngine;
using HundunWorld.Game.UI.Core;
using HundunWorld.Game.UI.Events;
using HundunWorld.Game.UI.Controllers;
using HundunWorld.Game.UI.States;
using HundunWorld.Game.UI;
using Horizon.Game.Message.Enums;

namespace Game.UI.Controllers
{
    /// <summary>
    /// 杩囨浮绫诲瀷鏋氫妇
    /// </summary>
    

    /// <summary>
    /// 鍒囨崲浼樺厛绾?
    /// </summary>
    

    /// <summary>
    /// UI鍒囨崲璇锋眰鍙傛暟
    /// </summary>
    public class SwitchRequest
    {
        public SceneType TargetScene { get; set; }
        public TransitionType TransitionType { get; set; } = TransitionType.Fade;
        public SwitchPriority Priority { get; set; } = SwitchPriority.Normal;
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
        public bool ForceSwitch { get; set; } = false;
        public bool EnableAnimation { get; set; } = true;
        public bool CreateSnapshot { get; set; } = true;
        public string Reason { get; set; } = "";
        public string RequestId { get; set; } = Guid.NewGuid().ToString();
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
        public float Duration { get; set; } = 1.0f;
    }

    /// <summary>
    /// UI鍒囨崲缁撴灉
    /// </summary>
    public class SwitchResult
    {
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; } = "";
        public TimeSpan Duration { get; set; }
        public string SnapshotId { get; set; } = "";
        public TransitionState TransitionState { get; internal set; }
    }

    /// <summary>
    /// UI鍒囨崲鎺у埗鍣?
    /// 浣滀负UI鍦烘櫙鍒囨崲鐨勭粺涓€鍏ュ彛锛屽崗璋冨悇瀛愭帶鍒跺櫒瀹屾垚澶嶆潅鐨勫垏鎹㈡祦绋?
    /// 璐熻矗鍒囨崲缂栨帓銆佹潯浠堕獙璇併€佽繘搴︾洃鎺у拰寮傚父澶勭悊
    /// </summary>
    public class UISwitchController : Script
    {
        private static UISwitchController _instance;
        private static readonly object _lock = new object();

        // 鏍稿績绠＄悊鍣?
        private UnifiedStateManager _stateManager;
        private StateSnapshotManager _snapshotManager;
        private UIEventBus _eventBus;

        // 瀛愭帶鍒跺櫒
        private SceneController _sceneController;
        private AnimationController _animationController;
        private ErrorHandler _errorHandler;

        // 鍒囨崲闃熷垪鍜岀姸鎬?
        private readonly Queue<SwitchRequest> _switchQueue = new Queue<SwitchRequest>();
        private bool _isProcessing = false;
        private SwitchRequest _currentRequest;
        private DateTime _currentSwitchStartTime;

        // 閰嶇疆鍙傛暟
        public bool EnableQueueing { get; set; } = true;
        public int MaxQueueSize { get; set; } = 10;
        public bool LogSwitchOperations { get; set; } = true;
        public bool EnableAutomaticRecovery { get; set; } = true;

        /// <summary>
        /// 鍗曚緥瀹炰緥
        /// </summary>
        public static UISwitchController Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            var gameObject = Level.FindActor("UISwitchController") ?? new EmptyActor();
                            gameObject.Name = "UISwitchController";
                            _instance = gameObject.GetScript<UISwitchController>() ?? gameObject.AddScript<UISwitchController>();
                            _instance.OnAwake();
                            Engine.RequestingExit += () => { _instance = null; };
                        }
                    }
                }
                return _instance;
            }
        }

        #region 鐢熷懡鍛ㄦ湡

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

            InitializeController();
        }

        public override void OnStart()
        {
            FlaxEngine.Debug.Log("UI鍒囨崲鎺у埗鍣ㄥ垵濮嬪寲瀹屾垚");
        }

        public override void OnUpdate()
        {
            ProcessSwitchQueue();
        }

        public override void OnDestroy()
        {
            CleanupController();

            if (_instance == this)
            {
                _instance = null;
            }
        }

        #endregion

        #region 鍒濆鍖?

        /// <summary>
        /// 鍒濆鍖栨帶鍒跺櫒
        /// </summary>
        private void InitializeController()
        {
            // 鑾峰彇鏍稿績绠＄悊鍣?
            _stateManager = UnifiedStateManager.Instance;
            _snapshotManager = new StateSnapshotManager(_stateManager);
            _eventBus = UIEventBus.Instance;

            // 鍒濆鍖栧瓙鎺у埗鍣?
            InitializeSubControllers();

            // 璁㈤槄浜嬩欢
            SubscribeToEvents();

            FlaxEngine.Debug.Log("UI鍒囨崲鎺у埗鍣ㄥ垵濮嬪寲瀹屾垚");
        }

        /// <summary>
        /// 鍒濆鍖栧瓙鎺у埗鍣?
        /// </summary>
        private void InitializeSubControllers()
        {
            // 鍒涘缓鍦烘櫙鎺у埗鍣?
            var sceneControllerActor = new EmptyActor { Name = "SceneController", Parent = Actor };
            _sceneController = sceneControllerActor.AddScript<SceneController>();

            // 鍒涘缓鍔ㄧ敾鎺у埗鍣?
            var animationControllerActor = new EmptyActor { Name = "AnimationController", Parent = Actor };
            _animationController = animationControllerActor.AddScript<AnimationController>();

            // 鍒涘缓閿欒澶勭悊鍣?
            var errorHandlerActor = new EmptyActor { Name = "ErrorHandler", Parent = Actor };
            _errorHandler = errorHandlerActor.AddScript<ErrorHandler>();
        }

        /// <summary>
        /// 璁㈤槄浜嬩欢
        /// </summary>
        private void SubscribeToEvents()
        {
            _eventBus.Subscribe<ErrorOccurredEvent>(OnErrorOccurred, subscriberName: "UISwitchController");
        }

        /// <summary>
        /// 娓呯悊鎺у埗鍣?
        /// </summary>
        private void CleanupController()
        {
            _eventBus?.UnsubscribeAll("UISwitchController");
            _switchQueue.Clear();
            FlaxEngine.Debug.Log("UI鍒囨崲鎺у埗鍣ㄨ祫婧愬凡娓呯悊");
        }

        #endregion

        #region 鍏叡鎺ュ彛

        /// <summary>
        /// 璇锋眰鍦烘櫙鍒囨崲锛堝悓姝ワ級
        /// </summary>
        /// <param name="targetScene">鐩爣鍦烘櫙</param>
        /// <param name="parameters">鍒囨崲鍙傛暟</param>
        /// <param name="forceSwitch">鏄惁寮哄埗鍒囨崲</param>
        /// <returns>鍒囨崲缁撴灉</returns>
        public SwitchResult RequestSceneSwitch(SceneType targetScene, Dictionary<string, object> parameters = null, bool forceSwitch = false)
        {
            var request = new SwitchRequest
            {
                TargetScene = targetScene,
                Parameters = parameters ?? new Dictionary<string, object>(),
                ForceSwitch = forceSwitch,
                Reason = "鐢ㄦ埛璇锋眰"
            };

            return ProcessSwitchRequest(request);
        }

        /// <summary>
        /// 璇锋眰鍦烘櫙鍒囨崲锛堝紓姝ワ級
        /// </summary>
        /// <param name="targetScene">鐩爣鍦烘櫙</param>
        /// <param name="parameters">鍒囨崲鍙傛暟</param>
        /// <param name="forceSwitch">鏄惁寮哄埗鍒囨崲</param>
        /// <returns>鍒囨崲缁撴灉浠诲姟</returns>
        public async Task<SwitchResult> RequestSceneSwitchAsync(SceneType targetScene, TransitionType transitionType=  TransitionType.Fade, Dictionary<string, object> parameters = null, bool forceSwitch = false)
        {
            var request = new SwitchRequest
            {
                TargetScene = targetScene,
                Parameters = parameters ?? new Dictionary<string, object>(),
                ForceSwitch = forceSwitch,
                Reason = "寮傛鐢ㄦ埛璇锋眰",
                TransitionType = transitionType,
                Priority = SwitchPriority.Normal,
                CreateSnapshot = true,
                EnableAnimation = true,
                Timeout = TimeSpan.FromSeconds(1),
                Duration = 0.5f
            };

            if (EnableQueueing && _isProcessing)
            {
                return await QueueSwitchRequest(request);
            }
            else
            {
                return ProcessSwitchRequest(request);
            }
        }

        /// <summary>
        /// 注册成功后切换到登录界面并自动填充账号信息
        /// </summary>
        /// <param name="passportId">服务器返回的PassportId</param>
        /// <param name="password">注册时填写的密码</param>
        /// <returns>切换结果任务</returns>
        public async Task<SwitchResult> OnRegisterSuccessAsync(string passportId, string password)
        {
            FlaxEngine.Debug.Log($"[OnRegisterSuccessAsync] 注册成功，准备切换到登录界面，PassportId: {passportId}");
            
            // 构建切换参数，传入注册成功后的账号信息
            var parameters = new Dictionary<string, object>
            {
                { "RegisterSuccess", true },
                { "PassportId", passportId ?? "" },
                { "Password", password ?? "" },
                { "AutoFillCredentials", true }
            };
            
            // 切换到登录场景
            var result = await RequestSceneSwitchAsync(
                SceneType.Login, 
                TransitionType.Slide,
                parameters, 
                forceSwitch: true);
            
            if (result.IsSuccess)
            {
                FlaxEngine.Debug.Log($"[OnRegisterSuccessAsync] 场景切换成功，已保存账号信息: PassportId={passportId}");
            }
            else
            {
                FlaxEngine.Debug.LogError($"[OnRegisterSuccessAsync] 场景切换失败: {result.ErrorMessage}");
            }
            
            return result;
        }

        /// <summary>
        /// 璇锋眰楂樼骇鍦烘櫙鍒囨崲
        /// </summary>
        /// <param name="request">鍒囨崲璇锋眰</param>
        /// <returns>鍒囨崲缁撴灉</returns>
        public SwitchResult RequestAdvancedSwitch(SwitchRequest request)
        {
            if (request == null)
            {
                return CreateErrorResult("鍒囨崲璇锋眰涓嶈兘涓虹┖");
            }

            return ProcessSwitchRequest(request);
        }

        /// <summary>
        /// 鍙栨秷褰撳墠鍒囨崲
        /// </summary>
        /// <param name="reason">鍙栨秷鍘熷洜</param>
        /// <returns>鏄惁鎴愬姛鍙栨秷</returns>
        public bool CancelCurrentSwitch(string reason = "鐢ㄦ埛鍙栨秷")
        {
            if (!_isProcessing || _currentRequest == null)
            {
                return false;
            }

            try
            {
                var currentTransition = _stateManager.GetCurrentTransition();
                if (currentTransition != null && currentTransition.CanCancel)
                {
                    currentTransition.Cancel(reason);
                    _stateManager.CompleteSceneTransition(false, $"鍒囨崲宸插彇娑? {reason}");

                    _isProcessing = false;
                    _currentRequest = null;

                    if (LogSwitchOperations)
                    {
                        FlaxEngine.Debug.Log($"鍒囨崲宸插彇娑? {reason}");
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"鍙栨秷鍒囨崲鏃跺彂鐢熼敊璇? {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// 鑾峰彇褰撳墠鍒囨崲鐘舵€?
        /// </summary>
        /// <returns>Current transition state</returns>
        public TransitionState GetCurrentSwitchStatus()
        {
            return _stateManager.GetCurrentTransition();
        }

        /// <summary>
        /// 妫€鏌ユ槸鍚﹀彲浠ュ垏鎹㈠埌鎸囧畾鍦烘櫙
        /// </summary>
        /// <param name="targetScene">鐩爣鍦烘櫙</param>
        /// <param name="ignoreCurrentTransition">Whether to ignore current transition state</param>
        /// <returns>鏄惁鍙互鍒囨崲</returns>
        public bool CanSwitchToScene(SceneType targetScene, bool ignoreCurrentTransition = false)
        {
            var currentState = _stateManager.GetCurrentState();

            // 检查是否正在切换
            if (!ignoreCurrentTransition && currentState.IsTransitioning)
            {
                return false;
            }

            // 检查是否是同一场景
            if (currentState.CurrentScene == targetScene)
            {
                return false;
            }

            // 检查场景状态
            var targetSceneState = _stateManager.GetSceneState(targetScene);
            if (targetSceneState == null)
            {
                return false;
            }

            // 可以添加更多验证逻辑
            return true;
        }

        #endregion

        #region 切换处理

        /// <summary>
        /// 处理切换请求
        /// </summary>
        /// <param name="request">切换请求</param>
        /// <returns>切换结果</returns>
        private SwitchResult ProcessSwitchRequest(SwitchRequest request)
        {
            _currentSwitchStartTime = DateTime.UtcNow;
            _currentRequest = request;
            _isProcessing = true;

            try
            {
                // 验证切换条件
                if (!ValidateSwitchRequest(request, out string validationError))
                {
                    return CreateErrorResult(validationError);
                }

                // 创建切换前快照
                string snapshotId = "";
                if (request.CreateSnapshot)
                {
                    snapshotId = _snapshotManager.CreateSceneTransitionSnapshot(
                        _stateManager.GetCurrentState().CurrentScene,
                        request.TargetScene);
                }

                // 开始切换
                var transition = _stateManager.BeginSceneTransition(
                    request.TargetScene,
                    request.Parameters,
                    request.ForceSwitch);

                if (transition == null)
                {
                    return CreateErrorResult("无法开始场景切换");
                }

                // 执行切换流程
                var result = ExecuteSwitchWorkflow(request, transition);
                result.SnapshotId = snapshotId;

                return result;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"处理切换请求时发生错误: {ex.Message}");
                return CreateErrorResult($"切换过程中发生错误: {ex.Message}");
            }
            finally
            {
                _isProcessing = false;
                _currentRequest = null;
            }
        }

        /// <summary>
        /// 处理切换请求
        /// </summary>
        /// <param name="request">切换请求</param>
        /// <param name="transition">切换状态</param>
        /// <returns>切换结果</returns>
        private SwitchResult ExecuteSwitchWorkflow(SwitchRequest request, TransitionState transition)
        {
            try
            {
                // 阶段1: 准备阶段
                _stateManager.UpdateTransitionProgress(0.0f, TransitionPhase.Preparing);
                if (LogSwitchOperations)
                {
                    FlaxEngine.Debug.Log($"开始场景切换: {transition.FromScene} -> {transition.ToScene}");
                }

                // 阶段2: 验证阶段
                _stateManager.UpdateTransitionProgress(0.1f, TransitionPhase.Validating);
                if (!ValidateTransitionState(transition))
                {
                    throw new InvalidOperationException("切换状态验证失败");
                }

                // 阶段3: 退出动画
                if (request.EnableAnimation)
                {
                    _stateManager.UpdateTransitionProgress(0.2f, TransitionPhase.ExitAnimation);
                    _animationController.PlayExitAnimation(transition.FromScene);
                }

                // 阶段4: 场景切换
                _stateManager.UpdateTransitionProgress(0.4f, TransitionPhase.SceneSwitch);
                _sceneController.SwitchScene(transition.FromScene, transition.ToScene);

                // 阶段5: 数据加载
                _stateManager.UpdateTransitionProgress(0.6f, TransitionPhase.DataLoading);
                LoadSceneData(request.TargetScene, request.Parameters);

                // 阶段6: 进入动画
                if (request.EnableAnimation)
                {
                    _stateManager.UpdateTransitionProgress(0.8f, TransitionPhase.EnterAnimation);
                    _animationController.PlayEnterAnimation(transition.ToScene);
                }

                // 阶段7: 完成
                _stateManager.UpdateTransitionProgress(1.0f, TransitionPhase.Completed);
                _stateManager.CompleteSceneTransition(true);

                var duration = DateTime.UtcNow - _currentSwitchStartTime;

                if (LogSwitchOperations)
                {
                    FlaxEngine.Debug.Log($"场景切换成功完成，耗时: {duration.TotalMilliseconds:F0}ms");
                }

                return new SwitchResult
                {
                    IsSuccess = true,
                    TransitionState = transition,
                    Duration = duration
                };
            }
            catch (Exception ex)
            {
                var duration = DateTime.UtcNow - _currentSwitchStartTime;
                FlaxEngine.Debug.LogError($"场景切换失败: {ex.Message}");

                // 尝试恢复
                if (EnableAutomaticRecovery)
                {
                    _errorHandler.HandleSwitchError(transition, ex);
                }

                _stateManager.CompleteSceneTransition(false, ex.Message);

                return new SwitchResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message,
                    TransitionState = transition,
                    Duration = duration
                };
            }
        }

        /// <summary>
        /// 验证切换请求
        /// </summary>
        /// <param name="request">切换请求</param>
        /// <param name="errorMessage">错误信息</param>
        /// <returns>是否有效</returns>
        private bool ValidateSwitchRequest(SwitchRequest request, out string errorMessage)
        {
            errorMessage = "";

            // 检查目标场景
            if (!Enum.IsDefined(typeof(SceneType), request.TargetScene))
            {
                errorMessage = "无效的目标场景";
                return false;
            }

            // 检查切换条件
            if (!request.ForceSwitch && !CanSwitchToScene(request.TargetScene))
            {
                errorMessage = "当前无法切换到目标场景";
                return false;
            }

            // 检查超时设置
            if (request.Timeout <= TimeSpan.Zero)
            {
                errorMessage = "超时时间必须大于0";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 验证切换状态
        /// </summary>
        /// <param name="transition">切换状态</param>
        /// <returns>是否有效</returns>
        private bool ValidateTransitionState(TransitionState transition)
        {
            return transition != null &&
                   !transition.IsCancelled &&
                   transition.CurrentPhase != TransitionPhase.Failed;
        }

        /// <summary>
        /// 加载场景数据
        /// </summary>
        /// <param name="sceneType">场景类型</param>
        /// <param name="parameters">参数</param>
        private void LoadSceneData(SceneType sceneType, Dictionary<string, object> parameters)
        {
            // 这里可以实现场景特定的数据加载逻辑
            // 例如加载用户数据、角色数据等

            switch (sceneType)
            {
                case SceneType.CharacterSelection:
                    // 加载角色列表
                    break;

                case SceneType.GameWorld:
                    // 加载游戏世界数据
                    break;
            }
        }

        #endregion

        #region 队列处理

        /// <summary>
        /// 将切换请求加入队列
        /// </summary>
        /// <param name="request">切换请求</param>
        /// <returns>切换结果任务</returns>
        private async Task<SwitchResult> QueueSwitchRequest(SwitchRequest request)
        {
            if (_switchQueue.Count >= MaxQueueSize)
            {
                return CreateErrorResult("切换队列已满");
            }

            _switchQueue.Enqueue(request);

            // 等待处理完成
            while (_switchQueue.Contains(request) || _isProcessing)
            {
                await Task.Delay(50);
            }

            // 这里应该返回实际的处理结果，简化实现
            return new SwitchResult { IsSuccess = true };
        }

        /// <summary>
        /// 处理切换队列
        /// </summary>
        private void ProcessSwitchQueue()
        {
            if (_isProcessing || _switchQueue.Count == 0) return;

            var nextRequest = _switchQueue.Dequeue();
            ProcessSwitchRequest(nextRequest);
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 错误事件处理
        /// </summary>
        /// <param name="eventData">错误事件数据</param>
        private void OnErrorOccurred(ErrorOccurredEvent eventData)
        {
            if (eventData.Severity == ErrorSeverity.Critical && _isProcessing)
            {
                FlaxEngine.Debug.LogWarning("检测到关键错误，尝试取消当前切换");
                CancelCurrentSwitch("关键错误");
            }
        }

        #endregion

        #region 工具方法

        /// <summary>
        /// 创建错误结果
        /// </summary>
        /// <param name="errorMessage">错误消息</param>
        /// <returns>错误结果</returns>
        private SwitchResult CreateErrorResult(string errorMessage)
        {
            return new SwitchResult
            {
                IsSuccess = false,
                ErrorMessage = errorMessage,
                Duration = DateTime.UtcNow - _currentSwitchStartTime
            };
        }

        #endregion
    }
}
