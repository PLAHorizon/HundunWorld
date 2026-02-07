using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 背包实体
    /// </summary>
    [Table("Game_HunduShijie_Bag"), TableDescription(Name = "Game_HunduShijie_Bag", Order = "HunduShijie_006", Description = "背包信息")]
    [Comment("背包信息表")]
    [EntityStorage("Game")]
    public class BagEntity : BaseGameModel<long>
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
        /// 背包类型 0-主背包 1-材料背包 2-任务背包 3-时装背包
        /// </summary>
        [Column("bag_type", TypeName = "int", Order = 3), TableDescription(TypeName = "int", Name = "bag_type", Order = "3", Description = "背包类型")]
        [Comment("背包类型 0-主背包 1-材料背包 2-任务背包 3-时装背包")]
        public int BagType { get; set; }

        /// <summary>
        /// 当前格子数
        /// </summary>
        [Column("current_slots", TypeName = "int", Order = 4), TableDescription(TypeName = "int", Name = "current_slots", Order = "4", Description = "当前格子数")]
        [Comment("当前格子数")]
        public int CurrentSlots { get; set; }

        /// <summary>
        /// 最大格子数
        /// </summary>
        [Column("max_slots", TypeName = "int", Order = 5), TableDescription(TypeName = "int", Name = "max_slots", Order = "5", Description = "最大格子数")]
        [Comment("最大格子数")]
        public int MaxSlots { get; set; }

        /// <summary>
        /// 已使用格子数
        /// </summary>
        [Column("used_slots", TypeName = "int", Order = 6), TableDescription(TypeName = "int", Name = "used_slots", Order = "6", Description = "已使用格子数")]
        [Comment("已使用格子数")]
        public int UsedSlots { get; set; }

        /// <summary>
        /// 解锁时间
        /// </summary>
        [Column("unlock_time", TypeName = "datetime", Order = 7), TableDescription(TypeName = "datetime", Name = "unlock_time", Order = "7", Description = "解锁时间")]
        [Comment("解锁时间")]
        public DateTime? UnlockTime { get; set; }

        /// <summary>
        /// 最后整理时间
        /// </summary>
        [Column("last_sort_time", TypeName = "datetime", Order = 8), TableDescription(TypeName = "datetime", Name = "last_sort_time", Order = "8", Description = "最后整理时间")]
        [Comment("最后整理时间")]
        public DateTime? LastSortTime { get; set; }
    }
}
