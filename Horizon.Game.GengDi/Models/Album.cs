using System;

namespace Horizon.Game.GengDi.Models
{
    public class Album
    {
        [LiteDB.BsonId]
        public string Id { get; set; }
        public string Name { get; set; }
        public string CoverUrl { get; set; }
        public string ArtistId { get; set; }
        public string ArtistName { get; set; }
        public DateTime ReleaseDate { get; set; }
        public string Description { get; set; }
        public int SongCount { get; set; }
    }
}
