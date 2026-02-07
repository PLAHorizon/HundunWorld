using System;
using System.Collections.Generic;
using System.Threading;
using FlaxEngine;

namespace Game.Performance
{
    /// <summary>
    /// 内存优化器
    /// 负责优化内存使用，减少GC压力
    /// </summary>
    public class MemoryOptimizer : IDisposable
    {
        private readonly object _lockObject = new object();
        private readonly Dictionary<string, ObjectPoolBase> _objectPools = new Dictionary<string, ObjectPoolBase>();
        private Timer _gcTimer;
        private readonly int _gcIntervalMs;
        private bool _isGcTimerRunning = false;
        private bool _disposed = false;

        public MemoryOptimizer(int gcIntervalMs = 30000)
        {
            _gcIntervalMs = gcIntervalMs;
        }

        /// <summary>
        /// 创建对象池
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="poolName">池名称</param>
        /// <param name="initialCapacity">初始容量</param>
        /// <param name="maxCapacity">最大容量</param>
        /// <param name="factory">对象创建工厂</param>
        /// <param name="resetAction">对象重置操作</param>
        public void CreateObjectPool<T>(string poolName, int initialCapacity, int maxCapacity, 
            Func<T> factory, Action<T> resetAction) where T : class
        {
            lock (_lockObject)
            {
                if (!_objectPools.ContainsKey(poolName))
                {
                    var pool = new ObjectPool<T>(initialCapacity, maxCapacity, factory, resetAction);
                    _objectPools[poolName] = pool;
                }
            }
        }

        /// <summary>
        /// 从对象池获取对象
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="poolName">池名称</param>
        /// <returns>对象实例</returns>
        public T GetObject<T>(string poolName) where T : class
        {
            lock (_lockObject)
            {
                if (_objectPools.TryGetValue(poolName, out var pool))
                {
                    return ((ObjectPool<T>)pool).Get();
                }
                throw new InvalidOperationException($"对象池 '{poolName}' 不存在");
            }
        }

        /// <summary>
        /// 将对象返回到对象池
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="poolName">池名称</param>
        /// <param name="obj">对象实例</param>
        public void ReturnObject<T>(string poolName, T obj) where T : class
        {
            lock (_lockObject)
            {
                if (_objectPools.TryGetValue(poolName, out var pool))
                {
                    ((ObjectPool<T>)pool).Return(obj);
                }
            }
        }

        /// <summary>
        /// 预分配对象池中的对象
        /// </summary>
        /// <param name="poolName">池名称</param>
        /// <param name="count">预分配数量</param>
        public void PreallocateObjects(string poolName, int count)
        {
            lock (_lockObject)
            {
                if (_objectPools.TryGetValue(poolName, out var pool))
                {
                    pool.Preallocate(count);
                }
            }
        }

        /// <summary>
        /// 启动自动GC定时器
        /// </summary>
        public void StartAutoGCTimer()
        {
            lock (_lockObject)
            {
                if (!_isGcTimerRunning)
                {
                    _gcTimer = new Timer(PerformGarbageCollection, null, _gcIntervalMs, _gcIntervalMs);
                    _isGcTimerRunning = true;
                }
            }
        }

        /// <summary>
        /// 停止自动GC定时器
        /// </summary>
        public void StopAutoGCTimer()
        {
            lock (_lockObject)
            {
                if (_isGcTimerRunning)
                {
                    _gcTimer?.Dispose();
                    _gcTimer = null;
                    _isGcTimerRunning = false;
                }
            }
        }

