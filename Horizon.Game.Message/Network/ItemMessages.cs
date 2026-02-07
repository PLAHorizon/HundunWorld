using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;
using System;
using System.Collections.Generic;

namespace Horizon.Game.Message.Network
{
    #region 背包与装备消息

    /// <summary>
    /// 背包更新消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class InventoryUpdateMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 物品变化列表
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public List<ItemChangeInfo> ItemChanges { get; set; } = new();

        /// <summary>
        /// 更新时间
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long UpdateTime { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.InventoryUpdate;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 物品变化信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ItemChangeInfo : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 物品ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public long ItemId { get; set; }

        /// <summary>
        /// 物品模板ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int TemplateId { get; set; }

        /// <summary>
        /// 变化数量
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int ChangeCount { get; set; }

        /// <summary>
        /// 变化类型（0=增加，1=减少，2=更新）
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int ChangeType { get; set; }

        /// <summary>
        /// 物品详细信息
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public ItemInfo ItemDetails { get; set; } = new();

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.InventoryUpdate;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 物品信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ItemInfo : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 物品ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public long ItemId { get; set; }

        /// <summary>
        /// 物品模板ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int TemplateId { get; set; }

        /// <summary>
        /// 物品名称
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Name { get; set; } = "";

        /// <summary>
        /// 物品描述
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Description { get; set; } = "";

        /// <summary>
        /// 物品类型
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int ItemType { get; set; }

        /// <summary>
        /// 物品数量
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int Count { get; set; }

        /// <summary>
        /// 物品等级
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public int Level { get; set; }

        /// <summary>
        /// 物品品质
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public int Quality { get; set; }

        /// <summary>
        /// 绑定状态
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public bool IsBound { get; set; }

        /// <summary>
        /// 过期时间
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public long ExpireTime { get; set; }

        /// <summary>
        /// 属性列表
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public Dictionary<string, object> Attributes { get; set; } = new();

        [MemoryPackOrder(11)]
        [Id(11)]
        public MessageType Type { get; set; } = MessageType.InventoryUpdate;
        [MemoryPackOrder(12)]
        [Id(12)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 装备物品消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class EquipItemMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 物品ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long ItemId { get; set; }

        /// <summary>
        /// 装备槽位
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Slot { get; set; }

        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.EquipItem;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 武器切换消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class WeaponSwitchMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 当前武器槽位
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int CurrentWeaponSlot { get; set; }

        /// <summary>
        /// 目标武器槽位
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int TargetWeaponSlot { get; set; }

        /// <summary>
        /// 切换时间
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long SwitchTime { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.WeaponSwitch;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 物品使用消息

    /// <summary>
    /// 使用物品请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class UseItemRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 物品ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long ItemId { get; set; }

        /// <summary>
        /// 使用数量
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Count { get; set; }

        /// <summary>
        /// 目标ID（如果是对目标使用）
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong TargetId { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.UseItem;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 使用物品响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class UseItemResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 效果列表
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public List<ItemEffect> Effects { get; set; } = new();

        /// <summary>
        /// 剩余物品数量
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int RemainingCount { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.UseItem;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 物品效果
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ItemEffect : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 效果类型
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string EffectType { get; set; } = "";

        /// <summary>
        /// 效果值
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string EffectValue { get; set; } = "";

        /// <summary>
        /// 持续时间（毫秒）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long Duration { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.UseItem;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 装备强化与精炼消息

    /// <summary>
    /// 装备信息消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class EquipmentInfoMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 装备ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public long EquipmentId { get; set; }

        /// <summary>
        /// 装备模板ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int TemplateId { get; set; }

        /// <summary>
        /// 装备名称
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Name { get; set; } = "";

        /// <summary>
        /// 强化等级
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int EnhanceLevel { get; set; }

        /// <summary>
        /// 精炼等级
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int RefineLevel { get; set; }

        /// <summary>
        /// 基础属性
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public Dictionary<string, object> BaseAttributes { get; set; } = new();

        /// <summary>
        /// 强化属性加成
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public Dictionary<string, object> EnhanceAttributes { get; set; } = new();

        /// <summary>
        /// 精炼属性加成
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public Dictionary<string, object> RefineAttributes { get; set; } = new();

        [MemoryPackOrder(8)]
        [Id(8)]
        public MessageType Type { get; set; } = MessageType.EquipmentInfo;
        [MemoryPackOrder(9)]
        [Id(9)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 装备强化请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class EquipmentEnhanceRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 装备ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long EquipmentId { get; set; }

        /// <summary>
        /// 强化材料列表
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public List<long> MaterialIds { get; set; } = new();

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.EquipmentEnhance;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 装备强化响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class EquipmentEnhanceResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 新的强化等级
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int NewEnhanceLevel { get; set; }

        /// <summary>
        /// 消耗的材料
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public List<long> ConsumedMaterials { get; set; } = new();

        /// <summary>
        /// 消耗的金币
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long ConsumedGold { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.EquipmentEnhance;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 装备精炼请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class EquipmentRefineRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 装备ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long EquipmentId { get; set; }

        /// <summary>
        /// 精炼材料列表
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public List<long> MaterialIds { get; set; } = new();

        /// <summary>
        /// 精炼石ID
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long RefineStoneId { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.EquipmentRefine;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 装备精炼响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class EquipmentRefineResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 新的精炼等级
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int NewRefineLevel { get; set; }

        /// <summary>
        /// 消耗的材料
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public List<long> ConsumedMaterials { get; set; } = new();

        /// <summary>
        /// 消耗的精炼石
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long ConsumedRefineStone { get; set; }

        /// <summary>
        /// 消耗的金币
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long ConsumedGold { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public MessageType Type { get; set; } = MessageType.EquipmentRefine;
        [MemoryPackOrder(7)]
        [Id(7)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 背包信息消息

    /// <summary>
    /// 背包信息消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class InventoryInfo : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 背包物品列表
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public List<ItemInfo> Items { get; set; } = new();

        /// <summary>
        /// 背包容量
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Capacity { get; set; }

        /// <summary>
        /// 当前物品数量
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int CurrentCount { get; set; }

        /// <summary>
        /// 背包类型
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string InventoryType { get; set; } = "";

        /// <summary>
        /// 更新时间
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long UpdateTime { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public MessageType Type { get; set; } = MessageType.InventoryInfo;
        [MemoryPackOrder(7)]
        [Id(7)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 合成与继承消息

    /// <summary>
    /// 合成请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class CraftingRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 合成配方ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int RecipeId { get; set; }

        /// <summary>
        /// 材料列表
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public List<long> MaterialIds { get; set; } = new();

        /// <summary>
        /// 合成数量
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int Count { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.Crafting;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 合成响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class CraftingResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 获得的物品列表
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public List<ItemInfo> CraftedItems { get; set; } = new();

        /// <summary>
        /// 消耗的材料
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public List<long> ConsumedMaterials { get; set; } = new();

        /// <summary>
        /// 消耗的金币
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long ConsumedGold { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.CraftingResult;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 属性继承请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class AttributeInheritanceRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 源装备ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long SourceEquipmentId { get; set; }

        /// <summary>
        /// 目标装备ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long TargetEquipmentId { get; set; }

        /// <summary>
        /// 继承属性类型列表
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public List<string> AttributeTypes { get; set; } = new();

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.AttributeInheritance;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
        [MemoryPackOrder(6)]
        [Id(6)] public List<long> MaterialIds { get; set; }

        
    }

    /// <summary>
    /// 属性继承响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class AttributeInheritanceResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 继承的属性
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public Dictionary<string, object> InheritedAttributes { get; set; } = new();

        /// <summary>
        /// 消耗的金币
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long ConsumedGold { get; set; }

        /// <summary>
        /// 消耗的材料
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public List<long> ConsumedMaterials { get; set; } = new();

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.AttributeInheritance;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 五行合成请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class WuXingCraftingRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 五行材料列表
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public Dictionary<string, List<long>> WuXingMaterials { get; set; } = new();

        /// <summary>
        /// 合成目标类型
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string TargetType { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.WuXingCrafting;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 五行合成响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class WuXingCraftingResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 获得的物品
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ItemInfo CraftedItem { get; set; } = new();

        /// <summary>
        /// 消耗的材料
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public Dictionary<string, List<long>> ConsumedMaterials { get; set; } = new();

        /// <summary>
        /// 消耗的金币
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long ConsumedGold { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.WuXingCrafting;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

 

    #region 合成配方消息

    /// <summary>
    /// 合成配方消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class CraftingRecipe : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 配方ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int RecipeId { get; set; }

        /// <summary>
        /// 配方名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Name { get; set; } = "";

        /// <summary>
        /// 描述
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Description { get; set; } = "";

        /// <summary>
        /// 所需材料列表
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public Dictionary<long, int> RequiredMaterials { get; set; } = new();

        /// <summary>
        /// 所需金币
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long RequiredGold { get; set; }

        /// <summary>
        /// 所需等级
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int RequiredLevel { get; set; }

        /// <summary>
        /// 所需技能等级
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public Dictionary<string, int> RequiredSkills { get; set; } = new();

        /// <summary>
        /// 产出物品列表
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public Dictionary<long, int> OutputItems { get; set; } = new();

        /// <summary>
        /// 合成时间（毫秒）
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public long CraftingTime { get; set; }

        /// <summary>
        /// 成功率
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public float SuccessRate { get; set; } = 1.0f;

        /// <summary>
        /// 是否可重复合成
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public bool IsRepeatable { get; set; } = true;

        /// <summary>
        /// 配方类型
        /// </summary>
        [MemoryPackOrder(11)]
        [Id(11)]
        public string RecipeType { get; set; } = "";

        /// <summary>
        /// 限制条件
        /// </summary>
        [MemoryPackOrder(12)]
        [Id(12)]
        public Dictionary<string, object> Restrictions { get; set; } = new();

        [MemoryPackOrder(13)]
        [Id(13)]
        public MessageType Type { get; set; } = MessageType.CraftingRecipe;
        [MemoryPackOrder(14)]
        [Id(14)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion
}