using System;

namespace Horizon.Game.Sim.Items;

/// <summary>
/// 物品品质。
/// <para>
/// <b>编号沿用本工程既有语义：0 = 普通（最低档）</b>。三处既有实现一致——客户端
/// <c>InkWashTheme.InkQuality</c>（Common/Uncommon/Rare/Epic/Legendary）、库列注释
/// （0 普通、1 精良、2 稀有、3 史诗、4 传说、5 神器）、种子数据的实际取值。
/// <b>不要改成 WoW 的 0=粗糙</b>：那会让库里每一件物品的显示品质整体下移一档。
/// 6=神话是编辑器新增的顶档（库里历史数据不会出现）。
/// </para>
/// </summary>
public enum ItemQuality
{
    /// <summary>普通（白）</summary>
    Common = 0,
    /// <summary>优秀（绿）</summary>
    Uncommon = 1,
    /// <summary>精良（蓝）</summary>
    Rare = 2,
    /// <summary>史诗（紫）</summary>
    Epic = 3,
    /// <summary>传说（橙）</summary>
    Legendary = 4,
    /// <summary>神器（金）</summary>
    Artifact = 5,
    /// <summary>神话（红，编辑器新增顶档）</summary>
    Mythic = 6,
}

/// <summary>
/// 物品大类，取值与 `Game_HunduShijie_ItemTemplate.item_type` 的列注释逐项同序。
/// <para>
/// 注意：外观物品用 <see cref="InventoryType.Appearance"/> 表达，<b>不再复用材料大类</b>。
/// 历史种子把外观写成了 <c>item_type=4</c>（材料），那是缺陷，编辑器只读兼容。
/// </para>
/// </summary>
public enum ItemClass
{
    Weapon = 0,
    Armor = 1,
    Accessory = 2,
    Consumable = 3,
    Material = 4,
    Quest = 5,
    Book = 6,
    Recipe = 7,
    Container = 8,
    /// <summary>外观：仅在编辑器内部用于筛选，落库时 item_type 仍取 1/2（按外形层），并靠 inventory_type=15 区分。</summary>
    Appearance = 9,
}

/// <summary>
/// 装备部位。
/// <para>
/// <b>0..16 与客户端 <c>EquipmentSlot</c> 逐项同序</b>（Head=0 … Appearance=15），另加 16=背包、17=不装备的占位。
/// 注意 <c>inventory_type = 该枚举值 - 1</c>（0 表示不装备），所以 <b>Body 是 5 而不是 4</b>——
/// 少了"背部"那一格会让身体之后的每一件装备都穿错槽位，而库里看不出来。
/// </para>
/// </summary>
public enum InventoryType
{
    /// <summary>不装备</summary>
    None = 0,
    Head = 1,
    Neck = 2,
    Shoulder = 3,
    /// <summary>背部（披风）</summary>
    Back = 4,
    Body = 5,
    Waist = 6,
    Legs = 7,
    Feet = 8,
    RightHand = 9,
    LeftHand = 10,
    RightRing = 11,
    LeftRing = 12,
    RightWrist = 13,
    LeftWrist = 14,
    Face = 15,
    Appearance = 16,
    /// <summary>背包（不占战斗槽位）</summary>
    Bag = 17,
}

/// <summary>
/// 属性类型。
/// <para>
/// <b>取值与 <c>Horizon.Game.Message.Enums.AttributeType</c> 的成员一一对应</b>：本枚举额外加了 0=空行。
/// 这样 <c>base_attributes</c> JSON 可以直接用成员名当键（见 <see cref="ItemCore.AttributeKey"/>），
/// 服务端 <c>ItemAttributeContract</c> 不用认第二套词表。
/// </para>
/// </summary>
public enum ItemStatType
{
    None = 0,
    Strength = 1,
    Agility = 2,
    Intelligence = 3,
    Constitution = 4,
    Luck = 5,
    Attack = 6,
    Defense = 7,
    MagicAttack = 8,
    MagicDefense = 9,
    CriticalRate = 10,
    CriticalDamage = 11,
    HitRate = 12,
    DodgeRate = 13,
    MaxHealth = 14,
    MaxMana = 15,
    HealthRegen = 16,
    ManaRegen = 17,
    MoveSpeed = 18,
    AttackSpeed = 19,
    CastSpeed = 20,
    MetalAttack = 21,
    WoodAttack = 22,
    WaterAttack = 23,
    FireAttack = 24,
    EarthAttack = 25,
    MetalDefense = 26,
    WoodDefense = 27,
    WaterDefense = 28,
    FireDefense = 29,
    EarthDefense = 30,
    MetalPenetration = 31,
    WoodPenetration = 32,
    WaterPenetration = 33,
    FirePenetration = 34,
    EarthPenetration = 35,
    MetalAffinity = 36,
    WoodAffinity = 37,
    WaterAffinity = 38,
    FireAffinity = 39,
    EarthAffinity = 40,
    Energy = 41,
}

