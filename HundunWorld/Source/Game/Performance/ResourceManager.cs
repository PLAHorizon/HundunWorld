using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlaxEngine;

namespace Game.Performance
{
    /// <summary>
    /// 资源管理器
    /// 负责优化资源加载、缓存和卸载
    /// </summary>
    public class ResourceManager : IDisposable
    {
        private readonly Dictionary<string, ResourceCacheEntry> _resourceCache = new Dictionary<string, ResourceCacheEntry>();
        private readonly Dictionary<string, Task<object>> _loadingTasks = new Dictionary<string, Task<object>>();
        private readonly object _lockObject = new object();
        private readonly int _maxCacheSize;
        private readonly TimeSpan _defaultExpirationTime;
        private bool _disposed = false;

        public ResourceManager(int maxCacheSize = 1000, int defaultExpirationMinutes = 30)
        {
            _maxCacheSize = maxCacheSize;
            _defaultExpirationTime = TimeSpan.FromMinutes(defaultExpirationMinutes);
        }

        /// <summary>
        /// 异步加载资源
        /// </summary>
        /// <typeparam name="T">资源类型</typeparam>
        /// <param name="resourceId">资源ID</param>
        /// <param name="loader">资源加载器</param>
        /// <param name="expirationTime">过期时间</param>
        /// <returns>资源实例</returns>
        public async Task<T> LoadResourceAsync<T>(string resourceId, Func<Task<T>> loader, 
            TimeSpan? expirationTime = null) where T : class
        {
            // 检查缓存
            if (TryGetCachedResource<T>(resourceId, out var cachedResource))
            {
                return cachedResource;
            }

            // 检查是否正在加载
            Task<object> loadingTask;
            lock (_lockObject)
            {
                if (_loadingTasks.TryGetValue(resourceId, out loadingTask))
                {
                    // 等待加载完成
                   
                    return (T)loadingTask.Result;
                }
                else
                {
                    // 开始加载
                    loadingTask = LoadResourceInternalAsync(resourceId, loader, expirationTime);
                    _loadingTasks[resourceId] = loadingTask;
                }
            }

            try
            {
                var result = await loadingTask;
                return (T)result;
            }
            finally
            {
                lock (_lockObject)
                {
                    _loadingTasks.Remove(resourceId);
                }
            }
        }

