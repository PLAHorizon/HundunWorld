using FlaxEngine;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HundunWorld.Game.Network.Sync
{
    /// <summary>
    /// AOI(Area Of Interest)兴趣区域管理器
    /// 实现视野范围管理和动态实体加载/卸载
    /// 设计参考: client-core-feature-development.md - 8.1.4 AOI算法
    /// </summary>
    public class AoiManager : Script
    {
        #region 实体类型定义

        /// <summary>
        /// 实体类型
        /// </summary>
        public enum EntityType
        {
            Player,      // 玩家
            Npc,         // NPC
            Monster,     // 怪物
            Item,        // 物品
            Effect       // 特效
        }

        #endregion

        #region AOI实体数据

        /// <summary>
        /// AOI实体数据
        /// </summary>
        public class AoiEntity
        {
            public ulong EntityId;
            public EntityType Type;
            public Actor EntityActor;
            public Vector3 Position;
            public float Radius;          // 实体半径
            public int Priority;          // 优先级(0-100)
            public bool IsVisible;        // 当前是否可见
            public float EnterTime;       // 进入AOI的时间
            public float ExitTime;        // 离开AOI的时间
            public int GridX;             // 所在网格X坐标
            public int GridZ;             // 所在网格Z坐标
        }

        #endregion

        #region 配置参数

        [Header("AOI范围配置")]
        [Tooltip("视野范围半径(米)")]
        public float ViewRadius = 100f;

        [Tooltip("缓冲范围半径(米) - 防止频繁加卸载")]
        public float BufferRadius = 120f;

        [Tooltip("更新频率(秒)")]
        public float UpdateInterval = 1.0f;

        [Header("网格划分")]
        [Tooltip("是否启用网格优化")]
        public bool EnableGridOptimization = true;

        [Tooltip("网格大小(米)")]
        public float GridSize = 50f;

        [Tooltip("世界大小X(米)")]
        public float WorldSizeX = 2000f;

        [Tooltip("世界大小Z(米)")]
        public float WorldSizeZ = 2000f;

        [Header("实体限制")]
        [Tooltip("最大可见玩家数")]
        public int MaxVisiblePlayers = 100;

        [Tooltip("最大可见NPC数")]
        public int MaxVisibleNpcs = 200;

        [Tooltip("最大可见怪物数")]
        public int MaxVisibleMonsters = 150;

        [Tooltip("最大可见物品数")]
        public int MaxVisibleItems = 50;

        [Header("优先级权重")]
        [Tooltip("玩家优先级")]
        public int PlayerPriority = 100;

        [Tooltip("Boss优先级")]
        public int BossPriority = 95;

        [Tooltip("精英怪优先级")]
        public int ElitePriority = 80;

        [Tooltip("普通NPC优先级")]
        public int NpcPriority = 50;

        [Header("调试")]
        [Tooltip("是否启用日志")]
        public bool EnableLogging = false;

        [Tooltip("是否显示调试可视化")]
        public bool ShowDebugVisualization = false;

        #endregion

        #region 私有字段

        // 所有实体字典
        private readonly Dictionary<ulong, AoiEntity> _allEntities = new();

        // 可见实体列表
        private readonly HashSet<ulong> _visibleEntities = new();

        // 进入AOI的实体队列
        private readonly Queue<AoiEntity> _enterQueue = new();

        // 离开AOI的实体队列
        private readonly Queue<AoiEntity> _exitQueue = new();

        // 网格系统(用于快速查询附近实体)
        private Dictionary<(int, int), HashSet<ulong>> _grid;
        private int _gridCountX;
        private int _gridCountZ;

        // 玩家引用
        private Actor _player;
        private Vector3 _lastPlayerPosition;

        // 更新计时器
        private float _updateTimer = 0f;

        // 统计数据
        private int _totalEntityCount = 0;
        private int _visibleEntityCount = 0;
        private int _enterEventCount = 0;
        private int _exitEventCount = 0;

        #endregion

        #region 事件定义

        /// <summary>
        /// 实体进入AOI事件
        /// </summary>
        public event Action<AoiEntity> EntityEntered;

        /// <summary>
        /// 实体离开AOI事件
        /// </summary>
        public event Action<AoiEntity> EntityExited;

        /// <summary>
        /// AOI更新事件
        /// </summary>
        public event Action<int, int> AoiUpdated;  // (进入数量, 离开数量)

        #endregion

        #region 初始化

        public override void OnEnable()
        {
            // 初始化网格系统
            if (EnableGridOptimization)
            {
                InitializeGrid();
            }

            // 查找玩家
            _player = Scene.FindActor<Actor>("Player");

            if (_player == null)
            {
                Debug.LogWarning("[AOI] 未找到玩家对象");
            }
            else
            {
                _lastPlayerPosition = _player.Position;
            }

            if (EnableLogging)
            {
                Debug.Log("[AOI] AOI管理器已启动");
            }
        }

        /// <summary>
        /// 初始化网格系统
        /// </summary>
        private void InitializeGrid()
        {
            _gridCountX = Mathf.CeilToInt(WorldSizeX / GridSize);
            _gridCountZ = Mathf.CeilToInt(WorldSizeZ / GridSize);
            _grid = new Dictionary<(int, int), HashSet<ulong>>();

            if (EnableLogging)
            {
                Debug.Log($"[AOI] 初始化网格系统 - {_gridCountX}x{_gridCountZ} ({GridSize}m)");
            }
        }

        #endregion

        #region 实体注册与管理

        /// <summary>
        /// 注册实体
        /// </summary>
        public void RegisterEntity(ulong entityId, EntityType type, Actor actor, float radius = 1.0f)
        {
            if (_allEntities.ContainsKey(entityId))
            {
                Debug.LogWarning($"[AOI] 实体 {entityId} 已存在，忽略重复注册");
                return;
            }

            var entity = new AoiEntity
            {
                EntityId = entityId,
                Type = type,
                EntityActor = actor,
                Position = actor != null ? actor.Position : Vector3.Zero,
                Radius = radius,
                Priority = CalculatePriority(type),
                IsVisible = false
            };

            _allEntities.Add(entityId, entity);
            _totalEntityCount++;

            // 添加到网格
            if (EnableGridOptimization)
            {
                AddToGrid(entity);
            }

            if (EnableLogging)
            {
                Debug.Log($"[AOI] 注册实体 - ID:{entityId}, 类型:{type}, 总数:{_totalEntityCount}");
            }
        }

        /// <summary>
        /// 注销实体
        /// </summary>
        public void UnregisterEntity(ulong entityId)
        {
            if (_allEntities.TryGetValue(entityId, out var entity))
            {
                // 从网格移除
                if (EnableGridOptimization)
                {
                    RemoveFromGrid(entity);
                }

                // 如果实体当前可见，触发离开事件
                if (entity.IsVisible)
                {
                    entity.IsVisible = false;
                    entity.ExitTime = Time.GameTime;
                    EntityExited?.Invoke(entity);
                    _exitEventCount++;
                }

                _allEntities.Remove(entityId);
                _visibleEntities.Remove(entityId);
                _totalEntityCount--;

                if (EnableLogging)
                {
                    Debug.Log($"[AOI] 注销实体 - ID:{entityId}, 剩余:{_totalEntityCount}");
                }
            }
        }

        /// <summary>
        /// 更新实体位置
        /// </summary>
        public void UpdateEntityPosition(ulong entityId, Vector3 position)
        {
            if (_allEntities.TryGetValue(entityId, out var entity))
            {
                // 更新网格(如果位置发生变化)
                if (EnableGridOptimization)
                {
                    var oldGrid = WorldToGrid(entity.Position);
                    var newGrid = WorldToGrid(position);

                    if (oldGrid != newGrid)
                    {
                        RemoveFromGrid(entity);
                        entity.Position = position;
                        AddToGrid(entity);
                    }
                    else
                    {
                        entity.Position = position;
                    }
                }
                else
                {
                    entity.Position = position;
                }
            }
        }

        /// <summary>
        /// 清除所有实体
        /// </summary>
        public void ClearAllEntities()
        {
            _allEntities.Clear();
            _visibleEntities.Clear();
            _enterQueue.Clear();
            _exitQueue.Clear();
            
            // 清空网格
            if (_grid != null)
            {
                _grid.Clear();
            }
            
            _totalEntityCount = 0;
            _visibleEntityCount = 0;
            
            if (EnableLogging)
            {
                Debug.Log("[AOI] 已清除所有实体");
            }
        }

        #endregion

        #region AOI更新

        public override void OnUpdate()
        {
            if (_player == null) return;

            _updateTimer += Time.DeltaTime;

            if (_updateTimer >= UpdateInterval)
            {
                UpdateAoi();
                _updateTimer = 0f;
            }
        }

        /// <summary>
        /// 更新AOI
        /// </summary>
        private void UpdateAoi()
        {
            Vector3 playerPos = _player.Position;

            // 检查玩家位置是否显著移动
            float movedDistance = Vector3.Distance(playerPos, _lastPlayerPosition);
            if (movedDistance < 1.0f)  // 移动距离小于1米，跳过更新
            {
                return;
            }

            _lastPlayerPosition = playerPos;

            // 清空队列
            _enterQueue.Clear();
            _exitQueue.Clear();

            // 获取附近实体
            HashSet<ulong> nearbyEntities = EnableGridOptimization 
                ? GetNearbyEntitiesFromGrid(playerPos)
                : new HashSet<ulong>(_allEntities.Keys);

            // 检查每个实体是否在AOI范围内
            foreach (var entityId in nearbyEntities)
            {
                if (!_allEntities.TryGetValue(entityId, out var entity))
                {
                    continue;
                }

                float distance = Vector3.Distance(playerPos, entity.Position);
                bool shouldBeVisible = distance <= ViewRadius;
                bool inBuffer = distance <= BufferRadius;

                if (shouldBeVisible && !entity.IsVisible)
                {
                    // 实体进入AOI
                    entity.IsVisible = true;
                    entity.EnterTime = Time.GameTime;
                    _visibleEntities.Add(entityId);
                    _enterQueue.Enqueue(entity);
                }
                else if (!inBuffer && entity.IsVisible)
                {
                    // 实体离开AOI(超出缓冲范围)
                    entity.IsVisible = false;
                    entity.ExitTime = Time.GameTime;
                    _visibleEntities.Remove(entityId);
                    _exitQueue.Enqueue(entity);
                }
            }

            _visibleEntityCount = _visibleEntities.Count;

            // 应用实体数量限制
            ApplyEntityLimits();

            // 触发事件
            ProcessAoiEvents();

            // 触发更新事件
            AoiUpdated?.Invoke(_enterQueue.Count, _exitQueue.Count);

            if (EnableLogging && (_enterQueue.Count > 0 || _exitQueue.Count > 0))
            {
                Debug.Log($"[AOI] 更新完成 - 进入:{_enterQueue.Count}, 离开:{_exitQueue.Count}, 可见:{_visibleEntityCount}");
            }
        }

        /// <summary>
        /// 应用实体数量限制
        /// </summary>
        private void ApplyEntityLimits()
        {
            // 按类型分组
            var playerEntities = _visibleEntities.Where(id => _allEntities[id].Type == EntityType.Player).ToList();
            var npcEntities = _visibleEntities.Where(id => _allEntities[id].Type == EntityType.Npc).ToList();
            var monsterEntities = _visibleEntities.Where(id => _allEntities[id].Type == EntityType.Monster).ToList();

            // 超过限制时，移除优先级最低的
            RemoveExcessEntities(playerEntities, MaxVisiblePlayers);
            RemoveExcessEntities(npcEntities, MaxVisibleNpcs);
            RemoveExcessEntities(monsterEntities, MaxVisibleMonsters);
        }

        /// <summary>
        /// 移除超量实体
        /// </summary>
        private void RemoveExcessEntities(List<ulong> entities, int maxCount)
        {
            if (entities.Count <= maxCount) return;

            // 按优先级排序(优先级低的在前)
            entities.Sort((a, b) => _allEntities[a].Priority.CompareTo(_allEntities[b].Priority));

            // 移除优先级最低的实体
            int removeCount = entities.Count - maxCount;
            for (int i = 0; i < removeCount; i++)
            {
                var entity = _allEntities[entities[i]];
                if (entity.IsVisible)
                {
                    entity.IsVisible = false;
                    entity.ExitTime = Time.GameTime;
                    _visibleEntities.Remove(entity.EntityId);
                    _exitQueue.Enqueue(entity);
                }
            }
        }

        /// <summary>
        /// 处理AOI事件
        /// </summary>
        private void ProcessAoiEvents()
        {
            // 处理进入事件
            while (_enterQueue.Count > 0)
            {
                var entity = _enterQueue.Dequeue();
                EntityEntered?.Invoke(entity);
                _enterEventCount++;
            }

            // 处理离开事件
            while (_exitQueue.Count > 0)
            {
                var entity = _exitQueue.Dequeue();
                EntityExited?.Invoke(entity);
                _exitEventCount++;
            }
        }

        #endregion

        #region 网格系统

        /// <summary>
        /// 将实体添加到网格
        /// </summary>
        private void AddToGrid(AoiEntity entity)
        {
            var gridPos = WorldToGrid(entity.Position);
            entity.GridX = gridPos.Item1;
            entity.GridZ = gridPos.Item2;

            if (!_grid.ContainsKey(gridPos))
            {
                _grid[gridPos] = new HashSet<ulong>();
            }

            _grid[gridPos].Add(entity.EntityId);
        }

        /// <summary>
        /// 从网格移除实体
        /// </summary>
        private void RemoveFromGrid(AoiEntity entity)
        {
            var gridPos = (entity.GridX, entity.GridZ);

            if (_grid.TryGetValue(gridPos, out var gridEntities))
            {
                gridEntities.Remove(entity.EntityId);

                if (gridEntities.Count == 0)
                {
                    _grid.Remove(gridPos);
                }
            }
        }

        /// <summary>
        /// 世界坐标转网格坐标
        /// </summary>
        private (int, int) WorldToGrid(Vector3 position)
        {
            int gridX = Mathf.FloorToInt((position.X + WorldSizeX / 2f) / GridSize);
            int gridZ = Mathf.FloorToInt((position.Z + WorldSizeZ / 2f) / GridSize);

            gridX = Mathf.Clamp(gridX, 0, _gridCountX - 1);
            gridZ = Mathf.Clamp(gridZ, 0, _gridCountZ - 1);

            return (gridX, gridZ);
        }

        /// <summary>
        /// 从网格获取附近实体
        /// </summary>
        private HashSet<ulong> GetNearbyEntitiesFromGrid(Vector3 position)
        {
            var result = new HashSet<ulong>();
            var centerGrid = WorldToGrid(position);

            // 计算需要检查的网格范围
            int gridRadius = Mathf.CeilToInt(BufferRadius / GridSize);

            for (int x = centerGrid.Item1 - gridRadius; x <= centerGrid.Item1 + gridRadius; x++)
            {
                for (int z = centerGrid.Item2 - gridRadius; z <= centerGrid.Item2 + gridRadius; z++)
                {
                    if (x < 0 || x >= _gridCountX || z < 0 || z >= _gridCountZ)
                    {
                        continue;
                    }

                    if (_grid.TryGetValue((x, z), out var gridEntities))
                    {
                        foreach (var entityId in gridEntities)
                        {
                            result.Add(entityId);
                        }
                    }
                }
            }

            return result;
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 计算实体优先级
        /// </summary>
        private int CalculatePriority(EntityType type)
        {
            return type switch
            {
                EntityType.Player => PlayerPriority,
                EntityType.Npc => NpcPriority,
                EntityType.Monster => ElitePriority,  // 默认为精英
                EntityType.Item => 40,
                EntityType.Effect => 30,
                _ => 50
            };
        }

        /// <summary>
        /// 获取可见实体列表
        /// </summary>
        public List<AoiEntity> GetVisibleEntities()
        {
            return _visibleEntities
                .Where(id => _allEntities.ContainsKey(id))
                .Select(id => _allEntities[id])
                .ToList();
        }

        /// <summary>
        /// 获取指定类型的可见实体数量
        /// </summary>
        public int GetVisibleEntityCount(EntityType type)
        {
            return _visibleEntities.Count(id => _allEntities.ContainsKey(id) && _allEntities[id].Type == type);
        }

        #endregion

        #region 统计与调试

        /// <summary>
        /// 获取统计信息
        /// </summary>
        public string GetStatistics()
        {
            return $"AOI统计:\n" +
                   $"  总实体数: {_totalEntityCount}\n" +
                   $"  可见实体: {_visibleEntityCount}\n" +
                   $"  玩家: {GetVisibleEntityCount(EntityType.Player)}/{MaxVisiblePlayers}\n" +
                   $"  NPC: {GetVisibleEntityCount(EntityType.Npc)}/{MaxVisibleNpcs}\n" +
                   $"  怪物: {GetVisibleEntityCount(EntityType.Monster)}/{MaxVisibleMonsters}\n" +
                   $"  进入事件: {_enterEventCount}\n" +
                   $"  离开事件: {_exitEventCount}\n" +
                   $"  网格数量: {_grid?.Count ?? 0}";
        }

        /// <summary>
        /// 重置统计
        /// </summary>
        public void ResetStatistics()
        {
            _enterEventCount = 0;
            _exitEventCount = 0;
        }

        /// <summary>
        /// 绘制调试可视化
        /// </summary>
        public override void OnDebugDraw()
        {
            if (!ShowDebugVisualization || _player == null) return;

            Vector3 playerPos = _player.Position;

            // 绘制AOI范围
            DebugDraw.DrawCircle(playerPos, Vector3.Up, ViewRadius, Color.Green, 1.0f);
            DebugDraw.DrawCircle(playerPos, Vector3.Up, BufferRadius, Color.Yellow, 1.0f);

            // 绘制网格
            if (EnableGridOptimization)
            {
                DrawGrid();
            }

            // 绘制可见实体
            foreach (var entityId in _visibleEntities)
            {
                if (_allEntities.TryGetValue(entityId, out var entity))
                {
                    Color color = entity.Type switch
                    {
                        EntityType.Player => Color.Blue,
                        EntityType.Npc => Color.Yellow,
                        EntityType.Monster => Color.Red,
                        EntityType.Item => Color.Green,
                        EntityType.Effect => Color.Cyan,
                        _ => Color.Gray
                    };

                    DebugDraw.DrawSphere(new BoundingSphere(entity.Position, entity.Radius), color, 1.0f);
                }
            }
        }

        /// <summary>
        /// 绘制网格
        /// </summary>
        private void DrawGrid()
        {
            if (_player == null) return;

            var playerGrid = WorldToGrid(_player.Position);
            int gridRadius = Mathf.CeilToInt(BufferRadius / GridSize);

            for (int x = playerGrid.Item1 - gridRadius; x <= playerGrid.Item1 + gridRadius; x++)
            {
                for (int z = playerGrid.Item2 - gridRadius; z <= playerGrid.Item2 + gridRadius; z++)
                {
                    if (x < 0 || x >= _gridCountX || z < 0 || z >= _gridCountZ)
                    {
                        continue;
                    }

                    float worldX = x * GridSize - WorldSizeX / 2f;
                    float worldZ = z * GridSize - WorldSizeZ / 2f;

                    Vector3 corner1 = new Vector3(worldX, 0, worldZ);
                    Vector3 corner2 = new Vector3(worldX + GridSize, 0, worldZ);
                    Vector3 corner3 = new Vector3(worldX + GridSize, 0, worldZ + GridSize);
                    Vector3 corner4 = new Vector3(worldX, 0, worldZ + GridSize);

                    Color gridColor = _grid.ContainsKey((x, z)) ? Color.White : Color.Gray;
                    gridColor.A = 0.3f;

                    DebugDraw.DrawLine(corner1, corner2, gridColor, 0.5f);
                    DebugDraw.DrawLine(corner2, corner3, gridColor, 0.5f);
                    DebugDraw.DrawLine(corner3, corner4, gridColor, 0.5f);
                    DebugDraw.DrawLine(corner4, corner1, gridColor, 0.5f);
                }
            }
        }

        #endregion
    }
}