        /// <summary>
        /// 执行垃圾回收
        /// </summary>
        private void PerformGarbageCollection(object state)
        {
            try
            {
                // 执行轻量级GC
                GC.Collect(0, GCCollectionMode.Optimized);
            }
            catch (Exception ex)
            {
                Debug.LogError($"自动垃圾回收失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 强制执行垃圾回收
        /// </summary>
        public void ForceGarbageCollection()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            catch (Exception ex)
            {
                Debug.LogError($"强制垃圾回收失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取对象池统计信息
        /// </summary>
        /// <returns>对象池统计信息</returns>
        public Dictionary<string, ObjectPoolStats> GetPoolStats()
        {
            lock (_lockObject)
            {
                var stats = new Dictionary<string, ObjectPoolStats>();
                foreach (var kvp in _objectPools)
                {
                    stats[kvp.Key] = kvp.Value.GetStats();
                }
                return stats;
            }
        }

        /// <summary>
        /// 清理所有对象池
        /// </summary>
        public void ClearAllPools()
        {
            lock (_lockObject)
            {
                foreach (var pool in _objectPools.Values)
                {
                    pool.Clear();
                }
                _objectPools.Clear();
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
                    StopAutoGCTimer();
                    ClearAllPools();
                }

                // 释放非托管资源

                _disposed = true;
            }
        }

        /// <summary>
        /// 析构函数
        /// </summary>
        ~MemoryOptimizer()
        {
            Dispose(false);
        }
    }

    /// <summary>
    /// 对象池基类
    /// </summary>
    public abstract class ObjectPoolBase
    {
        public abstract void Preallocate(int count);
        public abstract void Clear();
        public abstract ObjectPoolStats GetStats();
    }

    /// <summary>
    /// 对象池
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    public class ObjectPool<T> : ObjectPoolBase where T : class
    {
        private readonly Stack<T> _pool = new Stack<T>();
        private readonly Func<T> _factory;
        private readonly Action<T> _resetAction;
        private readonly int _maxCapacity;
        private readonly object _lockObject = new object();
        private int _totalAllocated = 0;
        private int _totalReturned = 0;

        public ObjectPool(int initialCapacity, int maxCapacity, Func<T> factory, Action<T> resetAction)
        {
            _maxCapacity = maxCapacity;
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _resetAction = resetAction;

            // 预分配对象
            for (int i = 0; i < initialCapacity; i++)
            {
                var obj = _factory();
                _pool.Push(obj);
            }
            _totalAllocated = initialCapacity;
        }

        /// <summary>
        /// 获取对象
        /// </summary>
        /// <returns>对象实例</returns>
        public T Get()
        {
            lock (_lockObject)
            {
                if (_pool.Count > 0)
                {
                    var obj = _pool.Pop();
                    _totalReturned++;
                    return obj;
                }
                else
                {
                    _totalAllocated++;
                    return _factory();
                }
            }
        }

        /// <summary>
        /// 返回对象到池中
        /// </summary>
        /// <param name="obj">对象实例</param>
        public void Return(T obj)
        {
            if (obj == null) return;

            lock (_lockObject)
            {
                // 重置对象状态
                _resetAction?.Invoke(obj);

                // 如果池未满，则返回到池中
                if (_pool.Count < _maxCapacity)
                {
                    _pool.Push(obj);
                }
                // 如果池已满，则丢弃对象，让GC处理
            }
        }

        /// <summary>
        /// 预分配对象
        /// </summary>
        /// <param name="count">预分配数量</param>
        public override void Preallocate(int count)
        {
            lock (_lockObject)
            {
                int currentCount = _pool.Count;
                int needed = count - currentCount;
                
                if (needed > 0)
                {
                    int toCreate = Math.Min(needed, _maxCapacity - currentCount);
                    for (int i = 0; i < toCreate; i++)
                    {
                        var obj = _factory();
                        _pool.Push(obj);
                        _totalAllocated++;
                    }
                }
            }
        }

        /// <summary>
        /// 清理池
        /// </summary>
        public override void Clear()
        {
            lock (_lockObject)
            {
                _pool.Clear();
                _totalAllocated = 0;
                _totalReturned = 0;
            }
        }

        /// <summary>
        /// 获取池统计信息
        /// </summary>
        /// <returns>统计信息</returns>
        public override ObjectPoolStats GetStats()
        {
            lock (_lockObject)
            {
                return new ObjectPoolStats
                {
                    PoolName = typeof(T).Name,
                    CurrentCount = _pool.Count,
                    MaxCapacity = _maxCapacity,
                    TotalAllocated = _totalAllocated,
                    TotalReturned = _totalReturned,
                    HitRate = _totalReturned > 0 ? (double)_totalReturned / (_totalReturned + _pool.Count) : 0
                };
            }
        }
    }

    /// <summary>
    /// 对象池统计信息
    /// </summary>
    public class ObjectPoolStats
    {
        /// <summary>
        /// 池名称
        /// </summary>
        public string PoolName { get; set; }

        /// <summary>
        /// 当前池中对象数量
        /// </summary>
        public int CurrentCount { get; set; }

        /// <summary>
        /// 最大容量
        /// </summary>
        public int MaxCapacity { get; set; }

        /// <summary>
        /// 总分配数量
        /// </summary>
        public int TotalAllocated { get; set; }

        /// <summary>
        /// 总返回数量
        /// </summary>
        public int TotalReturned { get; set; }

        /// <summary>
        /// 命中率
        /// </summary>
        public double HitRate { get; set; }
    }
}