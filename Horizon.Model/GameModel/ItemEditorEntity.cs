using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    // 物品编辑器的从表（设计文档 docs/ITEM_EDITOR_PLUGIN_DESIGN.md §3）。
    // 约定与怪物/宠物/ NPC 侧一致：**本文件的表不带 TableDescription**——
    // 半份 TableDescription 会让导出工具产出半个模型，比全没有更难发现。

    /// <summary>
    /// 物品属性行（物品 × 顺序 × 属性）。对应 WoW `item_template.stat_typeN/stat_valueN`。
    /// <para><b>为什么从表而不是 stat_type1..10 十组列</b>：列式存法的第 7 条以后没人写、没人读，
    /// 而且"同一属性出现两次"会静默只显示一条。从表能直接查重。</para>
    /// </summary>
    [Table("Game_HunduShijie_ItemStat")]
    [Comment("物品属性行（物品 × 属性）")]
    [EntityStorage("Game")]
    public class ItemStatTemplateEntity : BaseGameModel<int>
    {
        [Key]
        [Column("entry_id", TypeName = "int", Order = 1)]
        [Comment("条目 id（自增）")]
        public new int Id { get; set; }

        [Column("item_id", TypeName = "int", Order = 2)]
        [Comment("物品模板 id")]
        public int ItemId { get; set; }

        [Column("seq", TypeName = "int", Order = 3)]
        [Comment("条目顺序")]
        public int Seq { get; set; }

        /// <summary>属性类型，见 ItemCore.StatTypeName 的 0..18。</summary>
        [Column("stat_type", TypeName = "int", Order = 4)]
        [Comment("属性类型 1..18")]
        public int StatType { get; set; }

        [Column("stat_value", TypeName = "int", Order = 5)]
        [Comment("属性数值（百分比属性存整数百分点）")]
        public int StatValue { get; set; }
    }

    /// <summary>物品伤害段（主手/副手）。对应 WoW `dmg_minN/dmg_maxN/dmg_typeN`。</summary>
    [Table("Game_HunduShijie_ItemDamage")]
    [Comment("物品伤害段表")]
    [EntityStorage("Game")]
    public class ItemDamageTemplateEntity : BaseGameModel<int>
    {
        [Key]
        [Column("entry_id", TypeName = "int", Order = 1)]
        [Comment("条目 id（自增）")]
        public new int Id { get; set; }

        [Column("item_id", TypeName = "int", Order = 2)]
        [Comment("物品模板 id")]
        public int ItemId { get; set; }

        [Column("seq", TypeName = "int", Order = 3)]
        [Comment("伤害段顺序 1..2")]
        public int Seq { get; set; }

        /// <summary>0 物理，1..5 金木水火土。</summary>
        [Column("damage_type", TypeName = "int", Order = 4)]
        [Comment("伤害类型 0-5")]
        public int DamageType { get; set; }

        [Column("damage_min", TypeName = "int", Order = 5)]
        [Comment("最小伤害")]
        public int DamageMin { get; set; }

        [Column("damage_max", TypeName = "int", Order = 6)]
        [Comment("最大伤害")]
        public int DamageMax { get; set; }
    }

    /// <summary>物品法术触发。对应 WoW `spellid_N/spelltrigger_N/spellcharges_N/spellppmRate_N/spellcooldown_N`。</summary>
    [Table("Game_HunduShijie_ItemSpell")]
    [Comment("物品法术触发表")]
    [EntityStorage("Game")]
    public class ItemSpellTemplateEntity : BaseGameModel<int>
    {
        [Key]
        [Column("entry_id", TypeName = "int", Order = 1)]
        [Comment("条目 id（自增）")]
        public new int Id { get; set; }

        [Column("item_id", TypeName = "int", Order = 2)]
        [Comment("物品模板 id")]
        public int ItemId { get; set; }

        [Column("seq", TypeName = "int", Order = 3)]
        [Comment("条目顺序 1..5")]
        public int Seq { get; set; }

        [Column("spell_id", TypeName = "int", Order = 4)]
        [Comment("法术 id")]
        public int SpellId { get; set; }

        /// <summary>0 使用 1 装备 2 命中概率 3 灵魂石 4 无延迟使用 5 学习。</summary>
        [Column("spell_trigger", TypeName = "int", Order = 5)]
        [Comment("触发方式 0-5")]
        public int SpellTrigger { get; set; }

        /// <summary>-1 表示无限次。</summary>
        [Column("charges", TypeName = "int", Order = 6)]
        [Comment("充能次数（-1=无限）")]
        public int Charges { get; set; }

        [Column("ppm_rate", TypeName = "int", Order = 7)]
        [Comment("每分钟触发次数（ppm × 100）")]
        public int PpmRate { get; set; }

        [Column("cooldown_ms", TypeName = "int", Order = 8)]
        [Comment("物品冷却毫秒")]
        public int CooldownMs { get; set; }

        [Column("category_cooldown_ms", TypeName = "int", Order = 9)]
        [Comment("类别冷却毫秒")]
        public int CategoryCooldownMs { get; set; }
    }

    /// <summary>固定插槽。对应 WoW `socketColor_N/socketContent_N`；固定槽与随机孔概率（gem_slot_rates）并存。</summary>
    [Table("Game_HunduShijie_ItemSocket")]
    [Comment("物品固定插槽表")]
    [EntityStorage("Game")]
    public class ItemSocketTemplateEntity : BaseGameModel<int>
    {
        [Key]
        [Column("entry_id", TypeName = "int", Order = 1)]
        [Comment("条目 id（自增）")]
        public new int Id { get; set; }

        [Column("item_id", TypeName = "int", Order = 2)]
        [Comment("物品模板 id")]
        public int ItemId { get; set; }

        [Column("seq", TypeName = "int", Order = 3)]
        [Comment("插槽顺序 1..3")]
        public int Seq { get; set; }

        /// <summary>0 多彩 1 红 2 黄 3 蓝 4 无色。</summary>
        [Column("socket_color", TypeName = "int", Order = 4)]
        [Comment("插槽颜色 0-4")]
        public int SocketColor { get; set; }

        [Column("socket_bonus", TypeName = "int", Order = 5)]
        [Comment("该槽的插槽奖励 id（0=无）")]
        public int SocketBonus { get; set; }
    }

    /// <summary>
    /// 随机词缀（前缀/后缀）。对应 WoW `RandomProperty/RandomSuffix` 与 `item_enchantment_template`。
    /// <para>属性行在 <see cref="ItemRandomAffixStatEntity"/>；数值带在同一张表上，避免"每档一条词缀"。</para>
    /// </summary>
    [Table("Game_HunduShijie_ItemAffix")]
    [Comment("物品随机词缀表")]
    [EntityStorage("Game")]
    public class ItemRandomAffixEntity : BaseGameModel<int>
    {
        [Key]
        [Column("affix_id", TypeName = "int", Order = 1)]
        [Comment("词缀 id（900001..909999）")]
        public new int Id { get; set; }

        /// <summary>0 前缀 1 后缀。</summary>
        [Column("affix_kind", TypeName = "int", Order = 2)]
        [Comment("0 前缀 1 后缀")]
        public int AffixKind { get; set; }

        [Column("affix_name", TypeName = "nvarchar(50)", Order = 3)]
        [Comment("词缀名（显示在物品名前后）")]
        public string AffixName { get; set; } = string.Empty;

        [Column("description", TypeName = "nvarchar(200)", Order = 4)]
        [Comment("词缀说明")]
        public string Description { get; set; } = string.Empty;

        /// <summary>数值带下界（百分比刻度，属性行里的 value 是权重）。</summary>
        [Column("value_min", TypeName = "int", Order = 5)]
        [Comment("roll 下界")]
        public int ValueMin { get; set; } = 80;

        [Column("value_max", TypeName = "int", Order = 6)]
        [Comment("roll 上界")]
        public int ValueMax { get; set; } = 120;

        [Column("publish_state", TypeName = "tinyint", Order = 7)]
        [Comment("0 草稿 1 已发布 2 停用")]
        public byte PublishState { get; set; }
    }

    /// <summary>词缀的属性行（词缀 × 属性）。Value 是权重（相对值），最终值 = 权重 × roll / 100。</summary>
    [Table("Game_HunduShijie_ItemAffixStat")]
    [Comment("物品随机词缀属性行")]
    [EntityStorage("Game")]
    public class ItemRandomAffixStatEntity : BaseGameModel<int>
    {
        [Key]
        [Column("entry_id", TypeName = "int", Order = 1)]
        [Comment("条目 id（自增）")]
        public new int Id { get; set; }

        [Column("affix_id", TypeName = "int", Order = 2)]
        [Comment("词缀 id")]
        public int AffixId { get; set; }

        [Column("seq", TypeName = "int", Order = 3)]
        [Comment("条目顺序")]
        public int Seq { get; set; }

        [Column("stat_type", TypeName = "int", Order = 4)]
        [Comment("属性类型 1..18")]
        public int StatType { get; set; }

        [Column("stat_value", TypeName = "int", Order = 5)]
        [Comment("权重值（最终值 = 权重 × roll / 100）")]
        public int StatValue { get; set; }
    }

    /// <summary>套装。对应 WoW `itemset` + `ItemSet` DBC。</summary>
    [Table("Game_HunduShijie_ItemSet")]
    [Comment("物品套装表")]
    [EntityStorage("Game")]
    public class ItemSetEntity : BaseGameModel<int>
    {
        [Key]
        [Column("set_id", TypeName = "int", Order = 1)]
        [Comment("套装 id（920000..929999）")]
        public new int Id { get; set; }

        [Column("set_name", TypeName = "nvarchar(50)", Order = 2)]
        [Comment("套装名")]
        public string SetName { get; set; } = string.Empty;

        [Column("description", TypeName = "nvarchar(300)", Order = 3)]
        [Comment("套装说明")]
        public string Description { get; set; } = string.Empty;

        [Column("item_level", TypeName = "int", Order = 4)]
        [Comment("套装物品等级（显示用）")]
        public int ItemLevel { get; set; }

        [Column("publish_state", TypeName = "tinyint", Order = 5)]
        [Comment("0 草稿 1 已发布 2 停用")]
        public byte PublishState { get; set; }
    }

    /// <summary>套装加成档（按已穿戴件数生效）。</summary>
    [Table("Game_HunduShijie_ItemSetBonus")]
    [Comment("物品套装加成档")]
    [EntityStorage("Game")]
    public class ItemSetBonusEntity : BaseGameModel<int>
    {
        [Key]
        [Column("entry_id", TypeName = "int", Order = 1)]
        [Comment("条目 id（自增）")]
        public new int Id { get; set; }

        [Column("set_id", TypeName = "int", Order = 2)]
        [Comment("套装 id")]
        public int SetId { get; set; }

        [Column("pieces", TypeName = "int", Order = 3)]
        [Comment("生效件数 2..8")]
        public int Pieces { get; set; }

        [Column("bonus_description", TypeName = "nvarchar(200)", Order = 4)]
        [Comment("加成说明")]
        public string BonusDescription { get; set; } = string.Empty;

        [Column("spell_id", TypeName = "int", Order = 5)]
        [Comment("关联法术 id（0=纯属性）")]
        public int SpellId { get; set; }
    }

    /// <summary>套装加成档的属性行。</summary>
    [Table("Game_HunduShijie_ItemSetBonusStat")]
    [Comment("物品套装加成属性行")]
    [EntityStorage("Game")]
    public class ItemSetBonusStatEntity : BaseGameModel<int>
    {
        [Key]
        [Column("entry_id", TypeName = "int", Order = 1)]
        [Comment("条目 id（自增）")]
        public new int Id { get; set; }

        [Column("set_id", TypeName = "int", Order = 2)]
        [Comment("套装 id")]
        public int SetId { get; set; }

        [Column("pieces", TypeName = "int", Order = 3)]
        [Comment("所属档位件数")]
        public int Pieces { get; set; }

        [Column("stat_type", TypeName = "int", Order = 4)]
        [Comment("属性类型 1..18")]
        public int StatType { get; set; }

        [Column("stat_value", TypeName = "int", Order = 5)]
        [Comment("属性数值")]
        public int StatValue { get; set; }
    }

    /// <summary>
    /// 结构化出处（物品 × 来源）。`source_detail` JSON 保留为兼容字段，保存时由本表派生，
    /// 因此旧读取方不用改。
    /// </summary>
    [Table("Game_HunduShijie_ItemSource")]
    [Comment("物品出处表")]
    [EntityStorage("Game")]
    public class ItemSourceEntity : BaseGameModel<int>
    {
        [Key]
        [Column("entry_id", TypeName = "int", Order = 1)]
        [Comment("条目 id（自增）")]
        public new int Id { get; set; }

        [Column("item_id", TypeName = "int", Order = 2)]
        [Comment("物品模板 id")]
        public int ItemId { get; set; }

        [Column("seq", TypeName = "int", Order = 3)]
        [Comment("条目顺序")]
        public int Seq { get; set; }

        /// <summary>0 怪物掉落 1 采集 2 任务 3 副本 4 商店 5 合成 6 活动。</summary>
        [Column("source_type", TypeName = "int", Order = 4)]
        [Comment("出处类型 0-6")]
        public int SourceType { get; set; }

        /// <summary>0 怪物 1 掉落表 2 任务 3 NPC 4 物品 5 地图。</summary>
        [Column("ref_type", TypeName = "int", Order = 5)]
        [Comment("引用类型 0-5")]
        public int RefType { get; set; }

        [Column("ref_id", TypeName = "int", Order = 6)]
        [Comment("引用 id")]
        public int RefId { get; set; }

        [Column("note", TypeName = "nvarchar(200)", Order = 7)]
        [Comment("备注（如掉落概率说明）")]
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>物品多语言文本。首批只填 zhCN/enUS，其余语言留空。</summary>
    [Table("Game_HunduShijie_ItemLocalization")]
    [Comment("物品多语言文本表")]
    [EntityStorage("Game")]
    public class ItemLocalizationEntity : BaseGameModel<int>
    {
        [Key]
        [Column("entry_id", TypeName = "int", Order = 1)]
        [Comment("条目 id（自增）")]
        public new int Id { get; set; }

        [Column("item_id", TypeName = "int", Order = 2)]
        [Comment("物品模板 id")]
        public int ItemId { get; set; }

        [Column("locale", TypeName = "varchar(10)", Order = 3)]
        [Comment("语言标识 zhCN/enUS")]
        public string Locale { get; set; } = "zhCN";

        [Column("item_name", TypeName = "nvarchar(50)", Order = 4)]
        [Comment("本地化名称")]
        public string ItemName { get; set; } = string.Empty;

        [Column("description", TypeName = "nvarchar(500)", Order = 5)]
        [Comment("本地化描述")]
        public string Description { get; set; } = string.Empty;
    }
}