        /// <summary>
        /// 内部资源加载方法
        /// </summary>
        private async Task<object> LoadResourceInternalAsync<T>(string resourceId, Func<Task<T>> loader,
            TimeSpan? expirationTime = null) where T : class
        {
            try
            {
                // 加载资源
                var resource = await loader();

                // 缓存资源
                CacheResource(resourceId, resource, expirationTime ?? _defaultExpirationTime);

                return resource;
            }
            catch (Exception ex)
            {
                Debug.LogError($"资源加载失败 [{resourceId}]: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 尝试从缓存获取资源
        /// </summary>
        private bool TryGetCachedResource<T>(string resourceId, out T resource) where T : class
        {
            resource = null;
            lock (_lockObject)
            {
                if (_resourceCache.TryGetValue(resourceId, out var entry))
                {
                    // 检查是否过期
                    if (DateTime.UtcNow < entry.ExpirationTime)
                    {
                        resource = (T)entry.Resource;
                        entry.LastAccessTime = DateTime.UtcNow;
                        entry.AccessCount++;
                        return true;
                    }
                    else
                    {
                        // 移除过期资源
                        _resourceCache.Remove(resourceId);
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 缓存资源
        /// </summary>
        private void CacheResource(string resourceId, object resource, TimeSpan expirationTime)
        {
            lock (_lockObject)
            {
                // 如果缓存已满，移除最少使用的资源
                if (_resourceCache.Count >= _maxCacheSize)
                {
                    EvictLeastUsedResources();
                }

                var entry = new ResourceCacheEntry
                {
                    Resource = resource,
                    ExpirationTime = DateTime.UtcNow.Add(expirationTime),
                    LastAccessTime = DateTime.UtcNow,
                    AccessCount = 1
                };

                _resourceCache[resourceId] = entry;
            }
        }

        /// <summary>
        /// 移除最少使用的资源
        /// </summary>
        private void EvictLeastUsedResources()
        {
            string leastUsedKey = null;
            long minAccessCount = int.MaxValue;
            DateTime oldestAccessTime = DateTime.MaxValue;

            foreach (var kvp in _resourceCache)
            {
                // 优先移除访问次数最少的资源
                if (kvp.Value.AccessCount < minAccessCount)
                {
                    minAccessCount = kvp.Value.AccessCount;
                    oldestAccessTime = kvp.Value.LastAccessTime;
                    leastUsedKey = kvp.Key;
                }
                // 如果访问次数相同，移除最久未访问的资源
                else if (kvp.Value.AccessCount == minAccessCount && 
                         kvp.Value.LastAccessTime < oldestAccessTime)
                {
                    oldestAccessTime = kvp.Value.LastAccessTime;
                    leastUsedKey = kvp.Key;
                }
            }

            if (leastUsedKey != null)
            {
                _resourceCache.Remove(leastUsedKey);
            }
        }

        /// <summary>
        /// 预加载资源
        /// </summary>
        /// <param name="resourceId">资源ID</param>
        /// <param name="loader">资源加载器</param>
        /// <param name="expirationTime">过期时间</param>
        public void PreloadResource(string resourceId, Func<Task<object>> loader, 
            TimeSpan? expirationTime = null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await LoadResourceAsync<object>(resourceId, loader, expirationTime);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"资源预加载失败 [{resourceId}]: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 移除资源缓存
        /// </summary>
        /// <param name="resourceId">资源ID</param>
        public void RemoveResource(string resourceId)
        {
            lock (_lockObject)
            {
                _resourceCache.Remove(resourceId);
            }
        }

        /// <summary>
        /// 清空资源缓存
        /// </summary>
        public void ClearCache()
        {
            lock (_lockObject)
            {
                _resourceCache.Clear();
            }
        }

        /// <summary>
        /// 获取缓存统计信息
        /// </summary>
        /// <returns>缓存统计信息</returns>
        public ResourceCacheStats GetCacheStats()
        {
            lock (_lockObject)
            {
                int totalResources = _resourceCache.Count;
                long totalAccessCount = 0;
                int expiredResources = 0;

                foreach (var entry in _resourceCache.Values)
                {
                    totalAccessCount += entry.AccessCount;
                    if (DateTime.UtcNow >= entry.ExpirationTime)
                    {
                        expiredResources++;
                    }
                }

                return new ResourceCacheStats
                {
                    TotalResources = totalResources,
                    ExpiredResources = expiredResources,
                    TotalAccessCount = totalAccessCount,
                    CacheHitRate = totalAccessCount > 0 ? (double)(totalAccessCount - expiredResources) / totalAccessCount : 0
                };
            }
        }

        /// <summary>
        /// 清理过期资源
        /// </summary>
        public void CleanupExpiredResources()
        {
            lock (_lockObject)
            {
                var expiredKeys = new List<string>();
                foreach (var kvp in _resourceCache)
                {
                    if (DateTime.UtcNow >= kvp.Value.ExpirationTime)
                    {
                        expiredKeys.Add(kvp.Key);
                    }
                }

                foreach (var key in expiredKeys)
                {
                    _resourceCache.Remove(key);
                }
            }
        }
        
        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 释放托管资源
                    ClearCache();
                    _loadingTasks.Clear();
                }

                // 释放非托管资源

                _disposed = true;
            }
        }

        /// <summary>
        /// 析构函数
        /// </summary>
        ~ResourceManager()
        {
            Dispose(false);
        }
    }

    /// <summary>
    /// 资源缓存条目
    /// </summary>
    public class ResourceCacheEntry
    {
        /// <summary>
        /// 资源对象
        /// </summary>
        public object Resource { get; set; }

        /// <summary>
        /// 过期时间
        /// </summary>
        public DateTime ExpirationTime { get; set; }

        /// <summary>
        /// 最后访问时间
        /// </summary>
        public DateTime LastAccessTime { get; set; }

        /// <summary>
        /// 访问次数
        /// </summary>
        public long AccessCount { get; set; }
    }

    /// <summary>
    /// 资源缓存统计信息
    /// </summary>
    public class ResourceCacheStats
    {
        /// <summary>
        /// 总资源数
        /// </summary>
        public int TotalResources { get; set; }

        /// <summary>
        /// 过期资源数
        /// </summary>
        public int ExpiredResources { get; set; }

        /// <summary>
        /// 总访问次数
        /// </summary>
        public long TotalAccessCount { get; set; }

        /// <summary>
        /// 缓存命中率
        /// </summary>
        public double CacheHitRate { get; set; }
    }
}