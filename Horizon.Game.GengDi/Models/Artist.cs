using System;

namespace Horizon.Game.GengDi.Models
{
    public class Artist
    {
        [LiteDB.BsonId]
        public string Id { get; set; }
        public string Name { get; set; }
        public string AvatarUrl { get; set; }
        public string Description { get; set; }
        public int SongCount { get; set; }
        public int AlbumCount { get; set; }
    }
}
