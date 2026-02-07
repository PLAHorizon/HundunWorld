using Horizon.Core.Abstract;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 材料合成记录实体
    /// </summary>
    [Table("Game_HunduShijie_MaterialSynthesisLog")]
    [EntityStorage("Game")]
    public class MaterialSynthesisLogEntity : BaseGameModel<long>
    {
        /// <summary>
        /// 自增ID
        /// </summary>
        [Key]
        [Column("id")]
        public new  long Id { get; set; }
        
        /// <summary>
        /// 角色ID
        /// </summary>
        [Column("character_id")]
        public long CharacterId { get; set; }
        
        /// <summary>
        /// 配方ID
        /// </summary>
        [Column("recipe_id")]
        public int RecipeId { get; set; }
        
        /// <summary>
        /// 结果物品ID
        /// </summary>
        [Column("result_item_id")]
        public int ResultItemId { get; set; }
        
        /// <summary>
        /// 结果物品品质
        /// </summary>
        [Column("result_quality")]
        public int ResultQuality { get; set; }
        
        /// <summary>
        /// 结果数量
        /// </summary>
        [Column("result_quantity")]
        public int ResultQuantity { get; set; }
        
        /// <summary>
        /// 是否成功
        /// </summary>
        [Column("is_success")]
        public bool IsSuccess { get; set; }
        
        /// <summary>
        /// 使用材料（JSON格式）
        /// </summary>
        [Column("used_materials")]
        public string UsedMaterials { get; set; }
        
        /// <summary>
        /// 继承属性（JSON格式）
        /// </summary>
        [Column("inherited_attributes")]
        public string InheritedAttributes { get; set; }
        
        /// <summary>
        /// 五行加成率
        /// </summary>
        [Column("wuxing_bonus")]
        public float WuXingBonus { get; set; }
        
        /// <summary>
        /// 合成时间
        /// </summary>
        [Column("synthesis_time")]
        public DateTime SynthesisTime { get; set; }
    }
}