/// <summary>插槽颜色。Meta 只能插 Meta，其余槽可插同色或无色。</summary>
public enum SocketColor
{
    Meta = 0,
    Red = 1,
    Yellow = 2,
    Blue = 3,
    /// <summary>无色/棱彩：接受任意颜色。</summary>
    None = 4,
}

/// <summary>绑定类型，与库注释同序。</summary>
public enum ItemBindType
{
    None = 0,
    Pickup = 1,
    Equip = 2,
    Use = 3,
}

/// <summary>法术触发方式（对应 WoW `spelltrigger`）。</summary>
public enum ItemSpellTrigger
{
    Use = 0,
    OnEquip = 1,
    ChanceOnHit = 2,
    Soulstone = 3,
    OnNoDelayUse = 4,
    LearnSpell = 5,
}

/// <summary>物品行为位（对应 WoW `flags`/`flagsCustom` 的最小子集）。</summary>
[Flags]
public enum ItemFlags
{
    None = 0,
    NoSell = 1 << 0,
    NoDestroy = 1 << 1,
    NoTrade = 1 << 2,
    UniqueEquipped = 1 << 3,
    AccountBound = 1 << 4,
    HideFromLoot = 1 << 5,
    /// <summary>只允许在休息区使用</summary>
    RestAreaOnly = 1 << 6,
    /// <summary>战斗外可用</summary>
    OutOfCombatOnly = 1 << 7,
}

/// <summary>编号号段（唯一实现，CLI 与插件都调它）。</summary>
public enum ItemSegment
{
    /// <summary>历史遗留：任务/杂项</summary>
    Legacy = 0,
    /// <summary>装备：10001..49999</summary>
    Equipment = 1,
    /// <summary>消耗品：50001..59999</summary>
    Consumable = 2,
    /// <summary>宝石/镶嵌：60001..69999</summary>
    Gem = 3,
    /// <summary>材料：700001..799999</summary>
    Material = 4,
    /// <summary>秘籍/配方：800000..899999（800000+技能 id 硬绑定）</summary>
    Book = 5,
    /// <summary>词缀：900001..909999</summary>
    Affix = 6,
    /// <summary>外观：91000..91999（既存 91001..91004，`EquipmentDatabase.AppearanceIdBase = 91000`）</summary>
    Appearance = 7,
    /// <summary>套装：920000..929999</summary>
    Set = 8,
    /// <summary>五行灵材/信物：910000..919999（既存 910001..910015；<b>不是外观段</b>）</summary>
    WuxingMaterial = 9,
    /// <summary>号段外（非法）</summary>
    Invalid = 99,
}

/// <summary>一条属性行（从表 <c>ItemStatTemplate</c>）。</summary>
public struct ItemStatLine
{
    public int Type;
    public int Value;
}

/// <summary>一段武器伤害（从表 <c>ItemDamageTemplate</c>）。</summary>
public struct ItemDamageLine
{
    /// <summary>伤害类型：0 物理，1..5 金木水火土。</summary>
    public int Type;
    public int Min;
    public int Max;
}

/// <summary>一个固定插槽（从表 <c>ItemSocketTemplate</c>）。</summary>
public struct ItemSocketLine
{
    public int Color;
    /// <summary>插槽奖励 id（0=无）。</summary>
    public int Bonus;
}

/// <summary>一条法术触发（从表 <c>ItemSpellTemplate</c>）。</summary>
public struct ItemSpellLine
{
    public int SpellId;
    public int Trigger;
    public int Charges;
    public int PpmRate;
    public int CooldownMs;
    public int CategoryCooldownMs;
}

/// <summary>一条套装加成档（从表 <c>ItemSetBonusTemplate</c> + Stat 行）。</summary>
public sealed class ItemSetBonusTier
{
    public int Pieces;
    public string Description = string.Empty;
    public int SpellId;
    public ItemStatLine[] Stats = Array.Empty<ItemStatLine>();
}

/// <summary>整套套装（从表 <c>ItemSetTemplate</c>）。</summary>
public sealed class ItemSetSpec
{
    public int SetId;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public int ItemLevel;
    public ItemSetBonusTier[] Tiers = Array.Empty<ItemSetBonusTier>();
}

