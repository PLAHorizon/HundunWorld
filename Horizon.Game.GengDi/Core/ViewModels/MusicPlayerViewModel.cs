using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Enums;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public enum DisplayMode
    {
        Lyrics,
        Score
    }

    public class MusicPlayerViewModel : ViewModelBase
    {
        private readonly MusicPlayerService _playerService;
        private readonly MusicLibraryService _libraryService;
        private int _currentLyricIndex = -1;
        private bool _isFavorite;
        private bool _isAddToPlaylistDialogOpen;
        private List<Playlist> _availablePlaylists;
        private DisplayMode _displayMode = DisplayMode.Lyrics;
        private string _scoreText;
        private List<DisplayLyricLine> _displayLyricLines;

        public MusicPlayerViewModel()
        {
            _playerService = MusicPlayerService.Instance;
            _libraryService = MusicLibraryService.Instance;
            _playerService.PropertyChanged += OnPlayerServicePropertyChanged;
            _playerService.Queue.PropertyChanged += OnQueuePropertyChanged;

            PlayPauseCommand = new RelayCommand(TogglePlayPause);
            NextCommand = new RelayCommand(PlayNext);
            PreviousCommand = new RelayCommand(PlayPrevious);
            TogglePlayModeCommand = new RelayCommand(TogglePlayMode);
            SeekCommand = new RelayCommand<double>(SeekToProgress);
            CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
            ToggleFavoriteCommand = new RelayCommand(ToggleFavorite);
            OpenAddToPlaylistDialogCommand = new RelayCommand(OpenAddToPlaylistDialog);
            CloseAddToPlaylistDialogCommand = new RelayCommand(() => IsAddToPlaylistDialogOpen = false);
            AddToPlaylistCommand = new AsyncRelayCommand<Playlist>(AddToPlaylistAsync);
            ToggleDisplayModeCommand = new RelayCommand(ToggleDisplayMode);
            RetryCommand = new RelayCommand(RetryPlayback);
        }

        private void OnPlayerServicePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MusicPlayerService.CurrentSong):
                    OnPropertyChanged(nameof(Title));
                    OnPropertyChanged(nameof(Artist));
                    OnPropertyChanged(nameof(Album));
                    OnPropertyChanged(nameof(CoverUrl));
                    OnPropertyChanged(nameof(HasSong));
                    OnPropertyChanged(nameof(IsFavorite));
                    UpdateScoreText();
                    break;
                case nameof(MusicPlayerService.IsPlaying):
                    OnPropertyChanged(nameof(IsPlaying));
                    break;
                case nameof(MusicPlayerService.IsLoading):
                    OnPropertyChanged(nameof(IsLoading));
                    OnPropertyChanged(nameof(ShowLoadingIndicator));
                    break;
                case nameof(MusicPlayerService.IsError):
                    OnPropertyChanged(nameof(IsError));
                    OnPropertyChanged(nameof(ShowErrorBanner));
                    break;
                case nameof(MusicPlayerService.StatusMessage):
                    OnPropertyChanged(nameof(StatusMessage));
                    break;
                case nameof(MusicPlayerService.Progress):
                    OnPropertyChanged(nameof(Progress));
                    break;
                case nameof(MusicPlayerService.CurrentPositionText):
                case nameof(MusicPlayerService.TotalDurationText):
                    OnPropertyChanged(nameof(PositionText));
                    OnPropertyChanged(nameof(DurationText));
                    break;
                case nameof(MusicPlayerService.CurrentLyricLineIndex):
                    UpdateLyricHighlight();
                    break;
                case nameof(MusicPlayerService.HasLyrics):
                case nameof(MusicPlayerService.CurrentLyrics):
                    OnPropertyChanged(nameof(HasLyrics));
                    RebuildDisplayLyrics();
                    UpdateScoreText();
                    break;
                case nameof(MusicPlayerService.Volume):
                    OnPropertyChanged(nameof(Volume));
                    break;
            }
        }

        private void OnQueuePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlayQueue.PlayMode) || e.PropertyName == nameof(PlayQueue.PlayModeText))
            {
                OnPropertyChanged(nameof(PlayModeText));
            }
        }

        public bool HasSong => _playerService.HasCurrentSong;
        public string Title => _playerService.CurrentSong?.Title ?? "未在播放";
        public string Artist => _playerService.CurrentSong?.DisplayArtist ?? string.Empty;
        public string Album => _playerService.CurrentSong?.DisplayAlbum ?? string.Empty;
        public string CoverUrl => _playerService.CurrentSong?.CoverUrl;
        public bool IsPlaying => _playerService.IsPlaying;
        public bool IsLoading => _playerService.IsLoading;
        public bool IsError => _playerService.IsError;
        public string StatusMessage => _playerService.StatusMessage;
        public bool ShowLoadingIndicator => _playerService.IsLoading && _playerService.HasCurrentSong;
        public bool ShowErrorBanner => _playerService.IsError && _playerService.HasCurrentSong;
        public double Progress => _playerService.Progress;
        public double Volume
        {
            get => _playerService.Volume;
            set => _playerService.Volume = value;
        }
        public string PositionText => _playerService.CurrentPositionText;
        public string DurationText => _playerService.TotalDurationText;
        public bool HasLyrics => _playerService.HasLyrics;
        public string PlayModeText => _playerService.Queue.PlayModeText;

        public DisplayMode DisplayMode
        {
            get => _displayMode;
            set
            {
                if (_displayMode != value)
                {
                    SetProperty(ref _displayMode, value);
                    OnPropertyChanged(nameof(IsLyricsMode));
                    OnPropertyChanged(nameof(IsScoreMode));
                    OnPropertyChanged(nameof(DisplayModeText));
                }
            }
        }

        public bool IsLyricsMode => _displayMode == DisplayMode.Lyrics;
        public bool IsScoreMode => _displayMode == DisplayMode.Score;
        public string DisplayModeText => _displayMode == DisplayMode.Lyrics ? "歌词" : "曲谱";

        public string ScoreText
        {
            get => _scoreText;
            set => SetProperty(ref _scoreText, value);
        }

        public bool IsFavorite
        {
            get
            {
                var song = _playerService.CurrentSong;
                if (song == null) return false;
                var dbSong = _libraryService.GetSongById(song.Id);
                return dbSong?.IsFavorite ?? song.IsFavorite;
            }
            set => SetProperty(ref _isFavorite, value);
        }

        public bool IsAddToPlaylistDialogOpen
        {
            get => _isAddToPlaylistDialogOpen;
            set => SetProperty(ref _isAddToPlaylistDialogOpen, value);
        }

        public List<Playlist> AvailablePlaylists
        {
            get => _availablePlaylists;
            set => SetProperty(ref _availablePlaylists, value);
        }

        public List<DisplayLyricLine> DisplayLyricLines
        {
            get => _displayLyricLines;
            set => SetProperty(ref _displayLyricLines, value);
        }

        public int CurrentLyricIndex
        {
            get => _currentLyricIndex;
            set => SetProperty(ref _currentLyricIndex, value);
        }

        public ICommand PlayPauseCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand PreviousCommand { get; }
        public ICommand TogglePlayModeCommand { get; }
        public ICommand SeekCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand ToggleFavoriteCommand { get; }
        public ICommand OpenAddToPlaylistDialogCommand { get; }
        public ICommand CloseAddToPlaylistDialogCommand { get; }
        public ICommand AddToPlaylistCommand { get; }
        public ICommand ToggleDisplayModeCommand { get; }
        public ICommand RetryCommand { get; }

        public event EventHandler CloseRequested;
        public event EventHandler<int> LyricIndexChanged;

        private void TogglePlayPause() => _playerService.TogglePlayPause();
        private void PlayNext() => _playerService.Next();
        private void PlayPrevious() => _playerService.Previous();
        private void TogglePlayMode() => _playerService.TogglePlayMode();
        private void SeekToProgress(double progress) => _playerService.SeekToProgress(progress);
        private void RetryPlayback() => _playerService.RetryPlayback();

        private void ToggleDisplayMode()
        {
            DisplayMode = _displayMode == DisplayMode.Lyrics ? DisplayMode.Score : DisplayMode.Lyrics;
        }

        private void ToggleFavorite()
        {
            var song = _playerService.CurrentSong;
            if (song == null) return;
            _libraryService.ToggleSongFavorite(song.Id);
            OnPropertyChanged(nameof(IsFavorite));
        }

        private void OpenAddToPlaylistDialog()
        {
            var song = _playerService.CurrentSong;
            if (song == null) return;
            AvailablePlaylists = _libraryService.GetAllPlaylists();
            IsAddToPlaylistDialogOpen = true;
        }

        private async System.Threading.Tasks.Task AddToPlaylistAsync(Playlist playlist)
        {
            var song = _playerService.CurrentSong;
            if (song == null || playlist == null) return;
            await _libraryService.EnsureSongInLibraryAsync(song);
            await _libraryService.AddSongToPlaylistAsync(playlist.Id, song.Id);
            IsAddToPlaylistDialogOpen = false;
        }

        private void RebuildDisplayLyrics()
        {
            _currentLyricIndex = -1;
            var lyrics = _playerService.CurrentLyrics;
            if (lyrics == null || lyrics.Lines.Count == 0)
            {
                DisplayLyricLines = new List<DisplayLyricLine>();
                return;
            }
            DisplayLyricLines = lyrics.ToDisplayLines();
        }

        private void UpdateLyricHighlight()
        {
            var newIndex = _playerService.CurrentLyricLineIndex;
            if (newIndex == _currentLyricIndex) return;

            if (_displayLyricLines != null)
            {
                if (_currentLyricIndex >= 0 && _currentLyricIndex < _displayLyricLines.Count)
                {
                    _displayLyricLines[_currentLyricIndex].IsCurrent = false;
                }

                if (newIndex >= 0 && newIndex < _displayLyricLines.Count)
                {
                    _displayLyricLines[newIndex].IsCurrent = true;
                }
            }

            _currentLyricIndex = newIndex;
            CurrentLyricIndex = newIndex;
            LyricIndexChanged?.Invoke(this, newIndex);
        }

        private void UpdateScoreText()
        {
            var lyrics = _playerService.CurrentLyrics;
            if (lyrics == null || lyrics.Lines.Count == 0)
            {
                ScoreText = string.Empty;
                return;
            }

            var scoreLines = new List<string>();
            scoreLines.Add($"♪ {Title} ♪");
            scoreLines.Add($"词/曲: {Artist}");
            scoreLines.Add(string.Empty);

            var noteMap = new[] { "1", "2", "3", "4", "5", "6", "7" };
            var rhythmMap = new[] { "♩", "♪", "", "♬" };

            var random = new Random(Title.GetHashCode());

            for (int i = 0; i < lyrics.Lines.Count; i++)
            {
                var line = lyrics.Lines[i];
                var text = line.Text?.Split('\n')[0] ?? "";
                if (string.IsNullOrWhiteSpace(text)) continue;

                var barCount = Math.Max(2, Math.Min(4, text.Length / 3));
                var bars = new List<string>();

                for (int b = 0; b < barCount; b++)
                {
                    var noteCount = random.Next(3, 6);
                    var notes = new List<string>();
                    for (int n = 0; n < noteCount; n++)
                    {
                        var note = noteMap[random.Next(noteMap.Length)];
                        if (random.Next(4) == 0) note += "♯";
                        else if (random.Next(6) == 0) note += "♭";
                        if (random.Next(3) == 0) note += "̇";
                        else if (random.Next(4) == 0) note += "̣";
                        notes.Add(note);
                    }
                    bars.Add(string.Join(" ", notes) + " " + rhythmMap[random.Next(rhythmMap.Length)]);
                }

                scoreLines.Add(string.Join(" | ", bars));
                scoreLines.Add($"  {text}");
                scoreLines.Add(string.Empty);
            }

            ScoreText = string.Join("\n", scoreLines);
        }
    }
}
