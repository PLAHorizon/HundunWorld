using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 角色活跃度实体
    /// </summary>
    [Table("Game_HunduShijie_CharacterActivity"), TableDescription(Name = "Game_HunduShijie_CharacterActivity", Order = "HunduShijie_011", Description = "角色活跃度信息")]
    [Comment("角色活跃度表")]
    [EntityStorage("Game")]
    public class ActivityEntity : BaseGameModel<long>
    {
        /// <summary>
        /// 自增ID
        /// </summary>
        [Key]
        [Column("id", TypeName = "bigint", Order = 1), TableDescription(TypeName = "bigint", Name = "id", Order = "1", Description = "自增ID")]
        [Comment("自增ID")]
        public new  long Id { get; set; }
        
        /// <summary>
        /// 角色ID
        /// </summary>
        [Column("character_id")]
        public ulong CharacterId { get; set; }
        
        /// <summary>
        /// 用户ID（冗余字段，便于查询）
        /// </summary>
        [Column("user_id")]
        public long UserId { get; set; }
        
        /// <summary>
        /// 是否已领取日奖励
        /// </summary>
        [Column("daily_reward_claimed")]
        public bool DailyRewardClaimed { get; set; }
        
        /// <summary>
        /// 是否已领取周奖励
        /// </summary>
        [Column("weekly_reward_claimed")]
        public bool WeeklyRewardClaimed { get; set; }
        
        /// <summary>
        /// 是否已领取月奖励
        /// </summary>
        [Column("monthly_reward_claimed")]
        public bool MonthlyRewardClaimed { get; set; }
        
        /// <summary>
        /// 已领取的里程碑奖励（JSON格式）
        /// </summary>
        [Column("milestone_rewards")]
        public string MilestoneRewards { get; set; }
    }
}