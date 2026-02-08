using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlaxEngine;
using Arch.Core;
using HundunWorld.Game.ECS;
using HundunWorld.Game.ECS.Components;
using HundunWorld.Game.Network;

namespace HundunWorld.Game.Worlds
{
    /// <summary>
    /// 实体同步管理器
    /// 负责管理游戏世界中各种实体的同步状态
    /// </summary>
    public class EntitySynchronizationManager
    {
        private readonly NetworkManager _networkManager;
        private readonly World _world;
        private readonly Dictionary<ulong, EntitySyncInfo> _syncEntities;
        private readonly Dictionary<EntityType, EntitySyncConfig> _syncConfigs;
        private NetworkEntityRegistry _entityRegistry;
        
        // 同步时间间隔（毫秒）
        private const float POSITION_SYNC_INTERVAL = 100f; // 位置同步间隔
        private const float STATE_SYNC_INTERVAL = 500f;    // 状态同步间隔
        
        private float _lastPositionSyncTime = 0f;
        private float _lastStateSyncTime = 0f;
        
        public enum EntityType
        {
            Player,
            Npc,
            Monster,
            Item,
            Projectile,
            Environment
        }
        
        public enum SyncPriority
        {
            High = 0,    // 高优先级（玩家、重要NPC）
            Medium = 1,  // 中优先级（普通怪物、道具）
            Low = 2      // 低优先级（环境物体）
        }
        
        public class EntitySyncInfo
        {
            public ulong EntityId { get; set; }
            public EntityType Type { get; set; }
            public SyncPriority Priority { get; set; }
            public Entity Entity { get; set; }
            public Vector3 LastSyncPosition { get; set; }
            public Vector3 CurrentPosition { get; set; }
            public bool IsDirty { get; set; }
            public float LastSyncTime { get; set; }
            public float SyncInterval { get; set; }
        }
        
        public class EntitySyncConfig
        {
            public SyncPriority Priority { get; set; }
            public float SyncInterval { get; set; }
            public float PositionThreshold { get; set; } // 位置变化阈值
            public bool SyncRotation { get; set; }
            public bool SyncScale { get; set; }
        }
        
        public EntitySynchronizationManager(NetworkManager networkManager, World world)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _syncEntities = new Dictionary<ulong, EntitySyncInfo>();
            _syncConfigs = new Dictionary<EntityType, EntitySyncConfig>();
            
            InitializeSyncConfigs();
        }

        /// <summary>
        /// 设置网络实体注册表引用
        /// </summary>
        public void SetEntityRegistry(NetworkEntityRegistry registry)
        {
            _entityRegistry = registry ?? throw new ArgumentNullException(nameof(registry));
        }
        
        /// <summary>
        /// 初始化同步配置
        /// </summary>
        private void InitializeSyncConfigs()
        {
            _syncConfigs[EntityType.Player] = new EntitySyncConfig
            {
                Priority = SyncPriority.High,
                SyncInterval = 50f,
                PositionThreshold = 0.1f,
                SyncRotation = true,
                SyncScale = false
            };
            
            _syncConfigs[EntityType.Npc] = new EntitySyncConfig
            {
                Priority = SyncPriority.Medium,
                SyncInterval = 200f,
                PositionThreshold = 0.5f,
                SyncRotation = true,
                SyncScale = false
            };
            
            _syncConfigs[EntityType.Monster] = new EntitySyncConfig
            {
                Priority = SyncPriority.Medium,
                SyncInterval = 100f,
                PositionThreshold = 0.3f,
                SyncRotation = true,
                SyncScale = false
            };
            
            _syncConfigs[EntityType.Item] = new EntitySyncConfig
            {
                Priority = SyncPriority.Low,
                SyncInterval = 1000f,
                PositionThreshold = 1.0f,
                SyncRotation = false,
                SyncScale = false
            };
            
            _syncConfigs[EntityType.Projectile] = new EntitySyncConfig
            {
                Priority = SyncPriority.High,
                SyncInterval = 30f,
                PositionThreshold = 0.05f,
                SyncRotation = true,
                SyncScale = false
            };
        }
        
        /// <summary>
        /// 注册需要同步的实体
        /// </summary>
        public void RegisterEntity(ulong entityId, EntityType entityType, Entity entity)
        {
            if (_syncConfigs.TryGetValue(entityType, out var config))
            {
                var syncInfo = new EntitySyncInfo
                {
                    EntityId = entityId,
                    Type = entityType,
                    Priority = config.Priority,
                    Entity = entity,
                    SyncInterval = config.SyncInterval,
                    LastSyncTime = Time.GameTime
                };
                
                // 获取当前位置
                if (_world.Has<PositionComponent>(entity))
                {
                    syncInfo.CurrentPosition = _world.Get<PositionComponent>(entity).Position;
                    syncInfo.LastSyncPosition = syncInfo.CurrentPosition;
                }
                
                _syncEntities[entityId] = syncInfo;

                // 同步注册到网络实体注册表
                _entityRegistry?.Register(entityId, entity);

                Debug.Log($"[EntitySync] 实体已注册同步: ID={entityId}, Type={entityType}");
            }
        }
        
        /// <summary>
        /// 移除同步实体
        /// </summary>
        public void UnregisterEntity(ulong entityId)
        {
            if (_syncEntities.ContainsKey(entityId))
            {
                _syncEntities.Remove(entityId);

                // 同步从网络实体注册表注销
                _entityRegistry?.Unregister(entityId);

                Debug.Log($"[EntitySync] 实体已取消同步: ID={entityId}");
            }
        }
        
