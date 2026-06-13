using System;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Enums;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class MiniPlayerViewModel : ViewModelBase
    {
        private readonly MusicPlayerService _playerService;

        public MiniPlayerViewModel()
        {
            _playerService = MusicPlayerService.Instance;
            _playerService.PropertyChanged += (s, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(MusicPlayerService.CurrentSong):
                        OnPropertyChanged(nameof(Title));
                        OnPropertyChanged(nameof(Artist));
                        OnPropertyChanged(nameof(CoverUrl));
                        OnPropertyChanged(nameof(HasSong));
                        break;
                    case nameof(MusicPlayerService.IsPlaying):
                        OnPropertyChanged(nameof(IsPlaying));
                        OnPropertyChanged(nameof(PlayPauseIcon));
                        break;
                    case nameof(MusicPlayerService.IsLoading):
                        OnPropertyChanged(nameof(IsLoading));
                        OnPropertyChanged(nameof(ShowLoadingIndicator));
                        break;
                    case nameof(MusicPlayerService.IsError):
                        OnPropertyChanged(nameof(IsError));
                        OnPropertyChanged(nameof(ShowErrorStatus));
                        break;
                    case nameof(MusicPlayerService.StatusMessage):
                        OnPropertyChanged(nameof(StatusMessage));
                        break;
                    case nameof(MusicPlayerService.CurrentPositionText):
                    case nameof(MusicPlayerService.TotalDurationText):
                        OnPropertyChanged(nameof(PositionText));
                        OnPropertyChanged(nameof(DurationText));
                        break;
                    case nameof(MusicPlayerService.Progress):
                        OnPropertyChanged(nameof(Progress));
                        break;
                }
            };

            PlayPauseCommand = new RelayCommand(TogglePlayPause);
            NextCommand = new RelayCommand(PlayNext);
            PreviousCommand = new RelayCommand(PlayPrevious);
            RetryCommand = new RelayCommand(RetryPlayback);
        }

        public bool HasSong => _playerService.HasCurrentSong;
        public string Title => _playerService.CurrentSong?.Title ?? "未在播放";
        public string Artist => _playerService.CurrentSong?.DisplayArtist ?? string.Empty;
        public string CoverUrl => _playerService.CurrentSong?.CoverUrl;
        public bool IsPlaying => _playerService.IsPlaying;
        public bool IsLoading => _playerService.IsLoading;
        public bool IsError => _playerService.IsError;
        public string StatusMessage => _playerService.StatusMessage;
        public bool ShowLoadingIndicator => _playerService.IsLoading && _playerService.HasCurrentSong;
        public bool ShowErrorStatus => _playerService.IsError && _playerService.HasCurrentSong;
        public double Progress => _playerService.Progress;
        public string PositionText => _playerService.CurrentPositionText;
        public string DurationText => _playerService.TotalDurationText;

        public string PlayPauseIcon => _playerService.IsPlaying ? "\uE769" : "\uE768";

        public ICommand PlayPauseCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand PreviousCommand { get; }
        public ICommand RetryCommand { get; }

        public event EventHandler ExpandRequested;

        public void RequestExpand()
        {
            ExpandRequested?.Invoke(this, EventArgs.Empty);
        }

        private void TogglePlayPause()
        {
            _playerService.TogglePlayPause();
        }

        private void PlayNext()
        {
            _playerService.Next();
        }

        private void PlayPrevious()
        {
            _playerService.Previous();
        }

        private void RetryPlayback()
        {
            _playerService.RetryPlayback();
        }

        public void SeekToProgress(double progress)
        {
            _playerService.SeekToProgress(progress);
        }
    }
}
