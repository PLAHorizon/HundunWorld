using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 自创技能实体
    /// </summary>
    [Table("Game_HunduShijie_SkillCustomCreate"), TableDescription(Name = "Game_HunduShijie_SkillCustomCreate", Order = "HunduShijie_021", Description = "自创技能信息")]
    [Comment("自创技能表")]
    [EntityStorage("Game")]
    public class SkillCustomCreateEntity : BaseGameModel<long>
    {
        /// <summary>
        /// 自创技能ID
        /// </summary>
        [Key]
        [Column("custom_skill_id", TypeName = "bigint", Order = 1), TableDescription(TypeName = "bigint", Name = "custom_skill_id", Order = "1", Description = "自创技能ID")]
        [Comment("自创技能ID")]
        public new long Id { get; set; }
        
        /// <summary>
        /// 创建者ID
        /// </summary>
        [Column("creator_id", TypeName = "bigint", Order = 2), TableDescription(TypeName = "bigint", Name = "creator_id", Order = "2", Description = "创建者ID")]
        [Comment("创建者角色ID")]
        public long CreatorId { get; set; }
        
        /// <summary>
        /// 创建者名称
        /// </summary>
        [Column("creator_name", TypeName = "nvarchar(20)", Order = 3), TableDescription(TypeName = "nvarchar(20)", Name = "creator_name", Order = "3", Description = "创建者名称")]
        [Comment("创建者角色名")]
        public string CreatorName { get; set; }
        
        /// <summary>
        /// 技能名称
        /// </summary>
        [Required]
        [Column("skill_name", TypeName = "nvarchar(50)", Order = 4), TableDescription(TypeName = "nvarchar(50)", Name = "skill_name", Order = "4", Description = "技能名称")]
        [Comment("自创技能名称")]
        public string SkillName { get; set; }
        
        /// <summary>
        /// 技能描述
        /// </summary>
        [Column("description", TypeName = "nvarchar(500)", Order = 5), TableDescription(TypeName = "nvarchar(500)", Name = "description", Order = "5", Description = "技能描述")]
        [Comment("技能描述")]
        public string Description { get; set; }
        
        /// <summary>
        /// 基础技能ID
        /// </summary>
        [Column("base_skill_id", TypeName = "int", Order = 6), TableDescription(TypeName = "int", Name = "base_skill_id", Order = "6", Description = "基础技能ID")]
        [Comment("基础技能模板ID")]
        public int BaseSkillId { get; set; }
        
        /// <summary>
        /// 融合技能
        /// </summary>
        [Column("fusion_skills", TypeName = "varchar(200)", Order = 7), TableDescription(TypeName = "varchar(200)", Name = "fusion_skills", Order = "7", Description = "融合技能")]
        [Comment("融合技能ID列表（JSON格式）")]
        public string FusionSkills { get; set; }
        
        /// <summary>
        /// 创造类型
        /// </summary>
        [Column("create_type", TypeName = "int", Order = 8), TableDescription(TypeName = "int", Name = "create_type", Order = "8", Description = "创造类型")]
        [Comment("创造类型 0-改良 1-融合 2-顿悟 3-传承")]
        public int CreateType { get; set; }
        
        /// <summary>
        /// 技能效果
        /// </summary>
        [Column("skill_effects", TypeName = "nvarchar(2000)", Order = 9), TableDescription(TypeName = "nvarchar(2000)", Name = "skill_effects", Order = "9", Description = "技能效果")]
        [Comment("技能效果（JSON格式）")]
        public string SkillEffects { get; set; }
        
        /// <summary>
        /// 内力消耗
        /// </summary>
        [Column("energy_cost", TypeName = "int", Order = 10), TableDescription(TypeName = "int", Name = "energy_cost", Order = "10", Description = "内力消耗")]
        [Comment("内力消耗")]
        public int EnergyCost { get; set; }
        
        /// <summary>
        /// 冷却时间
        /// </summary>
        [Column("cooldown", TypeName = "int", Order = 11), TableDescription(TypeName = "int", Name = "cooldown", Order = "11", Description = "冷却时间")]
        [Comment("冷却时间（毫秒）")]
        public int Cooldown { get; set; }
        
        /// <summary>
        /// 威力系数
        /// </summary>
        [Column("power_factor", TypeName = "float", Order = 12), TableDescription(TypeName = "float", Name = "power_factor", Order = "12", Description = "威力系数")]
        [Comment("威力系数")]
        public float PowerFactor { get; set; }
        
        /// <summary>
        /// 创新度
        /// </summary>
        [Column("innovation_rate", TypeName = "float", Order = 13), TableDescription(TypeName = "float", Name = "innovation_rate", Order = "13", Description = "创新度")]
        [Comment("创新度评分（0-100）")]
        public float InnovationRate { get; set; }
        
        /// <summary>
        /// 完成度
        /// </summary>
        [Column("completion_rate", TypeName = "float", Order = 14), TableDescription(TypeName = "float", Name = "completion_rate", Order = "14", Description = "完成度")]
        [Comment("完成度（0-100）")]
        public float CompletionRate { get; set; }
        
        /// <summary>
        /// 传承次数
        /// </summary>
        [Column("inherit_count", TypeName = "int", Order = 15), TableDescription(TypeName = "int", Name = "inherit_count", Order = "15", Description = "传承次数")]
        [Comment("被传承次数")]
        public int InheritCount { get; set; }
        
        /// <summary>
        /// 评价分数
        /// </summary>
        [Column("rating_score", TypeName = "float", Order = 16), TableDescription(TypeName = "float", Name = "rating_score", Order = "16", Description = "评价分数")]
        [Comment("玩家评价分数")]
        public float RatingScore { get; set; }
        
        /// <summary>
        /// 评价人数
        /// </summary>
        [Column("rating_count", TypeName = "int", Order = 17), TableDescription(TypeName = "int", Name = "rating_count", Order = "17", Description = "评价人数")]
        [Comment("评价人数")]
        public int RatingCount { get; set; }
        
        /// <summary>
        /// 是否公开
        /// </summary>
        [Column("is_public", TypeName = "bit", Order = 18), TableDescription(TypeName = "bit", Name = "is_public", Order = "18", Description = "是否公开")]
        [Comment("是否公开")]
        public bool IsPublic { get; set; }
        
        /// <summary>
        /// 传承价格
        /// </summary>
        [Column("inherit_price", TypeName = "int", Order = 19), TableDescription(TypeName = "int", Name = "inherit_price", Order = "19", Description = "传承价格")]
        [Comment("传承价格（元宝）")]
        public int InheritPrice { get; set; }
        
        /// <summary>
        /// 创建时间
        /// </summary>
        [Column("create_time", TypeName = "datetime", Order = 20), TableDescription(TypeName = "datetime", Name = "create_time", Order = "20", Description = "创建时间")]
        [Comment("创建时间")]
        public DateTime CreateTime { get; set; }
        
        /// <summary>
        /// 更新时间
        /// </summary>
        [Column("update_time", TypeName = "datetime", Order = 21), TableDescription(TypeName = "datetime", Name = "update_time", Order = "21", Description = "更新时间")]
        [Comment("更新时间")]
        public DateTime UpdateTime { get; set; }
    }
}
