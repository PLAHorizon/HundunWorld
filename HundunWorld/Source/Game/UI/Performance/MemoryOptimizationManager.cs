using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FlaxEngine;
using HundunWorld.Game.UI.Events;

namespace Game.UI.Performance
{
    /// <summary>
    /// 内存优化管理器
    /// 提供对象池化、缓存管理、内存泄漏防护等功能
    /// </summary>
    public class MemoryOptimizationManager : Script
    {
        #region Singleton
        
        private static MemoryOptimizationManager _instance;
        public static MemoryOptimizationManager Instance => _instance;
        
        #endregion
        
        #region Private Fields
        
        private readonly Dictionary<Type, IObjectPool> _objectPools = new Dictionary<Type, IObjectPool>();
        private readonly Dictionary<string, ICacheManager> _cacheManagers = new Dictionary<string, ICacheManager>();
        private readonly ConcurrentDictionary<WeakReference, string> _trackedObjects = new ConcurrentDictionary<WeakReference, string>();
        
        private readonly System.Threading.Timer _cleanupTimer;
        private readonly System.Threading.Timer _memoryCheckTimer;
        
        // 配置参数
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _memoryCheckInterval = TimeSpan.FromSeconds(30);
        private readonly long _memoryPressureThreshold = 100 * 1024 * 1024; // 100MB
        private readonly int _maxCacheSize = 50;
        
        private bool _isOptimizationEnabled = true;
        private long _lastMemoryUsage = 0;
        
        #endregion
        
        #region Object Pool Interfaces
        
        /// <summary>
        /// 对象池接口
        /// </summary>
        public interface IObjectPool
        {
            object Rent();
            void Return(object obj);
            void Clear();
            int AvailableCount { get; }
            int TotalCount { get; }
        }
        
        /// <summary>
        /// 泛型对象池
        /// </summary>
        public class ObjectPool<T> : IObjectPool where T : class, new()
        {
            private readonly ConcurrentQueue<T> _objects = new ConcurrentQueue<T>();
            private readonly Func<T> _objectFactory;
            private readonly Action<T> _resetAction;
            private readonly int _maxSize;
            private int _totalCount = 0;
            
            public int AvailableCount => _objects.Count;
            public int TotalCount => _totalCount;
            
            public ObjectPool(int maxSize = 100, Func<T> factory = null, Action<T> resetAction = null)
            {
                _maxSize = maxSize;
                _objectFactory = factory ?? (() => new T());
                _resetAction = resetAction;
            }
            
            public object Rent()
            {
                if (_objects.TryDequeue(out T obj))
                {
                    return obj;
                }
                
                var newObj = _objectFactory();
                _totalCount++;
                return newObj;
            }
            
            public void Return(object obj)
            {
                if (obj is T typedObj && _objects.Count < _maxSize)
                {
                    _resetAction?.Invoke(typedObj);
                    _objects.Enqueue(typedObj);
                }
            }
            
            public void Clear()
            {
                while (_objects.TryDequeue(out _)) { }
                _totalCount = 0;
            }
        }
        
        #endregion
        
        #region Cache Manager Interfaces
        
        /// <summary>
        /// 缓存管理器接口
        /// </summary>
        public interface ICacheManager
        {
            void Set(string key, object value, TimeSpan? expiration = null);
            T Get<T>(string key);
            bool Remove(string key);
            void Clear();
            int Count { get; }
        }
        
        /// <summary>
        /// LRU缓存管理器
        /// </summary>
        public class LRUCacheManager : ICacheManager
        {
            private readonly Dictionary<string, CacheItem> _cache = new Dictionary<string, CacheItem>();
            private readonly LinkedList<string> _accessOrder = new LinkedList<string>();
            private readonly int _maxSize;
            
            public int Count => _cache.Count;
            
            private class CacheItem
            {
                public object Value { get; set; }
                public DateTime CreatedAt { get; set; }
                public DateTime? ExpiresAt { get; set; }
                public LinkedListNode<string> AccessNode { get; set; }
            }
            
            public LRUCacheManager(int maxSize = 100)
            {
                _maxSize = maxSize;
            }
            
            public void Set(string key, object value, TimeSpan? expiration = null)
            {
                lock (_cache)
                {
                    if (_cache.ContainsKey(key))
                    {
                        Remove(key);
                    }
                    
                    var expiresAt = expiration.HasValue ? DateTime.Now.Add(expiration.Value) : (DateTime?)null;
                    var accessNode = _accessOrder.AddFirst(key);
                    
                    _cache[key] = new CacheItem
                    {
                        Value = value,
                        CreatedAt = DateTime.Now,
                        ExpiresAt = expiresAt,
                        AccessNode = accessNode
                    };
                    
                    // 检查缓存大小限制
                    while (_cache.Count > _maxSize)
                    {
                        var lru = _accessOrder.Last.Value;
                        Remove(lru);
                    }
                }
            }
            
