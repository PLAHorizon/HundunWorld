using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Model.Base;
using Horizon.Core.Abstract;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 物品宝石镶嵌实体
    /// </summary>
    [Table("Game_HunduShijie_ItemGem")]
    [EntityStorage("Game")]
    public class ItemGemEntity : BaseGameModel<long>
    {
        /// <summary>
        /// 自增ID
        /// </summary>
        [Key]
        [Column("id")]
        public new long Id { get; set; }
        
        /// <summary>
        /// 物品唯一ID
        /// </summary>
        [Column("item_uid")]
        public long ItemUid { get; set; }
        
        /// <summary>
        /// 槽位索引（0-4）
        /// </summary>
        [Column("slot_index")]
        public int SlotIndex { get; set; }
        
        /// <summary>
        /// 宝石ID
        /// </summary>
        [Column("gem_id")]
        public int GemId { get; set; }
        
        /// <summary>
        /// 宝石等级
        /// </summary>
        [Column("gem_level")]
        public int GemLevel { get; set; }
        
        /// <summary>
        /// 宝石五行属性
        /// </summary>
        [Column("gem_element")]
        public int GemElement { get; set; }
        
        /// <summary>
        /// 镶嵌时间
        /// </summary>
        [Column("inlay_time")]
        public DateTime InlayTime { get; set; }
    }
}
