using FlaxEngine;
using Game.Character.Attributes;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Inventory;
using HundunWorld.Game.Network.Handlers;
using HundunWorld.Game.Network.MessageHandlers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ClientSlot = HundunWorld.Game.Equipment.EquipmentSlot;

namespace HundunWorld.Game.Services
{
    /// <summary>物品操作类型（请求→回包串联用）。</summary>
    public enum ItemOperationKind { Use, Equip, Unequip, GemSocket, GemRemove }

    /// <summary>一次物品操作的服务端回执。</summary>
    public readonly struct ItemOperationResult
    {
        public ItemOperationKind Operation { get; }
        public bool Success { get; }
        public string Message { get; }

        public ItemOperationResult(ItemOperationKind operation, bool success, string message)
        {
            Operation = operation;
            Success = success;
            Message = message ?? "";
        }
    }

    /// <summary>
    /// 本地玩家背包/装备的客户端权威缓存（数据源=服务端 grain，UI 只读这里）。
    /// <para>
    /// 链路：<c>RoleInventorySyncHandler</c>/<c>RoleEquipmentSyncHandler</c> 的回包喂入快照；
    /// 穿戴/卸下/使用经网关落 <c>IInventoryGrain</c> 后，回执由 <c>ItemOperationResponseHandler</c>
    /// 送进 <see cref="OnEquipResponse"/>/<see cref="OnUseItemResponse"/>——成功一律**重新拉全量快照**，
    /// 客户端不自行演算槽位交换，避免与服务端分叉。
    /// </para>
    /// </summary>
    public class PlayerInventoryService
    {
        private static PlayerInventoryService _instance;
        public static PlayerInventoryService Instance => _instance ??= new PlayerInventoryService();

        private readonly List<ItemInfo> _items = new List<ItemInfo>();
        private readonly Dictionary<int, EquippedSlotEntry> _equippedSlots = new Dictionary<int, EquippedSlotEntry>();

        /// <summary>快照（背包/装备槽/容量）发生变化，UI 应整体重取。</summary>
        public event Action Changed;

        /// <summary>一次穿戴/卸下/使用操作的服务端回执（成功时 Changed 随后也会触发）。</summary>
        public event Action<ItemOperationResult> OperationFinished;

        /// <summary>
        /// 基础背包格数：108 格（参考魔兽世界"基础背包 + 扩展背包"的口径）。
        /// 服务端快照里的容量会覆盖它（扩展背包/扩容道具由此生效），此处只是未收到快照前的默认值。
        /// </summary>
        public const int BaseCapacity = 108;

        public int Capacity { get; private set; } = BaseCapacity;

        /// <summary>是否已收到过服务端权威背包（false 时 UI 应保持占位而不是显示"空背包"）。</summary>
        public bool HasServerData { get; private set; }

        public IReadOnlyList<ItemInfo> Items => _items;

        // 使用请求的关联上下文：UseItemResponse 不带 ItemId，客户端串行发起 UI 操作，
        // 记住最后一次待回执的使用者即可定位效果。
        private long _pendingUseServerItemId;
        private int _pendingUseTemplateId;

        private PlayerInventoryService() { }

        private static ulong CurrentCharacterId => HundunWorldGame.Instance != null ? HundunWorldGame.Instance.PlayerId : 0UL;

        // ===================================================================
        // 快照写入（由同步处理器的回包调用，须在主线程）
        // ===================================================================

        public void ApplyInventorySnapshot(List<ItemInfo> items, int capacity)
        {
            _items.Clear();
            if (items != null)
            {
                foreach (var item in items)
                    if (item != null) _items.Add(item);
            }
            if (capacity > 0) Capacity = capacity;
            HasServerData = true;
            Changed?.Invoke();
        }

        public void ApplyEquipmentSnapshot(List<EquippedSlotEntry> entries)
        {
            _equippedSlots.Clear();
            if (entries != null)
            {
                foreach (var entry in entries)
                    if (entry != null) _equippedSlots[entry.Slot] = entry;
            }
            // 穿戴快照即权威装备态：先重算属性加成，再发 Changed，
            // 保证 UI 刷新时读到的组件数值已经包含本次穿戴/卸下。
            HundunWorld.Game.Character.EquipmentAttributeBridge.RefreshFromEquipped();
            Changed?.Invoke();
        }

        // ===================================================================
        // 查询
        // ===================================================================

        public bool TryGetItemByServerId(long serverItemId, out ItemInfo item)
        {
            foreach (var it in _items)
            {
                if (it.ItemId == serverItemId)
                {
                    item = it;
                    return true;
                }
            }
            item = null;
            return false;
        }