            public T Get<T>(string key)
            {
                lock (_cache)
                {
                    if (!_cache.TryGetValue(key, out var item))
                        return default(T);
                    
                    // 检查过期
                    if (item.ExpiresAt.HasValue && DateTime.Now > item.ExpiresAt.Value)
                    {
                        Remove(key);
                        return default(T);
                    }
                    
                    // 更新访问顺序
                    _accessOrder.Remove(item.AccessNode);
                    item.AccessNode = _accessOrder.AddFirst(key);
                    
                    return (T)item.Value;
                }
            }
            
            public bool Remove(string key)
            {
                lock (_cache)
                {
                    if (_cache.TryGetValue(key, out var item))
                    {
                        _accessOrder.Remove(item.AccessNode);
                        _cache.Remove(key);
                        return true;
                    }
                    return false;
                }
            }
            
            public void Clear()
            {
                lock (_cache)
                {
                    _cache.Clear();
                    _accessOrder.Clear();
                }
            }
            
            public void RemoveExpired()
            {
                lock (_cache)
                {
                    var now = DateTime.Now;
                    var expiredKeys = _cache
                        .Where(kvp => kvp.Value.ExpiresAt.HasValue && now > kvp.Value.ExpiresAt.Value)
                        .Select(kvp => kvp.Key)
                        .ToList();
                    
                    foreach (var key in expiredKeys)
                    {
                        Remove(key);
                    }
                }
            }
        }
        
        #endregion
        
        #region Memory Leak Detection
        
        /// <summary>
        /// 内存泄漏检测器
        /// </summary>
        public class MemoryLeakDetector
        {
            private readonly Dictionary<Type, int> _objectCounts = new Dictionary<Type, int>();
            private readonly Dictionary<Type, DateTime> _lastGrowthTime = new Dictionary<Type, DateTime>();
            private readonly Dictionary<Type, int> _growthRates = new Dictionary<Type, int>();
            
            public void TrackObject(object obj, string context)
            {
                var type = obj.GetType();
                
                if (!_objectCounts.ContainsKey(type))
                {
                    _objectCounts[type] = 0;
                    _lastGrowthTime[type] = DateTime.Now;
                    _growthRates[type] = 0;
                }
                
                _objectCounts[type]++;
                
                // 检测增长率
                var now = DateTime.Now;
                var timeDiff = now - _lastGrowthTime[type];
                if (timeDiff.TotalMinutes >= 1)
                {
                    var growthRate = _objectCounts[type] - _growthRates[type];
                    _growthRates[type] = _objectCounts[type];
                    _lastGrowthTime[type] = now;
                    
                    // 如果增长率过高，发出警告
                    if (growthRate > 100) // 每分钟超过100个对象
                    {
                        Debug.LogWarning($"[MemoryOptimization] 可能的内存泄漏: {type.Name}, 增长率: {growthRate}/分钟, 总数: {_objectCounts[type]}");
                    }
                }
            }
            
            public void ReleaseObject(object obj)
            {
                var type = obj.GetType();
                if (_objectCounts.ContainsKey(type) && _objectCounts[type] > 0)
                {
                    _objectCounts[type]--;
                }
            }
            
            public Dictionary<Type, int> GetObjectCounts()
            {
                return new Dictionary<Type, int>(_objectCounts);
            }
        }
        
        private readonly MemoryLeakDetector _leakDetector = new MemoryLeakDetector();
        
        #endregion
        
        #region Unity Lifecycle
        
        public override void OnAwake()
        {
            if (_instance == null)
            {
                _instance = this;
                // Flax引擎中不需要DontDestroyOnLoad，使用Actor的持久性机制
                
                InitializeDefaultPools();
                InitializeDefaultCaches();
                
                Debug.Log("[MemoryOptimization] 内存优化管理器已初始化");
            }
            else if (_instance != this)
            {
                Destroy(this);
            }
        }
        
