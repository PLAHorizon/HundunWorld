using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 物品模板实体
    /// </summary>
    [Table("Game_HunduShijie_ItemTemplate"), TableDescription(Name = "Game_HunduShijie_ItemTemplate", Order = "HunduShijie_013", Description = "物品模板信息")]
    [Comment("物品模板表")]
    [EntityStorage("Game")]
    public class ItemTemplateEntity : BaseGameModel<int>
    {
        /// <summary>
        /// 物品模板ID
        /// </summary>
        [Key]
        [Column("item_id", TypeName = "int", Order = 1), TableDescription(TypeName = "int", Name = "item_id", Order = "1", Description = "物品模板ID")]
        [Comment("物品模板ID")]
        public new int Id { get; set; }
        
        /// <summary>
        /// 物品名称
        /// </summary>
        [Required]
        [Column("item_name", TypeName = "nvarchar(50)", Order = 2), TableDescription(TypeName = "nvarchar(50)", Name = "item_name", Order = "2", Description = "物品名称")]
        [Comment("物品名称")]
        public string ItemName { get; set; }
        
        /// <summary>
        /// 物品描述
        /// </summary>
        [Column("description", TypeName = "nvarchar(500)", Order = 3), TableDescription(TypeName = "nvarchar(500)", Name = "description", Order = "3", Description = "物品描述")]
        [Comment("物品描述")]
        public string Description { get; set; }
        
        /// <summary>
        /// 物品类型
        /// </summary>
        [Column("item_type", TypeName = "int", Order = 4), TableDescription(TypeName = "int", Name = "item_type", Order = "4", Description = "物品类型")]
        [Comment("物品类型 0-武器 1-防具 2-饰品 3-消耗品 4-材料 5-任务物品 6-秘籍 7-配方 8-宝箱")]
        public int ItemType { get; set; }
        
        /// <summary>
        /// 物品子类型
        /// </summary>
        [Column("sub_type", TypeName = "int", Order = 5), TableDescription(TypeName = "int", Name = "sub_type", Order = "5", Description = "物品子类型")]
        [Comment("物品子类型（如武器类型、防具部位等）")]
        public int SubType { get; set; }
        
        /// <summary>
        /// 基础品质
        /// </summary>
        [Column("base_quality", TypeName = "int", Order = 6), TableDescription(TypeName = "int", Name = "base_quality", Order = "6", Description = "基础品质")]
        [Comment("基础品质 0-普通 1-精良 2-稀有 3-史诗 4-传说 5-神器")]
        public int BaseQuality { get; set; }
        
        /// <summary>
        /// 稀有度
        /// </summary>
        [Column("rarity", TypeName = "int", Order = 7), TableDescription(TypeName = "int", Name = "rarity", Order = "7", Description = "稀有度")]
        [Comment("稀有度 0-常见 1-少见 2-稀少 3-罕见 4-极其罕见 5-绝世珍稀")]
        public int Rarity { get; set; }
        
        /// <summary>
        /// 五行属性
        /// </summary>
        [Column("element", TypeName = "int", Order = 8), TableDescription(TypeName = "int", Name = "element", Order = "8", Description = "五行属性")]
        [Comment("五行属性 0-金 1-木 2-水 3-火 4-土 5-无")]
        public int Element { get; set; }
        
        /// <summary>
        /// 材料品阶
        /// </summary>
        [Column("material_grade", TypeName = "int", Order = 9), TableDescription(TypeName = "int", Name = "material_grade", Order = "9", Description = "材料品阶")]
        [Comment("材料品阶 1-9级")]
        public int MaterialGrade { get; set; }
        
        /// <summary>
        /// 等级需求
        /// </summary>
        [Column("level_require", TypeName = "int", Order = 10), TableDescription(TypeName = "int", Name = "level_require", Order = "10", Description = "等级需求")]
        [Comment("使用等级需求")]
        public int LevelRequire { get; set; }
        
        /// <summary>
        /// 境界需求
        /// </summary>
        [Column("realm_require", TypeName = "int", Order = 11), TableDescription(TypeName = "int", Name = "realm_require", Order = "11", Description = "境界需求")]
        [Comment("境界等级需求")]
        public int RealmRequire { get; set; }
        
        /// <summary>
        /// 职业限制
        /// </summary>
        [Column("profession_limit", TypeName = "varchar(50)", Order = 12), TableDescription(TypeName = "varchar(50)", Name = "profession_limit", Order = "12", Description = "职业限制")]
        [Comment("职业限制（逗号分隔的职业ID）")]
        public string ProfessionLimit { get; set; }
        
        /// <summary>
        /// 最大叠加数
        /// </summary>
        [Column("max_stack", TypeName = "int", Order = 13), TableDescription(TypeName = "int", Name = "max_stack", Order = "13", Description = "最大叠加数")]
        [Comment("最大叠加数")]
        public int MaxStack { get; set; }
        
        /// <summary>
        /// 基础掉落概率
        /// </summary>
        [Column("drop_rate", TypeName = "decimal(5,4)", Order = 14), TableDescription(TypeName = "decimal(5,4)", Name = "drop_rate", Order = "14", Description = "基础掉落概率")]
        [Comment("基础掉落概率（0.0001-1.0000）")]
        public decimal DropRate { get; set; }
        
        /// <summary>
        /// 出处类型
        /// </summary>
        [Column("source_type", TypeName = "int", Order = 15), TableDescription(TypeName = "int", Name = "source_type", Order = "15", Description = "出处类型")]
        [Comment("出处类型 0-怪物掉落 1-采集获得 2-任务奖励 3-副本产出 4-商店购买 5-合成产出 6-活动奖励")]
        public int SourceType { get; set; }
        
        /// <summary>
        /// 出处详情
        /// </summary>
        [Column("source_detail", TypeName = "nvarchar(500)", Order = 16), TableDescription(TypeName = "nvarchar(500)", Name = "source_detail", Order = "16", Description = "出处详情")]
        [Comment("出处详情（JSON格式，包含怪物ID、地图ID、NPC ID等）")]
        public string SourceDetail { get; set; }
        
        /// <summary>
        /// 采集类型
        /// </summary>
        [Column("gather_type", TypeName = "int", Order = 17), TableDescription(TypeName = "int", Name = "gather_type", Order = "17", Description = "采集类型")]
        [Comment("采集类型 0-矿物 1-草药 2-木材 3-兽皮 4-其他")]
        public int? GatherType { get; set; }
        
        /// <summary>
        /// 基础属性
        /// </summary>
        [Column("base_attributes", TypeName = "nvarchar(1000)", Order = 18), TableDescription(TypeName = "nvarchar(1000)", Name = "base_attributes", Order = "18", Description = "基础属性")]
        [Comment("基础属性（JSON格式，包含属性类型和数值）")]
        public string BaseAttributes { get; set; }
        
        /// <summary>
        /// 随机属性池
        /// </summary>
        [Column("random_attributes", TypeName = "nvarchar(2000)", Order = 19), TableDescription(TypeName = "nvarchar(2000)", Name = "random_attributes", Order = "19", Description = "随机属性池")]
        [Comment("随机属性池（JSON格式，定义可能出现的随机属性）")]
        public string RandomAttributes { get; set; }
        
        /// <summary>
        /// 随机属性数量
        /// </summary>
        [Column("random_attr_count", TypeName = "varchar(20)", Order = 20), TableDescription(TypeName = "varchar(20)", Name = "random_attr_count", Order = "20", Description = "随机属性数量")]
        [Comment("随机属性数量范围（如1-3）")]
        public string RandomAttrCount { get; set; }
        
        /// <summary>
        /// 宝石槽概率
        /// </summary>
        [Column("gem_slot_rates", TypeName = "varchar(100)", Order = 21), TableDescription(TypeName = "varchar(100)", Name = "gem_slot_rates", Order = "21", Description = "宝石槽概率")]
        [Comment("宝石槽概率（JSON格式，定义0-5个槽的概率）")]
        public string GemSlotRates { get; set; }
        
        /// <summary>
        /// 合成配方
        /// </summary>
        [Column("synthesis_recipe", TypeName = "nvarchar(500)", Order = 22), TableDescription(TypeName = "nvarchar(500)", Name = "synthesis_recipe", Order = "22", Description = "合成配方")]
        [Comment("合成配方（JSON格式，定义合成所需材料）")]
        public string SynthesisRecipe { get; set; }
        
        /// <summary>
        /// 属性继承率
        /// </summary>
        [Column("inherit_rate", TypeName = "varchar(50)", Order = 23), TableDescription(TypeName = "varchar(50)", Name = "inherit_rate", Order = "23", Description = "属性继承率")]
        [Comment("属性继承率范围（如10-80）")]
        public string InheritRate { get; set; }
        
        /// <summary>
        /// 五行加成系数
        /// </summary>
        [Column("element_bonus", TypeName = "decimal(3,2)", Order = 24), TableDescription(TypeName = "decimal(3,2)", Name = "element_bonus", Order = "24", Description = "五行加成系数")]
        [Comment("五行相生加成系数")]
        public decimal ElementBonus { get; set; }
        
        /// <summary>
        /// 五行减益系数
        /// </summary>
        [Column("element_penalty", TypeName = "decimal(3,2)", Order = 25), TableDescription(TypeName = "decimal(3,2)", Name = "element_penalty", Order = "25", Description = "五行减益系数")]
        [Comment("五行相克减益系数")]
        public decimal ElementPenalty { get; set; }
        
        /// <summary>
        /// 套装ID
        /// </summary>
        [Column("set_id", TypeName = "int", Order = 26), TableDescription(TypeName = "int", Name = "set_id", Order = "26", Description = "套装ID")]
        [Comment("所属套装ID")]
        public int? SetId { get; set; }
        
        /// <summary>
        /// 绑定类型
        /// </summary>
        [Column("bind_type", TypeName = "int", Order = 27), TableDescription(TypeName = "int", Name = "bind_type", Order = "27", Description = "绑定类型")]
        [Comment("绑定类型 0-不绑定 1-拾取绑定 2-装备绑定 3-使用绑定")]
        public int BindType { get; set; }
        
        /// <summary>
        /// 出售价格
        /// </summary>
        [Column("sell_price", TypeName = "int", Order = 28), TableDescription(TypeName = "int", Name = "sell_price", Order = "28", Description = "出售价格")]
        [Comment("出售价格（铜币）")]
        public int SellPrice { get; set; }
        
        /// <summary>
        /// 购买价格
        /// </summary>
        [Column("buy_price", TypeName = "int", Order = 29), TableDescription(TypeName = "int", Name = "buy_price", Order = "29", Description = "购买价格")]
        [Comment("购买价格（铜币）")]
        public int BuyPrice { get; set; }
        
        /// <summary>
        /// 图标路径
        /// </summary>
        [Column("icon_path", TypeName = "varchar(200)", Order = 30), TableDescription(TypeName = "varchar(200)", Name = "icon_path", Order = "30", Description = "图标路径")]
        [Comment("图标资源路径")]
        public string IconPath { get; set; }
        
        /// <summary>
        /// 模型路径
        /// </summary>
        [Column("model_path", TypeName = "varchar(200)", Order = 31), TableDescription(TypeName = "varchar(200)", Name = "model_path", Order = "31", Description = "模型路径")]
        [Comment("3D模型资源路径")]
        public string ModelPath { get; set; }
        
        /// <summary>
        /// 使用效果
        /// </summary>
        [Column("use_effect", TypeName = "nvarchar(500)", Order = 32), TableDescription(TypeName = "nvarchar(500)", Name = "use_effect", Order = "32", Description = "使用效果")]
        [Comment("使用效果（JSON格式，定义使用后的效果）")]
        public string UseEffect { get; set; }
        
        /// <summary>
        /// 是否可交易
        /// </summary>
        [Column("can_trade", TypeName = "bit", Order = 33), TableDescription(TypeName = "bit", Name = "can_trade", Order = "33", Description = "是否可交易")]
        [Comment("是否可交易")]
        public bool CanTrade { get; set; }
        
        /// <summary>
        /// 是否可销毁
        /// </summary>
        [Column("can_destroy", TypeName = "bit", Order = 34), TableDescription(TypeName = "bit", Name = "can_destroy", Order = "34", Description = "是否可销毁")]
        [Comment("是否可销毁")]
        public bool CanDestroy { get; set; }
        
        /// <summary>
        /// 是否唯一
        /// </summary>
        [Column("is_unique", TypeName = "bit", Order = 35), TableDescription(TypeName = "bit", Name = "is_unique", Order = "35", Description = "是否唯一")]
        [Comment("是否唯一物品")]
        public bool IsUnique { get; set; }
        
        /// <summary>
        /// 有效期
        /// </summary>
        [Column("valid_days", TypeName = "int", Order = 36), TableDescription(TypeName = "int", Name = "valid_days", Order = "36", Description = "有效期")]
        [Comment("有效期（天数，0表示永久）")]
        public int ValidDays { get; set; }
        
        /// <summary>
        /// 创建时间
        /// </summary>
        [Column("create_time", TypeName = "datetime", Order = 37), TableDescription(TypeName = "datetime", Name = "create_time", Order = "37", Description = "创建时间")]
        [Comment("创建时间")]
        public DateTime CreateTime { get; set; }
        
        /// <summary>
        /// 更新时间
        /// </summary>
        [Column("update_time", TypeName = "datetime", Order = 38), TableDescription(TypeName = "datetime", Name = "update_time", Order = "38", Description = "更新时间")]
        [Comment("更新时间")]
        public DateTime UpdateTime { get; set; }

        // ── 物品编辑器扩展（docs/ITEM_EDITOR_PLUGIN_DESIGN.md §3.1）──
        // 全部可空或有默认值：已有 200+ 行数据不为这次扩展做任何回填也能读。

        /// <summary>装备部位，1..16 对应客户端 EquipmentSlot 0..15，17=背包，0=不装备。见 ItemCore.InventoryTypeName。</summary>
        [Column("inventory_type", TypeName = "int", Order = 39), TableDescription(TypeName = "int", Name = "inventory_type", Order = "39", Description = "装备部位")]
        [Comment("装备部位 0=不装备 1头 2颈 3肩 4背 5衣 6腰 7腿 8脚 9主手 10副手 11右戒 12左戒 13右腕 14左腕 15面 16外观 17背包")]
        public int InventoryType { get; set; }

        /// <summary>物品等级：属性预算与掉落档位的输入（0=未标定）。</summary>
        [Column("item_level", TypeName = "int", Order = 40), TableDescription(TypeName = "int", Name = "item_level", Order = "40", Description = "物品等级")]
        [Comment("物品等级 0-300")]
        public int ItemLevel { get; set; }

        /// <summary>行为位，见 ItemFlags。</summary>
        [Column("item_flags", TypeName = "int", Order = 41), TableDescription(TypeName = "int", Name = "item_flags", Order = "41", Description = "行为位")]
        [Comment("行为位（ItemFlags 位掩码）")]
        public int ItemFlags { get; set; }

        /// <summary>持有上限（0=不限）；与 is_unique 不同，后者只表达"最多 1 个"。</summary>
        [Column("max_count", TypeName = "int", Order = 42), TableDescription(TypeName = "int", Name = "max_count", Order = "42", Description = "持有上限")]
        [Comment("持有上限 0=不限")]
        public int MaxCount { get; set; }

        /// <summary>背包格数（ContainerSlots）。</summary>
        [Column("container_slots", TypeName = "int", Order = 43), TableDescription(TypeName = "int", Name = "container_slots", Order = "43", Description = "背包格数")]
        [Comment("背包格数 0-36")]
        public int ContainerSlots { get; set; }

        /// <summary>武器攻击间隔（毫秒）。DPS 由 ItemCore.WeaponDps 现算，不存派生值。</summary>
        [Column("attack_delay_ms", TypeName = "int", Order = 44), TableDescription(TypeName = "int", Name = "attack_delay_ms", Order = "44", Description = "攻击间隔毫秒")]
        [Comment("攻击间隔（毫秒）")]
        public int AttackDelayMs { get; set; }

        /// <summary>护甲。</summary>
        [Column("armor", TypeName = "int", Order = 45), TableDescription(TypeName = "int", Name = "armor", Order = "45", Description = "护甲")]
        [Comment("护甲值")]
        public int Armor { get; set; }

        /// <summary>格挡值（盾牌）。</summary>
        [Column("block_value", TypeName = "int", Order = 46), TableDescription(TypeName = "int", Name = "block_value", Order = "46", Description = "格挡值")]
        [Comment("格挡值")]
        public int BlockValue { get; set; }

        /// <summary>抗性元素 0..5（金木水火土/无）。</summary>
        [Column("resistance_element", TypeName = "int", Order = 47), TableDescription(TypeName = "int", Name = "resistance_element", Order = "47", Description = "抗性元素")]
        [Comment("抗性元素 0金 1木 2水 3火 4土 5无")]
        public int ResistanceElement { get; set; }

        [Column("resistance_value", TypeName = "int", Order = 48), TableDescription(TypeName = "int", Name = "resistance_value", Order = "48", Description = "抗性值")]
        [Comment("抗性值")]
        public int ResistanceValue { get; set; }

        /// <summary>耐久度上限。</summary>
        [Column("durability", TypeName = "int", Order = 49), TableDescription(TypeName = "int", Name = "durability", Order = "49", Description = "耐久度")]
        [Comment("耐久度上限")]
        public int Durability { get; set; }

        /// <summary>使用所需专业 id（0=无）。</summary>
        [Column("required_skill", TypeName = "int", Order = 50), TableDescription(TypeName = "int", Name = "required_skill", Order = "50", Description = "所需专业")]
        [Comment("所需专业 id（0=无）")]
        public int RequiredSkill { get; set; }

        [Column("required_skill_rank", TypeName = "int", Order = 51), TableDescription(TypeName = "int", Name = "required_skill_rank", Order = "51", Description = "所需专业等级")]
        [Comment("所需专业等级")]
        public int RequiredSkillRank { get; set; }

        /// <summary>可用职业位掩码（0=不限）。</summary>
        [Column("allowable_class", TypeName = "int", Order = 52), TableDescription(TypeName = "int", Name = "allowable_class", Order = "52", Description = "可用职业掩码")]
        [Comment("可用职业位掩码（0=不限）")]
        public int AllowableClass { get; set; }

        /// <summary>可用种族位掩码（0=不限）。</summary>
        [Column("allowable_race", TypeName = "int", Order = 53), TableDescription(TypeName = "int", Name = "allowable_race", Order = "53", Description = "可用种族掩码")]
        [Comment("可用种族位掩码（0=不限）")]
        public int AllowableRace { get; set; }

        /// <summary>插槽奖励 id（0=无）。</summary>
        [Column("socket_bonus", TypeName = "int", Order = 54), TableDescription(TypeName = "int", Name = "socket_bonus", Order = "54", Description = "插槽奖励")]
        [Comment("插槽奖励 id（0=无）")]
        public int SocketBonus { get; set; }

        /// <summary>宝石属性 id（对应 WoW GemProperties）。</summary>
        [Column("gem_properties", TypeName = "int", Order = 55), TableDescription(TypeName = "int", Name = "gem_properties", Order = "55", Description = "宝石属性")]
        [Comment("宝石属性 id")]
        public int GemProperties { get; set; }

        /// <summary>随机前缀 id（词缀表）。</summary>
        [Column("random_property", TypeName = "int", Order = 56), TableDescription(TypeName = "int", Name = "random_property", Order = "56", Description = "随机前缀")]
        [Comment("随机前缀词缀 id（0=无）")]
        public int RandomProperty { get; set; }

        /// <summary>随机后缀 id（词缀表）。</summary>
        [Column("random_suffix", TypeName = "int", Order = 57), TableDescription(TypeName = "int", Name = "random_suffix", Order = "57", Description = "随机后缀")]
        [Comment("随机后缀词缀 id（0=无）")]
        public int RandomSuffix { get; set; }

        /// <summary>分解产出 id（分解系统未落地，先留字段）。</summary>
        [Column("disenchant_id", TypeName = "int", Order = 58), TableDescription(TypeName = "int", Name = "disenchant_id", Order = "58", Description = "分解产出")]
        [Comment("分解产出 id（0=不可分解）")]
        public int DisenchantId { get; set; }

        /// <summary>背包族（过滤器用）。</summary>
        [Column("bag_family", TypeName = "int", Order = 60), TableDescription(TypeName = "int", Name = "bag_family", Order = "60", Description = "背包族")]
        [Comment("背包族位掩码")]
        public int BagFamily { get; set; }

        /// <summary>食物类型（0=非食物）。</summary>
        [Column("food_type", TypeName = "int", Order = 61), TableDescription(TypeName = "int", Name = "food_type", Order = "61", Description = "食物类型")]
        [Comment("食物类型 0=非食物")]
        public int FoodType { get; set; }

        /// <summary>有效期秒数；>0 时优先于 valid_days（天）。</summary>
        [Column("duration_seconds", TypeName = "int", Order = 62), TableDescription(TypeName = "int", Name = "duration_seconds", Order = "62", Description = "有效期秒")]
        [Comment("有效期（秒，0=用 valid_days）")]
        public int DurationSeconds { get; set; }

        /// <summary>使用后开启的任务 id（0=无）。</summary>
        [Column("start_quest", TypeName = "int", Order = 63), TableDescription(TypeName = "int", Name = "start_quest", Order = "63", Description = "开启任务")]
        [Comment("使用后开启任务 id（0=无）")]
        public int StartQuest { get; set; }

        /// <summary>书页文本 id（0=无）。</summary>
        [Column("page_text", TypeName = "int", Order = 64), TableDescription(TypeName = "int", Name = "page_text", Order = "64", Description = "书页文本")]
        [Comment("书页文本 id（0=无）")]
        public int PageText { get; set; }

        /// <summary>语言 id（0=通用）。</summary>
        [Column("language_id", TypeName = "int", Order = 65), TableDescription(TypeName = "int", Name = "language_id", Order = "65", Description = "语言")]
        [Comment("语言 id（0=通用）")]
        public int LanguageId { get; set; }

        /// <summary>钱袋下限（铜）。</summary>
        [Column("min_money_loot", TypeName = "int", Order = 66), TableDescription(TypeName = "int", Name = "min_money_loot", Order = "66", Description = "钱袋下限")]
        [Comment("钱袋下限（铜）")]
        public int MinMoneyLoot { get; set; }

        [Column("max_money_loot", TypeName = "int", Order = 67), TableDescription(TypeName = "int", Name = "max_money_loot", Order = "67", Description = "钱袋上限")]
        [Comment("钱袋上限（铜）")]
        public int MaxMoneyLoot { get; set; }

        /// <summary>发布状态：0 草稿 1 已发布 2 停用。只有已发布的物品会进客户端内容表。</summary>
        [Column("publish_state", TypeName = "tinyint", Order = 68), TableDescription(TypeName = "tinyint", Name = "publish_state", Order = "68", Description = "发布状态")]
        [Comment("0 草稿 1 已发布 2 停用")]
        public byte PublishState { get; set; }

        /// <summary>内容版本（每次保存 +1，导出表带它）。</summary>
        [Column("content_version", TypeName = "int", Order = 69), TableDescription(TypeName = "int", Name = "content_version", Order = "69", Description = "内容版本")]
        [Comment("内容版本")]
        public int ContentVersion { get; set; }

        /// <summary>策划备注。<b>可空</b>：库里这一列允许 NULL，实体属性也必须可空——否则 EF 读到 NULL
        /// 直接抛 SqlNullValueException，而"打开一件老物品就报执行失败"从外观上像链路坏了。</summary>
        [Column("remark", TypeName = "nvarchar(200)", Order = 70), TableDescription(TypeName = "nvarchar(200)", Name = "remark", Order = "70", Description = "备注")]
        [Comment("策划备注")]
        public string? Remark { get; set; }
    }
}
