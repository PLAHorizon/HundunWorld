using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Performance
{
    /// <summary>
    /// UI对象池管理器
    /// 用于复用频繁创建和销毁的UI组件
    /// </summary>
    public class UIObjectPool<T> where T : Control, new()
    {
        private Queue<T> _pool = new Queue<T>();
        private int _maxPoolSize;
        private Func<T> _createFunc;
        private Action<T> _resetFunc;
        
        public UIObjectPool(int maxSize = 50, Func<T> createFunc = null, Action<T> resetFunc = null)
        {
            _maxPoolSize = maxSize;
            _createFunc = createFunc ?? (() => new T());
            _resetFunc = resetFunc;
        }
        
        /// <summary>
        /// 从池中获取对象
        /// </summary>
        public T Get()
        {
            if (_pool.Count > 0)
            {
                var item = _pool.Dequeue();
                _resetFunc?.Invoke(item);
                return item;
            }
            
            return _createFunc();
        }
        
        /// <summary>
        /// 归还对象到池中
        /// </summary>
        public void Return(T item)
        {
            if (item == null || _pool.Count >= _maxPoolSize)
                return;
                
            // 重置对象状态
            item.Visible = false;
            item.Parent?.RemoveChild(item);
            
            _pool.Enqueue(item);
        }
        
        /// <summary>
        /// 清空对象池
        /// </summary>
        public void Clear()
        {
            while (_pool.Count > 0)
            {
                var item = _pool.Dequeue();
                item?.Dispose();
            }
        }
        
        public int PoolSize => _pool.Count;
    }
    
    /// <summary>
    /// UI性能监控器
    /// 监控UI系统的性能指标
    /// </summary>
    public class UIPerformanceMonitor : Script
    {
        private static UIPerformanceMonitor _instance;
        
        // 性能计数器
        private int _frameCount = 0;
        private float _deltaTimeSum = 0f;
        private float _lastFPSUpdate = 0f;
        private float _currentFPS = 0f;
        
        // UI统计
        private int _totalUIElements = 0;
        private int _visibleUIElements = 0;
        private long _uiMemoryUsage = 0;
        
        // 渲染统计
        private int _drawCalls = 0;
        private int _batchedDrawCalls = 0;
        
        public static UIPerformanceMonitor Instance
        {
            get
            {
                if (_instance == null)
                {
                    var gameObject = Level.FindActor("UIPerformanceMonitor") ?? new EmptyActor();
                    gameObject.Name = "UIPerformanceMonitor";
                    _instance = gameObject.GetScript<UIPerformanceMonitor>() ?? gameObject.AddScript<UIPerformanceMonitor>();
                }
                return _instance;
            }
        }
        
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
        }
        
        public override void OnUpdate()
        {
            UpdateFPS();
            UpdateUIStatistics();
        }
        
        private void UpdateFPS()
        {
            _frameCount++;
            _deltaTimeSum += Time.DeltaTime;
            
            // 每秒更新一次FPS
            if (Time.GameTime - _lastFPSUpdate >= 1f)
            {
                _currentFPS = _frameCount / _deltaTimeSum;
                _frameCount = 0;
                _deltaTimeSum = 0f;
                _lastFPSUpdate = Time.GameTime;
            }
        }
        
        private void UpdateUIStatistics()
        {
            // 统计UI元素数量
            _totalUIElements = CountUIElements();
            _visibleUIElements = CountVisibleUIElements();
            
            // 估算内存使用
            _uiMemoryUsage = EstimateUIMemoryUsage();
        }
        
        private int CountUIElements()
        {
            // 简化的UI元素计数
            // 实际实现中需要遍历所有GUI根节点
            return 0; // placeholder
        }
        
        private int CountVisibleUIElements()
        {
            // 简化的可见UI元素计数
            return 0; // placeholder
        }
        
        private long EstimateUIMemoryUsage()
        {
            // 简化的内存使用估算
            return GC.GetTotalMemory(false);
        }
        
        /// <summary>
        /// 记录绘制调用
        /// </summary>
        public void RecordDrawCall(bool batched = false)
        {
            _drawCalls++;
            if (batched)
                _batchedDrawCalls++;
        }
        
        /// <summary>
        /// 获取性能报告
        /// </summary>
        public PerformanceReport GetPerformanceReport()
        {
            return new PerformanceReport
            {
                FPS = _currentFPS,
                TotalUIElements = _totalUIElements,
                VisibleUIElements = _visibleUIElements,
                MemoryUsage = _uiMemoryUsage,
                DrawCalls = _drawCalls,
                BatchedDrawCalls = _batchedDrawCalls,
                BatchingEfficiency = _drawCalls > 0 ? (float)_batchedDrawCalls / _drawCalls : 0f
            };
        }
        
        /// <summary>
        /// 重置统计计数器
        /// </summary>
        public void ResetCounters()
        {
            _drawCalls = 0;
            _batchedDrawCalls = 0;
        }
    }
    
    /// <summary>
    /// 性能报告数据结构
    /// </summary>
    public struct PerformanceReport
    {
        public float FPS;
        public int TotalUIElements;
        public int VisibleUIElements;
        public long MemoryUsage;
        public int DrawCalls;
        public int BatchedDrawCalls;
        public float BatchingEfficiency;
        
        public override string ToString()
        {
            return $"FPS: {FPS:F1}, UI Elements: {VisibleUIElements}/{TotalUIElements}, " +
                   $"Memory: {MemoryUsage / 1024 / 1024}MB, Draw Calls: {DrawCalls} (Batched: {BatchedDrawCalls})";
        }
    }
    
    /// <summary>
    /// UI资源管理器
    /// 管理UI纹理、字体等资源的加载和卸载
    /// </summary>
    public class UIResourceManager : Script
    {
        private static UIResourceManager _instance;
        
        // 资源缓存
        private Dictionary<string, FontAsset> _fontCache = new Dictionary<string, FontAsset>();
        private Dictionary<string, Texture> _textureCache = new Dictionary<string, Texture>();
        
        // 缓存配置
        private int _maxFontCacheSize = 20;
        private int _maxTextureCacheSize = 100;
        
        public static UIResourceManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var gameObject = Level.FindActor("UIResourceManager") ?? new EmptyActor();
                    gameObject.Name = "UIResourceManager";
                    _instance = gameObject.GetScript<UIResourceManager>() ?? gameObject.AddScript<UIResourceManager>();
                }
                return _instance;
            }
        }
        
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
        }
        
        /// <summary>
        /// 加载字体资源
        /// </summary>
        public FontAsset LoadFont(string path)
        {
            if (_fontCache.TryGetValue(path, out var cachedFont))
            {
                return cachedFont;
            }
            
            var font = Content.LoadAsync<FontAsset>(path);
            
            // 管理缓存大小
            if (_fontCache.Count >= _maxFontCacheSize)
            {
                // 移除最旧的字体（简化实现）
                var firstKey = ""; 
                foreach (var key in _fontCache.Keys)
                {
                    firstKey = key;
                    break;
                }
                if (!string.IsNullOrEmpty(firstKey))
                {
                    _fontCache.Remove(firstKey);
                }
            }
            
            _fontCache[path] = font;
            return font;
        }
        
        /// <summary>
        /// 加载纹理资源
        /// </summary>
        public Texture LoadTexture(string path)
        {
            if (_textureCache.TryGetValue(path, out var cachedTexture))
            {
                return cachedTexture;
            }
            
            var texture = Content.LoadAsync<Texture>(path);
            
            // 管理缓存大小
            if (_textureCache.Count >= _maxTextureCacheSize)
            {
                // 移除最旧的纹理（简化实现）
                var firstKey = "";
                foreach (var key in _textureCache.Keys)
                {
                    firstKey = key;
                    break;
                }
                if (!string.IsNullOrEmpty(firstKey))
                {
                    _textureCache.Remove(firstKey);
                }
            }
            
            _textureCache[path] = texture;
            return texture;
        }
        
        /// <summary>
        /// 预加载资源
        /// </summary>
        public void PreloadResources(string[] fontPaths, string[] texturePaths)
        {
            // 异步预加载字体
            foreach (var path in fontPaths)
            {
                LoadFont(path);
            }
            
            // 异步预加载纹理
            foreach (var path in texturePaths)
            {
                LoadTexture(path);
            }
        }
        
        /// <summary>
        /// 清理未使用的资源
        /// </summary>
        public void CleanupUnusedResources()
        {
            // 简化的清理逻辑
            // 实际实现中需要检查资源引用计数
            var fontKeysToRemove = new List<string>();
            var textureKeysToRemove = new List<string>();
            
            foreach (var kvp in _fontCache)
            {
                // 检查字体是否仍在使用
                // if (!IsFontInUse(kvp.Value))
                // {
                //     fontKeysToRemove.Add(kvp.Key);
                // }
            }
            
            foreach (var key in fontKeysToRemove)
            {
                _fontCache.Remove(key);
            }
            
            foreach (var key in textureKeysToRemove)
            {
                _textureCache.Remove(key);
            }
        }
        
        /// <summary>
        /// 获取缓存统计信息
        /// </summary>
        public (int fontCount, int textureCount) GetCacheStats()
        {
            return (_fontCache.Count, _textureCache.Count);
        }
        
        public override void OnDestroy()
        {
            _fontCache.Clear();
            _textureCache.Clear();
        }
    }
}