/// <summary>随机词缀（从表 <c>ItemRandomAffixTemplate</c> + Stat 行）。</summary>
public sealed class ItemAffixSpec
{
    public int AffixId;
    /// <summary>0=前缀 1=后缀</summary>
    public int Kind;
    public string Name = string.Empty;
    /// <summary>词缀属性数值带宽：同一条属性按 <see cref="ValueMin"/>..<see cref="ValueMax"/> 抽取。</summary>
    public ItemStatLine[] Stats = Array.Empty<ItemStatLine>();
    public int ValueMin;
    public int ValueMax;
}

/// <summary>一条结构化出处（从表 <c>ItemSourceLink</c>）。</summary>
public struct ItemSourceLine
{
    /// <summary>0 怪物掉落 1 采集 2 任务 3 副本 4 商店 5 合成 6 活动</summary>
    public int SourceType;
    /// <summary>引用类型：0 怪物 1 掉落表 2 任务 3 NPC 4 物品 5 地图</summary>
    public int RefType;
    public int RefId;
    public string Note;
}

/// <summary>
/// 物品模板在内存里的完整形状：CLI 读库后填它，校验/试算/tooltip/导出都只认它。
/// <para>刻意<b>不用</b> EF 实体做内核输入：内核零存储依赖，否则测试要拖一个数据库进来。</para>
/// </summary>
public sealed class ItemSpec
{
    public int TemplateId;
    public string Name = string.Empty;
    public string Description = string.Empty;

    public int ItemClass;
    public int SubType;
    public int Quality;
    public int Rarity;
    public int ItemLevel;
    public int InventoryType;
    public int Element;
    public int BindType;
    public int Flags;
    public int MaterialGrade;
    public int GatherType;
    public int SourceType;
    public string ProfessionLimit = string.Empty;

    public int LevelRequire;
    public int RealmRequire;
    public int RequiredSkill;
    public int RequiredSkillRank;
    public int AllowableClass;
    public int AllowableRace;

    public int MaxStack = 1;
    public int MaxCount;
    public int ContainerSlots;
    public int Durability;

    public int Armor;
    public int Block;
    public int AttackDelayMs;
    public int ResistanceElement;
    public int ResistanceValue;

    public int BuyPrice;
    public int SellPrice;
    public int MinMoneyLoot;
    public int MaxMoneyLoot;

    public int SocketBonus;
    public int GemProperties;
    public int SetId;
    public int RandomProperty;
    public int RandomSuffix;
    public int DisenchantId;
    public int BagFamily;
    public int FoodType;
    public int DurationSeconds;
    public int ValidDays;
    public int StartQuest;
    public int PageText;
    public int Language;

    public bool CanTrade = true;
    public bool CanDestroy = true;
    public bool IsUnique;

    public string IconPath = string.Empty;
    public string ModelPath = string.Empty;
    public string ScriptName = string.Empty;
    public string Remark = string.Empty;
    public string SourceDetail = string.Empty;
    /// <summary>使用效果 JSON（历史契约，形状由 <see cref="ItemCore.ParseRestoreFromUseEffect"/> 容错读取）。</summary>
    public string UseEffect = string.Empty;

    public ItemStatLine[] Stats = Array.Empty<ItemStatLine>();
    public ItemDamageLine[] Damage = Array.Empty<ItemDamageLine>();
    public ItemSocketLine[] Sockets = Array.Empty<ItemSocketLine>();
    public ItemSpellLine[] Spells = Array.Empty<ItemSpellLine>();
    public ItemSourceLine[] Sources = Array.Empty<ItemSourceLine>();

    /// <summary>物品等级的最小合法值（1；0 表示"未标定"，仅材料/任务物允许）。</summary>
    public const int MinItemLevel = 1;
    /// <summary>物品等级上限。定 300 而不是 int.MaxValue：库里这一列是 int，但面板要有限输入，且预算不会溢出。</summary>
    public const int MaxItemLevel = 300;
    /// <summary>属性行上限（与 WoW stat_type1..10 同数）。</summary>
    public const int MaxStatLines = 10;
    /// <summary>伤害段上限（主手/副手）。</summary>
    public const int MaxDamageLines = 2;
    /// <summary>固定插槽上限（与 WoW socketColor_1..3 同数）。</summary>
    public const int MaxSockets = 3;
    /// <summary>法术触发行上限（与 WoW spellid_1..5 同数）。</summary>
    public const int MaxSpellLines = 5;
    /// <summary>叠加上限。低于数据库 nvarchar 承载能力即可，497 是 WoW 的常见上限。</summary>
    public const int MaxStackLimit = 9999;
}
