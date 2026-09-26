using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using MemoryPack;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TouchSocket.Sockets;

namespace Horizon.Game.Core.Handlers
{
    public class ItemHandler : MessageHandlerBase
    {
        public ItemHandler(ILogger<MessageHandlerBase> logger, IClusterClient clusterClient, HorizonMessageAdapter adapter) : base(logger, clusterClient, adapter)
        {

        }


        public override List<MessageType> MessageTypes { get; } = new List<MessageType> {
            MessageType.InventoryUpdate,
            MessageType.EquipItem,
            MessageType.RoleEquipmentSync,
            MessageType.RoleInventorySync,
            MessageType.InventoryGrant,
            MessageType.WeaponSwitch,
            MessageType.UseItem,
            MessageType.GemSocket,
            MessageType.EquipmentInfo,
            MessageType.EquipmentEnhance,
            MessageType.EquipmentRefine,
            MessageType.Crafting,
            MessageType.CraftingResult,
            MessageType.AttributeInheritance,
            MessageType.WuXingCrafting
        };

        public override ServiceType ServiceType => ServiceType.Game;



        public override async Task<(bool IsSuccess, MessageUnion? Response)> HandleAsync(ITcpSessionClient client, HorizonMessagePacket message)
        {
            return await base.HandleAsync(client, message);
        }

