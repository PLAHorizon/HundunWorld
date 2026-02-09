using FlaxEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HundunWorld.Game.Scene
{
    /// <summary>
    /// 场景分块加载系统
    /// 实现大世界场景的分块管理、动态资源加载和卸载
    /// 设计目标：
    /// 1. 将大世界划分为固定大小的分块（100m x 100m）
    /// 2. 根据玩家位置动态加载/卸载分块
    /// 3. 支持预加载机制，提前加载即将进入的分块
    /// 4. 支持LOD层级，远距离使用低模，近距离使用高模
    /// 5. 内存管理和资源池化
    /// </summary>
    public class SceneChunkLoader : Script
    {
        #region 分块数据定义

        /// <summary>
        /// 场景分块数据
        /// </summary>
        public class SceneChunk
        {
            public int ChunkX;                          // 分块X坐标
            public int ChunkZ;                          // 分块Z坐标
            public Vector2 WorldPosition;               // 世界坐标位置（中心点）
            public ChunkLoadState LoadState;            // 加载状态
            public LodLevel CurrentLod;                 // 当前LOD层级
            public Actor ChunkRoot;                     // 分块根节点
            public List<Actor> LoadedActors;            // 已加载的Actor列表
            public float LoadTime;                      // 加载时间戳
            public float LastAccessTime;                // 最后访问时间
            public int Priority;                        // 优先级（用于预加载队列）
            public string ChunkAssetPath;               // 分块资源路径
            public bool IsPersistent;                   // 是否持久化（特殊分块，如主城）

            public SceneChunk(int x, int z, Vector2 worldPos)
            {
                ChunkX = x;
                ChunkZ = z;
                WorldPosition = worldPos;
                LoadState = ChunkLoadState.Unloaded;
                CurrentLod = LodLevel.None;
                LoadedActors = new List<Actor>();
                LastAccessTime = Time.GameTime;
            }

            /// <summary>
            /// 获取分块ID（用于字典键）
            /// </summary>
            public (int, int) GetChunkId() => (ChunkX, ChunkZ);
        }

        /// <summary>
        /// 分块加载状态
        /// </summary>
        public enum ChunkLoadState
        {
            Unloaded,       // 未加载
            Loading,        // 加载中
            Loaded,         // 已加载
            Unloading       // 卸载中
        }

        /// <summary>
        /// LOD层级
        /// </summary>
        public enum LodLevel
        {
            None,           // 未加载
            Lod2,           // 远距离低模（200m+）
            Lod1,           // 中距离（100-200m）
            Lod0            // 近距离高模（0-100m）
        }

        #endregion

        #region 配置参数

        [Header("分块配置")]
        [Tooltip("分块大小(米)")]
        public float ChunkSize = 100f;

        [Tooltip("加载距离(分块数) - 玩家周围N个分块会被加载")]
        public int LoadDistance = 3;

        [Tooltip("卸载距离(分块数) - 超过此距离的分块会被卸载")]
        public int UnloadDistance = 5;

        [Tooltip("世界大小X(分块数)")]
        public int WorldChunkCountX = 20;

        [Tooltip("世界大小Z(分块数)")]
        public int WorldChunkCountZ = 20;

        [Header("预加载配置")]
        [Tooltip("是否启用预加载")]
        public bool EnablePreload = true;

        [Tooltip("预加载缓冲区大小")]
        public int PreloadBufferSize = 10;

        [Tooltip("预加载优先级")]
        public int PreloadPriority = 50;

        [Header("LOD配置")]
        [Tooltip("是否启用LOD")]
        public bool EnableLod = true;

        [Tooltip("LOD0距离(米) - 近距离高模")]
        public float Lod0Distance = 100f;

        [Tooltip("LOD1距离(米) - 中距离")]
        public float Lod1Distance = 200f;

        [Tooltip("LOD2距离(米) - 远距离低模")]
        public float Lod2Distance = 400f;

        [Header("内存管理")]
        [Tooltip("最大同时加载分块数")]
        public int MaxLoadedChunks = 25;

        [Tooltip("自动卸载闲置时间(秒)")]
        public float AutoUnloadTime = 60f;

        [Tooltip("是否启用对象池")]
        public bool EnableObjectPool = true;

        [Header("性能配置")]
        [Tooltip("每帧最大加载数")]
        public int MaxLoadsPerFrame = 2;

        [Tooltip("每帧最大卸载数")]
        public int MaxUnloadsPerFrame = 3;

        [Tooltip("异步加载")]
        public bool AsyncLoading = true;

        [Header("调试")]
        [Tooltip("是否启用日志")]
        public bool EnableLogging = true;

        [Tooltip("是否显示调试可视化")]
        public bool ShowDebugVisualization = true;

        #endregion

        #region 私有字段

        // 所有分块字典 (ChunkX, ChunkZ) -> SceneChunk
        private readonly Dictionary<(int, int), SceneChunk> _chunks = new();

        // 已加载的分块列表
        private readonly HashSet<(int, int)> _loadedChunks = new();

        // 预加载队列（优先级队列）
        private readonly List<SceneChunk> _preloadQueue = new();

        // 待加载队列
        private readonly Queue<SceneChunk> _loadQueue = new();

        // 待卸载队列
        private readonly Queue<SceneChunk> _unloadQueue = new();

        // 对象池（按资源路径分类）
        private readonly Dictionary<string, Queue<Actor>> _objectPools = new();

        // 玩家引用
        private Actor _player;
        private Vector3 _lastPlayerPosition;
        private (int, int) _lastPlayerChunk = (-1, -1);

        // 更新计时器
        private float _updateTimer = 0f;
        private const float UpdateInterval = 0.5f;  // 每0.5秒更新一次

        // 加载计数器（当前帧）
        private int _loadsThisFrame = 0;
        private int _unloadsThisFrame = 0;

        // 统计数据
        private int _totalLoadedChunks = 0;
        private int _totalUnloadedChunks = 0;
        private float _totalLoadTime = 0f;
        private int _poolHitCount = 0;
        private int _poolMissCount = 0;

        #endregion

        #region 事件定义

        /// <summary>
        /// 分块加载完成事件
        /// </summary>
        public event Action<SceneChunk> ChunkLoaded;

        /// <summary>
        /// 分块卸载完成事件
        /// </summary>
        public event Action<SceneChunk> ChunkUnloaded;

        /// <summary>
        /// LOD变化事件
        /// </summary>
        public event Action<SceneChunk, LodLevel, LodLevel> LodChanged;  // (chunk, oldLod, newLod)

        #endregion

        #region 初始化

        public override void OnEnable()
        {
            // 查找玩家
            _player = Scene.FindActor<Actor>("Player");

            if (_player == null)
            {
                Debug.LogWarning("[SceneChunk] 未找到玩家对象");
                return;
            }

            _lastPlayerPosition = _player.Position;

            // 初始化分块数据
            InitializeChunks();

            // 加载初始分块
            LoadInitialChunks();

            if (EnableLogging)
            {
                Debug.Log($"[SceneChunk] 场景分块加载系统已启动 - 世界大小: {WorldChunkCountX}x{WorldChunkCountZ}, 分块大小: {ChunkSize}m");
            }
        }

        /// <summary>
        /// 初始化所有分块数据
        /// </summary>
        private void InitializeChunks()
        {
            for (int x = 0; x < WorldChunkCountX; x++)
            {
                for (int z = 0; z < WorldChunkCountZ; z++)
                {
                    Vector2 worldPos = new Vector2(
                        x * ChunkSize + ChunkSize * 0.5f,
                        z * ChunkSize + ChunkSize * 0.5f
                    );

                    var chunk = new SceneChunk(x, z, worldPos);

                    // 设置资源路径（实际项目中应该从配置文件读取）
                    chunk.ChunkAssetPath = $"Chunks/Chunk_{x}_{z}";

                    _chunks[(x, z)] = chunk;
                }
            }

            if (EnableLogging)
            {
                Debug.Log($"[SceneChunk] 初始化 {_chunks.Count} 个分块");
            }
        }

        /// <summary>
        /// 加载初始分块（玩家周围）
        /// </summary>
        private void LoadInitialChunks()
        {
            var playerChunk = GetChunkCoordFromPosition(_player.Position);
            _lastPlayerChunk = playerChunk;

            // 加载玩家周围的分块
            for (int x = playerChunk.Item1 - LoadDistance; x <= playerChunk.Item1 + LoadDistance; x++)
            {
                for (int z = playerChunk.Item2 - LoadDistance; z <= playerChunk.Item2 + LoadDistance; z++)
                {
                    if (IsValidChunkCoord(x, z))
                    {
                        var chunk = _chunks[(x, z)];
                        _loadQueue.Enqueue(chunk);
                    }
                }
            }
        }

        #endregion

        #region 预加载管理

        /// <summary>
        /// 更新预加载队列
        /// </summary>
        private void UpdatePreloadQueue((int, int) playerChunk)
        {
            _preloadQueue.Clear();

            // 获取玩家移动方向（基于速度）
            var velocity = _player.Position - _lastPlayerPosition;
            var moveDir = new Vector2(velocity.X, velocity.Z);
            if (moveDir.Length < 0.1f)
            {
                // 玩家静止，不需要预加载
                return;
            }

            moveDir.Normalize();

            // 预测玩家将要移动到的方向
            int predictX = (int)Math.Sign(moveDir.X);
            int predictZ = (int)Math.Sign(moveDir.Y);

            // 添加移动方向上的分块到预加载队列
            for (int dist = LoadDistance + 1; dist <= LoadDistance + 2; dist++)
            {
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
                    {
                        int x = playerChunk.Item1 + predictX * dist + offsetX;
                        int z = playerChunk.Item2 + predictZ * dist + offsetZ;

                        if (IsValidChunkCoord(x, z))
                        {
                            var chunkId = (x, z);
                            if (!_loadedChunks.Contains(chunkId))
                            {
                                var chunk = _chunks[chunkId];
                                if (chunk.LoadState == ChunkLoadState.Unloaded)
                                {
                                    chunk.Priority = PreloadPriority;
                                    _preloadQueue.Add(chunk);
                                }
                            }
                        }
                    }
                }
            }

            // 限制预加载队列大小
            if (_preloadQueue.Count > PreloadBufferSize)
            {
                _preloadQueue.RemoveRange(PreloadBufferSize, _preloadQueue.Count - PreloadBufferSize);
            }

            // 将预加载队列中的分块添加到加载队列（低优先级）
            foreach (var chunk in _preloadQueue)
            {
                if (!_loadQueue.Contains(chunk))
                {
                    _loadQueue.Enqueue(chunk);
                }
            }
        }

        #endregion

        #region 内存管理

        /// <summary>
        /// 自动卸载闲置分块
        /// </summary>
        private void AutoUnloadIdleChunks()
        {
            if (AutoUnloadTime <= 0) return;

            var currentTime = Time.GameTime;
            var chunksToUnload = new List<SceneChunk>();

            foreach (var chunkId in _loadedChunks)
            {
                var chunk = _chunks[chunkId];

                // 持久化分块不卸载
                if (chunk.IsPersistent)
                    continue;

                // 检查闲置时间
                float idleTime = currentTime - chunk.LastAccessTime;
                if (idleTime > AutoUnloadTime)
                {
                    chunksToUnload.Add(chunk);
                }
            }

            // 添加到卸载队列
            foreach (var chunk in chunksToUnload)
            {
                _unloadQueue.Enqueue(chunk);

                if (EnableLogging)
                {
                    Debug.Log($"[SceneChunk] 自动卸载闲置分块: ({chunk.ChunkX}, {chunk.ChunkZ})");
                }
            }
        }

        /// <summary>
        /// 从对象池获取Actor
        /// </summary>
        private Actor GetFromPool(string poolKey)
        {
            if (!_objectPools.ContainsKey(poolKey))
            {
                _objectPools[poolKey] = new Queue<Actor>();
            }

            var pool = _objectPools[poolKey];
            if (pool.Count > 0)
            {
                _poolHitCount++;
                var actor = pool.Dequeue();
                actor.IsActive = true;
                return actor;
            }

            _poolMissCount++;
            return new EmptyActor();
        }

        /// <summary>
        /// 归还Actor到对象池
        /// </summary>
        private void ReturnToPool(string poolKey, Actor actor)
        {
            if (!_objectPools.ContainsKey(poolKey))
            {
                _objectPools[poolKey] = new Queue<Actor>();
            }

            // 重置Actor状态
            actor.IsActive = false;
            actor.Parent = null;

            _objectPools[poolKey].Enqueue(actor);
        }

        /// <summary>
        /// 清空对象池
        /// </summary>
        public void ClearObjectPools()
        {
            foreach (var pool in _objectPools.Values)
            {
                while (pool.Count > 0)
                {
                    var actor = pool.Dequeue();
                    Destroy(actor);
                }
            }

            _objectPools.Clear();

            if (EnableLogging)
            {
                Debug.Log("[SceneChunk] 对象池已清空");
            }
        }

        #endregion

        #region 工具方法

        /// <summary>
        /// 根据世界坐标获取分块坐标
        /// </summary>
        private (int, int) GetChunkCoordFromPosition(Vector3 worldPos)
        {
            int x = Mathf.FloorToInt(worldPos.X / ChunkSize);
            int z = Mathf.FloorToInt(worldPos.Z / ChunkSize);
            return (x, z);
        }

        /// <summary>
        /// 检查分块坐标是否有效
        /// </summary>
        private bool IsValidChunkCoord(int x, int z)
        {
            return x >= 0 && x < WorldChunkCountX && z >= 0 && z < WorldChunkCountZ;
        }

        /// <summary>
        /// 获取分块（如果存在）
        /// </summary>
        public SceneChunk GetChunk(int x, int z)
        {
            if (IsValidChunkCoord(x, z))
            {
                return _chunks[(x, z)];
            }
            return null;
        }

        /// <summary>
        /// 获取玩家当前所在分块
        /// </summary>
        public SceneChunk GetPlayerChunk()
        {
            if (_player == null) return null;

            var coord = GetChunkCoordFromPosition(_player.Position);
            return GetChunk(coord.Item1, coord.Item2);
        }

        /// <summary>
        /// 强制加载指定分块
        /// </summary>
        public void ForceLoadChunk(int x, int z)
        {
            if (!IsValidChunkCoord(x, z))
            {
                Debug.LogWarning($"[SceneChunk] 无效的分块坐标: ({x}, {z})");
                return;
            }

            var chunk = _chunks[(x, z)];
            if (chunk.LoadState == ChunkLoadState.Unloaded)
            {
                chunk.Priority = 1000;  // 最高优先级
                _loadQueue.Enqueue(chunk);
            }
        }

        /// <summary>
        /// 强制卸载指定分块
        /// </summary>
        public void ForceUnloadChunk(int x, int z)
        {
            if (!IsValidChunkCoord(x, z))
            {
                Debug.LogWarning($"[SceneChunk] 无效的分块坐标: ({x}, {z})");
                return;
            }

            var chunk = _chunks[(x, z)];
            if (chunk.LoadState == ChunkLoadState.Loaded && !chunk.IsPersistent)
            {
                _unloadQueue.Enqueue(chunk);
            }
        }

        /// <summary>
        /// 设置分块为持久化（不会被自动卸载）
        /// </summary>
        public void SetChunkPersistent(int x, int z, bool persistent)
        {
            if (!IsValidChunkCoord(x, z))
                return;

            var chunk = _chunks[(x, z)];
            chunk.IsPersistent = persistent;

            if (EnableLogging)
            {
                Debug.Log($"[SceneChunk] 分块 ({x}, {z}) 持久化状态: {persistent}");
            }
        }

        #endregion

        #region 统计信息

        /// <summary>
        /// 当前已加载的分块数量
        /// </summary>
        public int LoadedChunkCount => _loadedChunks.Count;

        /// <summary>
        /// 获取统计信息
        /// </summary>
        public string GetStatistics()
        {
            float avgLoadTime = _totalLoadedChunks > 0 ? _totalLoadTime / _totalLoadedChunks : 0f;
            float poolHitRate = (_poolHitCount + _poolMissCount) > 0
                ? (float)_poolHitCount / (_poolHitCount + _poolMissCount) * 100f
                : 0f;

            return $"场景分块统计:\n" +
                   $"当前加载分块数: {_loadedChunks.Count}/{MaxLoadedChunks}\n" +
                   $"总加载次数: {_totalLoadedChunks}\n" +
                   $"总卸载次数: {_totalUnloadedChunks}\n" +
                   $"平均加载时间: {avgLoadTime:F3}s\n" +
                   $"对象池命中率: {poolHitRate:F1}% ({_poolHitCount}/{_poolHitCount + _poolMissCount})\n" +
                   $"加载队列: {_loadQueue.Count}\n" +
                   $"卸载队列: {_unloadQueue.Count}\n" +
                   $"预加载队列: {_preloadQueue.Count}";
        }

        /// <summary>
        /// 重置统计数据
        /// </summary>
        public void ResetStatistics()
        {
            _totalLoadedChunks = 0;
            _totalUnloadedChunks = 0;
            _totalLoadTime = 0f;
            _poolHitCount = 0;
            _poolMissCount = 0;

            if (EnableLogging)
            {
                Debug.Log("[SceneChunk] 统计数据已重置");
            }
        }

        #endregion

        #region 调试可视化

        /// <summary>
        /// 绘制调试可视化
        /// </summary>
        private void DrawDebugVisualization()
        {
            if (_player == null) return;

            var playerPos = _player.Position;
            var playerChunk = GetChunkCoordFromPosition(playerPos);

            // 绘制所有分块边界
            foreach (var chunk in _chunks.Values)
            {
                Color color;

                // 根据加载状态选择颜色
                switch (chunk.LoadState)
                {
                    case ChunkLoadState.Loaded:
                        color = Color.Green;
                        break;
                    case ChunkLoadState.Loading:
                        color = Color.Yellow;
                        break;
                    case ChunkLoadState.Unloading:
                        color = Color.Orange;
                        break;
                    default:
                        color = Color.Gray;
                        break;
                }

                // 玩家所在分块用红色高亮
                if (chunk.ChunkX == playerChunk.Item1 && chunk.ChunkZ == playerChunk.Item2)
                {
                    color = Color.Red;
                }

                // 绘制分块边界框
                Vector3 chunkCenter = new Vector3(chunk.WorldPosition.X, 0, chunk.WorldPosition.Y);
                Vector3 halfSize = new Vector3(ChunkSize * 0.5f, 1, ChunkSize * 0.5f);

                DebugDraw.DrawWireBox(new BoundingBox(chunkCenter - halfSize, chunkCenter + halfSize), color);

                // 绘制LOD信息
                if (chunk.LoadState == ChunkLoadState.Loaded)
                {
                    // TODO: 在分块中心绘制LOD层级文本
                    // DebugDraw.DrawText(...);
                }
            }

            // 绘制加载范围
            Vector3 playerPos2D = new Vector3(playerPos.X, 0, playerPos.Z);
            float loadRadius = LoadDistance * ChunkSize;
            // DebugDraw.DrawCircle(...) // Flax可能没有这个API，用其他方式代替

            // 绘制卸载范围
            float unloadRadius = UnloadDistance * ChunkSize;
            // DebugDraw.DrawCircle(...)
        }

        #endregion

        #region 生命周期

        public override void OnDisable()
        {
            // 卸载所有分块
            var chunksToUnload = _loadedChunks.ToList();
            foreach (var chunkId in chunksToUnload)
            {
                UnloadChunk(_chunks[chunkId]);
            }

            // 清空对象池
            if (EnableObjectPool)
            {
                ClearObjectPools();
            }

            if (EnableLogging)
            {
                Debug.Log("[SceneChunk] 场景分块加载系统已停止");
            }
        }

        #endregion

        #region 更新逻辑

        public override void OnUpdate()
        {
            if (_player == null) return;

            // 重置每帧计数器
            _loadsThisFrame = 0;
            _unloadsThisFrame = 0;

            // 定时更新
            _updateTimer += Time.DeltaTime;
            if (_updateTimer >= UpdateInterval)
            {
                _updateTimer = 0f;
                UpdateChunkLoading();
            }

            // 处理加载队列
            ProcessLoadQueue();

            // 处理卸载队列
            ProcessUnloadQueue();

            // 更新LOD
            if (EnableLod)
            {
                UpdateLodLevels();
            }

            // 自动卸载闲置分块
            AutoUnloadIdleChunks();

            // 调试可视化
            if (ShowDebugVisualization)
            {
                DrawDebugVisualization();
            }
        }

        /// <summary>
        /// 更新分块加载状态
        /// </summary>
        private void UpdateChunkLoading()
        {
            var currentPos = _player.Position;
            var playerChunk = GetChunkCoordFromPosition(currentPos);

            // 玩家移动到新分块
            if (playerChunk != _lastPlayerChunk)
            {
                _lastPlayerChunk = playerChunk;
                OnPlayerMovedToNewChunk(playerChunk);
            }

            // 检查需要加载的分块
            CheckChunksToLoad(playerChunk);

            // 检查需要卸载的分块
            CheckChunksToUnload(playerChunk);

            _lastPlayerPosition = currentPos;
        }

        /// <summary>
        /// 玩家移动到新分块时触发
        /// </summary>
        private void OnPlayerMovedToNewChunk((int, int) newChunk)
        {
            if (EnableLogging)
            {
                Debug.Log($"[SceneChunk] 玩家移动到新分块: ({newChunk.Item1}, {newChunk.Item2})");
            }

            // 更新预加载队列
            if (EnablePreload)
            {
                UpdatePreloadQueue(newChunk);
            }
        }

        /// <summary>
        /// 检查需要加载的分块
        /// </summary>
        private void CheckChunksToLoad((int, int) playerChunk)
        {
            for (int x = playerChunk.Item1 - LoadDistance; x <= playerChunk.Item1 + LoadDistance; x++)
            {
                for (int z = playerChunk.Item2 - LoadDistance; z <= playerChunk.Item2 + LoadDistance; z++)
                {
                    if (!IsValidChunkCoord(x, z)) continue;

                    var chunkId = (x, z);
                    if (!_loadedChunks.Contains(chunkId))
                    {
                        var chunk = _chunks[chunkId];
                        if (chunk.LoadState == ChunkLoadState.Unloaded)
                        {
                            // 计算优先级（距离越近优先级越高）
                            int dx = Math.Abs(x - playerChunk.Item1);
                            int dz = Math.Abs(z - playerChunk.Item2);
                            chunk.Priority = 100 - (dx + dz) * 10;

                            _loadQueue.Enqueue(chunk);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 检查需要卸载的分块
        /// </summary>
        private void CheckChunksToUnload((int, int) playerChunk)
        {
            var chunksToUnload = new List<(int, int)>();

            foreach (var chunkId in _loadedChunks)
            {
                int dx = Math.Abs(chunkId.Item1 - playerChunk.Item1);
                int dz = Math.Abs(chunkId.Item2 - playerChunk.Item2);

                // 超过卸载距离
                if (dx > UnloadDistance || dz > UnloadDistance)
                {
                    var chunk = _chunks[chunkId];
                    if (!chunk.IsPersistent && chunk.LoadState == ChunkLoadState.Loaded)
                    {
                        chunksToUnload.Add(chunkId);
                    }
                }
            }

            // 添加到卸载队列
            foreach (var chunkId in chunksToUnload)
            {
                _unloadQueue.Enqueue(_chunks[chunkId]);
            }
        }

        #endregion

        #region 加载/卸载处理

        /// <summary>
        /// 处理加载队列
        /// </summary>
        private void ProcessLoadQueue()
        {
            while (_loadQueue.Count > 0 && _loadsThisFrame < MaxLoadsPerFrame)
            {
                var chunk = _loadQueue.Dequeue();

                // 检查是否已经加载或正在加载
                if (chunk.LoadState != ChunkLoadState.Unloaded)
                    continue;

                // 检查是否达到最大加载数量限制
                if (_loadedChunks.Count >= MaxLoadedChunks)
                {
                    if (EnableLogging)
                    {
                        Debug.LogWarning($"[SceneChunk] 已达到最大加载分块数量限制: {MaxLoadedChunks}");
                    }
                    break;
                }

                LoadChunk(chunk);
                _loadsThisFrame++;
            }
        }

        /// <summary>
        /// 处理卸载队列
        /// </summary>
        private void ProcessUnloadQueue()
        {
            while (_unloadQueue.Count > 0 && _unloadsThisFrame < MaxUnloadsPerFrame)
            {
                var chunk = _unloadQueue.Dequeue();

                // 检查是否已经卸载或正在卸载
                if (chunk.LoadState != ChunkLoadState.Loaded)
                    continue;

                UnloadChunk(chunk);
                _unloadsThisFrame++;
            }
        }

        /// <summary>
        /// 加载分块
        /// </summary>
        private void LoadChunk(SceneChunk chunk)
        {
            if (chunk.LoadState != ChunkLoadState.Unloaded)
                return;

            chunk.LoadState = ChunkLoadState.Loading;
            chunk.LoadTime = Time.GameTime;

            if (AsyncLoading)
            {
                LoadChunkAsync(chunk);
            }
            else
            {
                LoadChunkSync(chunk);
            }
        }

        /// <summary>
        /// 同步加载分块
        /// </summary>
        private void LoadChunkSync(SceneChunk chunk)
        {
            try
            {
                // TODO: 实际加载分块资源
                // 这里应该从ContentManager加载对应的Prefab或场景资源

                // 示例：创建分块根节点
                chunk.ChunkRoot = new EmptyActor();
                chunk.ChunkRoot.Name = $"Chunk_{chunk.ChunkX}_{chunk.ChunkZ}";
                chunk.ChunkRoot.Position = new Vector3(chunk.WorldPosition.X, 0, chunk.WorldPosition.Y);
                chunk.ChunkRoot.Parent = Scene;

                // TODO: 加载分块中的所有Actor
                // 示例代码（实际应该从资源文件加载）
                LoadChunkActors(chunk);

                // 标记为已加载
                chunk.LoadState = ChunkLoadState.Loaded;
                _loadedChunks.Add(chunk.GetChunkId());
                _totalLoadedChunks++;

                float loadDuration = Time.GameTime - chunk.LoadTime;
                _totalLoadTime += loadDuration;

                if (EnableLogging)
                {
                    Debug.Log($"[SceneChunk] 分块加载完成: ({chunk.ChunkX}, {chunk.ChunkZ}) - 耗时: {loadDuration:F3}s");
                }

                // 触发事件
                ChunkLoaded?.Invoke(chunk);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SceneChunk] 加载分块失败: ({chunk.ChunkX}, {chunk.ChunkZ}) - {ex.Message}");
                chunk.LoadState = ChunkLoadState.Unloaded;
            }
        }

        /// <summary>
        /// 异步加载分块
        /// </summary>
        private async void LoadChunkAsync(SceneChunk chunk)
        {
            try
            {
                // 模拟异步加载
                await Task.Delay(100);  // 实际应该使用Flax的异步资源加载API

                // 切回主线程执行Actor创建
                Scripting.InvokeOnUpdate(() =>
                {
                    LoadChunkSync(chunk);
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SceneChunk] 异步加载分块失败: ({chunk.ChunkX}, {chunk.ChunkZ}) - {ex.Message}");
                chunk.LoadState = ChunkLoadState.Unloaded;
            }
        }

        /// <summary>
        /// 加载分块中的Actor（示例实现）
        /// </summary>
        private void LoadChunkActors(SceneChunk chunk)
        {
            // TODO: 实际项目中应该从资源文件或数据库加载
            // 这里只是演示如何使用对象池

            // 示例：在分块中创建一些测试对象
            for (int i = 0; i < 5; i++)
            {
                Actor actor;

                if (EnableObjectPool)
                {
                    actor = GetFromPool(chunk.ChunkAssetPath);
                }
                else
                {
                    actor = new EmptyActor();
                }

                actor.Name = $"ChunkActor_{i}";
                actor.Parent = chunk.ChunkRoot;

                // 随机位置
                float offsetX = (float)(new Random().NextDouble() * ChunkSize - ChunkSize * 0.5f);
                float offsetZ = (float)(new Random().NextDouble() * ChunkSize - ChunkSize * 0.5f);
                actor.LocalPosition = new Vector3(offsetX, 0, offsetZ);

                chunk.LoadedActors.Add(actor);
            }
        }

        /// <summary>
        /// 卸载分块
        /// </summary>
        private void UnloadChunk(SceneChunk chunk)
        {
            if (chunk.LoadState != ChunkLoadState.Loaded)
                return;

            chunk.LoadState = ChunkLoadState.Unloading;

            try
            {
                // 卸载分块中的所有Actor
                foreach (var actor in chunk.LoadedActors)
                {
                    if (actor != null)
                    {
                        if (EnableObjectPool)
                        {
                            ReturnToPool(chunk.ChunkAssetPath, actor);
                        }
                        else
                        {
                            Destroy(actor);
                        }
                    }
                }

                chunk.LoadedActors.Clear();

                // 销毁根节点
                if (chunk.ChunkRoot != null)
                {
                    Destroy(chunk.ChunkRoot);
                    chunk.ChunkRoot = null;
                }

                // 标记为已卸载
                chunk.LoadState = ChunkLoadState.Unloaded;
                chunk.CurrentLod = LodLevel.None;
                _loadedChunks.Remove(chunk.GetChunkId());
                _totalUnloadedChunks++;

                if (EnableLogging)
                {
                    Debug.Log($"[SceneChunk] 分块卸载完成: ({chunk.ChunkX}, {chunk.ChunkZ})");
                }

                // 触发事件
                ChunkUnloaded?.Invoke(chunk);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SceneChunk] 卸载分块失败: ({chunk.ChunkX}, {chunk.ChunkZ}) - {ex.Message}");
                chunk.LoadState = ChunkLoadState.Loaded;  // 回滚状态
            }
        }

        #endregion

        #region LOD管理

        /// <summary>
        /// 更新LOD层级
        /// </summary>
        private void UpdateLodLevels()
        {
            if (_player == null) return;

            var playerPos = _player.Position;

            foreach (var chunkId in _loadedChunks)
            {
                var chunk = _chunks[chunkId];
                if (chunk.LoadState != ChunkLoadState.Loaded)
                    continue;

                // 计算分块中心到玩家的距离
                float distance = Vector2.Distance(
                    new Vector2(playerPos.X, playerPos.Z),
                    chunk.WorldPosition
                );

                // 确定LOD层级
                LodLevel newLod = DetermineLodLevel(distance);

                // LOD变化
                if (newLod != chunk.CurrentLod)
                {
                    var oldLod = chunk.CurrentLod;
                    chunk.CurrentLod = newLod;

                    // 应用LOD变化
                    ApplyLodLevel(chunk, newLod);

                    // 触发事件
                    LodChanged?.Invoke(chunk, oldLod, newLod);
                }
            }
        }

        /// <summary>
        /// 根据距离确定LOD层级
        /// </summary>
        private LodLevel DetermineLodLevel(float distance)
        {
            if (distance < Lod0Distance)
                return LodLevel.Lod0;  // 近距离高模
            else if (distance < Lod1Distance)
                return LodLevel.Lod1;  // 中距离
            else if (distance < Lod2Distance)
                return LodLevel.Lod2;  // 远距离低模
            else
                return LodLevel.None;  // 超远距离，不显示
        }

        /// <summary>
        /// 应用LOD层级
        /// </summary>
        private void ApplyLodLevel(SceneChunk chunk, LodLevel lod)
        {
            // TODO: 实际应用LOD
            // 1. 切换模型LOD
            // 2. 调整渲染质量
            // 3. 禁用/启用某些组件

            foreach (var actor in chunk.LoadedActors)
            {
                if (actor == null) continue;

                // 示例：根据LOD调整可见性
                switch (lod)
                {
                    case LodLevel.None:
                        actor.IsActive = false;
                        break;
                    case LodLevel.Lod2:
                        actor.IsActive = true;
                        // TODO: 切换到LOD2模型
                        break;
                    case LodLevel.Lod1:
                        actor.IsActive = true;
                        // TODO: 切换到LOD1模型
                        break;
                    case LodLevel.Lod0:
                        actor.IsActive = true;
                        // TODO: 切换到LOD0高模
                        break;
                }
            }
        }

        #endregion
    }
}
