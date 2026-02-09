using FlaxEngine;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.Scene
{
    /// <summary>
    /// 基于分块的场景系统管理器
    /// 负责协调SceneChunkLoader和其他场景系统组件
    /// 提供高层API用于场景管理
    /// </summary>
    public class ChunkBasedSceneSystem : Script
    {
        #region 单例模式

        private static ChunkBasedSceneSystem _instance;
        public static ChunkBasedSceneSystem Instance => _instance;

        #endregion

        #region 配置参数

        [Header("系统配置")]
        [Tooltip("是否启用分块系统")]
        public bool EnableChunkSystem = true;

        [Tooltip("世界大小X(米)")]
        public float WorldSizeX = 2000f;

        [Tooltip("世界大小Z(米)")]
        public float WorldSizeZ = 2000f;

        [Tooltip("世界原点偏移")]
        public Vector3 WorldOrigin = Vector3.Zero;

        [Header("组件引用")]
        [Tooltip("场景分块加载器")]
        public SceneChunkLoader ChunkLoader;

        [Header("功能开关")]
        [Tooltip("是否启用流式加载")]
        public bool EnableStreaming = true;

        [Tooltip("是否启用分块烘焙")]
        public bool EnableChunkBaking = false;

        [Tooltip("是否启用动态遮挡剔除")]
        public bool EnableOcclusionCulling = true;

        [Header("调试")]
        [Tooltip("是否显示统计信息")]
        public bool ShowStatistics = true;

        [Tooltip("统计信息更新间隔(秒)")]
        public float StatisticsUpdateInterval = 1.0f;

        #endregion

        #region 私有字段

        // 玩家引用
        private Actor _player;

        // 当前加载的场景区域
        private string _currentSceneRegion = "";

        // 统计更新计时器
        private float _statisticsTimer = 0f;

        // 缓存的统计信息
        private string _cachedStatistics = "";

        #endregion

        #region 事件定义

        /// <summary>
        /// 场景区域变化事件
        /// </summary>
        public event Action<string, string> SceneRegionChanged;  // (oldRegion, newRegion)

        /// <summary>
        /// 系统就绪事件
        /// </summary>
        public event Action SystemReady;

        #endregion

        #region 初始化

        public override void OnAwake()
        {
            // 设置单例
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[ChunkSceneSystem] 检测到多个场景系统实例，销毁重复实例");
                Destroy(this);
                return;
            }

            _instance = this;
        }

        public override void OnEnable()
        {
            // 查找玩家
            _player = Scene.FindActor<Actor>("Player");

            if (_player == null)
            {
                Debug.LogWarning("[ChunkSceneSystem] 未找到玩家对象");
            }

            // 初始化分块加载器
            if (EnableChunkSystem && ChunkLoader == null)
            {
                ChunkLoader = Actor.GetScript<SceneChunkLoader>();
                
                if (ChunkLoader == null)
                {
                    // 自动添加SceneChunkLoader组件
                    ChunkLoader = Actor.AddScript<SceneChunkLoader>();
                    Debug.Log("[ChunkSceneSystem] 自动添加SceneChunkLoader组件");
                }
            }

            // 订阅分块事件
            if (ChunkLoader != null)
            {
                ChunkLoader.ChunkLoaded += OnChunkLoaded;
                ChunkLoader.ChunkUnloaded += OnChunkUnloaded;
                ChunkLoader.LodChanged += OnLodChanged;
            }

            // 初始化场景区域
            UpdateSceneRegion();

            Debug.Log("[ChunkSceneSystem] 分块场景系统已启动");
            
            // 触发就绪事件
            SystemReady?.Invoke();
        }

        #endregion

        #region 更新逻辑

        public override void OnUpdate()
        {
            if (!EnableChunkSystem) return;

            // 更新场景区域
            UpdateSceneRegion();

            // 更新统计信息
            if (ShowStatistics)
            {
                _statisticsTimer += Time.DeltaTime;
                if (_statisticsTimer >= StatisticsUpdateInterval)
                {
                    _statisticsTimer = 0f;
                    UpdateStatistics();
                }
            }
        }

        /// <summary>
        /// 更新场景区域
        /// </summary>
        private void UpdateSceneRegion()
        {
            if (_player == null || ChunkLoader == null) return;

            var playerChunk = ChunkLoader.GetPlayerChunk();
            if (playerChunk == null) return;

            // 根据玩家位置确定场景区域（示例：每4x4个分块为一个区域）
            int regionX = playerChunk.ChunkX / 4;
            int regionZ = playerChunk.ChunkZ / 4;
            string newRegion = $"Region_{regionX}_{regionZ}";

            if (newRegion != _currentSceneRegion)
            {
                string oldRegion = _currentSceneRegion;
                _currentSceneRegion = newRegion;

                Debug.Log($"[ChunkSceneSystem] 场景区域变化: {oldRegion} -> {newRegion}");
                
                // 触发区域变化事件
                SceneRegionChanged?.Invoke(oldRegion, newRegion);
            }
        }

        /// <summary>
        /// 更新统计信息
        /// </summary>
        private void UpdateStatistics()
        {
            if (ChunkLoader == null) return;

            _cachedStatistics = ChunkLoader.GetStatistics();
        }

        #endregion

        #region 分块事件处理

        /// <summary>
        /// 分块加载完成回调
        /// </summary>
        private void OnChunkLoaded(SceneChunkLoader.SceneChunk chunk)
        {
            // 可以在这里添加额外的处理逻辑
            // 例如：激活分块中的AI、触发器等

            Debug.Log($"[ChunkSceneSystem] 分块加载完成: ({chunk.ChunkX}, {chunk.ChunkZ})");
        }

        /// <summary>
        /// 分块卸载完成回调
        /// </summary>
        private void OnChunkUnloaded(SceneChunkLoader.SceneChunk chunk)
        {
            // 可以在这里添加额外的清理逻辑

            Debug.Log($"[ChunkSceneSystem] 分块卸载完成: ({chunk.ChunkX}, {chunk.ChunkZ})");
        }

        /// <summary>
        /// LOD变化回调
        /// </summary>
        private void OnLodChanged(SceneChunkLoader.SceneChunk chunk, 
                                  SceneChunkLoader.LodLevel oldLod, 
                                  SceneChunkLoader.LodLevel newLod)
        {
            // 可以在这里添加LOD切换的额外处理
            // 例如：调整物理精度、AI更新频率等

            // Debug.Log($"[ChunkSceneSystem] LOD变化: Chunk({chunk.ChunkX},{chunk.ChunkZ}) {oldLod} -> {newLod}");
        }

        #endregion

        #region 公共API

        /// <summary>
        /// 预加载指定位置周围的分块
        /// </summary>
        public void PreloadChunksAround(Vector3 position, int radius = 2)
        {
            if (ChunkLoader == null) return;

            var centerChunk = GetChunkCoordFromPosition(position);
            
            for (int x = centerChunk.Item1 - radius; x <= centerChunk.Item1 + radius; x++)
            {
                for (int z = centerChunk.Item2 - radius; z <= centerChunk.Item2 + radius; z++)
                {
                    ChunkLoader.ForceLoadChunk(x, z);
                }
            }

            Debug.Log($"[ChunkSceneSystem] 预加载分块: 中心({centerChunk.Item1},{centerChunk.Item2}), 半径{radius}");
        }

        /// <summary>
        /// 卸载指定位置周围的分块
        /// </summary>
        public void UnloadChunksAround(Vector3 position, int radius = 2)
        {
            if (ChunkLoader == null) return;

            var centerChunk = GetChunkCoordFromPosition(position);
            
            for (int x = centerChunk.Item1 - radius; x <= centerChunk.Item1 + radius; x++)
            {
                for (int z = centerChunk.Item2 - radius; z <= centerChunk.Item2 + radius; z++)
                {
                    ChunkLoader.ForceUnloadChunk(x, z);
                }
            }

            Debug.Log($"[ChunkSceneSystem] 卸载分块: 中心({centerChunk.Item1},{centerChunk.Item2}), 半径{radius}");
        }

        /// <summary>
        /// 传送玩家到指定位置（自动加载周围分块）
        /// </summary>
        public void TeleportPlayer(Vector3 targetPosition)
        {
            if (_player == null)
            {
                Debug.LogWarning("[ChunkSceneSystem] 无法传送：未找到玩家");
                return;
            }

            // 先预加载目标位置的分块
            PreloadChunksAround(targetPosition, 3);

            // 等待关键分块加载完成后再传送
            var targetChunk = GetChunkAtPosition(targetPosition);
            if (targetChunk != null && targetChunk.LoadState == SceneChunkLoader.ChunkLoadState.Loading)
            {
                // 如果目标分块正在加载，延迟传送到下一帧
                Scripting.InvokeOnUpdate(() =>
                {
                    if (_player != null)
                    {
                        _player.Position = targetPosition;
                        Debug.Log($"[ChunkSceneSystem] 玩家已传送到: {targetPosition}（等待分块加载后）");
                    }
                });
            }
            else
            {
                _player.Position = targetPosition;
                Debug.Log($"[ChunkSceneSystem] 玩家已传送到: {targetPosition}");
            }
        }

        /// <summary>
        /// 设置分块为持久化（不会被自动卸载）
        /// </summary>
        public void SetChunkPersistent(int chunkX, int chunkZ, bool persistent)
        {
            if (ChunkLoader == null) return;

            ChunkLoader.SetChunkPersistent(chunkX, chunkZ, persistent);
        }

        /// <summary>
        /// 设置区域内所有分块为持久化
        /// </summary>
        public void SetRegionPersistent(int centerX, int centerZ, int radius, bool persistent)
        {
            if (ChunkLoader == null) return;

            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                for (int z = centerZ - radius; z <= centerZ + radius; z++)
                {
                    ChunkLoader.SetChunkPersistent(x, z, persistent);
                }
            }

            Debug.Log($"[ChunkSceneSystem] 设置区域持久化: 中心({centerX},{centerZ}), 半径{radius}, 状态{persistent}");
        }

        /// <summary>
        /// 获取指定位置的分块
        /// </summary>
        public SceneChunkLoader.SceneChunk GetChunkAtPosition(Vector3 position)
        {
            if (ChunkLoader == null) return null;

            var coord = GetChunkCoordFromPosition(position);
            return ChunkLoader.GetChunk(coord.Item1, coord.Item2);
        }

        /// <summary>
        /// 获取当前加载的分块数量
        /// </summary>
        public int GetLoadedChunkCount()
        {
            if (ChunkLoader == null) return 0;

            // 通过ChunkLoader获取已加载分块数量
            return ChunkLoader.GetLoadedChunkCount();
        }

        /// <summary>
        /// 清空所有对象池
        /// </summary>
        public void ClearAllObjectPools()
        {
            if (ChunkLoader == null) return;

            ChunkLoader.ClearObjectPools();
            Debug.Log("[ChunkSceneSystem] 已清空所有对象池");
        }

        /// <summary>
        /// 重置所有统计数据
        /// </summary>
        public void ResetStatistics()
        {
            if (ChunkLoader == null) return;

            ChunkLoader.ResetStatistics();
            _cachedStatistics = "";
            Debug.Log("[ChunkSceneSystem] 统计数据已重置");
        }

        /// <summary>
        /// 获取统计信息
        /// </summary>
        public string GetStatistics()
        {
            return _cachedStatistics;
        }

        /// <summary>
        /// 获取当前场景区域名称
        /// </summary>
        public string GetCurrentRegion()
        {
            return _currentSceneRegion;
        }

        #endregion

        #region 工具方法

        /// <summary>
        /// 根据世界坐标获取分块坐标
        /// </summary>
        private (int, int) GetChunkCoordFromPosition(Vector3 worldPos)
        {
            if (ChunkLoader == null) return (0, 0);

            float chunkSize = ChunkLoader.ChunkSize;
            int x = Mathf.FloorToInt(worldPos.X / chunkSize);
            int z = Mathf.FloorToInt(worldPos.Z / chunkSize);
            return (x, z);
        }

        /// <summary>
        /// 根据分块坐标获取世界中心位置
        /// </summary>
        private Vector3 GetChunkWorldCenter(int chunkX, int chunkZ)
        {
            if (ChunkLoader == null) return Vector3.Zero;

            float chunkSize = ChunkLoader.ChunkSize;
            return new Vector3(
                chunkX * chunkSize + chunkSize * 0.5f,
                0,
                chunkZ * chunkSize + chunkSize * 0.5f
            ) + WorldOrigin;
        }

        #endregion

        #region 调试功能

        public override void OnDebugDraw()
        {
            if (!ShowStatistics || string.IsNullOrEmpty(_cachedStatistics))
                return;

            // 将统计信息写入日志（调试用途）
            // 注：DebugDraw.DrawText使用世界坐标，屏幕覆盖显示需通过UI系统实现
            if (ChunkLoader != null && EnableChunkSystem)
            {
                Debug.Log($"[ChunkSceneSystem] {_cachedStatistics}");
            }
        }

        /// <summary>
        /// 手动测试：加载测试分块
        /// </summary>
        [Button("测试加载分块")]
        public void TestLoadChunk()
        {
            if (ChunkLoader == null)
            {
                Debug.LogWarning("[ChunkSceneSystem] ChunkLoader未初始化");
                return;
            }

            // 加载玩家前方的分块
            if (_player != null)
            {
                var playerChunk = ChunkLoader.GetPlayerChunk();
                if (playerChunk != null)
                {
                    // 加载前方分块
                    ChunkLoader.ForceLoadChunk(playerChunk.ChunkX + 1, playerChunk.ChunkZ);
                    Debug.Log($"[ChunkSceneSystem] 测试加载分块: ({playerChunk.ChunkX + 1}, {playerChunk.ChunkZ})");
                }
            }
        }

        /// <summary>
        /// 手动测试：卸载测试分块
        /// </summary>
        [Button("测试卸载分块")]
        public void TestUnloadChunk()
        {
            if (ChunkLoader == null)
            {
                Debug.LogWarning("[ChunkSceneSystem] ChunkLoader未初始化");
                return;
            }

            // 卸载玩家后方的分块
            if (_player != null)
            {
                var playerChunk = ChunkLoader.GetPlayerChunk();
                if (playerChunk != null)
                {
                    // 卸载后方分块
                    ChunkLoader.ForceUnloadChunk(playerChunk.ChunkX - 1, playerChunk.ChunkZ);
                    Debug.Log($"[ChunkSceneSystem] 测试卸载分块: ({playerChunk.ChunkX - 1}, {playerChunk.ChunkZ})");
                }
            }
        }

        /// <summary>
        /// 手动测试：打印统计信息
        /// </summary>
        [Button("打印统计信息")]
        public void PrintStatistics()
        {
            if (ChunkLoader == null)
            {
                Debug.LogWarning("[ChunkSceneSystem] ChunkLoader未初始化");
                return;
            }

            string stats = ChunkLoader.GetStatistics();
            Debug.Log($"[ChunkSceneSystem] 统计信息:\n{stats}");
        }

        #endregion

        #region 生命周期

        public override void OnDisable()
        {
            // 取消订阅事件
            if (ChunkLoader != null)
            {
                ChunkLoader.ChunkLoaded -= OnChunkLoaded;
                ChunkLoader.ChunkUnloaded -= OnChunkUnloaded;
                ChunkLoader.LodChanged -= OnLodChanged;
            }

            // 清空单例
            if (_instance == this)
            {
                _instance = null;
            }

            Debug.Log("[ChunkSceneSystem] 分块场景系统已停止");
        }

        #endregion
    }
}
