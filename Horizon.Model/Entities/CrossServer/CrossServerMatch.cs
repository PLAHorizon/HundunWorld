using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.CrossServer
{
    [Table("cross_server_match")]
    public class CrossServerMatch
    {
        [Key]
        [MaxLength(64)]
        public string MatchId { get; set; }
        
        public int BattleType { get; set; } // 1: Guild vs Guild, 2: Server vs Server
        
        [MaxLength(64)]
        public string ParticipatingServerIds { get; set; }
        
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        
        public int WinnerServerId { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}