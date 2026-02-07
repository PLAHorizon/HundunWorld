using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 角色称号实体
    /// </summary>
    [Table("Game_HunduShijie_CharacterTitle"), TableDescription(Name = "Game_HunduShijie_CharacterTitle", Order = "HunduShijie_008", Description = "角色称号信息")]
    [Comment("角色称号表")]
    [EntityStorage("Game")]
    public class TitleEntity : BaseGameModel<long>
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
        public long CharacterId { get; set; }

        /// <summary>
        /// 称号名称
        /// </summary>
        [StringLength(50)]
        [Column("title_name", TypeName = "nvarchar(50)", Order = 3), TableDescription(TypeName = "nvarchar(50)", Name = "title_name", Order = "3", Description = "称号名称")]
        [Comment("称号名称")]
        public string TitleName { get; set; }

        /// <summary>
        /// 获得条件描述
        /// </summary>
        [StringLength(200)]
        [Column("acquire_condition", TypeName = "nvarchar(200)", Order = 4), TableDescription(TypeName = "nvarchar(200)", Name = "acquire_condition", Order = "4", Description = "获得条件描述")]
        [Comment("获得条件描述")]
        public string AcquireCondition { get; set; }

        /// <summary>
        /// 属性加成（JSON格式）
        /// </summary>
        [Column("attribute_bonus", TypeName = "nvarchar(max)", Order = 5), TableDescription(TypeName = "nvarchar(max)", Name = "attribute_bonus", Order = "5", Description = "属性加成")]
        [Comment("属性加成（JSON格式）")]
        public string AttributeBonus { get; set; }
    }
}