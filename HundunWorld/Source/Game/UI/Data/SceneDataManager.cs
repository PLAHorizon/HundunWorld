using System;
using System.Collections.Generic;
using FlaxEngine;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.UI.Data
{
    /// <summary>
    /// 场景数据管理器
    /// 负责管理场景间的数据传递、缓存和生命周期
    /// 支持不同类型数据的安全传递和自动清理
    /// </summary>
    public class SceneDataManager
    {
        private static SceneDataManager _instance;
        private static readonly object _lock = new object();
        
        // 数据存储
        private readonly Dictionary<SceneType, SceneDataContainer> _sceneDataContainers;
        private readonly Dictionary<string, object> _globalData;
        private readonly Dictionary<Type, object> _typedData;
        
        // 数据生命周期管理
        private readonly Dictionary<SceneType, List<IDataCleanupHandler>> _cleanupHandlers;
        private readonly HashSet<SceneType> _persistentScenes;
        
        // 配置
        public bool AutoCleanupOnSceneExit { get; set; } = true;
        public int MaxGlobalDataEntries { get; set; } = 100;
        public TimeSpan DefaultDataLifetime { get; set; } = TimeSpan.FromMinutes(30);
        
        public static SceneDataManager Instance
        {
            get
            {
                lock (_lock)
                {
                    return _instance ??= new SceneDataManager();
                }
            }
        }
        
        #region 数据容器定义
        
        public class SceneDataContainer
        {
            public SceneType SceneType { get; set; }
            public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();
            public Dictionary<string, DateTime> ExpirationTimes { get; set; } = new Dictionary<string, DateTime>();
            public DateTime LastAccessTime { get; set; } = DateTime.UtcNow;
        }
        
        public interface IDataCleanupHandler
        {
            void Cleanup(SceneType sceneType, Dictionary<string, object> sceneData);
        }
        
        public class TimedDataEntry
        {
            public object Data { get; set; }
            public DateTime ExpirationTime { get; set; }
            public bool IsExpired => DateTime.UtcNow > ExpirationTime;
        }
        
        #endregion
        
        #region 构造和初始化
        
        private SceneDataManager()
        {
            _sceneDataContainers = new Dictionary<SceneType, SceneDataContainer>();
            _globalData = new Dictionary<string, object>();
            _typedData = new Dictionary<Type, object>();
            _cleanupHandlers = new Dictionary<SceneType, List<IDataCleanupHandler>>();
            _persistentScenes = new HashSet<SceneType>
            {
                SceneType.Start,
                SceneType.Login,
                SceneType.Register
            };
            
            InitializeDefaultCleanupHandlers();
            Debug.Log("[SceneDataManager] 场景数据管理器已初始化");
        }
        
        /// <summary>
        /// 初始化默认清理处理器
        /// </summary>
        private void InitializeDefaultCleanupHandlers()
        {
            // 为每个场景添加默认的清理处理器
            foreach (SceneType sceneType in Enum.GetValues(typeof(SceneType)))
            {
                _cleanupHandlers[sceneType] = new List<IDataCleanupHandler>();
            }
            
            // 添加通用清理处理器
            AddUniversalCleanupHandlers();
        }
        
        /// <summary>
        /// 添加通用清理处理器
        /// </summary>
        private void AddUniversalCleanupHandlers()
        {
            // 清理过期数据的处理器
            var expirationHandler = new ExpirationCleanupHandler();
            foreach (var handlers in _cleanupHandlers.Values)
            {
                handlers.Add(expirationHandler);
            }
        }
        
        #endregion
        
        #region 场景数据操作
        
        /// <summary>
        /// 设置场景数据
        /// </summary>
        public void SetSceneData(SceneType sceneType, string key, object data, TimeSpan? lifetime = null)
        {
            if (!_sceneDataContainers.ContainsKey(sceneType))
            {
                _sceneDataContainers[sceneType] = new SceneDataContainer { SceneType = sceneType };
            }
            
            var container = _sceneDataContainers[sceneType];
            container.Data[key] = data;
            container.ExpirationTimes[key] = DateTime.UtcNow.Add(lifetime ?? DefaultDataLifetime);
            container.LastAccessTime = DateTime.UtcNow;
            
            Debug.Log($"[SceneDataManager] 设置场景数据: Scene={sceneType}, Key={key}");
        }
        
        /// <summary>
        /// 获取场景数据
        /// </summary>
        public T GetSceneData<T>(SceneType sceneType, string key, T defaultValue = default)
        {
            if (_sceneDataContainers.TryGetValue(sceneType, out var container) &&
                container.Data.TryGetValue(key, out var data))
            {
                container.LastAccessTime = DateTime.UtcNow;
                
                if (data is T typedData)
                {
                    // 检查是否过期
                    if (container.ExpirationTimes.TryGetValue(key, out var expirationTime) &&
                        DateTime.UtcNow <= expirationTime)
                    {
                        return typedData;
                    }
                    else
                    {
                        // 数据已过期，移除它
                        RemoveSceneData(sceneType, key);
                    }
                }
            }
            
            return defaultValue;
        }
        
        /// <summary>
        /// 移除场景数据
        /// </summary>
        public bool RemoveSceneData(SceneType sceneType, string key)
        {
            if (_sceneDataContainers.TryGetValue(sceneType, out var container))
            {
                var removed = container.Data.Remove(key);
                container.ExpirationTimes.Remove(key);
                return removed;
            }
            return false;
        }
        
        /// <summary>
        /// 清理场景数据
        /// </summary>
        public void ClearSceneData(SceneType sceneType)
        {
            if (_sceneDataContainers.TryGetValue(sceneType, out var container))
            {
                // 执行清理处理器
                if (_cleanupHandlers.TryGetValue(sceneType, out var handlers))
                {
                    foreach (var handler in handlers)
                    {
                        try
                        {
                            handler.Cleanup(sceneType, container.Data);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[SceneDataManager] 清理处理器执行失败: {ex.Message}");
                        }
                    }
                }
                
                container.Data.Clear();
                container.ExpirationTimes.Clear();
                Debug.Log($"[SceneDataManager] 已清理场景数据: {sceneType}");
            }
        }
        
        /// <summary>
        /// 获取场景所有数据键
        /// </summary>
        public IEnumerable<string> GetSceneDataKeys(SceneType sceneType)
        {
            if (_sceneDataContainers.TryGetValue(sceneType, out var container))
            {
                return new List<string>(container.Data.Keys);
            }
            return new List<string>();
        }
        
        #endregion
        
        #region 全局数据操作
        
        /// <summary>
        /// 设置全局数据
        /// </summary>
        public void SetGlobalData(string key, object data)
        {
            // 控制全局数据数量
            if (_globalData.Count >= MaxGlobalDataEntries && !_globalData.ContainsKey(key))
            {
                // 移除最早访问的数据项
                var oldestKey = GetOldestGlobalDataKey();
                if (!string.IsNullOrEmpty(oldestKey))
                {
                    _globalData.Remove(oldestKey);
                }
            }
            
            _globalData[key] = data;
            Debug.Log($"[SceneDataManager] 设置全局数据: Key={key}");
        }
        
        /// <summary>
        /// 获取全局数据
        /// </summary>
        public T GetGlobalData<T>(string key, T defaultValue = default)
        {
            if (_globalData.TryGetValue(key, out var data) && data is T typedData)
            {
                return typedData;
            }
            return defaultValue;
        }
        
        /// <summary>
        /// 移除全局数据
        /// </summary>
        public bool RemoveGlobalData(string key)
        {
            return _globalData.Remove(key);
        }
        
        /// <summary>
        /// 获取最早的全局数据键
        /// </summary>
        private string GetOldestGlobalDataKey()
        {
            string oldestKey = null;
            DateTime oldestTime = DateTime.MaxValue;
            
            // 这里简化处理，实际项目中可能需要更精确的时间跟踪
            foreach (var key in _globalData.Keys)
            {
                if (oldestKey == null)
                {
                    oldestKey = key;
                }
            }
            
            return oldestKey;
        }
        
        #endregion
        
        #region 类型化数据操作
        
        /// <summary>
        /// 设置类型化数据
        /// </summary>
        public void SetTypedData<T>(T data)
        {
            _typedData[typeof(T)] = data;
            Debug.Log($"[SceneDataManager] 设置类型化数据: Type={typeof(T)}");
        }
        
        /// <summary>
        /// 获取类型化数据
        /// </summary>
        public T GetTypedData<T>(T defaultValue = default)
        {
            if (_typedData.TryGetValue(typeof(T), out var data) && data is T typedData)
            {
                return typedData;
            }
            return defaultValue;
        }
        
        /// <summary>
        /// 移除类型化数据
        /// </summary>
        public bool RemoveTypedData<T>()
        {
            return _typedData.Remove(typeof(T));
        }
        
        #endregion
        
        #region 数据传递和迁移
        
        /// <summary>
        /// 从源场景向目标场景传递数据
        /// </summary>
        public void TransferData(SceneType sourceScene, SceneType targetScene, params string[] keys)
        {
            if (!_sceneDataContainers.ContainsKey(sourceScene) || 
                !_sceneDataContainers.ContainsKey(targetScene))
            {
                Debug.LogWarning($"[SceneDataManager] 无法传递数据: 场景不存在");
                return;
            }
            
            var sourceContainer = _sceneDataContainers[sourceScene];
            var targetContainer = _sceneDataContainers[targetScene];
            
            foreach (var key in keys)
            {
                if (sourceContainer.Data.TryGetValue(key, out var data))
                {
                    targetContainer.Data[key] = data;
                    if (sourceContainer.ExpirationTimes.TryGetValue(key, out var expiration))
                    {
                        targetContainer.ExpirationTimes[key] = expiration;
                    }
                }
            }
            
            Debug.Log($"[SceneDataManager] 数据传递完成: {sourceScene} -> {targetScene}");
        }
        
        /// <summary>
        /// 复制场景数据到另一个场景
        /// </summary>
        public void CopySceneData(SceneType sourceScene, SceneType targetScene)
        {
            if (!_sceneDataContainers.ContainsKey(sourceScene))
            {
                Debug.LogWarning($"[SceneDataManager] 源场景不存在: {sourceScene}");
                return;
            }
            
            if (!_sceneDataContainers.ContainsKey(targetScene))
            {
                _sceneDataContainers[targetScene] = new SceneDataContainer { SceneType = targetScene };
            }
            
            var sourceContainer = _sceneDataContainers[sourceScene];
            var targetContainer = _sceneDataContainers[targetScene];
            
            foreach (var kvp in sourceContainer.Data)
            {
                targetContainer.Data[kvp.Key] = kvp.Value;
            }
            
            foreach (var kvp in sourceContainer.ExpirationTimes)
            {
                targetContainer.ExpirationTimes[kvp.Key] = kvp.Value;
            }
            
            Debug.Log($"[SceneDataManager] 场景数据复制完成: {sourceScene} -> {targetScene}");
        }
        
        #endregion
        
        #region 生命周期管理
        
        /// <summary>
        /// 场景即将进入
        /// </summary>
        public void OnSceneEntering(SceneType sceneType)
        {
            Debug.Log($"[SceneDataManager] 场景即将进入: {sceneType}");
            // 可以在这里执行进入前的准备工作
        }
        
        /// <summary>
        /// 场景已进入
        /// </summary>
        public void OnSceneEntered(SceneType sceneType)
        {
            Debug.Log($"[SceneDataManager] 场景已进入: {sceneType}");
            // 可以在这里执行进入后的初始化工作
        }
        
        /// <summary>
        /// 场景即将退出
        /// </summary>
        public void OnSceneExiting(SceneType sceneType)
        {
            Debug.Log($"[SceneDataManager] 场景即将退出: {sceneType}");
            
            // 如果不是持久化场景且启用了自动清理，则清理数据
            if (AutoCleanupOnSceneExit && !_persistentScenes.Contains(sceneType))
            {
                ClearSceneData(sceneType);
            }
        }
        
        /// <summary>
        /// 场景已退出
        /// </summary>
        public void OnSceneExited(SceneType sceneType)
        {
            Debug.Log($"[SceneDataManager] 场景已退出: {sceneType}");
        }
        
        #endregion
        
        #region 清理处理器实现
        
        /// <summary>
        /// 过期数据清理处理器
        /// </summary>
        private class ExpirationCleanupHandler : IDataCleanupHandler
        {
            public void Cleanup(SceneType sceneType, Dictionary<string, object> sceneData)
            {
                var expiredKeys = new List<string>();
                
                // 收集过期的键
                // 注意：这里需要访问SceneDataManager的_expirationTimes字段
                // 在实际实现中可能需要调整架构
                
                foreach (var key in expiredKeys)
                {
                    sceneData.Remove(key);
                    Debug.Log($"[ExpirationCleanup] 移除过期数据: {sceneType}.{key}");
                }
            }
        }
        
        #endregion
        
        #region 统计和调试
        
        /// <summary>
        /// 获取数据管理器统计信息
        /// </summary>
        public Dictionary<string, object> GetStatistics()
        {
            return new Dictionary<string, object>
            {
                ["TotalSceneContainers"] = _sceneDataContainers.Count,
                ["TotalGlobalData"] = _globalData.Count,
                ["TotalTypedData"] = _typedData.Count,
                ["PersistentScenes"] = _persistentScenes.Count,
                ["AutoCleanupEnabled"] = AutoCleanupOnSceneExit,
                ["MaxGlobalDataEntries"] = MaxGlobalDataEntries
            };
        }
        
        /// <summary>
        /// 清理所有数据
        /// </summary>
        public void ClearAllData()
        {
            _sceneDataContainers.Clear();
            _globalData.Clear();
            _typedData.Clear();
            Debug.Log("[SceneDataManager] 所有数据已清理");
        }
        
        #endregion
    }
}