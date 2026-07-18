using System;

using HundunWorld.Game.UI.States;
using Horizon.Game.Message.Network;
using System.Collections.Generic;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.UI.Events
{
    /// <summary>
    /// UI浜嬩欢鍩虹被
    /// 鎵€鏈塙I鐩稿叧浜嬩欢閮藉簲璇ョ户鎵挎绫?    /// </summary>
    public abstract class UIEvent
    {
        /// <summary>
        /// 浜嬩欢鍞竴鏍囪瘑
        /// </summary>
        public string EventId { get; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 浜嬩欢鏃堕棿鎴?        /// </summary>
        public DateTime Timestamp { get; } = DateTime.UtcNow;

        /// <summary>
        /// 浜嬩欢婧愭爣璇?        /// </summary>
        public string Source { get; set; } = "";

        /// <summary>
        /// 浜嬩欢浼樺厛绾?        /// </summary>
        public int Priority { get; set; } = 0;

        /// <summary>
        /// 鏄惁鍙互鍙栨秷
        /// </summary>
        public virtual bool CanCancel => false;

        /// <summary>
        /// 鏄惁宸茶鍙栨秷
        /// </summary>
        public bool IsCancelled { get; private set; } = false;

        /// <summary>
        /// 鍙栨秷浜嬩欢
        /// </summary>
        public void Cancel()
        {
            if (CanCancel)
            {
                IsCancelled = true;
            }
        }
    }

    /// <summary>
    /// 鍦烘櫙鍒囨崲浜嬩欢
    /// </summary>
    public class SceneTransitionEvent : UIEvent
    {
        public SceneType FromScene { get; set; }
        public SceneType ToScene { get; set; }
        public TransitionState TransitionState { get; set; }
        public override bool CanCancel => true;

        public SceneTransitionEvent(SceneType from, SceneType to, TransitionState transition)
        {
            FromScene = from;
            ToScene = to;
            TransitionState = transition;
        }
    }

    /// <summary>
    /// 鍦烘櫙鍒囨崲寮€濮嬩簨浠?    /// </summary>
    public class SceneTransitionStartedEvent : UIEvent
    {
        public SceneType FromScene { get; set; }
        public SceneType ToScene { get; set; }
        public TransitionState TransitionState { get; set; }

        public SceneTransitionStartedEvent(SceneType from, SceneType to, TransitionState transition)
        {
            FromScene = from;
            ToScene = to;
            TransitionState = transition;
        }
    }

    /// <summary>
    /// 鍦烘櫙鍒囨崲瀹屾垚浜嬩欢
    /// </summary>
    public class SceneTransitionCompletedEvent : UIEvent
    {
        public SceneType FromScene { get; set; }
        public SceneType ToScene { get; set; }
        public TransitionState TransitionState { get; set; }
        public bool IsSuccess { get; set; } = true;

        public SceneTransitionCompletedEvent(SceneType from, SceneType to, TransitionState transition, bool success = true)
        {
            FromScene = from;
            ToScene = to;
            TransitionState = transition;
            IsSuccess = success;
        }
    }

    /// <summary>
    /// 鍦烘櫙鍒囨崲杩涘害浜嬩欢
    /// </summary>
    public class SceneTransitionProgressEvent : UIEvent
    {
        public SceneType FromScene { get; set; }
        public SceneType ToScene { get; set; }
        public TransitionState TransitionState { get; set; }
        public float Progress { get; set; }

        public SceneTransitionProgressEvent(SceneType from, SceneType to, TransitionState transition, float progress)
        {
            FromScene = from;
            ToScene = to;
            TransitionState = transition;
            Progress = progress;
        }
    }

    /// <summary>
    /// 鐘舵€佸彉鏇翠簨浠?    /// </summary>
    public class StateChangedEvent : UIEvent
    {
        public UIState OldState { get; set; }
        public UIState NewState { get; set; }
        public string ChangeDescription { get; set; } = "";

        public StateChangedEvent(UIState oldState, UIState newState, string description = "")
        {
            OldState = oldState;
            NewState = newState;
            ChangeDescription = description;
        }
    }

    /// <summary>
    /// 鍔犺浇鐘舵€佸彉鏇翠簨浠?    /// </summary>
    public class LoadingStateChangedEvent : UIEvent
    {
        public bool IsLoading { get; set; }
        public string LoadingMessage { get; set; } = "";
        public float Progress { get; set; } = 0.0f;

        public LoadingStateChangedEvent(bool isLoading, string message = "", float progress = 0.0f)
        {
            IsLoading = isLoading;
            LoadingMessage = message;
            Progress = progress;
        }
    }

    /// <summary>
    /// 閿欒浜嬩欢
    /// </summary>
    public class ErrorOccurredEvent : UIEvent
    {
        public string ErrorMessage { get; set; } = "";
        public Exception Exception { get; set; } = null;
        public string ErrorCode { get; set; } = "";
        public ErrorSeverity Severity { get; set; } = ErrorSeverity.Warning;

        public ErrorOccurredEvent(string message, Exception exception = null, string code = "", ErrorSeverity severity = ErrorSeverity.Warning)
        {
            ErrorMessage = message;
            Exception = exception;
            ErrorCode = code;
            Severity = severity;
        }
    }

    /// <summary>
    /// 閿欒涓ラ噸绋嬪害鏋氫妇
    /// </summary>
    

    /// <summary>
    /// 鐢ㄦ埛浼氳瘽鍙樻洿浜嬩欢
    /// </summary>
    public class UserSessionChangedEvent : UIEvent
    {
        public UserSession OldSession { get; set; }
        public UserSession NewSession { get; set; }
        public UserSessionChangedEvent() { }
        public UserSessionChangedEvent(UserSession oldSession, UserSession newSession)
        {
            OldSession = oldSession;
            NewSession = newSession;
        }
    }

    /// <summary>
    /// 瑙掕壊鍒楄〃鏇存柊浜嬩欢
    /// </summary>
    public class CharacterListUpdatedEvent : UIEvent
    {
        public List<CharacterInfo> Characters { get; set; }

        public CharacterListUpdatedEvent(List<CharacterInfo> characters)
        {
            Characters = characters ?? new List<CharacterInfo>();
        }
    }

    /// <summary>
    /// 瑙掕壊閫夋嫨鍙樻洿浜嬩欢
    /// </summary>
    public class SelectedCharacterChangedEvent : UIEvent
    {
        public CharacterInfo OldCharacter { get; set; }
        public CharacterInfo NewCharacter { get; set; }

        public SelectedCharacterChangedEvent(CharacterInfo oldCharacter, CharacterInfo newCharacter)
        {
            OldCharacter = oldCharacter;
            NewCharacter = newCharacter;
        }
    }

    /// <summary>
    /// 鍦烘櫙鐘舵€佸彉鏇翠簨浠?    /// </summary>
    public class SceneStateChangedEvent : UIEvent
    {
        public SceneType SceneType { get; set; }
        public SceneState OldState { get; set; }
        public SceneState NewState { get; set; }

        public SceneStateChangedEvent(SceneType sceneType, SceneState oldState, SceneState newState)
        {
            SceneType = sceneType;
            OldState = oldState;
            NewState = newState;
        }
    }

    /// <summary>
    /// 蹇収鍒涘缓浜嬩欢
    /// </summary>
    public class SnapshotCreatedEvent : UIEvent
    {
        public StateSnapshot Snapshot { get; set; }

        public SnapshotCreatedEvent(StateSnapshot snapshot)
        {
            Snapshot = snapshot;
        }
    }

    /// <summary>
    /// 蹇収鎭㈠浜嬩欢
    /// </summary>
    public class SnapshotRestoredEvent : UIEvent
    {
        public StateSnapshot Snapshot { get; set; }
        public bool IsSuccess { get; set; } = true;

        public SnapshotRestoredEvent(StateSnapshot snapshot, bool success = true)
        {
            Snapshot = snapshot;
            IsSuccess = success;
        }
    }

    /// <summary>
    /// UI鏉冮檺鍙樻洿浜嬩欢
    /// </summary>
    public class PermissionChangedEvent : UIEvent
    {
        public List<string> OldPermissions { get; set; }
        public List<string> NewPermissions { get; set; }

        public PermissionChangedEvent(List<string> oldPermissions, List<string> newPermissions)
        {
            OldPermissions = oldPermissions ?? new List<string>();
            NewPermissions = newPermissions ?? new List<string>();
        }
    }

    /// <summary>
    /// 缃戠粶杩炴帴鐘舵€佸彉鏇翠簨浠?    /// </summary>
    public class NetworkStateChangedEvent : UIEvent
    {
        public bool IsConnected { get; set; }
        public string ConnectionInfo { get; set; } = "";

        public NetworkStateChangedEvent(bool isConnected, string connectionInfo = "")
        {
            IsConnected = isConnected;
            ConnectionInfo = connectionInfo;
        }
    }

    /// <summary>
    /// 閰嶇疆鍙樻洿浜嬩欢
    /// </summary>
    public class ConfigurationChangedEvent : UIEvent
    {
        public string ConfigKey { get; set; } = "";
        public object OldValue { get; set; }
        public object NewValue { get; set; }
        public string Key { get; internal set; }
        public ConfigurationChangedEvent() { }
        public ConfigurationChangedEvent(string key, object oldValue, object newValue)
        {
            ConfigKey = key;
            OldValue = oldValue;
            NewValue = newValue;
        }
    }
}
