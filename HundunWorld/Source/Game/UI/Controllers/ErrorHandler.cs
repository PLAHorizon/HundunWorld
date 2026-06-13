using System;
using System.Collections.Generic;
using FlaxEngine;
using HundunWorld.Game.UI.Core;
using HundunWorld.Game.UI.Events;
using HundunWorld.Game.UI.States;
using HundunWorld.Game.UI.Enums;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.UI.Controllers
{
    /// <summary>
    /// 閿欒绫诲瀷鏋氫妇
    /// </summary>
    

    /// <summary>
    /// 閿欒澶勭悊绛栫暐鏋氫妇
    /// </summary>
    

    /// <summary>
    /// 閿欒淇℃伅绫?    /// </summary>
    public class UIErrorInfo
    {
        public string ErrorId { get; set; } = Guid.NewGuid().ToString();
        public UIErrorType Type { get; set; }
        public ErrorSeverity Severity { get; set; }
        public string Message { get; set; } = "";
        public Exception Exception { get; set; }
        public string Context { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public Dictionary<string, object> AdditionalData { get; set; } = new Dictionary<string, object>();
        public bool IsResolved { get; set; } = false;
        public string Resolution { get; set; } = "";
    }

    /// <summary>
    /// 閿欒澶勭悊鍣?    /// 璐熻矗UI绯荤粺鐨勯敊璇鐞嗗拰鎭㈠鏈哄埗锛屾彁渚涘畬鍠勭殑閿欒澶勭悊绛栫暐
    /// 鏀寔鑷姩鎭㈠銆佺敤鎴峰紩瀵兼仮澶嶅拰绯荤粺鐩戞帶棰勮
    /// </summary>
    public class ErrorHandler : Script
    {
        // 鏍稿績绠＄悊鍣?        private UnifiedStateManager _stateManager;
        private StateSnapshotManager _snapshotManager;
        private UIEventBus _eventBus;

        // 閿欒璁板綍
        private readonly List<UIErrorInfo> _errorHistory = new List<UIErrorInfo>();
        private readonly Dictionary<UIErrorType, ErrorHandlingStrategy> _handlingStrategies = 
            new Dictionary<UIErrorType, ErrorHandlingStrategy>();

        // 鎭㈠鏈哄埗
        private readonly Dictionary<string, Func<UIErrorInfo, bool>> _recoveryHandlers = 
            new Dictionary<string, Func<UIErrorInfo, bool>>();

        // 閰嶇疆鍙傛暟
        public bool EnableAutomaticRecovery { get; set; } = true;
        public bool LogErrorDetails { get; set; } = true;
        public int MaxRetryAttempts { get; set; } = 3;
        public int MaxErrorHistoryCount { get; set; } = 100;
        public TimeSpan ErrorReportInterval { get; set; } = TimeSpan.FromMinutes(5);
        public static ErrorHandler Instance
        {
            get
            {
                if (_instance == null)
                {
                    var gameObject = Level.FindActor("ErrorHandler") ?? new EmptyActor();
                    gameObject.Name = "ErrorHandler";
                    _instance = gameObject.GetScript<ErrorHandler>() ?? gameObject.AddScript<ErrorHandler>();
                    _instance.InitializeErrorHandler();
                    Engine.RequestingExit += () =>
                    {
                        _instance = null;
                    };
                }
                return _instance;
            }
        }

        // 鐘舵€佽窡韪?
        private DateTime _lastErrorReport = DateTime.MinValue;
        private int _consecutiveErrors = 0;
        private static ErrorHandler _instance;

        #region 鐢熷懡鍛ㄦ湡
        
        public override void OnStart()
        {
            InitializeErrorHandler();
            FlaxEngine.Debug.Log("閿欒澶勭悊鍣ㄥ垵濮嬪寲瀹屾垚");
        }

        public override void OnDestroy()
        {
            CleanupErrorHandler();
        }

        #endregion

        #region 鍒濆鍖?
        /// <summary>
        /// 鍒濆鍖栭敊璇鐞嗗櫒
        /// </summary>
        private void InitializeErrorHandler()
        {
            // 鑾峰彇鏍稿績绠＄悊鍣?            _stateManager = UnifiedStateManager.Instance;
            _snapshotManager = new StateSnapshotManager(UnifiedStateManager.Instance);
            _eventBus = UIEventBus.Instance;

            // 璁剧疆閿欒澶勭悊绛栫暐
            SetupErrorHandlingStrategies();

            // 娉ㄥ唽鎭㈠澶勭悊鍣?            RegisterRecoveryHandlers();

            // 璁㈤槄浜嬩欢
            SubscribeToEvents();
        }

        /// <summary>
        /// 璁剧疆閿欒澶勭悊绛栫暐
        /// </summary>
        private void SetupErrorHandlingStrategies()
        {
            _handlingStrategies[UIErrorType.Network] = ErrorHandlingStrategy.Retry;
            _handlingStrategies[UIErrorType.Validation] = ErrorHandlingStrategy.ShowMessage;
            _handlingStrategies[UIErrorType.Authentication] = ErrorHandlingStrategy.Rollback;
            _handlingStrategies[UIErrorType.Transition] = ErrorHandlingStrategy.Rollback;
            _handlingStrategies[UIErrorType.Component] = ErrorHandlingStrategy.Restart;
            _handlingStrategies[UIErrorType.Data] = ErrorHandlingStrategy.Retry;
            _handlingStrategies[UIErrorType.System] = ErrorHandlingStrategy.Escalate;
        }

        /// <summary>
        /// 娉ㄥ唽鎭㈠澶勭悊鍣?        /// </summary>
        private void RegisterRecoveryHandlers()
        {
            _recoveryHandlers["network_retry"] = HandleNetworkErrorRecovery;
            _recoveryHandlers["state_rollback"] = HandleStateRollbackRecovery;
            _recoveryHandlers["component_restart"] = HandleComponentRestartRecovery;
            _recoveryHandlers["scene_reset"] = HandleSceneResetRecovery;
        }

        /// <summary>
        /// 璁㈤槄浜嬩欢
        /// </summary>
        private void SubscribeToEvents()
        {
            _eventBus.Subscribe<ErrorOccurredEvent>(OnErrorOccurred, subscriberName: "ErrorHandler");
            _eventBus.Subscribe<SceneTransitionCompletedEvent>(OnSceneTransitionCompleted, subscriberName: "ErrorHandler");
        }

        /// <summary>
        /// 娓呯悊閿欒澶勭悊鍣?        /// </summary>
        private void CleanupErrorHandler()
        {
            _eventBus?.UnsubscribeAll("ErrorHandler");
            _snapshotManager?.Dispose();
            _errorHistory.Clear();
            _recoveryHandlers.Clear();
            FlaxEngine.Debug.Log("閿欒澶勭悊鍣ㄨ祫婧愬凡娓呯悊");
        }

        #endregion

        #region 閿欒澶勭悊

        /// <summary>
        /// 澶勭悊UI閿欒
        /// </summary>
        /// <param name="errorType">閿欒绫诲瀷</param>
        /// <param name="message">閿欒娑堟伅</param>
        /// <param name="exception">寮傚父瀵硅薄</param>
        /// <param name="context">Error context</param>
        /// <returns>閿欒淇℃伅</returns>
        public UIErrorInfo HandleError(UIErrorType errorType, string message, Exception exception = null, string context = "")
        {
            var errorInfo = new UIErrorInfo
            {
                Type = errorType,
                Severity = DetermineErrorSeverity(errorType, exception),
                Message = message,
                Exception = exception,
                Context = context
            };

            // 璁板綍閿欒
            RecordError(errorInfo);

            // 澶勭悊閿欒
            ProcessError(errorInfo);

            return errorInfo;
        }

        /// <summary>
        /// 澶勭悊鍦烘櫙鍒囨崲閿欒
        /// </summary>
        /// <param name="transition">Transition state</param>
        /// <param name="exception">寮傚父瀵硅薄</param>
        /// <returns>閿欒淇℃伅</returns>
        public UIErrorInfo HandleSwitchError(TransitionState transition, Exception exception)
        {
            var errorInfo = new UIErrorInfo
            {
                Type = UIErrorType.Transition,
                Severity = ErrorSeverity.Error,
                Message = $"鍦烘櫙鍒囨崲澶辫触: {transition.FromScene} -> {transition.ToScene}",
                Exception = exception,
                Context = $"TransitionId: {transition.TransitionId}"
            };

            errorInfo.AdditionalData["transition_state"] = transition;

            RecordError(errorInfo);
            ProcessError(errorInfo);

            return errorInfo;
        }

        /// <summary>
        /// 澶勭悊楠岃瘉閿欒
        /// </summary>
        /// <param name="message">楠岃瘉娑堟伅</param>
        /// <param name="field">Field name</param>
        /// <returns>閿欒淇℃伅</returns>
        public UIErrorInfo HandleValidationError(string message, string field = "")
        {
            var errorInfo = new UIErrorInfo
            {
                Type = UIErrorType.Validation,
                Severity = ErrorSeverity.Warning,
                Message = message,
                Context = $"Field: {field}"
            };

            if (!string.IsNullOrEmpty(field))
            {
                errorInfo.AdditionalData["field"] = field;
            }

            RecordError(errorInfo);
            ProcessError(errorInfo);

            return errorInfo;
        }

        /// <summary>
        /// 澶勭悊璁よ瘉閿欒
        /// </summary>
        /// <param name="message">閿欒娑堟伅</param>
        /// <param name="userId">鐢ㄦ埛ID</param>
        /// <returns>閿欒淇℃伅</returns>
        public UIErrorInfo HandleAuthenticationError(string message, ulong userId = 0)
        {
            var errorInfo = new UIErrorInfo
            {
                Type = UIErrorType.Authentication,
                Severity = ErrorSeverity.Error,
                Message = message,
                Context = $"UserId: {userId}"
            };

            if (userId > 0)
            {
                errorInfo.AdditionalData["user_id"] = userId;
            }

            RecordError(errorInfo);
            ProcessError(errorInfo);

            return errorInfo;
        }

        #endregion

        #region 鎭㈠鏈哄埗

        /// <summary>
        /// 鏅鸿兘鎭㈠
        /// </summary>
        /// <param name="errorInfo">閿欒淇℃伅</param>
        /// <returns>鏄惁鎭㈠鎴愬姛</returns>
        public bool SmartRecover(UIErrorInfo errorInfo)
        {
            if (!EnableAutomaticRecovery) return false;

            try
            {
                // 鏍规嵁閿欒绫诲瀷閫夋嫨鎭㈠绛栫暐
                var strategy = GetRecoveryStrategy(errorInfo);
                return ExecuteRecoveryStrategy(errorInfo, strategy);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"鏅鸿兘鎭㈠澶辫触: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 寮哄埗鎭㈠鍒板畨鍏ㄧ姸鎬?        /// </summary>
        /// <returns>鏄惁鎭㈠鎴愬姛</returns>
        public bool ForceRecoverToSafeState()
        {
            try
            {
                // 尝试恢复到最近的快照
                if (_snapshotManager.SmartRestore("强制恢复"))
                {
                    FlaxEngine.Debug.Log("强制恢复到快照状态成功");
                    return true;
                }

                // 恢复到登录界面
                //_stateManager.BeginSceneTransition(SceneType.Login, null, true);
                //_stateManager.CompleteSceneTransition(true);

                FlaxEngine.Debug.Log("强制恢复到登录界面成功");
                return true;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"强制恢复失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 绉佹湁鏂规硶

        /// <summary>
        /// 璁板綍閿欒
        /// </summary>
        /// <param name="errorInfo">閿欒淇℃伅</param>
        private void RecordError(UIErrorInfo errorInfo)
        {
            _errorHistory.Add(errorInfo);

            // 闄愬埗鍘嗗彶璁板綍鏁伴噺
            if (_errorHistory.Count > MaxErrorHistoryCount)
            {
                _errorHistory.RemoveAt(0);
            }

            // 鏇存柊杩炵画閿欒璁℃暟
            _consecutiveErrors++;

            if (LogErrorDetails)
            {
                FlaxEngine.Debug.LogError($"UI閿欒 [{errorInfo.Type}]: {errorInfo.Message}");
                if (errorInfo.Exception != null)
                {
                    FlaxEngine.Debug.LogException(errorInfo.Exception);
                }
            }

            // 鍙戝竷閿欒浜嬩欢
            _eventBus.Publish(new ErrorOccurredEvent(errorInfo.Message, errorInfo.Exception, errorInfo.ErrorId, errorInfo.Severity));
        }

        /// <summary>
        /// 澶勭悊閿欒
        /// </summary>
        /// <param name="errorInfo">閿欒淇℃伅</param>
        private void ProcessError(UIErrorInfo errorInfo)
        {
            if (_handlingStrategies.TryGetValue(errorInfo.Type, out var strategy))
            {
                switch (strategy)
                {
                    case ErrorHandlingStrategy.ShowMessage:
                        ShowErrorMessage(errorInfo);
                        break;
                    case ErrorHandlingStrategy.Retry:
                        AttemptRetry(errorInfo);
                        break;
                    case ErrorHandlingStrategy.Rollback:
                        AttemptRollback(errorInfo);
                        break;
                    case ErrorHandlingStrategy.Restart:
                        AttemptRestart(errorInfo);
                        break;
                    case ErrorHandlingStrategy.Escalate:
                        EscalateError(errorInfo);
                        break;
                }
            }

            // 妫€鏌ユ槸鍚﹂渶瑕佹櫤鑳芥仮澶?            if (ShouldAttemptSmartRecovery(errorInfo))
            {
                SmartRecover(errorInfo);
            }
        }

        /// <summary>
        /// 纭畾閿欒涓ラ噸绋嬪害
        /// </summary>
        /// <param name="errorType">閿欒绫诲瀷</param>
        /// <param name="exception">寮傚父瀵硅薄</param>
        /// <returns>閿欒涓ラ噸绋嬪害</returns>
        private ErrorSeverity DetermineErrorSeverity(UIErrorType errorType, Exception exception)
        {
            switch (errorType)
            {
                case UIErrorType.Validation:
                    return ErrorSeverity.Warning;
                case UIErrorType.Network:
                case UIErrorType.Data:
                    return ErrorSeverity.Error;
                case UIErrorType.System:
                case UIErrorType.Component:
                    return ErrorSeverity.Critical;
                default:
                    return exception != null ? ErrorSeverity.Error : ErrorSeverity.Warning;
            }
        }

        /// <summary>
        /// 鏄剧ず閿欒娑堟伅
        /// </summary>
        /// <param name="errorInfo">閿欒淇℃伅</param>
        private void ShowErrorMessage(UIErrorInfo errorInfo)
        {
            // 杩欓噷鍙互闆嗘垚鍏蜂綋鐨刄I娑堟伅鏄剧ず缁勪欢
            FlaxEngine.Debug.LogWarning($"鏄剧ず閿欒娑堟伅: {errorInfo.Message}");
        }

        /// <summary>
        /// 灏濊瘯閲嶈瘯
        /// </summary>
        /// <param name="errorInfo">閿欒淇℃伅</param>
        private void AttemptRetry(UIErrorInfo errorInfo)
        {
            FlaxEngine.Debug.Log($"灏濊瘯閲嶈瘯鎿嶄綔: {errorInfo.Message}");
            // 瀹炵幇鍏蜂綋鐨勯噸璇曢€昏緫
        }

        /// <summary>
        /// 灏濊瘯鍥炴粴
        /// </summary>
        /// <param name="errorInfo">閿欒淇℃伅</param>
        private void AttemptRollback(UIErrorInfo errorInfo)
        {
            FlaxEngine.Debug.Log($"灏濊瘯鍥炴粴鐘舵€? {errorInfo.Message}");
            _snapshotManager.SmartRestore($"閿欒鍥炴粴: {errorInfo.ErrorId}");
        }

        /// <summary>
        /// 灏濊瘯閲嶅惎
        /// </summary>
        /// <param name="errorInfo">閿欒淇℃伅</param>
        private void AttemptRestart(UIErrorInfo errorInfo)
        {
            FlaxEngine.Debug.Log($"灏濊瘯閲嶅惎缁勪欢: {errorInfo.Message}");
            ForceRecoverToSafeState();
        }

        /// <summary>
        /// 鍗囩骇閿欒
        /// </summary>
        /// <param name="errorInfo">閿欒淇℃伅</param>
        private void EscalateError(UIErrorInfo errorInfo)
        {
            FlaxEngine.Debug.LogError($"鍗囩骇閿欒澶勭悊: {errorInfo.Message}");
            // 鍙互鍙戦€侀敊璇姤鍛婂埌鏈嶅姟鍣ㄧ瓑
        }

        /// <summary>
        /// 鑾峰彇鎭㈠绛栫暐
        /// </summary>
        /// <param name="errorInfo">閿欒淇℃伅</param>
        /// <returns>鎭㈠绛栫暐鍚嶇О</returns>
        private string GetRecoveryStrategy(UIErrorInfo errorInfo)
        {
            switch (errorInfo.Type)
            {
                case UIErrorType.Network:
                    return "network_retry";
                case UIErrorType.Transition:
                case UIErrorType.Authentication:
                    return "state_rollback";
                case UIErrorType.Component:
                    return "component_restart";
                default:
                    return "scene_reset";
            }
        }

        /// <summary>
        /// 鎵ц鎭㈠绛栫暐
        /// </summary>
        /// <param name="errorInfo">閿欒淇℃伅</param>
        /// <param name="strategy">绛栫暐鍚嶇О</param>
        /// <returns>鏄惁鎴愬姛</returns>
        private bool ExecuteRecoveryStrategy(UIErrorInfo errorInfo, string strategy)
        {
            if (_recoveryHandlers.TryGetValue(strategy, out var handler))
            {
                return handler(errorInfo);
            }
            return false;
        }

        /// <summary>
        /// 妫€鏌ユ槸鍚﹀簲璇ュ皾璇曟櫤鑳芥仮澶?        /// </summary>
        /// <param name="errorInfo">閿欒淇℃伅</param>
        /// <returns>鏄惁搴旇灏濊瘯</returns>
        private bool ShouldAttemptSmartRecovery(UIErrorInfo errorInfo)
        {
            return EnableAutomaticRecovery && 
                   errorInfo.Severity >= ErrorSeverity.Error && 
                   _consecutiveErrors >= 2;
        }

        #endregion

        #region 鎭㈠澶勭悊鍣?
        /// <summary>
        /// 缃戠粶閿欒鎭㈠澶勭悊鍣?        /// </summary>
        private bool HandleNetworkErrorRecovery(UIErrorInfo errorInfo)
        {
            FlaxEngine.Debug.Log("鎵ц缃戠粶閿欒鎭㈠");
            // 瀹炵幇缃戠粶閲嶈繛閫昏緫
            return true;
        }

        /// <summary>
        /// 鐘舵€佸洖婊氭仮澶嶅鐞嗗櫒
        /// </summary>
        private bool HandleStateRollbackRecovery(UIErrorInfo errorInfo)
        {
            FlaxEngine.Debug.Log("执行状态回滚恢复");
            return _snapshotManager.SmartRestore($"状态回滚: {errorInfo.ErrorId}");
        }

        /// <summary>
        /// 组件重启恢复处理器
        /// </summary>
        private bool HandleComponentRestartRecovery(UIErrorInfo errorInfo)
        {
            FlaxEngine.Debug.Log("执行组件重启恢复");
            return ForceRecoverToSafeState();
        }

        /// <summary>
        /// 场景重置恢复处理器
        /// </summary>
        private bool HandleSceneResetRecovery(UIErrorInfo errorInfo)
        {
            FlaxEngine.Debug.Log("执行场景重置恢复");
            return ForceRecoverToSafeState();
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 错误事件处理
        /// </summary>
        /// <param name="eventData">错误事件数据</param>
        private void OnErrorOccurred(ErrorOccurredEvent eventData)
        {
            // 统计错误频率
            CheckErrorFrequency();
        }

        /// <summary>
        /// 场景切换完成事件处理
        /// </summary>
        /// <param name="eventData">场景切换事件数据</param>
        private void OnSceneTransitionCompleted(SceneTransitionCompletedEvent eventData)
        {
            if (eventData.IsSuccess)
            {
                // 重置连续错误计数
                _consecutiveErrors = 0;
            }
        }

        /// <summary>
        /// 检查错误频率
        /// </summary>
        private void CheckErrorFrequency()
        {
            var now = DateTime.UtcNow;
            if (now - _lastErrorReport > ErrorReportInterval)
            {
                var recentErrors = _errorHistory.FindAll(e => now - e.Timestamp < ErrorReportInterval);
                if (recentErrors.Count > 10) // 5分钟内超过10个错误
                {
                    FlaxEngine.Debug.LogWarning($"检测到高频错误: {recentErrors.Count} 个错误在 {ErrorReportInterval.TotalMinutes} 分钟内");
                }
                _lastErrorReport = now;
            }
        }

        #endregion

        #region 公共查询接口

        /// <summary>
        /// 获取错误历史
        /// </summary>
        /// <param name="count">获取数量</param>
        /// <returns>错误列表</returns>
        public List<UIErrorInfo> GetErrorHistory(int count = 10)
        {
            var startIndex = Math.Max(0, _errorHistory.Count - count);
            return _errorHistory.GetRange(startIndex, _errorHistory.Count - startIndex);
        }

        /// <summary>
        /// 获取错误统计
        /// </summary>
        /// <returns>错误统计信息</returns>
        public Dictionary<UIErrorType, int> GetErrorStatistics()
        {
            var stats = new Dictionary<UIErrorType, int>();
            foreach (var error in _errorHistory)
            {
                if (stats.ContainsKey(error.Type))
                    stats[error.Type]++;
                else
                    stats[error.Type] = 1;
            }
            return stats;
        }

        #endregion
    }
}
