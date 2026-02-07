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
            MessageType.WeaponSwitch,
            MessageType.UseItem,
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
                case MessageType.WeaponSwitch:
                    return await HandleWeaponSwitchAsync(message);
                case MessageType.UseItem:
                    return await HandleUseItemAsync(message);
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
                // 处理装备物品逻辑
                var response = new EquipItemMessage
                {
                    CharacterId = equipItemMessage.CharacterId,
                    ItemId = equipItemMessage.ItemId,
                    Slot = equipItemMessage.Slot,
                    Success = true,
                    Message = "装备成功"
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

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleUseItemAsync(HorizonMessagePacket message)
        {
            try
            {
                UseItemRequest useItemRequest = message.Body as UseItemRequest;
                // 处理使用物品逻辑
                var response = new UseItemResponse
                {
                    Success = true,
                    Message = "物品使用成功",
                    Effects = new List<ItemEffect>(),
                    RemainingCount = 0
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理使用物品消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理使用物品消息失败" }));
            }
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