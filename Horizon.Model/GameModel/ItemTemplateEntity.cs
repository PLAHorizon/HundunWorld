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
    }
}
