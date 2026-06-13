using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.Arena
{
    [Table("log_arena_match")]
    public class ArenaMatchRecord
    {
        [Key]
        [MaxLength(64)]
        public string MatchId { get; set; }
        
        public int SeasonId { get; set; }
        
        public long RedTeamCharacterId { get; set; }
        public long BlueTeamCharacterId { get; set; }
        
        public int WinnerTeam { get; set; }
        
        public int RedTeamRatingChange { get; set; }
        public int BlueTeamRatingChange { get; set; }
        
        public int DurationSeconds { get; set; }
        
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        
        [MaxLength(2048)]
        public string MatchReplayData { get; set; }
    }
}
