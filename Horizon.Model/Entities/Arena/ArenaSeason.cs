using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.Arena
{
    [Table("cfg_arena_season")]
    public class ArenaSeason
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        
        [MaxLength(64)]
        public string SeasonName { get; set; }
        
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool IsActive { get; set; }
        
        [MaxLength(256)]
        public string Description { get; set; }
        
        public int RequiredLevel { get; set; }
        
        [MaxLength(2048)]
        public string RewardConfig { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
