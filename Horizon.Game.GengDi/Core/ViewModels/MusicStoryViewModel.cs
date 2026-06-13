using System;
using System.Collections.Generic;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class MusicStoryViewModel : ViewModelBase
    {
        private static MusicStoryViewModel _instance;
        private static readonly object _lock = new object();

        public static MusicStoryViewModel Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new MusicStoryViewModel();
                        }
                    }
                }
                return _instance;
            }
        }

        private bool _isOpen;
        private bool _isLoading;
        private MusicStory _currentStory;
        private Song _currentSong;

        private MusicStoryViewModel()
        {
            CloseCommand = new RelayCommand(() => IsOpen = false);
            OpenStoryCommand = new RelayCommand<Song>(OpenStory);
        }

        public bool IsOpen
        {
            get => _isOpen;
            set => SetProperty(ref _isOpen, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public MusicStory CurrentStory
        {
            get => _currentStory;
            set => SetProperty(ref _currentStory, value);
        }

        public Song CurrentSong
        {
            get => _currentSong;
            set => SetProperty(ref _currentSong, value);
        }

        public bool HasStory => _currentStory != null && _currentStory.Sections.Count > 0;
        public string SongTitle => _currentStory?.SongTitle ?? _currentSong?.Title ?? "";
        public string ArtistName => _currentStory?.ArtistName ?? _currentSong?.DisplayArtist ?? "";
        public string Summary => _currentStory?.Summary ?? "";
        public List<MusicStorySection> Sections => _currentStory?.Sections ?? new List<MusicStorySection>();

        public ICommand CloseCommand { get; }
        public ICommand OpenStoryCommand { get; }

        private async void OpenStory(Song song)
        {
            if (song == null) return;

            CurrentSong = song;
            IsLoading = true;
            IsOpen = true;
            CurrentStory = null;

            OnPropertyChanged(nameof(SongTitle));
            OnPropertyChanged(nameof(ArtistName));

            try
            {
                CurrentStory = await MusicStoryService.Instance.GetStoryAsync(song);
            }
            catch
            {
                CurrentStory = null;
            }

            IsLoading = false;
            OnPropertyChanged(nameof(HasStory));
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(Sections));
        }
    }
}
