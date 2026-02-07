using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 技能进阶路径实体
    /// </summary>
    [Table("Game_HunduShijie_SkillAdvancePath"), TableDescription(Name = "Game_HunduShijie_SkillAdvancePath", Order = "HunduShijie_020", Description = "技能进阶路径")]
    [Comment("技能进阶路径表")]
    [EntityStorage("Game")]
    public class SkillAdvancePathEntity : BaseGameModel<int>
    {
        /// <summary>
        /// 路径ID
        /// </summary>
        [Key]
        [Column("path_id", TypeName = "int", Order = 1), TableDescription(TypeName = "int", Name = "path_id", Order = "1", Description = "路径ID")]
        [Comment("路径ID")]
        public new int Id { get; set; }
        
        /// <summary>
        /// 基础技能ID
        /// </summary>
        [Column("base_skill_id", TypeName = "int", Order = 2), TableDescription(TypeName = "int", Name = "base_skill_id", Order = "2", Description = "基础技能ID")]
        [Comment("基础技能ID")]
        public int BaseSkillId { get; set; }
        
        /// <summary>
        /// 进阶技能ID
        /// </summary>
        [Column("advance_skill_id", TypeName = "int", Order = 3), TableDescription(TypeName = "int", Name = "advance_skill_id", Order = "3", Description = "进阶技能ID")]
        [Comment("进阶技能ID")]
        public int AdvanceSkillId { get; set; }
        
        /// <summary>
        /// 路径名称
        /// </summary>
        [Column("path_name", TypeName = "nvarchar(50)", Order = 4), TableDescription(TypeName = "nvarchar(50)", Name = "path_name", Order = "4", Description = "路径名称")]
        [Comment("路径名称")]
        public string PathName { get; set; }
        
        /// <summary>
        /// 路径描述
        /// </summary>
        [Column("description", TypeName = "nvarchar(500)", Order = 5), TableDescription(TypeName = "nvarchar(500)", Name = "description", Order = "5", Description = "路径描述")]
        [Comment("路径描述")]
        public string Description { get; set; }
        
        /// <summary>
        /// 进阶类型
        /// </summary>
        [Column("advance_type", TypeName = "int", Order = 6), TableDescription(TypeName = "int", Name = "advance_type", Order = "6", Description = "进阶类型")]
        [Comment("进阶类型 0-正统进阶 1-变异进阶 2-融合进阶 3-顿悟进阶")]
        public int AdvanceType { get; set; }
        
        /// <summary>
        /// 技能等级要求
        /// </summary>
        [Column("skill_level_require", TypeName = "int", Order = 7), TableDescription(TypeName = "int", Name = "skill_level_require", Order = "7", Description = "技能等级要求")]
        [Comment("基础技能等级要求")]
        public int SkillLevelRequire { get; set; }
        
        /// <summary>
        /// 技能境界要求
        /// </summary>
        [Column("skill_realm_require", TypeName = "int", Order = 8), TableDescription(TypeName = "int", Name = "skill_realm_require", Order = "8", Description = "技能境界要求")]
        [Comment("技能境界要求")]
        public int SkillRealmRequire { get; set; }
        
        /// <summary>
        /// 角色等级要求
        /// </summary>
        [Column("level_require", TypeName = "int", Order = 9), TableDescription(TypeName = "int", Name = "level_require", Order = "9", Description = "角色等级要求")]
        [Comment("角色等级要求")]
        public int LevelRequire { get; set; }
        
        /// <summary>
        /// 境界要求
        /// </summary>
        [Column("realm_require", TypeName = "int", Order = 10), TableDescription(TypeName = "int", Name = "realm_require", Order = "10", Description = "境界要求")]
        [Comment("境界要求")]
        public int RealmRequire { get; set; }
        
        /// <summary>
        /// 悟性要求
        /// </summary>
        [Column("comprehension_require", TypeName = "int", Order = 11), TableDescription(TypeName = "int", Name = "comprehension_require", Order = "11", Description = "悟性要求")]
        [Comment("悟性要求")]
        public int ComprehensionRequire { get; set; }
        
        /// <summary>
        /// 材料需求
        /// </summary>
        [Column("material_require", TypeName = "nvarchar(1000)", Order = 12), TableDescription(TypeName = "nvarchar(1000)", Name = "material_require", Order = "12", Description = "材料需求")]
        [Comment("材料需求（JSON格式）")]
        public string MaterialRequire { get; set; }
        
        /// <summary>
        /// 货币消耗
        /// </summary>
        [Column("currency_cost", TypeName = "nvarchar(500)", Order = 13), TableDescription(TypeName = "nvarchar(500)", Name = "currency_cost", Order = "13", Description = "货币消耗")]
        [Comment("货币消耗（JSON格式）")]
        public string CurrencyCost { get; set; }
        
        /// <summary>
        /// 成功率
        /// </summary>
        [Column("success_rate", TypeName = "float", Order = 14), TableDescription(TypeName = "float", Name = "success_rate", Order = "14", Description = "成功率")]
        [Comment("基础成功率（0-100）")]
        public float SuccessRate { get; set; }
        
        /// <summary>
        /// 辅助技能
        /// </summary>
        [Column("assist_skills", TypeName = "varchar(200)", Order = 15), TableDescription(TypeName = "varchar(200)", Name = "assist_skills", Order = "15", Description = "辅助技能")]
        [Comment("辅助技能ID（逗号分隔，可提高成功率）")]
        public string AssistSkills { get; set; }
        
        /// <summary>
        /// 特殊条件
        /// </summary>
        [Column("special_condition", TypeName = "nvarchar(500)", Order = 16), TableDescription(TypeName = "nvarchar(500)", Name = "special_condition", Order = "16", Description = "特殊条件")]
        [Comment("特殊条件（JSON格式）")]
        public string SpecialCondition { get; set; }
        
        /// <summary>
        /// 失败惩罚
        /// </summary>
        [Column("fail_penalty", TypeName = "nvarchar(500)", Order = 17), TableDescription(TypeName = "nvarchar(500)", Name = "fail_penalty", Order = "17", Description = "失败惩罚")]
        [Comment("失败惩罚（JSON格式）")]
        public string FailPenalty { get; set; }
        
        /// <summary>
        /// 是否可重复
        /// </summary>
        [Column("is_repeatable", TypeName = "bit", Order = 18), TableDescription(TypeName = "bit", Name = "is_repeatable", Order = "18", Description = "是否可重复")]
        [Comment("是否可重复进阶")]
        public bool IsRepeatable { get; set; }
        
        /// <summary>
        /// 创建时间
        /// </summary>
        [Column("create_time", TypeName = "datetime", Order = 19), TableDescription(TypeName = "datetime", Name = "create_time", Order = "19", Description = "创建时间")]
        [Comment("创建时间")]
        public DateTime CreateTime { get; set; }
    }
}
