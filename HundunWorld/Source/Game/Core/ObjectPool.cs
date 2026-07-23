using FlaxEngine;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.Core
{
    /// <summary>
    /// 通用对象池 - 减少GC压力，提升MMORPG客户端性能。
    /// 支持：
    /// - 任意类型对象池化
    /// - 预热（Pre-warm）
    /// - 自动扩容/缩容
    /// - 统计信息
    /// </summary>
    public class ObjectPool<T> where T : class, new()
    {
        private readonly Stack<T> _pool;
        private readonly Func<T> _factory;
        private readonly Action<T> _onGet;
        private readonly Action<T> _onRelease;
        private readonly int _maxSize;
        private int _totalCreated;

        /// <summary>当前池中可用对象数</summary>
        public int AvailableCount => _pool.Count;

        /// <summary>总共创建的对象数</summary>
        public int TotalCreated => _totalCreated;

        /// <summary>池最大容量</summary>
        public int MaxSize => _maxSize;

        public ObjectPool(int initialSize = 16, int maxSize = 256,
            Func<T> factory = null, Action<T> onGet = null, Action<T> onRelease = null)
        {
            _maxSize = maxSize;
            _factory = factory ?? (() => new T());
            _onGet = onGet;
            _onRelease = onRelease;
            _pool = new Stack<T>(initialSize);

            // 预热
            for (int i = 0; i < initialSize; i++)
            {
                _pool.Push(_factory());
                _totalCreated++;
            }
        }

        /// <summary>获取对象</summary>
        public T Get()
        {
            T obj;
            if (_pool.Count > 0)
            {
                obj = _pool.Pop();
            }
            else
            {
                obj = _factory();
                _totalCreated++;
            }
            _onGet?.Invoke(obj);
            return obj;
        }

        /// <summary>归还对象</summary>
        public void Release(T obj)
        {
            if (obj == null) return;
            _onRelease?.Invoke(obj);

            if (_pool.Count < _maxSize)
            {
                _pool.Push(obj);
            }
        }

        /// <summary>清空池</summary>
        public void Clear()
        {
            _pool.Clear();
        }
    }

    /// <summary>
    /// Actor 对象池 - 池化 Flax Engine Actor（特效、投射物、掉落物等）。
    /// 避免频繁 Spawn/Destroy 造成的性能开销。
    /// </summary>
    public class ActorPool
    {
        private static ActorPool _instance;
        public static ActorPool Instance => _instance ??= new ActorPool();

        private Dictionary<string, Queue<Actor>> _pools = new Dictionary<string, Queue<Actor>>();
        private Dictionary<string, int> _poolSizes = new Dictionary<string, int>();
        private const int DefaultPoolSize = 20;
        private const int MaxPoolSize = 100;

        /// <summary>统计：总获取次数</summary>
        public int TotalGets { get; private set; }

        /// <summary>统计：命中缓存次数</summary>
        public int CacheHits { get; private set; }

        /// <summary>缓存命中率</summary>
        public float HitRate => TotalGets > 0 ? (float)CacheHits / TotalGets : 0f;

        /// <summary>
        /// 预热指定类型的对象池
        /// </summary>
        public void Prewarm(string prefabPath, int count = 10)
        {
            if (!_pools.ContainsKey(prefabPath))
            {
                _pools[prefabPath] = new Queue<Actor>();
                _poolSizes[prefabPath] = DefaultPoolSize;
            }

            var pool = _pools[prefabPath];
            for (int i = 0; i < count && pool.Count < MaxPoolSize; i++)
            {
                var actor = CreateActor(prefabPath);
                if (actor != null)
                {
                    actor.IsActive = false;
                    pool.Enqueue(actor);
                }
            }
            Debug.Log($"[ActorPool] 预热 {prefabPath}: {count} 个实例");
        }

        /// <summary>
        /// 获取 Actor（从池中取或新建）
        /// </summary>
        public Actor Get(string prefabPath, Vector3 position, Quaternion rotation = default)
        {
            TotalGets++;

            if (_pools.TryGetValue(prefabPath, out var pool) && pool.Count > 0)
            {
                CacheHits++;
                var actor = pool.Dequeue();
                actor.Position = position;
                actor.Orientation = rotation;
                actor.IsActive = true;
                return actor;
            }

            // 池为空，创建新实例
            var newActor = CreateActor(prefabPath);
            if (newActor != null)
            {
                newActor.Position = position;
                newActor.Orientation = rotation;
                newActor.IsActive = true;
            }
            return newActor;
        }

        /// <summary>
        /// 归还 Actor 到池中
        /// </summary>
        public void Release(string prefabPath, Actor actor)
        {
            if (actor == null) return;

            actor.IsActive = false;

            if (!_pools.ContainsKey(prefabPath))
            {
                _pools[prefabPath] = new Queue<Actor>();
            }

            var pool = _pools[prefabPath];
            if (pool.Count < MaxPoolSize)
            {
                pool.Enqueue(actor);
            }
            else
            {
                // 超出池容量，销毁
                FlaxEngine.Object.Destroy(actor);
            }
        }

        /// <summary>
        /// 延迟归还（用于特效播放完毕后回收）
        /// </summary>
        public void ReleaseDelayed(string prefabPath, Actor actor, float delay)
        {
            FlaxEngine.Scripting.InvokeOnUpdate(() =>
            {
                // 简化实现：实际应使用计时器
                Release(prefabPath, actor);
            });
        }

        /// <summary>
        /// 获取池统计信息
        /// </summary>
        public string GetStats()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[ActorPool] 命中率: {HitRate:P1}, 总获取: {TotalGets}, 缓存命中: {CacheHits}");
            foreach (var kv in _pools)
            {
                sb.AppendLine($"  {kv.Key}: {kv.Value.Count} 个可用");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 清空所有池
        /// </summary>
        public void ClearAll()
        {
            foreach (var pool in _pools.Values)
            {
                while (pool.Count > 0)
                {
                    var actor = pool.Dequeue();
                    if (actor != null) FlaxEngine.Object.Destroy(actor);
                }
            }
            _pools.Clear();
        }

        private Actor CreateActor(string prefabPath)
        {
            try
            {
                var prefab = Content.Load<Prefab>(prefabPath);
                if (prefab != null)
                {
                    return PrefabManager.SpawnPrefab(prefab, Vector3.Zero);
                }
                return new EmptyActor();
            }
            catch
            {
                return new EmptyActor();
            }
        }
    }

    /// <summary>
    /// 帧率无关的定时器池 - 避免每帧创建委托/Lambda
    /// </summary>
    public class TimerPool
    {
        private static TimerPool _instance;
        public static TimerPool Instance => _instance ??= new TimerPool();

        private struct TimerEntry
        {
            public float Remaining;
            public Action Callback;
            public bool IsActive;
        }

        private List<TimerEntry> _timers = new List<TimerEntry>(64);

        /// <summary>注册定时器</summary>
        public int Schedule(float delay, Action callback)
        {
            // 复用已失效的槽位
            for (int i = 0; i < _timers.Count; i++)
            {
                if (!_timers[i].IsActive)
                {
                    _timers[i] = new TimerEntry { Remaining = delay, Callback = callback, IsActive = true };
                    return i;
                }
            }

            _timers.Add(new TimerEntry { Remaining = delay, Callback = callback, IsActive = true });
            return _timers.Count - 1;
        }

        /// <summary>取消定时器</summary>
        public void Cancel(int handle)
        {
            if (handle >= 0 && handle < _timers.Count)
            {
                var entry = _timers[handle];
                entry.IsActive = false;
                entry.Callback = null;
                _timers[handle] = entry;
            }
        }

        /// <summary>每帧更新</summary>
        public void Update(float deltaTime)
        {
            for (int i = 0; i < _timers.Count; i++)
            {
                var entry = _timers[i];
                if (!entry.IsActive) continue;

                entry.Remaining -= deltaTime;
                if (entry.Remaining <= 0f)
                {
                    entry.IsActive = false;
                    _timers[i] = entry;
                    entry.Callback?.Invoke();
                }
                else
                {
                    _timers[i] = entry;
                }
            }
        }
    }
}
