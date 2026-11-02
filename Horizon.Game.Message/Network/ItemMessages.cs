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
    [NetworkMessage(33, Enums.ServiceType.Game)]
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
    [NetworkMessage(65, Enums.ServiceType.Game)]
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
    [NetworkMessage(31, Enums.ServiceType.Game)]
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

        /// <summary>
        /// 已镶宝石（按槽位顺序存宝石模板 ID，-1 = 空槽）。
        /// <para>
        /// 长度即该装备开放的孔数：客户端按装备品质规则渲染，服务端不建物品表、
        /// 只按 <c>GemSocketRequest</c> 落库（上界 <c>InventoryGrain.MaxGemSlots</c> 兜底）。
        /// 语义上只对装备条目有意义；消耗品/材料恒为空。
        /// </para>
        /// </summary>
        [MemoryPackOrder(13)]
        [Id(13)]
        public List<int> Gems { get; set; } = new();
    }

    /// <summary>
    /// 装备物品消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(66, Enums.ServiceType.Game)]
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

        /// <summary>
        /// true = 从 <see cref="Slot"/> 卸下装备（此时 <see cref="ItemId"/> 忽略）；
        /// false/缺省 = 把 <see cref="ItemId"/> 装到 <see cref="Slot"/>。
        /// <para>
        /// 之所以复用同一条消息而不是新增一条：客户端卸下装备本就走同一个交互入口，
        /// 且缺省值 false 让旧客户端的既包语义保持不变（向后兼容）。
        /// </para>
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public bool Unequip { get; set; }

        /// <summary>
        /// 装备**定义/模板** ID（客户端编号空间，如 10001..30001）。
        /// <para>
        /// <see cref="ItemId"/> 是服务端背包 grain 的自增实例 ID，客户端并不掌握该编号空间。
        /// 因此客户端发起的穿戴一律填本字段、<see cref="ItemId"/> 留 0；服务端优先按本字段解析。
        /// </para>
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public int TemplateId { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.EquipItem;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 单个已装备条目（服务端权威 → 客户端渲染）。
    /// <para>
    /// <b>必须带 TemplateId</b>：服务端 <c>ItemInfo.ItemId</c> 是背包 grain 的自增**实例** ID
    /// （从 1 起），而客户端 <c>EquipmentDatabase.GetEquipment(int)</c> 的入参是定义表 ID
    /// （10001..30001，即 TemplateId 语义）。只回 ItemId 会让客户端全部解析成 null（角色光身子）。
    /// </para>
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class EquippedSlotEntry
    {
        /// <summary>装备槽位，取值与客户端 <c>EquipmentSlot</c> 枚举一致（0..14，14=Face）。</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int Slot { get; set; }

        /// <summary>背包内物品实例 ID（用于回发卸下/换装）。</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long ItemId { get; set; }

        /// <summary>物品模板 ID = 客户端装备定义表 ID，3D 换装按它解析。</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int TemplateId { get; set; }

        /// <summary>数量（装备通常 1，但保真起见一并下发）。</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int Count { get; set; }

        /// <summary>品质。</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int Quality { get; set; }

        /// <summary>
        /// 已镶宝石（槽位序，-1=空槽）。客户端把"镶嵌宝石的五行加成"计入角色属性，
        /// 必须随装备条目一起下发——只给模板 ID 列表的话，五行/属性加成会随换装丢失。
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public List<int> Gems { get; set; } = new();
    }

    /// <summary>
    /// 角色已装备清单同步：客户端只填 <see cref="CharacterId"/> 请求，服务端回填
    /// <see cref="Entries"/>。<br/>
    /// 存在理由：客户端 3D 换装此前只读本地 SQLite，服务端换了装备客户端并不知道；
    /// 既有的 <c>EquipmentInfoMessage</c> 处理器只把请求字段原样回显，给不出真实装备态。
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(230, Enums.ServiceType.Game)]
    public partial class RoleEquipmentSyncMessage : MessageUnion, INetworkMessage
    {
        /// <summary>目标角色 ID。</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>已装备条目（请求时为空头）。</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public List<EquippedSlotEntry> Entries { get; set; } = new();

        /// <summary>是否成功。</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public bool Success { get; set; }

        /// <summary>失败原因。</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.RoleEquipmentSync;

        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 角色背包清单同步：客户端只填 <see cref="CharacterId"/> 请求，服务端回填 <see cref="Items"/>。
    /// <para>
    /// <see cref="Success"/> 与"空清单"必须能区分开：<c>Success=true 且 Items 为空</c> 表示服务端
    /// 确认背包为空（客户端可以安全地把历史本地清单迁移进来）；<c>Success=false</c> 表示 grain
    /// 不可用/查询失败，此时客户端**绝不能**上传，否则每次断线重连都会重复播种一份物品。
    /// </para>
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(231, Enums.ServiceType.Game)]
    public partial class RoleInventorySyncMessage : MessageUnion, INetworkMessage
    {
        /// <summary>目标角色 ID。</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>背包内物品（服务端权威；请求时为空）。</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public List<ItemInfo> Items { get; set; } = new();

        /// <summary>背包容量上限。</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Capacity { get; set; }

        /// <summary>是否成功（false 时客户端应保持本地渲染、且不得迁移）。</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public bool Success { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.RoleInventorySync;

        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>批量发放条目：只带模板 ID 与数量（客户端编号空间）。</summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class InventoryGrantEntry
    {
        /// <summary>物品模板 ID = 客户端定义表 ID。</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int TemplateId { get; set; }

        /// <summary>数量（同模板多件应先在客户端聚合，服务端按 TemplateId+Quality 堆叠）。</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int Count { get; set; }
    }

    /// <summary>
    /// 按模板批量发放物品。当前唯一用途是把玩家改动前的本地背包一次性迁到服务端权威存储。
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(232, Enums.ServiceType.Game)]
    public partial class InventoryGrantMessage : MessageUnion, INetworkMessage
    {
        /// <summary>目标角色 ID。</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>待发放条目。</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public List<InventoryGrantEntry> Entries { get; set; } = new();

        /// <summary>实际发放成功的条目数。</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int GrantedCount { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public bool Success { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.InventoryGrant;

        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 武器切换消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(67, Enums.ServiceType.Game)]
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
    [NetworkMessage(68, Enums.ServiceType.Game)]
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
    [NetworkMessage(69, Enums.ServiceType.Game)]
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
    [NetworkMessage(70, Enums.ServiceType.Game)]
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
    [NetworkMessage(32, Enums.ServiceType.Game)]
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
    [NetworkMessage(71, Enums.ServiceType.Game)]
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
    [NetworkMessage(72, Enums.ServiceType.Game)]
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
    [NetworkMessage(73, Enums.ServiceType.Game)]
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
    [NetworkMessage(74, Enums.ServiceType.Game)]
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
    [NetworkMessage(118, Enums.ServiceType.Game)]
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
    [NetworkMessage(75, Enums.ServiceType.Game)]
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

        /// <summary>
        /// 正在交互的 NPC 运行时实体 id；0 = 没有 NPC 语境（旧调用方）。
        /// <para>服务端据此解析"训练师教不教/设施在不在"并核对邻近；价格与材料一律由服务端裁决。</para>
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public ulong EntityId { get; set; }

        /// <summary>动作：0 = 制作，1 = 向训练师学配方。</summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public int Action { get; set; }
    }

    /// <summary>
    /// 合成响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(76, Enums.ServiceType.Game)]
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

        /// <summary>
        /// 配方清单（<c>CraftingRequest.Action = 2</c> 时回填）：这位 NPC 教的、或这个设施能做的。
        /// <para>客户端据此画卡片，<b>不在本地维护一份写死的配方表</b>。</para>
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public List<CraftingRecipe> Recipes { get; set; } = new();
    }

    /// <summary>
    /// 属性继承请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(77, Enums.ServiceType.Game)]
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
    [NetworkMessage(78, Enums.ServiceType.Game)]
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
    [NetworkMessage(79, Enums.ServiceType.Game)]
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
    [NetworkMessage(80, Enums.ServiceType.Game)]
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
    [NetworkMessage(119, Enums.ServiceType.Game)]
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

    #region 背包管理消息（Phase 7）

    /// <summary>
    /// 背包排序请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(204, Enums.ServiceType.Game)]
    public partial class InventorySortRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 排序方式（0=按类型，1=按品质，2=按名称，3=按数量）
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int SortMode { get; set; }

        /// <summary>
        /// 排序方向（0=升序，1=降序）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int SortDirection { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.InventorySort;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 物品拆分请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(205, Enums.ServiceType.Game)]
    public partial class ItemSplitRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 原始槽位索引
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int SourceSlot { get; set; }

        /// <summary>
        /// 拆分数量
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int SplitCount { get; set; }

        /// <summary>
        /// 目标槽位索引（-1表示自动分配）
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int TargetSlot { get; set; } = -1;

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.ItemSplit;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 物品拆分响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(206, Enums.ServiceType.Game)]
    public partial class ItemSplitResponse : MessageUnion, INetworkMessage
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
        /// 原始槽位剩余数量
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int SourceRemainingCount { get; set; }

        /// <summary>
        /// 新槽位物品数量
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int NewSlotCount { get; set; }

        /// <summary>
        /// 新槽位索引
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int NewSlotIndex { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.ItemSplit;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 物品丢弃请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(207, Enums.ServiceType.Game)]
    public partial class ItemDiscardRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 槽位索引
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int SlotIndex { get; set; }

        /// <summary>
        /// 丢弃数量（-1表示全部丢弃）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int DiscardCount { get; set; } = -1;

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.ItemDiscard;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 物品丢弃响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(208, Enums.ServiceType.Game)]
    public partial class ItemDiscardResponse : MessageUnion, INetworkMessage
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
        /// 丢弃的物品信息
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ItemInfo DiscardedItem { get; set; } = new();

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.ItemDiscard;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 物品锁定请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(209, Enums.ServiceType.Game)]
    public partial class ItemLockRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 槽位索引
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int SlotIndex { get; set; }

        /// <summary>
        /// 锁定状态（true=锁定，false=解锁）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public bool IsLocked { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.ItemLock;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 背包扩容请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(210, Enums.ServiceType.Game)]
    public partial class InventoryExpandRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 扩容数量
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int ExpandCount { get; set; }

        /// <summary>
        /// 扩容方式（0=金币，1=道具，2=充值）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int ExpandMethod { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.InventoryExpand;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 背包扩容响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(211, Enums.ServiceType.Game)]
    public partial class InventoryExpandResponse : MessageUnion, INetworkMessage
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
        /// 新的背包容量
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int NewCapacity { get; set; }

        /// <summary>
        /// 消耗的金币
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long ConsumedGold { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.InventoryExpand;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 宝石镶嵌消息

    /// <summary>
    /// 宝石镶嵌/卸下请求。
    /// <para>
    /// <see cref="EquipmentItemId"/> 是服务端背包的装备**实例** ID——客户端物品详情本就持有
    /// 权威 <c>ItemInfo.ItemId</c>，镶嵌是针对单件装备实例的操作，不能用模板 ID 寻址。
    /// <see cref="GemTemplateId"/> 用模板 ID（=客户端定义表 ID）：服务端按它扣/还堆叠，
    /// 与穿戴接口的"客户端只说模板 ID"约定一致。0 = 卸下 <see cref="GemSlot"/> 的宝石放回背包。
    /// </para>
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(233, Enums.ServiceType.Game)]
    public partial class GemSocketRequest : MessageUnion, INetworkMessage
    {
        /// <summary>角色ID</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>目标装备的背包实例 ID</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long EquipmentItemId { get; set; }

        /// <summary>宝石槽位下标（0 起，上界见服务端 MaxGemSlots）</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int GemSlot { get; set; }

        /// <summary>要镶入的宝石模板 ID；0 = 卸下该槽</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int GemTemplateId { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.GemSocket;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 宝石镶嵌/卸下响应。携带操作后的完整 <see cref="Gems"/>，客户端可直接更新该条目，
    /// 无需等服务端再回推一次背包全量。
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(234, Enums.ServiceType.Game)]
    public partial class GemSocketResponse : MessageUnion, INetworkMessage
    {
        /// <summary>是否成功</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>结果说明</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>回显：目标装备实例 ID</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long EquipmentItemId { get; set; }

        /// <summary>回显：槽位</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int GemSlot { get; set; }

        /// <summary>操作后该装备的宝石数组（-1=空槽）；失败时为操作前原状。</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public List<int> Gems { get; set; } = new();

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.GemSocket;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion
}