        public bool TryGetEquipped(ClientSlot slot, out EquippedSlotEntry entry)
            => _equippedSlots.TryGetValue((int)slot, out entry);

        // ===================================================================
        // 请求发送（UI 按钮入口）
        // ===================================================================

        /// <summary>向服务端重新拉取权威背包 + 装备清单 + 权威战斗画像。</summary>
        public void RequestFullRefresh()
        {
            var characterId = CurrentCharacterId;
            if (characterId == 0UL) return;
            _ = RoleInventorySyncHandler.RequestAsync(characterId);
            _ = RoleEquipmentSyncHandler.RequestAsync(characterId);
            // 穿戴会改五行三行与通用攻防基值，画像必须跟着重取，否则面板停在换装前的权威值上。
            _ = HundunWorld.Game.Network.MessageHandlers.CharacterAttributeRefreshHandler
                .RequestAsync(characterId, HundunWorld.Game.Network.MessageHandlers.CharacterAttributeRefreshHandler.ReasonEquipment);
        }

        /// <summary>使用物品（服务端扣数）。返回请求是否发出。</summary>
        public async Task<bool> RequestUseItemAsync(ItemInfo item, int count = 1)
        {
            if (item == null) return false;
            var characterId = CurrentCharacterId;
            if (characterId == 0UL || !CanSend("使用物品"))
            {
                RaiseFailed(ItemOperationKind.Use, "网络未连接");
                return false;
            }

            _pendingUseServerItemId = item.ItemId;
            _pendingUseTemplateId = item.TemplateId;

            var sent = await HundunWorldGame.Instance.NetworkManager.SendAsync(new UseItemRequest
            {
                CharacterId = characterId,
                ItemId = item.ItemId,
                Count = Math.Max(1, count),
            });
            if (!sent)
            {
                _pendingUseServerItemId = 0;
                _pendingUseTemplateId = 0;
                RaiseFailed(ItemOperationKind.Use, "发送失败，请检查网络");
            }
            return sent;
        }

        /// <summary>穿戴背包中的装备（按模板 ID 由服务端解析实例）。</summary>
        public async Task<bool> RequestEquipAsync(ItemInfo item)
        {
            if (item == null) return false;
            var definition = ItemCatalog.Resolve(item.TemplateId);
            if (!definition.IsEquippable)
            {
                RaiseFailed(ItemOperationKind.Equip, $"{definition.Name} 不是可穿戴的装备");
                return false;
            }
            var characterId = CurrentCharacterId;
            if (characterId == 0UL || !CanSend("穿戴装备"))
            {
                RaiseFailed(ItemOperationKind.Equip, "网络未连接");
                return false;
            }
            var equipSent = await RoleEquipmentSyncHandler.SendEquipAsync(characterId, item.TemplateId, definition.EquipSlot.Value);
            if (!equipSent) RaiseFailed(ItemOperationKind.Equip, "发送失败，请检查网络");
            return equipSent;
        }

        /// <summary>
        /// 按模板 ID 穿戴（视图层只有定义表编号、拿不到服务端 ItemInfo 时的入口）。
        /// 返回请求是否发出；结果以服务端回推快照为准。
        /// </summary>
        public async Task<bool> RequestEquipByTemplateAsync(int templateId)
        {
            foreach (var item in _items)
            {
                if (item.TemplateId == templateId)
                    return await RequestEquipAsync(item);
            }
            RaiseFailed(ItemOperationKind.Equip, "物品不在背包中（快照已更新，请重试）");
            return false;
        }

        /// <summary>卸下指定槽位装备。</summary>
        public async Task<bool> RequestUnequipAsync(ClientSlot slot)
        {
            var characterId = CurrentCharacterId;
            if (characterId == 0UL || !CanSend("卸下装备"))
            {
                RaiseFailed(ItemOperationKind.Unequip, "网络未连接");
                return false;
            }
            var unequipSent = await RoleEquipmentSyncHandler.SendUnequipAsync(characterId, slot);
            if (!unequipSent) RaiseFailed(ItemOperationKind.Unequip, "发送失败，请检查网络");
            return unequipSent;
        }

