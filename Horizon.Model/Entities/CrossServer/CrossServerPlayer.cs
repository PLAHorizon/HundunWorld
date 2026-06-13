using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.CrossServer
{
    [Table("cross_server_player")]
    public class CrossServerPlayer
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long CharacterId { get; set; }
        
        public int SourceServerId { get; set; }
        public int CurrentIslandId { get; set; }
        
        [MaxLength(64)]
        public string CurrentMatchId { get; set; }
        
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int ContributionPoints { get; set; }
        
        public DateTime LastTransferTime { get; set; }
    }
}