        public override void OnDestroy()
        {
            if (_instance == this)
            {
                _cleanupTimer?.Dispose();
                _memoryCheckTimer?.Dispose();
                
                // 清理所有缓存和对象池
                foreach (var pool in _objectPools.Values)
                {
                    pool.Clear();
                }
                
                foreach (var cache in _cacheManagers.Values)
                {
                    cache.Clear();
                }
                
                _instance = null;
            }
        }
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// 注册对象池
        /// </summary>
        public void RegisterObjectPool<T>(int maxSize = 100, Func<T> factory = null, Action<T> resetAction = null) 
            where T : class, new()
        {
            var pool = new ObjectPool<T>(maxSize, factory, resetAction);
            _objectPools[typeof(T)] = pool;
            
            Debug.Log($"[MemoryOptimization] 注册对象池: {typeof(T).Name}, 最大大小: {maxSize}");
        }
        
        /// <summary>
        /// 从对象池获取对象
        /// </summary>
        public T RentObject<T>() where T : class, new()
        {
            if (_objectPools.TryGetValue(typeof(T), out var pool))
            {
                var obj = (T)pool.Rent();
                _leakDetector.TrackObject(obj, "ObjectPool");
                return obj;
            }
            
            // 如果没有注册对象池，创建新对象并自动注册池
            RegisterObjectPool<T>();
            return RentObject<T>();
        }
        
        /// <summary>
        /// 归还对象到对象池
        /// </summary>
        public void ReturnObject<T>(T obj) where T : class
        {
            if (obj == null) return;
            
            if (_objectPools.TryGetValue(typeof(T), out var pool))
            {
                pool.Return(obj);
                _leakDetector.ReleaseObject(obj);
            }
        }
        
        /// <summary>
        /// 注册缓存管理器
        /// </summary>
        public void RegisterCache(string name, ICacheManager cacheManager)
        {
            _cacheManagers[name] = cacheManager;
            Debug.Log($"[MemoryOptimization] 注册缓存管理器: {name}");
        }
        
        /// <summary>
        /// 获取缓存管理器
        /// </summary>
        public ICacheManager GetCache(string name)
        {
            if (_cacheManagers.TryGetValue(name, out var cache))
                return cache;
            
            // 自动创建LRU缓存
            var newCache = new LRUCacheManager(_maxCacheSize);
            RegisterCache(name, newCache);
            return newCache;
        }
        
        /// <summary>
        /// 启用/禁用优化
        /// </summary>
        public void SetOptimizationEnabled(bool enabled)
        {
            _isOptimizationEnabled = enabled;
            Debug.Log($"[MemoryOptimization] 优化 {(enabled ? "启用" : "禁用")}");
        }
        
        /// <summary>
        /// 执行内存清理
        /// </summary>
        public void PerformCleanup()
        {
            if (!_isOptimizationEnabled) return;
            
            Debug.Log("[MemoryOptimization] 开始内存清理...");
            
            // 清理过期缓存
            foreach (var cache in _cacheManagers.Values)
            {
                if (cache is LRUCacheManager lruCache)
                {
                    lruCache.RemoveExpired();
                }
            }
            
            // 清理弱引用
            CleanupWeakReferences();
            
            // 检查内存压力
            CheckMemoryPressure();
            
            Debug.Log("[MemoryOptimization] 内存清理完成");
        }
        
        /// <summary>
        /// 强制垃圾回收
        /// </summary>
        public void ForceGarbageCollection()
        {
            if (!_isOptimizationEnabled) return;
            
            Debug.Log("[MemoryOptimization] 执行强制垃圾回收...");
            
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            var currentMemory = GC.GetTotalMemory(false);
            Debug.Log($"[MemoryOptimization] 垃圾回收完成，当前内存: {currentMemory / (1024 * 1024):F1} MB");
        }
        
        /// <summary>
        /// 获取内存统计信息
        /// </summary>
        public MemoryStats GetMemoryStats()
        {
            var stats = new MemoryStats
            {
                TotalMemory = GC.GetTotalMemory(false),
                ObjectPools = new Dictionary<string, ObjectPoolStats>(),
                Caches = new Dictionary<string, CacheStats>(),
                TrackedObjects = _leakDetector.GetObjectCounts()
            };
            
            // 对象池统计
            foreach (var kvp in _objectPools)
            {
                var pool = kvp.Value;
                stats.ObjectPools[kvp.Key.Name] = new ObjectPoolStats
                {
                    AvailableCount = pool.AvailableCount,
                    TotalCount = pool.TotalCount,
                    UtilizationRate = pool.TotalCount > 0 ? (float)(pool.TotalCount - pool.AvailableCount) / pool.TotalCount : 0
                };
            }
            
            // 缓存统计
            foreach (var kvp in _cacheManagers)
            {
                var cache = kvp.Value;
                stats.Caches[kvp.Key] = new CacheStats
                {
                    ItemCount = cache.Count,
                    MaxSize = _maxCacheSize // 假设都是相同大小
                };
            }
            
            return stats;
        }
        
