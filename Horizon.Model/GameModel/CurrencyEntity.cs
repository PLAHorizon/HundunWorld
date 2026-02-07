using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 货币实体
    /// </summary>
    [Table("Game_HunduShijie_Currency"), TableDescription(Name = "Game_HunduShijie_Currency", Order = "HunduShijie_006", Description = "货币信息")]
    [Comment("货币信息表")]
    [EntityStorage("Game")]
    public class CurrencyEntity : BaseGameModel<long>
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
        [Column("character_id")]
        public long CharacterId { get; set; }
        
        /// <summary>
        /// 货币类型 0-铜币 1-银两 2-金锭 3-元宝 等
        /// </summary>
        [Column("currency_type")]
        public int CurrencyType { get; set; }
        
        /// <summary>
        /// 数量
        /// </summary>
        [Column("amount")]
        public long Amount { get; set; }
        
        /// <summary>
        /// 累计获得
        /// </summary>
        [Column("total_earned")]
        public long TotalEarned { get; set; }
        
        /// <summary>
        /// 累计消耗
        /// </summary>
        [Column("total_spent")]
        public long TotalSpent { get; set; }
        
        /// <summary>
        /// 更新时间
        /// </summary>
        [Column("update_time")]
        public DateTime UpdateTime { get; set; }
    }
}
