using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 角色技能实体
    /// </summary>
    [Table("Game_HunduShijie_CharacterSkill"), TableDescription(Name = "Game_HunduShijie_CharacterSkill", Order = "HunduShijie_019", Description = "角色技能信息")]
    [Comment("角色技能表")]
    [EntityStorage("Game")]
    public class CharacterSkillEntity : BaseGameModel<long>
    {
        /// <summary>
        /// 自增ID
        /// </summary>
        [Key]
        [Column("id", TypeName = "bigint", Order = 1), TableDescription(TypeName = "bigint", Name = "id", Order = "1", Description = "自增ID")]
        [Comment("自增ID")]
        public new long Id { get; set; }
        
        /// <summary>
        /// 角色ID
        /// </summary>
        [Column("character_id", TypeName = "bigint", Order = 2), TableDescription(TypeName = "bigint", Name = "character_id", Order = "2", Description = "角色ID")]
        [Comment("角色ID")]
        public ulong CharacterId { get; set; }
        
        /// <summary>
        /// 技能ID
        /// </summary>
        [Column("skill_id", TypeName = "int", Order = 3), TableDescription(TypeName = "int", Name = "skill_id", Order = "3", Description = "技能ID")]
        [Comment("技能ID")]
        public int SkillId { get; set; }
        
        /// <summary>
        /// 技能等级
        /// </summary>
        [Column("skill_level", TypeName = "int", Order = 4), TableDescription(TypeName = "int", Name = "skill_level", Order = "4", Description = "技能等级")]
        [Comment("技能等级")]
        public int SkillLevel { get; set; }
        
        /// <summary>
        /// 当前熟练度
        /// </summary>
        [Column("proficiency", TypeName = "bigint", Order = 5), TableDescription(TypeName = "bigint", Name = "proficiency", Order = "5", Description = "当前熟练度")]
        [Comment("当前熟练度")]
        public long Proficiency { get; set; }
        
        /// <summary>
        /// 升级所需熟练度
        /// </summary>
        [Column("max_proficiency", TypeName = "bigint", Order = 6), TableDescription(TypeName = "bigint", Name = "max_proficiency", Order = "6", Description = "升级所需熟练度")]
        [Comment("升级所需熟练度")]
        public long MaxProficiency { get; set; }
        
        /// <summary>
        /// 技能境界
        /// </summary>
        [Column("skill_realm", TypeName = "int", Order = 7), TableDescription(TypeName = "int", Name = "skill_realm", Order = "7", Description = "技能境界")]
        [Comment("技能境界 0-初窥门径 1-登堂入室 2-融会贯通 3-炉火纯青 4-登峰造极")]
        public int SkillRealm { get; set; }
        
        /// <summary>
        /// 领悟度
        /// </summary>
        [Column("comprehension_rate", TypeName = "float", Order = 8), TableDescription(TypeName = "float", Name = "comprehension_rate", Order = "8", Description = "领悟度")]
        [Comment("领悟度（0-100）")]
        public float ComprehensionRate { get; set; }
        
        /// <summary>
        /// 是否装备
        /// </summary>
        [Column("is_equipped", TypeName = "bit", Order = 9), TableDescription(TypeName = "bit", Name = "is_equipped", Order = "9", Description = "是否装备")]
        [Comment("是否装备到快捷栏")]
        public bool IsEquipped { get; set; }
        
        /// <summary>
        /// 快捷栏位置
        /// </summary>
        [Column("slot_index", TypeName = "int", Order = 10), TableDescription(TypeName = "int", Name = "slot_index", Order = "10", Description = "快捷栏位置")]
        [Comment("快捷栏位置")]
        public int? SlotIndex { get; set; }
        
        /// <summary>
        /// 学习来源
        /// </summary>
        [Column("learn_source", TypeName = "int", Order = 11), TableDescription(TypeName = "int", Name = "learn_source", Order = "11", Description = "学习来源")]
        [Comment("学习来源 0-门派传授 1-秘籍学习 2-自创 3-传功 4-顿悟")]
        public int LearnSource { get; set; }
        
        /// <summary>
        /// 学习时间
        /// </summary>
        [Column("learn_time", TypeName = "datetime", Order = 12), TableDescription(TypeName = "datetime", Name = "learn_time", Order = "12", Description = "学习时间")]
        [Comment("学习时间")]
        public DateTime LearnTime { get; set; }
        
        /// <summary>
        /// 最后使用时间
        /// </summary>
        [Column("last_use_time", TypeName = "datetime", Order = 13), TableDescription(TypeName = "datetime", Name = "last_use_time", Order = "13", Description = "最后使用时间")]
        [Comment("最后使用时间")]
        public DateTime? LastUseTime { get; set; }
        
        /// <summary>
        /// 使用次数
        /// </summary>
        [Column("use_count", TypeName = "bigint", Order = 14), TableDescription(TypeName = "bigint", Name = "use_count", Order = "14", Description = "使用次数")]
        [Comment("使用次数")]
        public long UseCount { get; set; }
        
        /// <summary>
        /// 是否自创
        /// </summary>
        [Column("is_custom", TypeName = "bit", Order = 15), TableDescription(TypeName = "bit", Name = "is_custom", Order = "15", Description = "是否自创")]
        [Comment("是否自创技能")]
        public bool IsCustom { get; set; }
        
        /// <summary>
        /// 自创名称
        /// </summary>
        [Column("custom_name", TypeName = "nvarchar(50)", Order = 16), TableDescription(TypeName = "nvarchar(50)", Name = "custom_name", Order = "16", Description = "自创名称")]
        [Comment("自创技能名称")]
        public string CustomName { get; set; }
        
        /// <summary>
        /// 技能加成
        /// </summary>
        [Column("skill_bonus", TypeName = "nvarchar(500)", Order = 17), TableDescription(TypeName = "nvarchar(500)", Name = "skill_bonus", Order = "17", Description = "技能加成")]
        [Comment("技能加成（JSON格式）")]
        public string SkillBonus { get; set; }
        
        /// <summary>
        /// 是否锁定
        /// </summary>
        [Column("is_locked", TypeName = "bit", Order = 18), TableDescription(TypeName = "bit", Name = "is_locked", Order = "18", Description = "是否锁定")]
        [Comment("是否锁定（防止误操作）")]
        public bool IsLocked { get; set; }
        
        /// <summary>
        /// 更新时间
        /// </summary>
        [Column("update_time", TypeName = "datetime", Order = 19), TableDescription(TypeName = "datetime", Name = "update_time", Order = "19", Description = "更新时间")]
        [Comment("更新时间")]
        public DateTime UpdateTime { get; set; }
    }
}
