using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Horizon.Game.GengDi.Models
{
    public class Playlist
    {
        [LiteDB.BsonId]
        public string Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string CoverUrl { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string CreatorId { get; set; } = string.Empty;
        public string CreatorName { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
        public DateTime UpdatedDate { get; set; }
        public string TagsJson { get; set; } = "[]";
        public string SongIdsJson { get; set; } = "[]";
        public int PlayCount { get; set; }
        public bool IsSystem { get; set; }
        public bool IsFavorite { get; set; }

        [LiteDB.BsonIgnore]
        public List<string> Tags
        {
            get => JsonConvert.DeserializeObject<List<string>>(TagsJson ?? "[]");
            set => TagsJson = JsonConvert.SerializeObject(value ?? new List<string>());
        }

        [LiteDB.BsonIgnore]
        public List<string> SongIds
        {
            get => JsonConvert.DeserializeObject<List<string>>(SongIdsJson ?? "[]");
            set => SongIdsJson = JsonConvert.SerializeObject(value ?? new List<string>());
        }

        [LiteDB.BsonIgnore]
        public int SongCount => SongIds?.Count ?? 0;
    }
}