        /// <summary>
        /// 镶嵌/卸下宝石。<paramref name="gemTemplateId"/> 传 0 = 卸下该槽宝石放回背包。
        /// 服务端权威落库（扣宝石/换下的旧宝石回背包），回执见 <see cref="OnGemSocketResponse"/>。
        /// </summary>
        public async Task<bool> RequestSocketGemAsync(long equipmentItemId, int gemSlot, int gemTemplateId)
        {
            var characterId = CurrentCharacterId;
            var operation = gemTemplateId == 0 ? ItemOperationKind.GemRemove : ItemOperationKind.GemSocket;
            if (characterId == 0UL || equipmentItemId <= 0 || !CanSend("宝石操作"))
            {
                RaiseFailed(operation, "网络未连接");
                return false;
            }

            var sent = await HundunWorldGame.Instance.NetworkManager.SendAsync(new GemSocketRequest
            {
                CharacterId = characterId,
                EquipmentItemId = equipmentItemId,
                GemSlot = gemSlot,
                GemTemplateId = gemTemplateId,
            });
            if (!sent) RaiseFailed(operation, "发送失败，请检查网络");
            return sent;
        }

        // ===================================================================
        // 回包处理（由 ItemOperationResponseHandler 在主线程调用）
        // ===================================================================

        public void OnEquipResponse(EquipItemMessage response)
        {
            var operation = response.Unequip ? ItemOperationKind.Unequip : ItemOperationKind.Equip;
            OperationFinished?.Invoke(new ItemOperationResult(operation, response.Success, response.Message));
            if (response.Success)
                RequestFullRefresh();
        }

        /// <summary>
        /// 宝石镶嵌/卸下回执：成功时把权威 Gems 立即写进本地缓存条目（详情弹窗无闪烁更新），
        /// 并因堆叠数变化（宝石 −1 / 换下的旧宝石 +1）重拉全量快照。
        /// </summary>
        public void OnGemSocketResponse(GemSocketResponse response)
        {
            if (response.Success)
            {
                if (TryGetItemByServerId(response.EquipmentItemId, out var item))
                    item.Gems = response.Gems ?? new List<int>();
                Changed?.Invoke();
                RequestFullRefresh();
            }
            OperationFinished?.Invoke(new ItemOperationResult(
                ItemOperationKind.GemSocket, response.Success,
                string.IsNullOrEmpty(response.Message) ? (response.Success ? "宝石操作成功" : "宝石操作失败") : response.Message));
        }

        public void OnUseItemResponse(UseItemResponse response)
        {
            string effectText = null;
            if (response.Success)
            {
                effectText = ApplyPendingUseEffect();
                RequestFullRefresh();
            }
            else
            {
                _pendingUseServerItemId = 0;
                _pendingUseTemplateId = 0;
            }
            string message = string.IsNullOrEmpty(response.Message) ? (response.Success ? "使用成功" : "使用失败") : response.Message;
            if (response.Success && !string.IsNullOrEmpty(effectText))
                message += "，" + effectText;
            OperationFinished?.Invoke(new ItemOperationResult(ItemOperationKind.Use, response.Success, message));
        }

        /// <summary>
        /// 消耗品效果在客户端本地结算（服务端物品表尚未建立，权威部分只有"扣数量"）。
        /// 返回效果描述供 UI 回执展示——满血满蓝时数值无可见变化，靠这行字自证生效。
        /// </summary>
        private string ApplyPendingUseEffect()
        {
            var actor = HundunWorldGame.Instance != null ? HundunWorldGame.Instance.LocalPlayerActor : null;
            var attributes = actor != null ? actor.GetScript<CharacterAttributesComponent>() : null;
            _pendingUseServerItemId = 0;
            if (_pendingUseTemplateId == 0 || attributes == null)
            {
                _pendingUseTemplateId = 0;
                return null;
            }

            var definition = ItemCatalog.Resolve(_pendingUseTemplateId);
            _pendingUseTemplateId = 0;

            var parts = new List<string>();
            if (definition.HpRestore > 0f)
            {
                attributes.Heal(definition.HpRestore);
                parts.Add($"生命 +{definition.HpRestore:0}");
            }
            if (definition.EnergyRestore > 0f)
            {
                attributes.CurrentEnergy = Math.Min(attributes.MaxEnergy, attributes.CurrentEnergy + definition.EnergyRestore);
                parts.Add($"内力 +{definition.EnergyRestore:0}");
            }
            return parts.Count > 0 ? string.Join("，", parts) : null;
        }

        private bool CanSend(string what)
        {
            var networkManager = HundunWorldGame.Instance?.NetworkManager;
            if (networkManager == null || !networkManager.CanSendMessage())
            {
                Debug.LogWarning($"[PlayerInventory] 未连接，{what}请求未发出。");
                return false;
            }
            return true;
        }

        private void RaiseFailed(ItemOperationKind operation, string message)
        {
            OperationFinished?.Invoke(new ItemOperationResult(operation, false, message));
        }
    }
}
