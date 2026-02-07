using System;
using System.Collections.Generic;
using Horizon.Game.Message.Enums;
using HundunWorld.Game.UI.Enums;

namespace HundunWorld.Game.UI.States
{
    /// <summary>
    /// 单个场景的状态信息
    /// 管理每个UI场景的生命周期和相关数据
    /// </summary>
    [Serializable]
    public class SceneState
    {
        /// <summary>
        /// 场景类型
        /// </summary>
        public SceneType SceneType { get; set; }

        /// <summary>
        /// 当前生命周期状态
        /// </summary>
        public SceneLifecycleState LifecycleState { get; set; } = SceneLifecycleState.Uninitialized;

        /// <summary>
        /// 是否为活动场景
        /// </summary>
        public bool IsActive { get; set; } = false;

        /// <summary>
        /// 是否已加载
        /// </summary>
        public bool IsLoaded { get; set; } = false;

        /// <summary>
        /// 是否可见
        /// </summary>
        public bool IsVisible { get; set; } = false;

        /// <summary>
        /// 场景本地数据
        /// 存储该场景特有的状态数据
        /// </summary>
        public Dictionary<string, object> LocalData { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// 访问该场景所需的权限列表
        /// </summary>
        public List<string> RequiredPermissions { get; set; } = new List<string>();

        /// <summary>
        /// 场景激活时间
        /// </summary>
        public DateTime ActivatedTime { get; set; } = DateTime.MinValue;

        /// <summary>
        /// 场景创建时间
        /// </summary>
        public DateTime CreatedTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 最后访问时间
        /// </summary>
        public DateTime LastAccessTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 场景配置参数
        /// </summary>
        public Dictionary<string, object> Configuration { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// 错误信息
        /// </summary>
        public string ErrorMessage { get; set; } = "";

        /// <summary>
        /// 加载进度 (0.0 - 1.0)
        /// </summary>
        public float LoadingProgress { get; set; } = 0.0f;

        /// <summary>
        /// 场景优先级（用于资源管理）
        /// </summary>
        public int Priority { get; set; } = 0;

        /// <summary>
        /// 是否可以被缓存
        /// </summary>
        public bool CanBeCached { get; set; } = true;

        /// <summary>
        /// 缓存过期时间（分钟）
        /// </summary>
        public int CacheExpirationMinutes { get; set; } = 30;
        public string LoadingMessage { get; internal set; }
        public bool IsLoading { get; internal set; }
        public DateTime LoadTime { get; internal set; }

        /// <summary>
        /// 创建SceneState的深拷贝
        /// </summary>
        /// <returns>SceneState的副本</returns>
        public SceneState Clone()
        {
            return new SceneState
            {
                SceneType = this.SceneType,
                LifecycleState = this.LifecycleState,
                IsActive = this.IsActive,
                IsLoaded = this.IsLoaded,
                IsVisible = this.IsVisible,
                LocalData = new Dictionary<string, object>(this.LocalData),
                RequiredPermissions = new List<string>(this.RequiredPermissions),
                ActivatedTime = this.ActivatedTime,
                CreatedTime = this.CreatedTime,
                LastAccessTime = this.LastAccessTime,
                Configuration = new Dictionary<string, object>(this.Configuration),
                ErrorMessage = this.ErrorMessage,
                LoadingProgress = this.LoadingProgress,
                Priority = this.Priority,
                CanBeCached = this.CanBeCached,
                CacheExpirationMinutes = this.CacheExpirationMinutes
            };
        }

        /// <summary>
        /// 设置生命周期状态
        /// </summary>
        /// <param name="newState">新状态</param>
        public void SetLifecycleState(SceneLifecycleState newState)
        {
            LifecycleState = newState;
            LastAccessTime = DateTime.UtcNow;

            // 根据状态自动更新相关属性
            switch (newState)
            {
                case SceneLifecycleState.Active:
                    IsActive = true;
                    IsVisible = true;
                    ActivatedTime = DateTime.UtcNow;
                    break;

                case SceneLifecycleState.Hidden:
                    IsActive = false;
                    IsVisible = false;
                    break;

                case SceneLifecycleState.Ready:
                    IsLoaded = true;
                    break;

                case SceneLifecycleState.Error:
                    IsActive = false;
                    break;
            }
        }

        /// <summary>
        /// 设置本地数据
        /// </summary>
        /// <param name="key">数据键</param>
        /// <param name="value">数据值</param>
        public void SetLocalData(string key, object value)
        {
            LocalData[key] = value;
            LastAccessTime = DateTime.UtcNow;
        }

        /// <summary>
        /// 获取本地数据
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="key">数据键</param>
        /// <returns>数据值，如果不存在则返回默认值</returns>
        public T GetLocalData<T>(string key)
        {
            if (LocalData.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }
            return default(T);
        }

        /// <summary>
        /// 移除本地数据
        /// </summary>
        /// <param name="key">数据键</param>
        /// <returns>是否成功移除</returns>
        public bool RemoveLocalData(string key)
        {
            return LocalData.Remove(key);
        }

        /// <summary>
        /// 设置配置参数
        /// </summary>
        /// <param name="key">配置键</param>
        /// <param name="value">配置值</param>
        public void SetConfiguration(string key, object value)
        {
            Configuration[key] = value;
        }

        /// <summary>
        /// 获取配置参数
        /// </summary>
        /// <typeparam name="T">参数类型</typeparam>
        /// <param name="key">配置键</param>
        /// <returns>配置值，如果不存在则返回默认值</returns>
        public T GetConfiguration<T>(string key)
        {
            if (Configuration.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }
            return default(T);
        }

        /// <summary>
        /// 设置错误状态
        /// </summary>
        /// <param name="errorMessage">错误信息</param>
        public void SetError(string errorMessage)
        {
            ErrorMessage = errorMessage;
            SetLifecycleState(SceneLifecycleState.Error);
        }

        /// <summary>
        /// 清除错误状态
        /// </summary>
        public void ClearError()
        {
            ErrorMessage = "";
            if (LifecycleState == SceneLifecycleState.Error)
            {
                SetLifecycleState(SceneLifecycleState.Ready);
            }
        }

        /// <summary>
        /// 检查是否可以切换到该场景
        /// </summary>
        /// <param name="userPermissions">用户权限列表</param>
        /// <returns>是否有权限访问</returns>
        public bool CanAccess(List<string> userPermissions)
        {
            if (RequiredPermissions == null || RequiredPermissions.Count == 0)
            {
                return true;
            }

            if (userPermissions == null || userPermissions.Count == 0)
            {
                return false;
            }

            // 检查用户是否拥有所有必需的权限
            foreach (var requiredPermission in RequiredPermissions)
            {
                if (!userPermissions.Contains(requiredPermission))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 检查缓存是否过期
        /// </summary>
        /// <returns>是否过期</returns>
        public bool IsCacheExpired()
        {
            if (!CanBeCached) return true;

            var expiration = LastAccessTime.AddMinutes(CacheExpirationMinutes);
            return DateTime.UtcNow > expiration;
        }

        /// <summary>
        /// 重置场景状态到初始状态
        /// </summary>
        public void Reset()
        {
            LifecycleState = SceneLifecycleState.Uninitialized;
            IsActive = false;
            IsLoaded = false;
            IsVisible = false;
            ActivatedTime = DateTime.MinValue;
            LastAccessTime = DateTime.UtcNow;
            ErrorMessage = "";
            LoadingProgress = 0.0f;
            LocalData.Clear();
        }
    }
}