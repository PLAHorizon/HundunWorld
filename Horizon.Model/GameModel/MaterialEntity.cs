using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 材料实体
    /// </summary>
    [Table("Game_HunduShijie_Material"), TableDescription(Name = "Game_HunduShijie_Material", Order = "HunduShijie_005", Description = "材料信息")]
    [Comment("材料信息表")]
    [EntityStorage("Game")]
    public class MaterialEntity : BaseGameModel<long>
    {
        /// <summary>
        /// 材料唯一ID
        /// </summary>
        [Key]
        [Column("material_uid", TypeName = "bigint", Order = 1), TableDescription(TypeName = "bigint", Name = "material_uid", Order = "1", Description = "材料唯一ID")]
        [Comment("材料唯一ID")]
        public new long Id { get; set; }
        
        /// <summary>
        /// 材料ID
        /// </summary>
        [Column("material_id")]
        public int MaterialId { get; set; }
        
        /// <summary>
        /// 拥有者ID（角色ID）
        /// </summary>
        [Column("owner_id")]
        public long OwnerId { get; set; }
        
        /// <summary>
        /// 五行属性 0-金 1-木 2-水 3-火 4-土
        /// </summary>
        [Column("element")]
        public int Element { get; set; }
        
        /// <summary>
        /// 材料品阶
        /// </summary>
        [Column("grade")]
        public int Grade { get; set; }
        
        /// <summary>
        /// 稀有度
        /// </summary>
        [Column("rarity")]
        public int Rarity { get; set; }
        
        /// <summary>
        /// 数量
        /// </summary>
        [Column("quantity")]
        public int Quantity { get; set; }
        
        /// <summary>
        /// 是否绑定
        /// </summary>
        [Column("is_bound")]
        public bool IsBound { get; set; }
        
        /// <summary>
        /// 获得时间
        /// </summary>
        [Column("acquire_time")]
        public DateTime AcquireTime { get; set; }
        
        /// <summary>
        /// 来源类型
        /// </summary>
        [Column("source_type")]
        public int SourceType { get; set; }
        
        /// <summary>
        /// 来源ID
        /// </summary>
        [Column("source_id")]
        public int? SourceId { get; set; }
    }
}
