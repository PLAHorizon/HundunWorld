using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 技能模板实体
    /// </summary>
    [Table("Game_HunduShijie_SkillTemplate"), TableDescription(Name = "Game_HunduShijie_SkillTemplate", Order = "HunduShijie_018", Description = "技能模板信息")]
    [Comment("技能模板表")]
    [EntityStorage("Game")]
    public class SkillTemplateEntity : BaseGameModel<int>
    {
        /// <summary>
        /// 技能ID
        /// </summary>
        [Key]
        [Column("skill_id", TypeName = "int", Order = 1), TableDescription(TypeName = "int", Name = "skill_id", Order = "1", Description = "技能ID")]
        [Comment("技能ID")]
        public new int Id { get; set; }
        
        /// <summary>
        /// 技能名称
        /// </summary>
        [Required]
        [Column("skill_name", TypeName = "nvarchar(50)", Order = 2), TableDescription(TypeName = "nvarchar(50)", Name = "skill_name", Order = "2", Description = "技能名称")]
        [Comment("技能名称")]
        public string SkillName { get; set; }
        
        /// <summary>
        /// 技能描述
        /// </summary>
        [Column("description", TypeName = "nvarchar(500)", Order = 3), TableDescription(TypeName = "nvarchar(500)", Name = "description", Order = "3", Description = "技能描述")]
        [Comment("技能描述")]
        public string Description { get; set; }
        
        /// <summary>
        /// 技能类型
        /// </summary>
        [Column("skill_type", TypeName = "int", Order = 4), TableDescription(TypeName = "int", Name = "skill_type", Order = "4", Description = "技能类型")]
        [Comment("技能类型 0-主动攻击 1-主动辅助 2-被动增益 3-心法 4-轻功 5-内功 6-绝学")]
        public int SkillType { get; set; }
        
        /// <summary>
        /// 技能流派
        /// </summary>
        [Column("skill_school", TypeName = "int", Order = 5), TableDescription(TypeName = "int", Name = "skill_school", Order = "5", Description = "技能流派")]
        [Comment("技能流派 0-外功 1-内功 2-医术 3-毒术 4-音律 5-机关")]
        public int SkillSchool { get; set; }
        
        /// <summary>
        /// 所属门派
        /// </summary>
        [Column("sect_id", TypeName = "int", Order = 6), TableDescription(TypeName = "int", Name = "sect_id", Order = "6", Description = "所属门派")]
        [Comment("所属门派ID（0表示通用）")]
        public int SectId { get; set; }
        
        /// <summary>
        /// 职业限制
        /// </summary>
        [Column("profession_limit", TypeName = "varchar(50)", Order = 7), TableDescription(TypeName = "varchar(50)", Name = "profession_limit", Order = "7", Description = "职业限制")]
        [Comment("职业限制（逗号分隔的职业ID）")]
        public string ProfessionLimit { get; set; }
        
        /// <summary>
        /// 最大等级
        /// </summary>
        [Column("max_level", TypeName = "int", Order = 8), TableDescription(TypeName = "int", Name = "max_level", Order = "8", Description = "最大等级")]
        [Comment("最大等级")]
        public int MaxLevel { get; set; }
        
        /// <summary>
        /// 学习等级需求
        /// </summary>
        [Column("learn_level_require", TypeName = "int", Order = 9), TableDescription(TypeName = "int", Name = "learn_level_require", Order = "9", Description = "学习等级需求")]
        [Comment("学习等级需求")]
        public int LearnLevelRequire { get; set; }
        
        /// <summary>
        /// 学习境界需求
        /// </summary>
        [Column("learn_realm_require", TypeName = "int", Order = 10), TableDescription(TypeName = "int", Name = "learn_realm_require", Order = "10", Description = "学习境界需求")]
        [Comment("学习境界需求")]
        public int LearnRealmRequire { get; set; }
        
        /// <summary>
        /// 前置技能ID
        /// </summary>
        [Column("pre_skill_id", TypeName = "int", Order = 11), TableDescription(TypeName = "int", Name = "pre_skill_id", Order = "11", Description = "前置技能ID")]
        [Comment("前置技能ID")]
        public int? PreSkillId { get; set; }
        
        /// <summary>
        /// 前置技能等级
        /// </summary>
        [Column("pre_skill_level", TypeName = "int", Order = 12), TableDescription(TypeName = "int", Name = "pre_skill_level", Order = "12", Description = "前置技能等级")]
        [Comment("前置技能等级")]
        public int? PreSkillLevel { get; set; }
        
        /// <summary>
        /// 学习消耗
        /// </summary>
        [Column("learn_cost", TypeName = "nvarchar(500)", Order = 13), TableDescription(TypeName = "nvarchar(500)", Name = "learn_cost", Order = "13", Description = "学习消耗")]
        [Comment("学习消耗（JSON格式，包含货币、物品等）")]
        public string LearnCost { get; set; }
        
        /// <summary>
        /// 悟性需求
        /// </summary>
        [Column("comprehension_require", TypeName = "int", Order = 14), TableDescription(TypeName = "int", Name = "comprehension_require", Order = "14", Description = "悟性需求")]
        [Comment("悟性需求")]
        public int ComprehensionRequire { get; set; }
        
        /// <summary>
        /// 内力消耗公式
        /// </summary>
        [Column("energy_cost_formula", TypeName = "varchar(200)", Order = 15), TableDescription(TypeName = "varchar(200)", Name = "energy_cost_formula", Order = "15", Description = "内力消耗公式")]
        [Comment("内力消耗公式")]
        public string EnergyCostFormula { get; set; }
        
        /// <summary>
        /// 冷却时间公式
        /// </summary>
        [Column("cooldown_formula", TypeName = "varchar(200)", Order = 16), TableDescription(TypeName = "varchar(200)", Name = "cooldown_formula", Order = "16", Description = "冷却时间公式")]
        [Comment("冷却时间公式（毫秒）")]
        public string CooldownFormula { get; set; }
        
        /// <summary>
        /// 施法时间公式
        /// </summary>
        [Column("cast_time_formula", TypeName = "varchar(200)", Order = 17), TableDescription(TypeName = "varchar(200)", Name = "cast_time_formula", Order = "17", Description = "施法时间公式")]
        [Comment("施法时间公式（毫秒）")]
        public string CastTimeFormula { get; set; }
        
        /// <summary>
        /// 攻击范围
        /// </summary>
        [Column("attack_range", TypeName = "float", Order = 18), TableDescription(TypeName = "float", Name = "attack_range", Order = "18", Description = "攻击范围")]
        [Comment("攻击范围")]
        public float AttackRange { get; set; }
        
        /// <summary>
        /// 目标类型
        /// </summary>
        [Column("target_type", TypeName = "int", Order = 19), TableDescription(TypeName = "int", Name = "target_type", Order = "19", Description = "目标类型")]
        [Comment("目标类型 0-自己 1-单体敌人 2-群体敌人 3-单体友方 4-群体友方 5-地面")]
        public int TargetType { get; set; }
        
        /// <summary>
        /// 效果范围
        /// </summary>
        [Column("effect_range", TypeName = "float", Order = 20), TableDescription(TypeName = "float", Name = "effect_range", Order = "20", Description = "效果范围")]
        [Comment("效果范围（AOE技能）")]
        public float EffectRange { get; set; }
        
        /// <summary>
        /// 最大目标数
        /// </summary>
        [Column("max_targets", TypeName = "int", Order = 21), TableDescription(TypeName = "int", Name = "max_targets", Order = "21", Description = "最大目标数")]
        [Comment("最大目标数")]
        public int MaxTargets { get; set; }
        
        /// <summary>
        /// 技能效果
        /// </summary>
        [Column("skill_effects", TypeName = "nvarchar(2000)", Order = 22), TableDescription(TypeName = "nvarchar(2000)", Name = "skill_effects", Order = "22", Description = "技能效果")]
        [Comment("技能效果（JSON格式）")]
        public string SkillEffects { get; set; }
        
        /// <summary>
        /// 升级效果
        /// </summary>
        [Column("level_effects", TypeName = "nvarchar(2000)", Order = 23), TableDescription(TypeName = "nvarchar(2000)", Name = "level_effects", Order = "23", Description = "升级效果")]
        [Comment("升级效果（JSON格式）")]
        public string LevelEffects { get; set; }
        
        /// <summary>
        /// 连招技能ID
        /// </summary>
        [Column("combo_skill_ids", TypeName = "varchar(100)", Order = 24), TableDescription(TypeName = "varchar(100)", Name = "combo_skill_ids", Order = "24", Description = "连招技能ID")]
        [Comment("连招技能ID（逗号分隔）")]
        public string ComboSkillIds { get; set; }
        
        /// <summary>
        /// 是否可自创
        /// </summary>
        [Column("can_create", TypeName = "bit", Order = 25), TableDescription(TypeName = "bit", Name = "can_create", Order = "25", Description = "是否可自创")]
        [Comment("是否可自创")]
        public bool CanCreate { get; set; }
        
        /// <summary>
        /// 是否可进阶
        /// </summary>
        [Column("can_advance", TypeName = "bit", Order = 26), TableDescription(TypeName = "bit", Name = "can_advance", Order = "26", Description = "是否可进阶")]
        [Comment("是否可进阶")]
        public bool CanAdvance { get; set; }
        
        /// <summary>
        /// 进阶技能ID
        /// </summary>
        [Column("advance_skill_id", TypeName = "int", Order = 27), TableDescription(TypeName = "int", Name = "advance_skill_id", Order = "27", Description = "进阶技能ID")]
        [Comment("进阶技能ID")]
        public int? AdvanceSkillId { get; set; }
        
        /// <summary>
        /// 图标路径
        /// </summary>
        [Column("icon_path", TypeName = "varchar(200)", Order = 28), TableDescription(TypeName = "varchar(200)", Name = "icon_path", Order = "28", Description = "图标路径")]
        [Comment("图标路径")]
        public string IconPath { get; set; }
        
        /// <summary>
        /// 特效ID
        /// </summary>
        [Column("effect_id", TypeName = "int", Order = 29), TableDescription(TypeName = "int", Name = "effect_id", Order = "29", Description = "特效ID")]
        [Comment("特效ID")]
        public int EffectId { get; set; }
        
        /// <summary>
        /// 音效ID
        /// </summary>
        [Column("sound_id", TypeName = "int", Order = 30), TableDescription(TypeName = "int", Name = "sound_id", Order = "30", Description = "音效ID")]
        [Comment("音效ID")]
        public int SoundId { get; set; }
        
        /// <summary>
        /// 创建时间
        /// </summary>
        [Column("create_time", TypeName = "datetime", Order = 31), TableDescription(TypeName = "datetime", Name = "create_time", Order = "31", Description = "创建时间")]
        [Comment("创建时间")]
        public DateTime CreateTime { get; set; }
        
        /// <summary>
        /// 更新时间
        /// </summary>
        [Column("update_time", TypeName = "datetime", Order = 32), TableDescription(TypeName = "datetime", Name = "update_time", Order = "32", Description = "更新时间")]
        [Comment("更新时间")]
        public DateTime UpdateTime { get; set; }
    }
}
