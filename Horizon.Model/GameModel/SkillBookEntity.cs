using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 技能秘籍实体
    /// </summary>
    [Table("Game_HunduShijie_SkillBook"), TableDescription(Name = "Game_HunduShijie_SkillBook", Order = "HunduShijie_022", Description = "技能秘籍信息")]
    [Comment("技能秘籍表")]
    [EntityStorage("Game")]
    public class SkillBookEntity : BaseGameModel<int>
    {
        /// <summary>
        /// 秘籍ID
        /// </summary>
        [Key]
        [Column("book_id", TypeName = "int", Order = 1), TableDescription(TypeName = "int", Name = "book_id", Order = "1", Description = "秘籍ID")]
        [Comment("秘籍ID")]
        public new int Id { get; set; }
        
        /// <summary>
        /// 秘籍名称
        /// </summary>
        [Required]
        [Column("book_name", TypeName = "nvarchar(50)", Order = 2), TableDescription(TypeName = "nvarchar(50)", Name = "book_name", Order = "2", Description = "秘籍名称")]
        [Comment("秘籍名称")]
        public string BookName { get; set; }
        
        /// <summary>
        /// 秘籍描述
        /// </summary>
        [Column("description", TypeName = "nvarchar(500)", Order = 3), TableDescription(TypeName = "nvarchar(500)", Name = "description", Order = "3", Description = "秘籍描述")]
        [Comment("秘籍描述")]
        public string Description { get; set; }
        
        /// <summary>
        /// 技能ID
        /// </summary>
        [Column("skill_id", TypeName = "int", Order = 4), TableDescription(TypeName = "int", Name = "skill_id", Order = "4", Description = "技能ID")]
        [Comment("对应技能ID")]
        public int SkillId { get; set; }
        
        /// <summary>
        /// 秘籍品质
        /// </summary>
        [Column("quality", TypeName = "int", Order = 5), TableDescription(TypeName = "int", Name = "quality", Order = "5", Description = "秘籍品质")]
        [Comment("秘籍品质 0-普通 1-精良 2-稀有 3-史诗 4-传说 5-神话")]
        public int Quality { get; set; }
        
        /// <summary>
        /// 秘籍类型
        /// </summary>
        [Column("book_type", TypeName = "int", Order = 6), TableDescription(TypeName = "int", Name = "book_type", Order = "6", Description = "秘籍类型")]
        [Comment("秘籍类型 0-完整秘籍 1-残卷 2-心得 3-传承玉简")]
        public int BookType { get; set; }
        
        /// <summary>
        /// 学习等级需求
        /// </summary>
        [Column("level_require", TypeName = "int", Order = 7), TableDescription(TypeName = "int", Name = "level_require", Order = "7", Description = "学习等级需求")]
        [Comment("学习等级需求")]
        public int LevelRequire { get; set; }
        
        /// <summary>
        /// 悟性需求
        /// </summary>
        [Column("comprehension_require", TypeName = "int", Order = 8), TableDescription(TypeName = "int", Name = "comprehension_require", Order = "8", Description = "悟性需求")]
        [Comment("悟性需求")]
        public int ComprehensionRequire { get; set; }
        
        /// <summary>
        /// 学习时长
        /// </summary>
        [Column("learn_duration", TypeName = "int", Order = 9), TableDescription(TypeName = "int", Name = "learn_duration", Order = "9", Description = "学习时长")]
        [Comment("学习时长（分钟）")]
        public int LearnDuration { get; set; }
        
        /// <summary>
        /// 学习成功率
        /// </summary>
        [Column("learn_success_rate", TypeName = "float", Order = 10), TableDescription(TypeName = "float", Name = "learn_success_rate", Order = "10", Description = "学习成功率")]
        [Comment("基础学习成功率")]
        public float LearnSuccessRate { get; set; }
        
        /// <summary>
        /// 可学习技能等级
        /// </summary>
        [Column("max_skill_level", TypeName = "int", Order = 11), TableDescription(TypeName = "int", Name = "max_skill_level", Order = "11", Description = "可学习技能等级")]
        [Comment("可学习到的最高技能等级")]
        public int MaxSkillLevel { get; set; }
        
        /// <summary>
        /// 使用次数限制
        /// </summary>
        [Column("use_limit", TypeName = "int", Order = 12), TableDescription(TypeName = "int", Name = "use_limit", Order = "12", Description = "使用次数限制")]
        [Comment("使用次数限制（0为无限）")]
        public int UseLimit { get; set; }
        
        /// <summary>
        /// 额外效果
        /// </summary>
        [Column("extra_effects", TypeName = "nvarchar(500)", Order = 13), TableDescription(TypeName = "nvarchar(500)", Name = "extra_effects", Order = "13", Description = "额外效果")]
        [Comment("额外效果（JSON格式）")]
        public string ExtraEffects { get; set; }
        
        /// <summary>
        /// 出处说明
        /// </summary>
        [Column("source_info", TypeName = "nvarchar(500)", Order = 14), TableDescription(TypeName = "nvarchar(500)", Name = "source_info", Order = "14", Description = "出处说明")]
        [Comment("出处说明")]
        public string SourceInfo { get; set; }
        
        /// <summary>
        /// 创建时间
        /// </summary>
        [Column("create_time", TypeName = "datetime", Order = 15), TableDescription(TypeName = "datetime", Name = "create_time", Order = "15", Description = "创建时间")]
        [Comment("创建时间")]
        public DateTime CreateTime { get; set; }
    }
}
