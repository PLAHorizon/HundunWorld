using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.Arena
{
    [Table("user_arena_record")]
    public class ArenaPlayerRecord
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long CharacterId { get; set; }
        
        public int SeasonId { get; set; }
        
        public int CurrentRating { get; set; }
        public int HighestRating { get; set; }
        
        public int TotalMatches { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Draws { get; set; }
        
        public int CurrentWinStreak { get; set; }
        public int HighestWinStreak { get; set; }
        
        public DateTime LastMatchTime { get; set; }
        public DateTime UpdatedAt { get; set; }
        
        [NotMapped]
        public double WinRate => TotalMatches > 0 ? (double)Wins / TotalMatches : 0;
    }
}
