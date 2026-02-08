using Arch.Core;
using System;
using System.Collections.Generic;
using HundunWorld.Game.ECS.Components;

namespace HundunWorld.Game.ECS
{
    /// <summary>
    /// 网络实体注册表
    /// 提供网络实体ID（ulong）与Arch ECS Entity之间的双向映射
    /// 解决网络消息处理器和技能系统无法通过网络ID查找ECS实体的问题
    /// </summary>
    public class NetworkEntityRegistry
    {
        private readonly Dictionary<ulong, Entity> _networkIdToEntity;
        private readonly Dictionary<int, ulong> _ecsIdToNetworkId;
        private readonly object _lock = new object();

        public NetworkEntityRegistry()
        {
            _networkIdToEntity = new Dictionary<ulong, Entity>();
            _ecsIdToNetworkId = new Dictionary<int, ulong>();
        }

        /// <summary>
        /// 已注册实体数量
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _networkIdToEntity.Count;
                }
            }
        }

        /// <summary>
        /// 注册实体映射
        /// </summary>
        /// <param name="networkId">网络实体ID</param>
        /// <param name="entity">ECS实体</param>
        public void Register(ulong networkId, Entity entity)
        {
            if (networkId == 0)
                throw new ArgumentException("Network ID cannot be zero.", nameof(networkId));

            lock (_lock)
            {
                // 移除旧映射（如果存在）
                if (_networkIdToEntity.TryGetValue(networkId, out var oldEntity))
                {
                    _ecsIdToNetworkId.Remove(oldEntity.Id);
                }
                if (_ecsIdToNetworkId.TryGetValue(entity.Id, out var oldNetworkId))
                {
                    _networkIdToEntity.Remove(oldNetworkId);
                }

                _networkIdToEntity[networkId] = entity;
                _ecsIdToNetworkId[entity.Id] = networkId;
            }
        }

        /// <summary>
        /// 通过网络ID查找ECS实体
        /// </summary>
        /// <param name="networkId">网络实体ID</param>
        /// <param name="entity">找到的ECS实体</param>
        /// <returns>是否找到</returns>
        public bool TryGetEntity(ulong networkId, out Entity entity)
        {
            lock (_lock)
            {
                return _networkIdToEntity.TryGetValue(networkId, out entity);
            }
        }

        /// <summary>
        /// 通过ECS实体ID查找网络ID
        /// </summary>
        /// <param name="ecsEntityId">ECS实体ID</param>
        /// <param name="networkId">找到的网络ID</param>
        /// <returns>是否找到</returns>
        public bool TryGetNetworkId(int ecsEntityId, out ulong networkId)
        {
            lock (_lock)
            {
                return _ecsIdToNetworkId.TryGetValue(ecsEntityId, out networkId);
            }
        }

        /// <summary>
        /// 注销实体映射（通过网络ID）
        /// </summary>
        /// <param name="networkId">网络实体ID</param>
        /// <returns>是否成功注销</returns>
        public bool Unregister(ulong networkId)
        {
            lock (_lock)
            {
                if (_networkIdToEntity.TryGetValue(networkId, out var entity))
                {
                    _networkIdToEntity.Remove(networkId);
                    _ecsIdToNetworkId.Remove(entity.Id);
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// 注销实体映射（通过ECS实体）
        /// </summary>
        /// <param name="entity">ECS实体</param>
        /// <returns>是否成功注销</returns>
        public bool UnregisterByEntity(Entity entity)
        {
            lock (_lock)
            {
                if (_ecsIdToNetworkId.TryGetValue(entity.Id, out var networkId))
                {
                    _ecsIdToNetworkId.Remove(entity.Id);
                    _networkIdToEntity.Remove(networkId);
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// 检查网络ID是否已注册
        /// </summary>
        public bool Contains(ulong networkId)
        {
            lock (_lock)
            {
                return _networkIdToEntity.ContainsKey(networkId);
            }
        }

        /// <summary>
        /// 清除所有注册信息
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _networkIdToEntity.Clear();
                _ecsIdToNetworkId.Clear();
            }
        }

        /// <summary>
        /// 获取所有已注册的网络ID
        /// </summary>
        public IReadOnlyCollection<ulong> GetAllNetworkIds()
        {
            lock (_lock)
            {
                return new List<ulong>(_networkIdToEntity.Keys);
            }
        }
    }
}
