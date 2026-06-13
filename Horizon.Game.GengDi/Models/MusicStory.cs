using System;
using System.Collections.Generic;

namespace Horizon.Game.GengDi.Models
{
    public class MusicStorySection
    {
        public string Type { get; set; }
        public string Content { get; set; }
        public string ImageUrl { get; set; }
        public string ImageCaption { get; set; }
        public string Quote { get; set; }
        public string QuoteAuthor { get; set; }
    }

    public class MusicStory
    {
        public string SongId { get; set; }
        public string SongTitle { get; set; }
        public string ArtistName { get; set; }
        public string Summary { get; set; }
        public string Era { get; set; }
        public string Genre { get; set; }
        public List<MusicStorySection> Sections { get; set; } = new List<MusicStorySection>();
        public DateTime FetchedAt { get; set; }
    }
}
