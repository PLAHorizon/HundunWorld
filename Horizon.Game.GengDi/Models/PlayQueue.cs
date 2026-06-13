using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Horizon.Game.GengDi.Enums;

namespace Horizon.Game.GengDi.Models
{
    public class PlayQueue : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private ObservableCollection<Song> _songs = new ObservableCollection<Song>();
        private int _currentIndex = -1;
        private PlayMode _playMode = PlayMode.Sequential;
        private readonly Random _random = new Random(Guid.NewGuid().GetHashCode());

        public ObservableCollection<Song> Songs => _songs;

        public int CurrentIndex
        {
            get => _currentIndex;
            set
            {
                if (_currentIndex != value)
                {
                    _currentIndex = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentSong));
                    OnPropertyChanged(nameof(HasCurrentSong));
                    OnPropertyChanged(nameof(CanPlayPrevious));
                    OnPropertyChanged(nameof(CanPlayNext));
                }
            }
        }

        public PlayMode PlayMode
        {
            get => _playMode;
            set
            {
                if (_playMode != value)
                {
                    _playMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PlayModeText));
                }
            }
        }

        [LiteDB.BsonIgnore]
        public Song CurrentSong => _currentIndex >= 0 && _currentIndex < _songs.Count
            ? _songs[_currentIndex]
            : null;

        [LiteDB.BsonIgnore]
        public bool HasCurrentSong => CurrentSong != null;

        [LiteDB.BsonIgnore]
        public bool CanPlayPrevious => _songs.Count > 0;

        [LiteDB.BsonIgnore]
        public bool CanPlayNext => _songs.Count > 0;

        [LiteDB.BsonIgnore]
        public string PlayModeText => _playMode switch
        {
            PlayMode.Sequential => "顺序播放",
            PlayMode.LoopOne => "单曲循环",
            PlayMode.LoopAll => "列表循环",
            PlayMode.Shuffle => "随机播放",
            _ => "顺序播放"
        };

        public int GetNextIndex()
        {
            if (_songs.Count == 0) return -1;

            return _playMode switch
            {
                PlayMode.LoopOne => _currentIndex,
                PlayMode.LoopAll => (_currentIndex + 1) % _songs.Count,
                PlayMode.Shuffle => _random.Next(_songs.Count),
                _ => _currentIndex + 1 < _songs.Count ? _currentIndex + 1 : -1
            };
        }

        public int GetPreviousIndex()
        {
            if (_songs.Count == 0) return -1;

            return _currentIndex > 0 ? _currentIndex - 1 : _songs.Count - 1;
        }

        public void AddSong(Song song)
        {
            _songs.Add(song);
            if (_currentIndex < 0) CurrentIndex = 0;
        }

        public void AddSongs(IEnumerable<Song> songs)
        {
            foreach (var song in songs)
                _songs.Add(song);
            if (_currentIndex < 0 && _songs.Count > 0) CurrentIndex = 0;
        }

        public void RemoveSong(int index)
        {
            if (index < 0 || index >= _songs.Count) return;
            _songs.RemoveAt(index);
            if (_songs.Count == 0) { CurrentIndex = -1; return; }
            if (_currentIndex >= _songs.Count) CurrentIndex = _songs.Count - 1;
            else if (_currentIndex == index) OnPropertyChanged(nameof(CurrentSong));
        }

        public void Clear()
        {
            _songs.Clear();
            CurrentIndex = -1;
        }
    }
}
