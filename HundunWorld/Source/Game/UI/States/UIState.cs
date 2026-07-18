using System;
using System.Collections.Generic;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;


namespace HundunWorld.Game.UI.States
{
    /// <summary>
    /// UI全局状态模型
    /// 包含所有UI相关的状态信息，确保状态的一致性和可追溯性
    /// </summary>
    [Serializable]
    public class UIState
    {
        /// <summary>
        /// 当前场景类型
        /// </summary>
        public SceneType CurrentScene { get; set; } = SceneType.Start;

        /// <summary>
        /// 前一个场景类型
        /// </summary>
        public SceneType PreviousScene { get; set; } = SceneType.Start;

        /// <summary>
        /// 是否正在切换场景
        /// </summary>
        public bool IsTransitioning { get; set; } = false;

        /// <summary>
        /// 当前切换操作的唯一标识
        /// </summary>
        public string TransitionId { get; set; } = "";

        /// <summary>
        /// 场景间共享的数据
        /// 键为数据标识，值为序列化的数据对象
        /// </summary>
        public Dictionary<string, object> SceneData { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// 用户会话信息
        /// </summary>
        public UserSession UserSession { get; set; } = new UserSession();

        /// <summary>
        /// 角色列表
        /// </summary>
        public List<CharacterInfo> Characters { get; set; } = new List<CharacterInfo>();

        /// <summary>
        /// 当前选中的角色
        /// </summary>
        public CharacterInfo SelectedCharacter { get; set; } = null;

        /// <summary>
        /// 最后更新时间戳
        /// </summary>
        public DateTime LastUpdateTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 状态版本号，用于并发控制
        /// </summary>
        public long Version { get; set; } = 1;

        /// <summary>
        /// 是否有错误状态
        /// </summary>
        public bool HasError { get; set; } = false;

        /// <summary>
        /// 当前错误信息
        /// </summary>
        public string ErrorMessage { get; set; } = "";

        /// <summary>
        /// 是否正在加载
        /// </summary>
        public bool IsLoading { get; set; } = false;

        /// <summary>
        /// 加载进度 (0.0 - 1.0)
        /// </summary>
        public float LoadingProgress { get; set; } = 0.0f;

        /// <summary>
        /// 加载状态描述
        /// </summary>
        public string LoadingMessage { get; set; } = "";

        /// <summary>
        /// 创建UIState的深拷贝
        /// </summary>
        /// <returns>UIState的副本</returns>
        public UIState Clone()
        {
            return new UIState
            {
                CurrentScene = this.CurrentScene,
                PreviousScene = this.PreviousScene,
                IsTransitioning = this.IsTransitioning,
                TransitionId = this.TransitionId,
                SceneData = new Dictionary<string, object>(this.SceneData),
                UserSession = this.UserSession?.Clone() ?? new UserSession(),
                Characters = new List<CharacterInfo>(this.Characters ?? new List<CharacterInfo>()),
                SelectedCharacter = this.SelectedCharacter,
                LastUpdateTime = this.LastUpdateTime,
                Version = this.Version,
                HasError = this.HasError,
                ErrorMessage = this.ErrorMessage,
                IsLoading = this.IsLoading,
                LoadingProgress = this.LoadingProgress,
                LoadingMessage = this.LoadingMessage
            };
        }

        /// <summary>
        /// 更新状态版本号
        /// </summary>
        public void IncrementVersion()
        {
            Version++;
            LastUpdateTime = DateTime.UtcNow;
        }

        /// <summary>
        /// 设置错误状态
        /// </summary>
        /// <param name="errorMessage">错误信息</param>
        public void SetError(string errorMessage)
        {
            HasError = true;
            ErrorMessage = errorMessage;
            IncrementVersion();
        }

        /// <summary>
        /// 清除错误状态
        /// </summary>
        public void ClearError()
        {
            HasError = false;
            ErrorMessage = "";
            IncrementVersion();
        }

        /// <summary>
        /// 设置加载状态
        /// </summary>
        /// <param name="isLoading">是否正在加载</param>
        /// <param name="message">加载消息</param>
        /// <param name="progress">加载进度</param>
        public void SetLoadingState(bool isLoading, string message = "", float progress = 0.0f)
        {
            IsLoading = isLoading;
            LoadingMessage = message;
            LoadingProgress = progress;
            IncrementVersion();
        }

        /// <summary>
        /// 设置场景数据
        /// </summary>
        /// <param name="key">数据键</param>
        /// <param name="value">数据值</param>
        public void SetSceneData(string key, object value)
        {
            SceneData[key] = value;
            IncrementVersion();
        }

        /// <summary>
        /// 获取场景数据
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="key">数据键</param>
        /// <returns>数据值，如果不存在则返回默认值</returns>
        public T GetSceneData<T>(string key)
        {
            if (SceneData.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }
            return default(T);
        }

        /// <summary>
        /// 移除场景数据
        /// </summary>
        /// <param name="key">数据键</param>
        /// <returns>是否成功移除</returns>
        public bool RemoveSceneData(string key)
        {
            if (SceneData.Remove(key))
            {
                IncrementVersion();
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 用户会话信息扩展
    /// </summary>
    public static class UserSessionExtensions
    {
        /// <summary>
        /// 创建UserSession的深拷贝
        /// </summary>
        /// <param name="session">原始会话</param>
        /// <returns>会话副本</returns>
        public static UserSession Clone(this UserSession session)
        {
            if (session == null) return new UserSession();

            return new UserSession
            {
                Username = session.Username,
                UserId = session.UserId,
                AccessToken = session.AccessToken,
                RefreshToken = session.RefreshToken
            };
        }
    }
    
    
}