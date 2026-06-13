using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Horizon.Game.GengDi.Models
{
    public class Song : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        [LiteDB.BsonId]
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ArtistId { get; set; } = string.Empty;
        public string ArtistName { get; set; } = string.Empty;
        public string AlbumId { get; set; } = string.Empty;
        public string AlbumName { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public string CoverUrl { get; set; } = string.Empty;
        public string AudioUrl { get; set; } = string.Empty;
        public string LyricsJson { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string TagsJson { get; set; } = string.Empty;
        public DateTime AddedDate { get; set; }
        public int PlayCount { get; set; }
        public bool IsFavorite { get; set; }
        public string LocalFilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string FileFormat { get; set; } = string.Empty;
        [LiteDB.BsonIgnore]
        public string LastPlaySuccessSource { get; set; } = string.Empty;

        [LiteDB.BsonIgnore]
        public bool IsLocal => Source == "local";

        private List<string> _availableSources;
        [LiteDB.BsonIgnore]
        public List<string> AvailableSources
        {
            get => _availableSources;
            set { if (_availableSources != value) { _availableSources = value; OnPropertyChanged(); } }
        }

        private Dictionary<string, int> _sourcePriority;
        [LiteDB.BsonIgnore]
        public Dictionary<string, int> SourcePriority
        {
            get => _sourcePriority;
            set { if (_sourcePriority != value) { _sourcePriority = value; OnPropertyChanged(); } }
        }

        private bool _isPlaying;
        [LiteDB.BsonIgnore]
        public bool IsPlaying
        {
            get => _isPlaying;
            set { if (_isPlaying != value) { _isPlaying = value; OnPropertyChanged(); } }
        }

        [LiteDB.BsonIgnore]
        public string DurationText => Duration.TotalMinutes >= 1
            ? $"{(int)Duration.TotalMinutes}:{Duration.Seconds:D2}"
            : $"0:{Duration.Seconds:D2}";

        [LiteDB.BsonIgnore]
        public string DisplayArtist => string.IsNullOrWhiteSpace(ArtistName) ? "未知艺术家" : ArtistName;

        [LiteDB.BsonIgnore]
        public string DisplayAlbum => string.IsNullOrWhiteSpace(AlbumName) ? "未知专辑" : AlbumName;
    }
}
