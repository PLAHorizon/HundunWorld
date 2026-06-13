using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class PlaylistManageViewModel : ViewModelBase
    {
        private readonly MusicLibraryService _libraryService;
        private readonly MusicPlayerService _playerService;
        private List<Playlist> _playlists;
        private Playlist _selectedPlaylist;
        private List<Song> _playlistSongs;
        private bool _isCreateDialogOpen;
        private string _newPlaylistName;
        private string _newPlaylistDescription;
        private bool _isLoading;
        private bool _isAddSongDialogOpen;
        private List<Song> _availableSongs;
        private string _addSongSearchText;
        private LocalMusicImportViewModel _localImportViewModel;

        public PlaylistManageViewModel()
        {
            _libraryService = MusicLibraryService.Instance;
            _playerService = MusicPlayerService.Instance;

            CreatePlaylistCommand = new AsyncRelayCommand(CreatePlaylistAsync);
            DeletePlaylistCommand = new AsyncRelayCommand<Playlist>(DeletePlaylistAsync);
            SelectPlaylistCommand = new AsyncRelayCommand<Playlist>(SelectPlaylistAsync);
            PlayPlaylistCommand = new RelayCommand<Playlist>(PlayPlaylist);
            PlaySongCommand = new RelayCommand<Song>(PlaySong);
            RemoveSongFromPlaylistCommand = new AsyncRelayCommand<Song>(RemoveSongFromPlaylistAsync);
            CloseCreateDialogCommand = new RelayCommand(() => IsCreateDialogOpen = false);
            OpenCreateDialogCommand = new RelayCommand(() => IsCreateDialogOpen = true);
            OpenAddSongDialogCommand = new AsyncRelayCommand(OpenAddSongDialogAsync);
            CloseAddSongDialogCommand = new RelayCommand(() => IsAddSongDialogOpen = false);
            AddSongToCurrentPlaylistCommand = new AsyncRelayCommand<Song>(AddSongToCurrentPlaylistAsync);
            ToggleFavoriteCommand = new RelayCommand<Song>(ToggleSongFavorite);
            OpenLocalImportDialogCommand = new RelayCommand(OpenLocalImportDialog);

            _localImportViewModel = new LocalMusicImportViewModel();
        }

        public List<Playlist> Playlists
        {
            get => _playlists;
            set => SetProperty(ref _playlists, value);
        }

        public Playlist SelectedPlaylist
        {
            get => _selectedPlaylist;
            set => SetProperty(ref _selectedPlaylist, value);
        }

        public List<Song> PlaylistSongs
        {
            get => _playlistSongs;
            set => SetProperty(ref _playlistSongs, value);
        }

        public bool IsCreateDialogOpen
        {
            get => _isCreateDialogOpen;
            set => SetProperty(ref _isCreateDialogOpen, value);
        }

        public string NewPlaylistName
        {
            get => _newPlaylistName;
            set => SetProperty(ref _newPlaylistName, value);
        }

        public string NewPlaylistDescription
        {
            get => _newPlaylistDescription;
            set => SetProperty(ref _newPlaylistDescription, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool IsAddSongDialogOpen
        {
            get => _isAddSongDialogOpen;
            set => SetProperty(ref _isAddSongDialogOpen, value);
        }

        public List<Song> AvailableSongs
        {
            get => _availableSongs;
            set => SetProperty(ref _availableSongs, value);
        }

        public string AddSongSearchText
        {
            get => _addSongSearchText;
            set
            {
                if (SetProperty(ref _addSongSearchText, value))
                {
                    FilterAvailableSongs();
                }
            }
        }

        public LocalMusicImportViewModel LocalImportViewModel
        {
            get => _localImportViewModel;
            set => SetProperty(ref _localImportViewModel, value);
        }

        public ICommand CreatePlaylistCommand { get; }
        public ICommand DeletePlaylistCommand { get; }
        public ICommand SelectPlaylistCommand { get; }
        public ICommand PlayPlaylistCommand { get; }
        public ICommand PlaySongCommand { get; }
        public ICommand RemoveSongFromPlaylistCommand { get; }
        public ICommand CloseCreateDialogCommand { get; }
        public ICommand OpenCreateDialogCommand { get; }
        public ICommand OpenAddSongDialogCommand { get; }
        public ICommand CloseAddSongDialogCommand { get; }
        public ICommand AddSongToCurrentPlaylistCommand { get; }
        public ICommand ToggleFavoriteCommand { get; }
        public ICommand OpenLocalImportDialogCommand { get; }

        public void Initialize()
        {
            LoadPlaylistsAsync();
        }

        private async void LoadPlaylistsAsync()
        {
            IsLoading = true;
            try
            {
                Playlists = await _libraryService.GetAllPlaylistsAsync();
                if (Playlists.Count == 0)
                {
                    var samplePlaylists = BuildSamplePlaylists();
                    foreach (var pl in samplePlaylists)
                        _libraryService.AddPlaylist(pl);
                    Playlists = _libraryService.GetAllPlaylists();
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task CreatePlaylistAsync()
        {
            if (string.IsNullOrWhiteSpace(NewPlaylistName)) return;

            var playlist = new Playlist
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = NewPlaylistName,
                Description = NewPlaylistDescription ?? string.Empty,
                CreatorId = App.CurrentUser?.PassportId ?? "local",
                CreatorName = App.CurrentUser?.Username ?? "本地用户",
                CreatedDate = DateTime.UtcNow,
                UpdatedDate = DateTime.UtcNow
            };

            _libraryService.AddPlaylist(playlist);
            Playlists = await _libraryService.GetAllPlaylistsAsync();
            NewPlaylistName = string.Empty;
            NewPlaylistDescription = string.Empty;
            IsCreateDialogOpen = false;
        }

        private async Task DeletePlaylistAsync(Playlist playlist)
        {
            if (playlist == null || playlist.IsSystem) return;
            _libraryService.DeletePlaylist(playlist.Id);
            if (SelectedPlaylist?.Id == playlist.Id)
            {
                SelectedPlaylist = null;
                PlaylistSongs = null;
            }
            Playlists = await _libraryService.GetAllPlaylistsAsync();
        }

        private async Task SelectPlaylistAsync(Playlist playlist)
        {
            SelectedPlaylist = playlist;
            if (playlist != null)
            {
                PlaylistSongs = await _libraryService.GetPlaylistSongsAsync(playlist.Id);
            }
            else
            {
                PlaylistSongs = null;
            }
        }

        private void PlayPlaylist(Playlist playlist)
        {
            if (playlist == null) return;
            var songs = _libraryService.GetPlaylistSongs(playlist.Id);
            if (songs.Count > 0)
            {
                _playerService.PlayAll(songs);
            }
        }

        private void PlaySong(Song song)
        {
            if (song == null) return;
            _playerService.Play(song);
        }

        private async Task RemoveSongFromPlaylistAsync(Song song)
        {
            if (song == null || SelectedPlaylist == null) return;
            _libraryService.RemoveSongFromPlaylist(SelectedPlaylist.Id, song.Id);
            PlaylistSongs = await _libraryService.GetPlaylistSongsAsync(SelectedPlaylist.Id);
            Playlists = await _libraryService.GetAllPlaylistsAsync();
        }

        private async Task OpenAddSongDialogAsync()
        {
            if (SelectedPlaylist == null) return;
            AddSongSearchText = string.Empty;
            var allSongs = await _libraryService.GetAllSongsAsync();
            AvailableSongs = allSongs
                .Where(s => !SelectedPlaylist.SongIds.Contains(s.Id))
                .ToList();
            IsAddSongDialogOpen = true;
        }

        private async Task AddSongToCurrentPlaylistAsync(Song song)
        {
            if (song == null || SelectedPlaylist == null) return;
            _libraryService.AddSongToPlaylist(SelectedPlaylist.Id, song.Id);
            PlaylistSongs = await _libraryService.GetPlaylistSongsAsync(SelectedPlaylist.Id);
            Playlists = await _libraryService.GetAllPlaylistsAsync();
            AvailableSongs = AvailableSongs.Where(s => s.Id != song.Id).ToList();
        }

        private void ToggleSongFavorite(Song song)
        {
            if (song == null) return;
            _libraryService.ToggleSongFavorite(song.Id);
            if (PlaylistSongs != null)
            {
                PlaylistSongs = _libraryService.GetPlaylistSongs(SelectedPlaylist.Id);
            }
        }

        private void OpenLocalImportDialog()
        {
            LocalImportViewModel = new LocalMusicImportViewModel();
            LocalImportViewModel.ImportCompleted += OnLocalImportCompleted;
            LocalImportViewModel.OpenDialog(SelectedPlaylist?.Id);
        }

        private async void OnLocalImportCompleted(List<Song> importedSongs)
        {
            if (SelectedPlaylist != null)
            {
                PlaylistSongs = await _libraryService.GetPlaylistSongsAsync(SelectedPlaylist.Id);
                Playlists = await _libraryService.GetAllPlaylistsAsync();
            }
        }

        private void FilterAvailableSongs()
        {
            if (SelectedPlaylist == null) return;
            var allSongs = _libraryService.GetAllSongs()
                .Where(s => !SelectedPlaylist.SongIds.Contains(s.Id));

            if (!string.IsNullOrWhiteSpace(AddSongSearchText))
            {
                var keyword = AddSongSearchText;
                allSongs = allSongs.Where(s =>
                    (s.Title?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (s.ArtistName?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            AvailableSongs = allSongs.ToList();
        }

        private static List<Playlist> BuildSamplePlaylists()
        {
            return new List<Playlist>
            {
                new Playlist
                {
                    Id = "pl_favorites",
                    Name = "我喜欢的音乐",
                    Description = "收藏的你最爱的歌曲",
                    CreatorId = "system",
                    CreatorName = "系统",
                    CreatedDate = DateTime.UtcNow,
                    UpdatedDate = DateTime.UtcNow,
                    IsSystem = true,
                    IsFavorite = true
                },
                new Playlist
                {
                    Id = "pl_recent",
                    Name = "最近播放",
                    Description = "你最近听过的歌曲",
                    CreatorId = "system",
                    CreatorName = "系统",
                    CreatedDate = DateTime.UtcNow,
                    UpdatedDate = DateTime.UtcNow,
                    IsSystem = true
                }
            };
        }
    }
}
