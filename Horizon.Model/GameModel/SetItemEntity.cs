using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 套装物品实体
    /// </summary>
    [Table("Game_HunduShijie_SetItem"), TableDescription(Name = "Game_HunduShijie_SetItem", Order = "HunduShijie_007", Description = "套装物品信息")]
    [Comment("套装物品表")]
    [EntityStorage("Game")]
    public class SetItemEntity : BaseGameModel<long>
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
        /// 套装ID
        /// </summary>
        [Column("set_id", TypeName = "int", Order = 3), TableDescription(TypeName = "int", Name = "set_id", Order = "3", Description = "套装ID")]
        [Comment("套装ID")]
        public int SetId { get; set; }

        /// <summary>
        /// 套装名称
        /// </summary>
        [StringLength(50)]
        [Column("set_name", TypeName = "nvarchar(50)", Order = 4), TableDescription(TypeName = "nvarchar(50)", Name = "set_name", Order = "4", Description = "套装名称")]
        [Comment("套装名称")]
        public string SetName { get; set; }

        /// <summary>
        /// 已装备件数
        /// </summary>
        [Column("equipped_count", TypeName = "int", Order = 5), TableDescription(TypeName = "int", Name = "equipped_count", Order = "5", Description = "已装备件数")]
        [Comment("已装备件数")]
        public int EquippedCount { get; set; }

        /// <summary>
        /// 套装总件数
        /// </summary>
        [Column("total_pieces", TypeName = "int", Order = 6), TableDescription(TypeName = "int", Name = "total_pieces", Order = "6", Description = "套装总件数")]
        [Comment("套装总件数")]
        public int TotalPieces { get; set; }

        /// <summary>
        /// 套装五行属性
        /// </summary>
        [Column("set_element", TypeName = "int", Order = 7), TableDescription(TypeName = "int", Name = "set_element", Order = "7", Description = "套装五行属性")]
        [Comment("套装五行属性")]
        public int SetElement { get; set; }

        /// <summary>
        /// 套装品质
        /// </summary>
        [Column("set_quality", TypeName = "int", Order = 8), TableDescription(TypeName = "int", Name = "set_quality", Order = "8", Description = "套装品质")]
        [Comment("套装品质")]
        public int SetQuality { get; set; }

        /// <summary>
        /// 激活的套装效果（JSON格式）
        /// </summary>
        [Column("active_effects", TypeName = "nvarchar(max)", Order = 9), TableDescription(TypeName = "nvarchar(max)", Name = "active_effects", Order = "9", Description = "激活的套装效果")]
        [Comment("激活的套装效果（JSON格式）")]
        public string ActiveEffects { get; set; }

        /// <summary>
        /// 装备的物品ID列表（JSON格式）
        /// </summary>
        [Column("equipped_items", TypeName = "nvarchar(max)", Order = 10), TableDescription(TypeName = "nvarchar(max)", Name = "equipped_items", Order = "10", Description = "装备的物品ID列表")]
        [Comment("装备的物品ID列表（JSON格式）")]
        public string EquippedItems { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        [Column("update_time", TypeName = "datetime", Order = 11), TableDescription(TypeName = "datetime", Name = "update_time", Order = "11", Description = "更新时间")]
        [Comment("更新时间")]
        public DateTime UpdateTime { get; set; }
    }
}