        /// <summary>
        /// 更新实体同步状态
        /// </summary>
        public void Update(float deltaTime)
        {
            // 更新所有实体的位置信息
            UpdateEntityPositions();
            
            // 检查是否需要同步
            CheckAndSendSyncUpdates();
        }
        
        /// <summary>
        /// 更新实体位置信息
        /// </summary>
        private void UpdateEntityPositions()
        {
            foreach (var syncInfo in _syncEntities.Values)
            {
                if (_world.Has<PositionComponent>(syncInfo.Entity))
                {
                    var position = _world.Get<PositionComponent>(syncInfo.Entity).Position;
                    syncInfo.CurrentPosition = position;
                    
                    // 检查位置是否发生变化
                    var distance = Vector3.Distance(syncInfo.CurrentPosition, syncInfo.LastSyncPosition);
                    if (distance > GetPositionThreshold(syncInfo.Type))
                    {
                        syncInfo.IsDirty = true;
                    }
                }
            }
        }
        
        /// <summary>
        /// 检查并发送同步更新
        /// </summary>
        private void CheckAndSendSyncUpdates()
        {
            float currentTime = Time.GameTime;
            
            // 按优先级排序实体
            var sortedEntities = new List<EntitySyncInfo>(_syncEntities.Values);
            sortedEntities.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            
            foreach (var syncInfo in sortedEntities)
            {
                // 检查同步间隔
                if (currentTime - syncInfo.LastSyncTime >= syncInfo.SyncInterval / 1000f)
                {
                    if (syncInfo.IsDirty)
                    {
                        SendEntityUpdate(syncInfo);
                        syncInfo.LastSyncTime = currentTime;
                        syncInfo.IsDirty = false;
                        syncInfo.LastSyncPosition = syncInfo.CurrentPosition;
                    }
                }
            }
        }
        
        /// <summary>
        /// 发送实体更新
        /// </summary>
        private async void SendEntityUpdate(EntitySyncInfo syncInfo)
        {
            try
            {
                // 构造同步消息（这里需要根据实际的消息结构进行调整）
                /*
                var updateMessage = new EntityUpdateMessage
                {
                    EntityId = syncInfo.EntityId,
                    EntityType = syncInfo.Type,
                    Position = syncInfo.CurrentPosition,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                
                if (_world.Has<RotationComponent>(syncInfo.Entity) && ShouldSyncRotation(syncInfo.Type))
                {
                    updateMessage.Rotation = _world.Get<RotationComponent>(syncInfo.Entity).Rotation;
                }
                
                var messagePacket = new HorizonMessagePacket
                {
                    ServiceType = ServiceType.World,
                    Header = new MessageHeader
                    {
                        MessageType = MessageType.EntityUpdate,
                        MessageId = Guid.NewGuid().ToString()
                    },
                    Body = new MessageUnion { EntityUpdate = updateMessage }
                };
                
                await _networkManager.SendMessageAsync(messagePacket);
                */
                
                Debug.Log($"[EntitySync] 发送实体更新: ID={syncInfo.EntityId}, Pos={syncInfo.CurrentPosition}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EntitySync] 发送实体更新失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 处理接收到的实体更新
        /// </summary>
        public void HandleEntityUpdate(/* EntityUpdateMessage updateMessage */ object updateMessage)
        {
            // 这里需要根据实际的消息结构进行调整
            /*
            if (_syncEntities.TryGetValue(updateMessage.EntityId, out var syncInfo))
            {
                // 更新本地实体状态
                if (_world.Has<PositionComponent>(syncInfo.Entity))
                {
                    var position = _world.Get<PositionComponent>(syncInfo.Entity);
                    position.Position = updateMessage.Position;
                    _world.Set(syncInfo.Entity, position);
                }
                
                if (updateMessage.Rotation.HasValue && _world.Has<RotationComponent>(syncInfo.Entity))
                {
                    var rotation = _world.Get<RotationComponent>(syncInfo.Entity);
                    rotation.Rotation = updateMessage.Rotation.Value;
                    _world.Set(syncInfo.Entity, rotation);
                }
                
                Debug.Log($"[EntitySync] 接收实体更新: ID={updateMessage.EntityId}");
            }
            */
        }
        
        /// <summary>
        /// 获取位置变化阈值
        /// </summary>
        private float GetPositionThreshold(EntityType entityType)
        {
            return _syncConfigs.TryGetValue(entityType, out var config) ? config.PositionThreshold : 0.1f;
        }
        
        /// <summary>
        /// 检查是否应该同步旋转
        /// </summary>
        private bool ShouldSyncRotation(EntityType entityType)
        {
            return _syncConfigs.TryGetValue(entityType, out var config) && config.SyncRotation;
        }
        
        /// <summary>
        /// 获取同步统计信息
        /// </summary>
        public Dictionary<string, object> GetSyncStatistics()
        {
            var stats = new Dictionary<string, object>
            {
                ["TotalEntities"] = _syncEntities.Count,
                ["HighPriority"] = _syncEntities.Values.Count(e => e.Priority == SyncPriority.High),
                ["MediumPriority"] = _syncEntities.Values.Count(e => e.Priority == SyncPriority.Medium),
                ["LowPriority"] = _syncEntities.Values.Count(e => e.Priority == SyncPriority.Low)
            };
            
            return stats;
        }
        
        /// <summary>
        /// 清理资源
        /// </summary>
        public void Dispose()
        {
            _syncEntities.Clear();
            Debug.Log("[EntitySync] 实体同步管理器已清理");
        }
    }
}