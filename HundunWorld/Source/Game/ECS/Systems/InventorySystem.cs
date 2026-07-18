using Arch.Core;
using Arch.Core.Utils;
using FlaxEngine;
using HundunWorld.Game.ECS.Components;
using HundunWorld.Game.Equipment;
using System;
using System.Collections.Generic;
using System.Linq;

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

        /// <summary>
        /// 判断装备类型是否为背包。
        /// 使用 ToString() 字符串比较，避免依赖 <see cref="EquipmentType.Bag"/> 枚举值是否已定义
        /// （Task 2 可能并行添加 EquipmentType.Bag）。
        /// </summary>
        private static bool IsBagType(EquipmentType type)
        {
            return type.ToString() == "Bag";
        }

        /// <summary>
        /// 从 EquipmentData 中获取背包提供的扩展格子数。
        /// 优先从 BaseStats["ExtraSlots"] 读取，否则默认 12 格。
        /// </summary>
        private static int GetBagExtraSlots(EquipmentData data)
        {
            if (data == null) return 0;
            if (data.BaseStats != null && data.BaseStats.TryGetValue("ExtraSlots", out float val) && val > 0)
                return (int)val;
            return 12;
        }

        /// <summary>
        /// 装备扩展背包到指定背包槽
        /// </summary>
        /// <param name="world">ECS世界</param>
        /// <param name="playerId">玩家ID</param>
        /// <param name="bagTemplateId">背包装备模板ID</param>
        /// <param name="bagSlotIndex">背包槽索引（0-3）</param>
        /// <returns>是否成功装备</returns>
        public bool EquipBag(World world, ulong playerId, int bagTemplateId, int bagSlotIndex)
        {
            // 校验背包槽索引范围
            if (bagSlotIndex < 0 || bagSlotIndex >= InventoryComponent.MaxBagSlots)
            {
                Debug.LogWarning($"[InventorySystem] 背包槽索引无效: {bagSlotIndex}，有效范围 0-{InventoryComponent.MaxBagSlots - 1}");
                return false;
            }

            // 校验物品存在且为背包类型
            var equipmentData = EquipmentDatabase.GetEquipment(bagTemplateId);
            if (equipmentData == null)
            {
                Debug.LogWarning($"[InventorySystem] 找不到装备模板: {bagTemplateId}");
                return false;
            }
            if (!IsBagType(equipmentData.Type))
            {
                Debug.LogWarning($"[InventorySystem] 装备 {equipmentData.Name}({bagTemplateId}) 不是背包类型");
                return false;
            }

            int extraSlots = GetBagExtraSlots(equipmentData);

            bool success = false;
            world.Query(in _inventoryQuery, (Entity entity, ref InventoryComponent inventory, ref PlayerComponent player) =>
            {
                if (player.PlayerId != playerId) return;

                if (inventory.BagSlots == null)
                    inventory.BagSlots = new List<EquippedBag>();

                // 校验目标背包槽是否已被占用
                if (inventory.BagSlots.Any(b => b.BagSlotIndex == bagSlotIndex))
                {
                    Debug.LogWarning($"[InventorySystem] 背包槽 {bagSlotIndex} 已被占用");
                    return;
                }

                // 校验装备后总容量不超过最大值
                int newTotal = inventory.TotalCapacity + extraSlots;
                if (newTotal > InventoryComponent.MaxTotalCapacity)
                {
                    Debug.LogWarning($"[InventorySystem] 装备后总容量 {newTotal} 超过上限 {InventoryComponent.MaxTotalCapacity}");
                    return;
                }

                // 从背包 Items 中移除该背包装备（按模板ID移除1个）
                if (!inventory.TryRemoveItem(bagTemplateId, 1))
                {
                    Debug.LogWarning($"[InventorySystem] 背包中没有该背包装备: {bagTemplateId}");
                    return;
                }

                // 添加到 BagSlots
                inventory.BagSlots.Add(new EquippedBag(bagSlotIndex, bagTemplateId, extraSlots));
                Debug.Log($"[InventorySystem] 装备背包成功: 槽 {bagSlotIndex}, 模板 {bagTemplateId}, 扩展 {extraSlots} 格");
                success = true;
            });

            return success;
        }

        /// <summary>
        /// 卸下指定背包槽的扩展背包
        /// </summary>
        /// <param name="world">ECS世界</param>
        /// <param name="playerId">玩家ID</param>
        /// <param name="bagSlotIndex">背包槽索引（0-3）</param>
        /// <returns>是否成功卸下</returns>
        public bool UnequipBag(World world, ulong playerId, int bagSlotIndex)
        {
            if (bagSlotIndex < 0 || bagSlotIndex >= InventoryComponent.MaxBagSlots)
            {
                Debug.LogWarning($"[InventorySystem] 背包槽索引无效: {bagSlotIndex}");
                return false;
            }

            bool success = false;
            world.Query(in _inventoryQuery, (Entity entity, ref InventoryComponent inventory, ref PlayerComponent player) =>
            {
                if (player.PlayerId != playerId) return;

                if (inventory.BagSlots == null || inventory.BagSlots.Count == 0)
                {
                    Debug.LogWarning($"[InventorySystem] 未装备任何扩展背包");
                    return;
                }

                // 查找该槽位的背包
                int bagIndex = -1;
                for (int i = 0; i < inventory.BagSlots.Count; i++)
                {
                    if (inventory.BagSlots[i].BagSlotIndex == bagSlotIndex)
                    {
                        bagIndex = i;
                        break;
                    }
                }
                if (bagIndex < 0)
                {
                    Debug.LogWarning($"[InventorySystem] 背包槽 {bagSlotIndex} 未装备背包");
                    return;
                }

                var bag = inventory.BagSlots[bagIndex];

                // 计算该背包对应的扩展槽位范围（按 BagSlots 列表顺序累计分配）
                // 起始 = BaseCapacity + 之前所有 bag 的 ExtraSlots 之和
                // 结束 = 起始 + 当前 bag.ExtraSlots
                int startSlot = InventoryComponent.BaseCapacity;
                for (int i = 0; i < bagIndex; i++)
                {
                    startSlot += inventory.BagSlots[i].ExtraSlots;
                }
                int endSlot = startSlot + bag.ExtraSlots;

                // 检查该范围内是否有物品
                if (inventory.Items != null)
                {
                    for (int s = startSlot; s < endSlot; s++)
                    {
                        if (inventory.Items.ContainsKey(s))
                        {
                            Debug.LogWarning($"[InventorySystem] 背包槽 {bagSlotIndex} 的扩展格子中有物品，无法卸下");
                            return;
                        }
                    }
                }

                // 卸下该背包后总容量会减少 bag.ExtraSlots
                // 放回的背包装备本身占用一个基础格子（属于 BaseCapacity 范围）
                // 因此需要确保 CurrentCount + 1 不超过新的总容量
                int newTotalCapacity = inventory.TotalCapacity - bag.ExtraSlots;
                if (inventory.CurrentCount + 1 > newTotalCapacity)
                {
                    Debug.LogWarning($"[InventorySystem] 卸下背包后空间不足，无法放回背包装备");
                    return;
                }

                // 从 BagSlots 移除
                inventory.BagSlots.RemoveAt(bagIndex);

                // 将背包装备放回 Items
                var equipmentData = EquipmentDatabase.GetEquipment(bag.TemplateId);
                string bagName = equipmentData != null ? equipmentData.Name : $"Bag_{bag.TemplateId}";
                int itemType = equipmentData != null ? (int)equipmentData.Type : 0;
                int quality = equipmentData != null ? equipmentData.Quality : 0;
                var returnItem = new InventoryItem(
                    itemId: 0,
                    templateId: bag.TemplateId,
                    itemName: bagName,
                    itemType: itemType,
                    count: 1,
                    quality: quality,
                    isBound: false,
                    slotIndex: -1);
                inventory.TryAddItem(returnItem);

                Debug.Log($"[InventorySystem] 卸下背包成功: 槽 {bagSlotIndex}, 模板 {bag.TemplateId}");
                success = true;
            });

            return success;
        }

        /// <summary>
        /// 获取指定玩家背包的总容量（基础容量 + 已装备背包扩展格子）
        /// </summary>
        /// <param name="world">ECS世界</param>
        /// <param name="playerId">玩家ID</param>
        /// <returns>总容量；若未找到玩家返回 0</returns>
        public int GetTotalCapacity(World world, ulong playerId)
        {
            int totalCapacity = 0;
            world.Query(in _inventoryQuery, (Entity entity, ref InventoryComponent inventory, ref PlayerComponent player) =>
            {
                if (player.PlayerId != playerId) return;
                totalCapacity = inventory.TotalCapacity;
            });
            return totalCapacity;
        }
    }
}