        #endregion
        
        #region Private Methods
        
        /// <summary>
        /// 初始化默认对象池
        /// </summary>
        private void InitializeDefaultPools()
        {
            // 注册常用UI对象的对象池
            RegisterObjectPool<Dictionary<string, object>>(50, 
                () => new Dictionary<string, object>(), 
                dict => dict.Clear());
            
            RegisterObjectPool<List<object>>(50, 
                () => new List<object>(), 
                list => list.Clear());
            
            RegisterObjectPool<StringBuilder>(20, 
                () => new System.Text.StringBuilder(), 
                sb => sb.Clear());
        }
        
        /// <summary>
        /// 初始化默认缓存
        /// </summary>
        private void InitializeDefaultCaches()
        {
            RegisterCache("UITextures", new LRUCacheManager(30));
            RegisterCache("UILayouts", new LRUCacheManager(20));
            RegisterCache("UserProfiles", new LRUCacheManager(10));
            RegisterCache("GameData", new LRUCacheManager(50));
        }
        
        /// <summary>
        /// 清理弱引用
        /// </summary>
        private void CleanupWeakReferences()
        {
            var deadReferences = new List<WeakReference>();
            
            foreach (var weakRef in _trackedObjects.Keys)
            {
                if (!weakRef.IsAlive)
                {
                    deadReferences.Add(weakRef);
                }
            }
            
            foreach (var deadRef in deadReferences)
            {
                _trackedObjects.TryRemove(deadRef, out _);
            }
            
            if (deadReferences.Count > 0)
            {
                Debug.Log($"[MemoryOptimization] 清理了 {deadReferences.Count} 个无效弱引用");
            }
        }
        
        /// <summary>
        /// 检查内存压力
        /// </summary>
        private void CheckMemoryPressure()
        {
            var currentMemory = GC.GetTotalMemory(false);
            var memoryIncrease = currentMemory - _lastMemoryUsage;
            
            if (memoryIncrease > _memoryPressureThreshold)
            {
                Debug.LogWarning($"[MemoryOptimization] 检测到内存压力，增长: {memoryIncrease / (1024 * 1024):F1} MB");
                
                // 执行激进清理
                PerformAggressiveCleanup();
            }
            
            _lastMemoryUsage = currentMemory;
        }
        
        /// <summary>
        /// 执行激进清理
        /// </summary>
        private void PerformAggressiveCleanup()
        {
            Debug.Log("[MemoryOptimization] 执行激进内存清理...");
            
            // 清空所有缓存
            foreach (var cache in _cacheManagers.Values)
            {
                cache.Clear();
            }
            
            // 清空所有对象池
            foreach (var pool in _objectPools.Values)
            {
                pool.Clear();
            }
            
            // 强制垃圾回收
            ForceGarbageCollection();
            
            // 发送内存压力事件
            var eventBus = UIEventBus.Instance;
            eventBus?.PublishAsync(new MemoryPressureEvent
            {
                MemoryUsage = GC.GetTotalMemory(false),
                Timestamp = DateTime.Now
            });
        }
        
        #endregion
        
        #region Statistics Classes
        
        /// <summary>
        /// 内存统计信息
        /// </summary>
        public class MemoryStats
        {
            public long TotalMemory { get; set; }
            public Dictionary<string, ObjectPoolStats> ObjectPools { get; set; }
            public Dictionary<string, CacheStats> Caches { get; set; }
            public Dictionary<Type, int> TrackedObjects { get; set; }
        }
        
        /// <summary>
        /// 对象池统计信息
        /// </summary>
        public class ObjectPoolStats
        {
            public int AvailableCount { get; set; }
            public int TotalCount { get; set; }
            public float UtilizationRate { get; set; }
        }
        
        /// <summary>
        /// 缓存统计信息
        /// </summary>
        public class CacheStats
        {
            public int ItemCount { get; set; }
            public int MaxSize { get; set; }
            public float UtilizationRate => MaxSize > 0 ? (float)ItemCount / MaxSize : 0;
        }
        
        #endregion
    }
    
    #region Event Types
    
    /// <summary>
    /// 内存压力事件
    /// </summary>
    public class MemoryPressureEvent:UIEvent
    {
        public long MemoryUsage { get; set; }
        public new DateTime Timestamp { get; set; }
    }
    
    #endregion
}