using Arch.Core;
using FlaxEngine;
using Horizon.Game.Message.Network;
using HundunWorld.Game.ECS.Components;
using HundunWorld.Game.Network;
using HundunWorld.Game.Worlds;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HundunWorld.Game.Worlds
{
    /// <summary>
    /// 世界管理器，负责管理统一游戏世界的状态同步
    /// 整合了实体同步和世界状态管理功能
    /// </summary>
    public class WorldManager
    {
        private readonly NetworkManager _networkManager;
        private Dictionary<ulong, Entity> _entities;
        private World _world;
        private WorldStateManager _worldStateManager;
        private bool _isInitialized = false;
        
        public bool IsSynchronizing => _worldStateManager?.IsSynchronizing ?? false;
        public ulong CurrentWorldId => _worldStateManager?.CurrentWorldId ?? 0;
        public string CurrentWorldName => _worldStateManager?.CurrentWorldName ?? "";

        public WorldManager(NetworkManager networkManager, World world)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _entities = new Dictionary<ulong, Entity>();
        }
        
        /// <summary>
        /// 初始化世界管理器
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized) return;
            
            _worldStateManager = new WorldStateManager(_networkManager, _world);
            _isInitialized = true;
            
            Debug.Log("[WorldManager] 世界管理器初始化完成");
        }

        /// <summary>
        /// 启动世界同步
        /// </summary>
        public void StartSynchronization()
        {
            if (!_isInitialized)
            {
                Initialize();
            }
            
            _worldStateManager?.StartSynchronization();
            Debug.Log("[WorldManager] 世界同步已启动");
        }

        /// <summary>
        /// 停止世界同步
        /// </summary>
        public void StopSynchronization()
        {
            _worldStateManager?.StopSynchronization();
            Debug.Log("[WorldManager] 世界同步已停止");
        }
        
        /// <summary>
        /// 进入指定世界
        /// </summary>
        public async Task<bool> EnterWorldAsync(ulong worldId, string worldName)
        {
            if (!_isInitialized)
            {
                Initialize();
            }
            
            return await _worldStateManager.EnterWorldAsync(worldId, worldName);
        }
        
        /// <summary>
        /// 退出当前世界
        /// </summary>
        public async Task<bool> ExitWorldAsync()
        {
            return await _worldStateManager.ExitWorldAsync();
        }

        /// <summary>
        /// 添加世界实体
        /// </summary>
        /// <param name="entityId">实体ID</param>
        /// <param name="entity">实体引用</param>
        public void AddEntity(ulong entityId, Entity entity)
        {
            _entities[entityId] = entity;
            
            // 注册到同步系统
            if (_worldStateManager != null && _worldStateManager.IsSynchronizing)
            {
                // 根据实体类型确定同步类型
                var entityType = DetermineEntityType(entity);
                _worldStateManager.RegisterEntityForSync(entityId, entityType, entity);
            }
            
            Debug.Log($"[WorldManager] 实体已添加: ID={entityId}");
        }
        
        /// <summary>
        /// 根据实体组件确定实体类型
        /// </summary>
        private EntitySynchronizationManager.EntityType DetermineEntityType(Entity entity)
        {
            // 这里可以根据实体拥有的组件来判断类型
            if (_world.Has<PlayerComponent>(entity))
                return EntitySynchronizationManager.EntityType.Player;
            else if (_world.Has<NpcComponent>(entity))
                return EntitySynchronizationManager.EntityType.Npc;
            else if (_world.Has<MonsterComponent>(entity))
                return EntitySynchronizationManager.EntityType.Monster;
            else if (_world.Has<ItemComponent>(entity))
                return EntitySynchronizationManager.EntityType.Item;
            else if (_world.Has<ProjectileComponent>(entity))
                return EntitySynchronizationManager.EntityType.Projectile;
            else
                return EntitySynchronizationManager.EntityType.Environment;
        }

        /// <summary>
        /// 移除世界实体
        /// </summary>
        /// <param name="entityId">实体ID</param>
        public void RemoveEntity(ulong entityId)
        {
            if (_entities.ContainsKey(entityId))
            {
                _entities.Remove(entityId);
                _worldStateManager?.UnregisterEntityFromSync(entityId);
                Debug.Log($"[WorldManager] 实体已移除: ID={entityId}");
            }
        }

        /// <summary>
        /// 获取世界实体
        /// </summary>
        /// <param name="entityId">实体ID</param>
        /// <returns>实体引用</returns>
        public Entity GetEntity(ulong entityId)
        {
            _entities.TryGetValue(entityId, out Entity entity);
            return entity;
        }
        
        /// <summary>
        /// 获取所有实体
        /// </summary>
        public IEnumerable<Entity> GetAllEntities()
        {
            return _entities.Values;
        }

        /// <summary>
        /// 更新本地实体状态并同步到服务器
        /// </summary>
        /// <param name="entityId">实体ID</param>
        /// <param name="newPosition">新位置</param>
        public async Task UpdateEntityPositionAsync(ulong entityId, Vector3 newPosition)
        {
            if (_entities.TryGetValue(entityId, out Entity entity))
            {
                // 更新本地状态
                if (_world.Has<PositionComponent>(entity))
                {
                    var position = _world.Get<PositionComponent>(entity);
                    position.Position = newPosition;
                    _world.Set(entity, position);
                }
                else
                {
                    _world.Add(entity, new PositionComponent(newPosition));
                }
                
                Debug.Log($"[WorldManager] 实体位置已更新: ID={entityId}, Pos={newPosition}");
            }
        }
        
        /// <summary>
        /// 设置世界属性
        /// </summary>
        public void SetWorldProperty(string key, object value)
        {
            _worldStateManager?.SetWorldProperty(key, value);
        }
        
        /// <summary>
        /// 获取世界属性
        /// </summary>
        public T? GetWorldProperty<T>(string key, T? defaultValue=default )where T : class
        {
            return _worldStateManager?.GetWorldProperty(key, defaultValue)??defaultValue;
        }
        
        /// <summary>
        /// 更新世界管理器
        /// </summary>
        public void Update(float deltaTime)
        {
            _worldStateManager?.Update(deltaTime);
        }
        
        /// <summary>
        /// 处理网络消息
        /// </summary>
        public void HandleNetworkMessage(/* HorizonMessagePacket message */ object message)
        {
            _worldStateManager?.HandleWorldMessage(message);
        }

        /// <summary>
        /// 获取世界统计信息
        /// </summary>
        public Dictionary<string, object> GetWorldStatistics()
        {
            var stats = new Dictionary<string, object>
            {
                ["TotalEntities"] = _entities.Count,
                ["IsSynchronizing"] = IsSynchronizing,
                ["CurrentWorldId"] = CurrentWorldId,
                ["CurrentWorldName"] = CurrentWorldName
            };
            
            if (_worldStateManager != null)
            {
                var worldStats = _worldStateManager.GetWorldStatistics();
                foreach (var kvp in worldStats)
                {
                    stats[$"World_{kvp.Key}"] = kvp.Value;
                }
            }
            
            return stats;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            // 停止同步
            StopSynchronization();
            
            // 清理实体
            _entities.Clear();
            
            // 清理世界状态管理器
            _worldStateManager?.Dispose();
            
            Debug.Log("[WorldManager] 世界管理器已清理");
        }
    }
}