        public override async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message)
        {

            switch (message.Header.MessageType)
            {
                default:
                case MessageType.InventoryUpdate:
                    return await HandleInventoryUpdateAsync(message);
                case MessageType.EquipItem:
                    return await HandleEquipItemAsync(message);
                case MessageType.RoleEquipmentSync:
                    return await HandleRoleEquipmentSyncAsync(message);
                case MessageType.RoleInventorySync:
                    return await HandleRoleInventorySyncAsync(message);
                case MessageType.InventoryGrant:
                    return await HandleInventoryGrantAsync(message);
                case MessageType.WeaponSwitch:
                    return await HandleWeaponSwitchAsync(message);
                case MessageType.UseItem:
                    return await HandleUseItemAsync(message);
                case MessageType.GemSocket:
                    return await HandleGemSocketAsync(message);
                case MessageType.EquipmentInfo:
                    return await HandleEquipmentInfoAsync(message);
                case MessageType.EquipmentEnhance:
                    return await HandleEquipmentEnhanceAsync(message);
                case MessageType.EquipmentRefine:
                    return await HandleEquipmentRefineAsync(message);
                case MessageType.Crafting:
                    return await HandleCraftingAsync(message);
                case MessageType.CraftingResult:
                    return await HandleCraftingResultAsync(message);
                case MessageType.AttributeInheritance:
                    return await HandleAttributeInheritanceAsync(message);
                case MessageType.WuXingCrafting:
                    return await HandleWuXingCraftingAsync(message);
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleInventoryUpdateAsync(HorizonMessagePacket message)
        {
            try
            {
                InventoryUpdateMessage inventoryUpdateMessage = message.Body as InventoryUpdateMessage;
                // 处理背包更新逻辑
                var response = new InventoryUpdateMessage
                {
                    CharacterId = inventoryUpdateMessage.CharacterId,
                    ItemChanges = inventoryUpdateMessage.ItemChanges,
                    UpdateTime = DateTime.UtcNow.Ticks
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理背包更新消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理背包更新消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleEquipItemAsync(HorizonMessagePacket message)
        {
            try
            {
                EquipItemMessage equipItemMessage = message.Body as EquipItemMessage;
                // 落到权威背包；失败时如实回传，不再无条件报"装备成功"。
                var (ok, text) = await ApplyEquipCommandAsync(
                    equipItemMessage.CharacterId, equipItemMessage.ItemId, equipItemMessage.TemplateId,
                    equipItemMessage.Slot, equipItemMessage.Unequip);

                var response = new EquipItemMessage
                {
                    CharacterId = equipItemMessage.CharacterId,
                    ItemId = equipItemMessage.ItemId,
                    Slot = equipItemMessage.Slot,
                    Unequip = equipItemMessage.Unequip,
                    TemplateId = equipItemMessage.TemplateId,
                    Success = ok,
                    Message = text
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理装备物品消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理装备物品消息失败" }));
            }
        }

        /// <summary>
        /// 回包角色当前已装备清单（服务端权威）。
        /// <para>
        /// 客户端 3D 换装据此渲染，取代此前"只读本地 SQLite"的做法。
        /// 条目必须携带 <c>TemplateId</c>：服务端 ItemId 是背包自增实例 ID，
        /// 与客户端装备定义表（10001..30001）不是一个编号空间，只回 ItemId 会全部解析失败。
        /// </para>
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleRoleEquipmentSyncAsync(HorizonMessagePacket message)
        {
            var request = message.Body as RoleEquipmentSyncMessage;
            var response = new RoleEquipmentSyncMessage
            {
                CharacterId = request?.CharacterId ?? 0UL
            };

            try
            {
                if (request is null || request.CharacterId == 0UL)
                {
                    response.Success = false;
                    response.Message = "角色ID无效";
                }
                else
                {
                    var inventory = _clusterClient.GetGrain<Horizon.Orleans.Interface.IInventoryGrain>(
                        Horizon.Game.Core.Sim.Server.GrainKeyMapper.ToInventoryKey(request.CharacterId));
                    var slots = await inventory.GetEquippedSlotsAsync();

                    foreach (var kv in slots)
                    {
                        response.Entries.Add(new EquippedSlotEntry
                        {
                            Slot = kv.Key,
                            ItemId = kv.Value.ItemId,
                            TemplateId = kv.Value.TemplateId,
                            Count = kv.Value.Count,
                            Quality = kv.Value.Quality,
                            // 宝石随装备条目下发：客户端把已镶宝石的五行加成计入角色属性。
                            Gems = kv.Value.Gems ?? new List<int>(),
                        });
                    }
                    response.Success = true;
                }
            }
            catch (Exception ex)
            {
                // 如实回失败：客户端据此继续用本地缓存渲染，而不是收到一份"空装备"的成功包。
                Logger.LogError(ex, "处理已装备清单同步失败。CharacterId={CharacterId}", request?.CharacterId ?? 0);
                response.Success = false;
                response.Message = "获取装备清单失败";
            }

            return (true, CreateHorizonMessage(response));
        }

        /// <summary>
        /// 回包角色背包清单（服务端权威）。
        /// 失败时 <c>Success=false</c> 且不带物品——客户端据此保持本地渲染并**放弃迁移**，
        /// 避免把"查询失败"误判成"背包为空"而重复播种一份物品。
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleRoleInventorySyncAsync(HorizonMessagePacket message)
        {
            var request = message.Body as RoleInventorySyncMessage;
            var response = new RoleInventorySyncMessage { CharacterId = request?.CharacterId ?? 0UL };

            try
            {
                if (request is null || request.CharacterId == 0UL)
                {
                    response.Message = "角色ID无效";
                }
                else
                {
                    var inventory = _clusterClient.GetGrain<Horizon.Orleans.Interface.IInventoryGrain>(
                        Horizon.Game.Core.Sim.Server.GrainKeyMapper.ToInventoryKey(request.CharacterId));
                    var info = await inventory.GetInventoryAsync();

                    response.Items = info.Items;
                    response.Capacity = info.Capacity;
                    response.Success = true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理背包清单同步失败。CharacterId={CharacterId}", request?.CharacterId ?? 0);
                response.Message = "获取背包失败";
            }

            return (true, CreateHorizonMessage(response));
        }

        /// <summary>
        /// 按模板批量发放物品（本地背包迁移入口）。
        /// <para>
        /// 逐条 <c>AddItemAsync(templateId, count, quality:0)</c>：grain 内部按 TemplateId+Quality
        /// 堆叠，因此同一模板重复调用会累加而不是新建实例——这也是客户端必须**先聚合再发送**、
        /// 并且只在服务端确认"背包为空"时发一次的原因。
        /// </para>
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleInventoryGrantAsync(HorizonMessagePacket message)
        {
            var request = message.Body as InventoryGrantMessage;
            var response = new InventoryGrantMessage { CharacterId = request?.CharacterId ?? 0UL };

            try
            {
                if (request is null || request.CharacterId == 0UL || request.Entries.Count == 0)
                {
                    response.Message = "角色ID或发放清单无效";
                }
                else
                {
                    var inventory = _clusterClient.GetGrain<Horizon.Orleans.Interface.IInventoryGrain>(
                        Horizon.Game.Core.Sim.Server.GrainKeyMapper.ToInventoryKey(request.CharacterId));

                    var granted = 0;
                    foreach (var entry in request.Entries)
                    {
                        if (entry.TemplateId <= 0 || entry.Count <= 0) continue;
                        if (await inventory.AddItemAsync(entry.TemplateId, entry.Count, 0)) granted++;
                    }

                    response.GrantedCount = granted;
                    response.Success = granted > 0;
                    response.Message = granted > 0 ? "发放成功" : "全部条目被拒绝（可能背包已满）";
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理背包物品发放失败。CharacterId={CharacterId}", request?.CharacterId ?? 0);
                response.Message = "发放失败";
            }

            return (true, CreateHorizonMessage(response));
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleWeaponSwitchAsync(HorizonMessagePacket message)
        {
            try
            {
                WeaponSwitchMessage weaponSwitchMessage = message.Body as WeaponSwitchMessage;
                // 处理武器切换逻辑
                var response = new WeaponSwitchMessage
                {
                    CharacterId = weaponSwitchMessage.CharacterId,
                    CurrentWeaponSlot = weaponSwitchMessage.CurrentWeaponSlot,
                    TargetWeaponSlot = weaponSwitchMessage.TargetWeaponSlot,
                    SwitchTime = DateTime.UtcNow.Ticks
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理武器切换消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理武器切换消息失败" }));
            }
        }

        /// <summary>
        /// 使用物品：落到权威背包（<c>IInventoryGrain.UseItemAsync</c> 扣数量并持久化）。
        /// <para>
        /// 原实现是硬编码 Success=true 的空桩，客户端据此白嫖消耗品效果且服务端数量不变。
        /// 效果结算（回血/回蓝）当前仍在客户端本地——服务端没有运行时物品表；
        /// 这里的职责是保证**数量与存在性**权威：物品不存在/数量不足一律如实回失败。
        /// </para>
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleUseItemAsync(HorizonMessagePacket message)
        {
            var request = message.Body as UseItemRequest;
            var response = new UseItemResponse();

            try
            {
                var count = request?.Count > 0 ? request.Count : 1;
                if (request is null || request.CharacterId == 0UL || request.ItemId <= 0)
                {
                    response.Message = "请求参数无效";
                    return (true, CreateHorizonMessage(response));
                }

                var inventory = _clusterClient.GetGrain<Horizon.Orleans.Interface.IInventoryGrain>(
                    Horizon.Game.Core.Sim.Server.GrainKeyMapper.ToInventoryKey(request.CharacterId));
                var info = await inventory.GetInventoryAsync();

                var target = null as ItemInfo;
                foreach (var item in info.Items)
                {
                    if (item.ItemId == request.ItemId) { target = item; break; }
                }

                if (target is null)
                {
                    response.Message = "物品不存在";
                }
                else if (target.Count < count)
                {
                    response.Message = "物品数量不足";
                    response.RemainingCount = target.Count;
                }
                else
                {
                    var ok = await inventory.UseItemAsync(request.ItemId, count);
                    response.Success = ok;
                    response.RemainingCount = ok ? Math.Max(0, target.Count - count) : target.Count;
                    response.Message = ok ? "物品使用成功" : "使用失败";
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理使用物品消息失败。CharacterId={CharacterId}", request?.CharacterId ?? 0);
                response.Message = "使用物品失败";
            }

            return (true, CreateHorizonMessage(response));
        }

        /// <summary>
        /// 宝石镶嵌/卸下：落到权威背包（<c>IInventoryGrain.SocketGemAsync</c>）。
        /// 如实回成败与操作后的宝石数组；失败时数组为操作前原状，客户端据此回滚显示。
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleGemSocketAsync(HorizonMessagePacket message)
        {
            var request = message.Body as GemSocketRequest;
            var response = new GemSocketResponse
            {
                EquipmentItemId = request?.EquipmentItemId ?? 0,
                GemSlot = request?.GemSlot ?? -1
            };

            try
            {
                if (request is null || request.CharacterId == 0UL || request.EquipmentItemId <= 0)
                {
                    response.Message = "请求参数无效";
                    return (true, CreateHorizonMessage(response));
                }

                var inventory = _clusterClient.GetGrain<Horizon.Orleans.Interface.IInventoryGrain>(
                    Horizon.Game.Core.Sim.Server.GrainKeyMapper.ToInventoryKey(request.CharacterId));
                var (ok, text, gems) = await inventory.SocketGemAsync(request.EquipmentItemId, request.GemSlot, request.GemTemplateId);

                response.Success = ok;
                response.Message = text;
                response.Gems = gems;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理宝石镶嵌消息失败。CharacterId={CharacterId}", request?.CharacterId ?? 0);
                response.Message = "宝石操作失败";
            }

            return (true, CreateHorizonMessage(response));
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleEquipmentInfoAsync(HorizonMessagePacket message)
        {
            try
            {
                EquipmentInfoMessage equipmentInfoMessage = message.Body as EquipmentInfoMessage;
                // 处理装备信息逻辑
                var response = new EquipmentInfoMessage
                {
                    EquipmentId = equipmentInfoMessage.EquipmentId,
                    TemplateId = equipmentInfoMessage.TemplateId,
                    Name = equipmentInfoMessage.Name,
                    EnhanceLevel = equipmentInfoMessage.EnhanceLevel,
                    RefineLevel = equipmentInfoMessage.RefineLevel,
                    BaseAttributes = equipmentInfoMessage.BaseAttributes,
                    EnhanceAttributes = equipmentInfoMessage.EnhanceAttributes,
                    RefineAttributes = equipmentInfoMessage.RefineAttributes
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理装备信息消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理装备信息消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleEquipmentEnhanceAsync(HorizonMessagePacket message)
        {
            try
            {
                EquipmentEnhanceRequest equipmentEnhanceRequest = message.Body as EquipmentEnhanceRequest;
                // 处理装备强化逻辑
                var response = new EquipmentEnhanceResponse
                {
                    Success = true,
                    Message = "装备强化成功",
                    NewEnhanceLevel = 1,
                    ConsumedMaterials = equipmentEnhanceRequest.MaterialIds,
                    ConsumedGold = 1000
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理装备强化消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理装备强化消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleEquipmentRefineAsync(HorizonMessagePacket message)
        {
            try
            {
                EquipmentRefineRequest equipmentRefineRequest = message.Body as EquipmentRefineRequest;
                // 处理装备精炼逻辑
                var response = new EquipmentRefineResponse
                {
                    Success = true,
                    Message = "装备精炼成功",
                    NewRefineLevel = 1,
                    ConsumedMaterials = equipmentRefineRequest.MaterialIds,
                    ConsumedRefineStone = equipmentRefineRequest.RefineStoneId,
                    ConsumedGold = 1000
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理装备精炼消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理装备精炼消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleCraftingAsync(HorizonMessagePacket message)
        {
            try
            {
                CraftingRequest craftingRequest = message.Body as CraftingRequest;
                // 处理合成逻辑
                var response = new CraftingResponse
                {
                    Success = true,
                    Message = "合成成功",
                    CraftedItems = new List<ItemInfo>(),
                    ConsumedMaterials = craftingRequest.MaterialIds,
                    ConsumedGold = 1000
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理合成消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理合成消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleCraftingResultAsync(HorizonMessagePacket message)
        {
            try
            {
                CraftingResponse craftingResponse = message.Body as CraftingResponse;
                // 处理合成结果逻辑
                var response = new CraftingResponse
                {
                    Success = craftingResponse.Success,
                    Message = craftingResponse.Message,
                    CraftedItems = craftingResponse.CraftedItems,
                    ConsumedMaterials = craftingResponse.ConsumedMaterials,
                    ConsumedGold = craftingResponse.ConsumedGold
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理合成结果消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理合成结果消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleAttributeInheritanceAsync(HorizonMessagePacket message)
        {
            try
            {
                AttributeInheritanceRequest attributeInheritanceRequest = message.Body as AttributeInheritanceRequest;
                // 处理属性继承逻辑
                var response = new AttributeInheritanceResponse
                {
                    Success = true,
                    Message = "属性继承成功",
                    InheritedAttributes = new Dictionary<string, object>(),
                    ConsumedGold = 1000,
                    ConsumedMaterials = attributeInheritanceRequest.MaterialIds
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理属性继承消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理属性继承消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleWuXingCraftingAsync(HorizonMessagePacket message)
        {
            try
            {
                WuXingCraftingRequest wuXingCraftingRequest = message.Body as WuXingCraftingRequest;
                // 处理五行合成逻辑
                var response = new WuXingCraftingResponse
                {
                    Success = true,
                    Message = "五行合成成功",
                    CraftedItem = new ItemInfo(),
                    ConsumedMaterials = wuXingCraftingRequest.WuXingMaterials,
                    ConsumedGold = 1000
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理五行合成消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理五行合成消息失败" }));
            }
        }
    }
}