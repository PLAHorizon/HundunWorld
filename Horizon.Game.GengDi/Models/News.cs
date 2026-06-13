using System;

namespace Horizon.Game.GengDi.Models
{
    public class News
    {
        [LiteDB.BsonId]
        public string Id { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public string Image { get; set; }
        public string GameId { get; set; }
        public DateTime PublishDate { get; set; }
        public string Author { get; set; }
        public string Category { get; set; }
    }
}