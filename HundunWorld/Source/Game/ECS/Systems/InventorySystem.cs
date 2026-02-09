using Arch.Core;
using Arch.Core.Utils;
using FlaxEngine;
using HundunWorld.Game.ECS.Components;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.ECS.Systems
{
    /// <summary>
    /// 背包系统，管理物品的存储、添加、移除和查询
    /// </summary>
    public class InventorySystem : BaseSystem
    {
        private QueryDescription _inventoryQuery;

        /// <summary>
        /// 物品添加事件
        /// </summary>
        public event Action<ulong, InventoryItem> OnItemAdded;

        /// <summary>
        /// 物品移除事件
        /// </summary>
        public event Action<ulong, int, int> OnItemRemoved;

        /// <summary>
        /// 背包已满事件
        /// </summary>
        public event Action<ulong> OnInventoryFull;

        public override void Initialize(World world)
        {
            base.Initialize(world);

            // 查询具有背包和玩家组件的实体
            _inventoryQuery = new QueryDescription().WithAll<InventoryComponent, PlayerComponent>();
        }

        public override void Update(World world, float deltaTime)
        {
            // 背包系统通常不需要每帧更新
            // 物品操作通过公共方法直接调用
        }

        /// <summary>
        /// 尝试向指定玩家的背包添加物品
        /// </summary>
        /// <param name="world">ECS世界</param>
        /// <param name="playerId">玩家ID</param>
        /// <param name="item">要添加的物品</param>
        /// <returns>是否成功添加</returns>
        public bool TryAddItem(World world, ulong playerId, InventoryItem item)
        {
            bool success = false;

            world.Query(in _inventoryQuery, (Entity entity, ref InventoryComponent inventory, ref PlayerComponent player) =>
            {
                if (player.PlayerId != playerId) return;

                if (inventory.IsFull)
                {
                    Debug.LogWarning($"背包已满，无法添加物品: {item.ItemName}");
                    OnInventoryFull?.Invoke(playerId);
                    return;
                }

                if (inventory.TryAddItem(item))
                {
                    Debug.Log($"添加物品成功: {item.ItemName} × {item.Count}");
                    OnItemAdded?.Invoke(playerId, item);
                    success = true;
                }
            });

            return success;
        }

        /// <summary>
        /// 尝试从指定玩家的背包移除物品
        /// </summary>
        /// <param name="world">ECS世界</param>
        /// <param name="playerId">玩家ID</param>
        /// <param name="templateId">物品模板ID</param>
        /// <param name="count">数量</param>
        /// <returns>是否成功移除</returns>
        public bool TryRemoveItem(World world, ulong playerId, int templateId, int count)
        {
            bool success = false;

            world.Query(in _inventoryQuery, (Entity entity, ref InventoryComponent inventory, ref PlayerComponent player) =>
            {
                if (player.PlayerId != playerId) return;

                if (inventory.TryRemoveItem(templateId, count))
                {
                    Debug.Log($"移除物品成功: 模板ID {templateId} × {count}");
                    OnItemRemoved?.Invoke(playerId, templateId, count);
                    success = true;
                }
                else
                {
                    Debug.LogWarning($"物品不足，无法移除: 模板ID {templateId}，需要 {count}");
                }
            });

            return success;
        }

        /// <summary>
        /// 检查指定玩家是否拥有足够数量的物品
        /// </summary>
        /// <param name="world">ECS世界</param>
        /// <param name="playerId">玩家ID</param>
        /// <param name="templateId">物品模板ID</param>
        /// <param name="count">需要的数量</param>
        /// <returns>是否有足够的物品</returns>
        public bool HasItem(World world, ulong playerId, int templateId, int count)
        {
            bool hasItem = false;

            world.Query(in _inventoryQuery, (Entity entity, ref InventoryComponent inventory, ref PlayerComponent player) =>
            {
                if (player.PlayerId != playerId) return;
                hasItem = inventory.HasItem(templateId, count);
            });

            return hasItem;
        }

        /// <summary>
        /// 获取指定玩家背包中某物品的数量
        /// </summary>
        /// <param name="world">ECS世界</param>
        /// <param name="playerId">玩家ID</param>
        /// <param name="templateId">物品模板ID</param>
        /// <returns>物品数量</returns>
        public int GetItemCount(World world, ulong playerId, int templateId)
        {
            int count = 0;

            world.Query(in _inventoryQuery, (Entity entity, ref InventoryComponent inventory, ref PlayerComponent player) =>
            {
                if (player.PlayerId != playerId) return;
                count = inventory.GetItemCount(templateId);
            });

            return count;
        }

        /// <summary>
        /// 获取指定玩家的背包容量信息
        /// </summary>
        /// <param name="world">ECS世界</param>
        /// <param name="playerId">玩家ID</param>
        /// <returns>（当前数量, 最大容量）</returns>
        public (int current, int capacity) GetInventoryCapacity(World world, ulong playerId)
        {
            int current = 0, capacity = 0;

            world.Query(in _inventoryQuery, (Entity entity, ref InventoryComponent inventory, ref PlayerComponent player) =>
            {
                if (player.PlayerId != playerId) return;
                current = inventory.CurrentCount;
                capacity = inventory.Capacity;
            });

            return (current, capacity);
        }

        /// <summary>
        /// 获取指定玩家背包中的所有物品
        /// </summary>
        /// <param name="world">ECS世界</param>
        /// <param name="playerId">玩家ID</param>
        /// <returns>物品列表</returns>
        public List<InventoryItem> GetAllItems(World world, ulong playerId)
        {
            var items = new List<InventoryItem>();

            world.Query(in _inventoryQuery, (Entity entity, ref InventoryComponent inventory, ref PlayerComponent player) =>
            {
                if (player.PlayerId != playerId) return;
                if (inventory.Items != null)
                {
                    items.AddRange(inventory.Items.Values);
                }
            });

            return items;
        }

        /// <summary>
        /// 检查所有指定材料是否足够
        /// </summary>
        /// <param name="world">ECS世界</param>
        /// <param name="playerId">玩家ID</param>
        /// <param name="materials">材料列表（模板ID -> 数量）</param>
        /// <returns>是否所有材料都足够</returns>
        public bool HasAllMaterials(World world, ulong playerId, Dictionary<int, int> materials)
        {
            bool hasAll = true;

            world.Query(in _inventoryQuery, (Entity entity, ref InventoryComponent inventory, ref PlayerComponent player) =>
            {
                if (player.PlayerId != playerId) return;
                foreach (var kvp in materials)
                {
                    if (!inventory.HasItem(kvp.Key, kvp.Value))
                    {
                        hasAll = false;
                        return;
                    }
                }
            });

            return hasAll;
        }

        /// <summary>
        /// 批量移除材料
        /// </summary>
        /// <param name="world">ECS世界</param>
        /// <param name="playerId">玩家ID</param>
        /// <param name="materials">材料列表（模板ID -> 数量）</param>
        /// <returns>是否全部成功移除</returns>
        public bool RemoveAllMaterials(World world, ulong playerId, Dictionary<int, int> materials)
        {
            bool success = true;

            world.Query(in _inventoryQuery, (Entity entity, ref InventoryComponent inventory, ref PlayerComponent player) =>
            {
                if (player.PlayerId != playerId) return;

                // 先检查是否都足够
                foreach (var kvp in materials)
                {
                    if (!inventory.HasItem(kvp.Key, kvp.Value))
                    {
                        success = false;
                        return;
                    }
                }

                // 执行移除
                foreach (var kvp in materials)
                {
                    inventory.TryRemoveItem(kvp.Key, kvp.Value);
                }
            });

            return success;
        }
    }
}
