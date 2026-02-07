using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 物品属性实体
    /// </summary>
    [Table("Game_HunduShijie_ItemAttribute"), TableDescription(Name = "Game_HunduShijie_ItemAttribute", Order = "HunduShijie_012", Description = "物品属性信息")]
    [Comment("物品属性表")]
    [EntityStorage("Game")]
    public class ItemAttributeEntity : BaseGameModel<long>
    {
        /// <summary>
        /// 自增ID
        /// </summary>
        [Key]
        [Column("id", TypeName = "bigint", Order = 1), TableDescription(TypeName = "bigint", Name = "id", Order = "1", Description = "自增ID")]
        [Comment("自增ID")]
        public new long Id { get; set; }
        
        /// <summary>
        /// 物品唯一ID
        /// </summary>
        [Column("item_uid")]
        public long ItemUid { get; set; }
        
        /// <summary>
        /// 属性类型（对应 AttributeType 枚举）
        /// </summary>
        [Column("attribute_type")]
        public int AttributeType { get; set; }
        
        /// <summary>
        /// 属性值
        /// </summary>
        [Column("attribute_value")]
        public float AttributeValue { get; set; }
        
        /// <summary>
        /// 值类型 0-固定值 1-百分比 2-基础值百分比
        /// </summary>
        [Column("value_type")]
        public int ValueType { get; set; }
        
        /// <summary>
        /// 是否随机属性
        /// </summary>
        [Column("is_random")]
        public bool IsRandom { get; set; }
        
        /// <summary>
        /// 属性来源 0-基础 1-强化 2-附魔 3-宝石 4-套装 5-合成继承
        /// </summary>
        [Column("source_type")]
        public int SourceType { get; set; }
        
        /// <summary>
        /// 来源ID（如宝石ID、附魔ID等）
        /// </summary>
        [Column("source_id")]
        public int? SourceId { get; set; }
        
        /// <summary>
        /// 属性品质（影响显示颜色）
        /// </summary>
        [Column("attribute_quality")]
        public int AttributeQuality { get; set; }
    }
}
