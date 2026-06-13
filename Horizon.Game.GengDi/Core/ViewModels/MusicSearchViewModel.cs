using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class MusicSearchViewModel : ViewModelBase
    {
        private readonly MusicLibraryService _libraryService;
        private readonly MusicPlayerService _playerService;
        private string _searchText;
        private List<Song> _songResults;
        private List<Playlist> _userPlaylistResults;
        private List<Playlist> _networkPlaylistResults;
        private List<Song> _networkSongResults;
        private bool _hasSearched;
        private bool _isSearching;
        private bool _isAddToPlaylistDialogOpen;
        private bool _isLoadingPlaylist;
        private string _loadingPlaylistName;
        private List<Playlist> _availablePlaylists;
        private Song _selectedSongForPlaylist;

        public MusicSearchViewModel()
        {
            _libraryService = MusicLibraryService.Instance;
            _playerService = MusicPlayerService.Instance;

            SearchCommand = new AsyncRelayCommand(SearchAsync);
            PlaySongCommand = new RelayCommand<Song>(PlaySong);
            PlayPlaylistCommand = new RelayCommand<Playlist>(PlayPlaylist);
            ClearSearchCommand = new RelayCommand(() => { SearchText = string.Empty; HasSearched = false; });
            ToggleFavoriteCommand = new RelayCommand<Song>(ToggleFavorite);
            AddToPlaylistCommand = new RelayCommand<Song>(OpenAddToPlaylistDialog);
            SelectPlaylistForSongCommand = new AsyncRelayCommand<Playlist>(SelectPlaylistForSongAsync);
            CloseAddToPlaylistDialogCommand = new RelayCommand(() => IsAddToPlaylistDialogOpen = false);

            MusicSearchCacheService.Instance.ClearExpired();
        }

        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        public List<Song> SongResults
        {
            get => _songResults;
            set => SetProperty(ref _songResults, value);
        }

        public List<Playlist> UserPlaylistResults
        {
            get => _userPlaylistResults;
            set => SetProperty(ref _userPlaylistResults, value);
        }

        public List<Playlist> NetworkPlaylistResults
        {
            get => _networkPlaylistResults;
            set => SetProperty(ref _networkPlaylistResults, value);
        }

        public List<Song> NetworkSongResults
        {
            get => _networkSongResults;
            set => SetProperty(ref _networkSongResults, value);
        }

        public bool HasSearched
        {
            get => _hasSearched;
            set => SetProperty(ref _hasSearched, value);
        }

        public bool IsSearching
        {
            get => _isSearching;
            set => SetProperty(ref _isSearching, value);
        }

        public bool IsLoadingPlaylist
        {
            get => _isLoadingPlaylist;
            set => SetProperty(ref _isLoadingPlaylist, value);
        }

        public string LoadingPlaylistName
        {
            get => _loadingPlaylistName;
            set => SetProperty(ref _loadingPlaylistName, value);
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

        public bool HasSongResults => SongResults?.Count > 0;
        public bool HasUserPlaylistResults => UserPlaylistResults?.Count > 0;
        public bool HasNetworkPlaylistResults => NetworkPlaylistResults?.Count > 0;
        public bool HasNetworkSongResults => NetworkSongResults?.Count > 0;
        public bool HasNoResults => HasSearched && !HasSongResults && !HasUserPlaylistResults && !HasNetworkPlaylistResults && !HasNetworkSongResults && !IsSearching;

        public ICommand SearchCommand { get; }
        public ICommand PlaySongCommand { get; }
        public ICommand PlayPlaylistCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand ToggleFavoriteCommand { get; }
        public ICommand AddToPlaylistCommand { get; }
        public ICommand SelectPlaylistForSongCommand { get; }
        public ICommand CloseAddToPlaylistDialogCommand { get; }

        private async Task SearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return;

            IsSearching = true;
            HasSearched = true;

            SongResults = await _libraryService.SearchSongsAsync(SearchText);
            UserPlaylistResults = await _libraryService.SearchPlaylistsAsync(SearchText);

            NotifyResultChanged();

            if (MusicSearchCacheService.Instance.TryGetSearchResult(SearchText, out var cached))
            {
                NetworkSongResults = cached.Songs;
                NetworkPlaylistResults = cached.Playlists;
                NotifyResultChanged();
                IsSearching = false;
                return;
            }

            var selector = new MusicSourceSelector();
            NetworkSongResults = await selector.SearchWithAllSources(SearchText, 30);
            try
            {
                NetworkPlaylistResults = await NeteaseMusicApiService.Instance.SearchPlaylistsAsync(SearchText, 20);
            }
            catch { NetworkPlaylistResults = new List<Playlist>(); }
            MusicSearchCacheService.Instance.SetSearchResult(SearchText, NetworkSongResults, NetworkPlaylistResults);

            NotifyResultChanged();
            IsSearching = false;
        }

        private void NotifyResultChanged()
        {
            OnPropertyChanged(nameof(HasSongResults));
            OnPropertyChanged(nameof(HasUserPlaylistResults));
            OnPropertyChanged(nameof(HasNetworkPlaylistResults));
            OnPropertyChanged(nameof(HasNetworkSongResults));
            OnPropertyChanged(nameof(HasNoResults));
        }

        private void PlaySong(Song song)
        {
            if (song == null) return;
            _libraryService.EnsureSongInLibrary(song);
            _playerService.Play(song);
        }

        private void PlayPlaylist(Playlist playlist)
        {
            if (playlist == null) return;
            _ = PlayPlaylistAsync(playlist);
        }

        private async Task PlayPlaylistAsync(Playlist playlist)
        {
            if (playlist == null) return;

            var songs = await _libraryService.GetPlaylistSongsAsync(playlist.Id);
            if (songs.Count > 0)
            {
                _playerService.PlayAll(songs);
                return;
            }

            if (!playlist.Id.StartsWith("netease_pl_")) return;

            if (MusicSearchCacheService.Instance.TryGetPlaylistSongs(playlist.Id, out var cachedSongs))
            {
                if (cachedSongs.Count > 0)
                {
                    _playerService.PlayAll(cachedSongs);
                    return;
                }
            }

            IsLoadingPlaylist = true;
            LoadingPlaylistName = playlist.Name ?? "歌单";

            try
            {
                var neteaseSongs = await NeteaseMusicApiService.Instance.GetPlaylistSongsWithAudioUrlsAsync(playlist.Id);
                if (neteaseSongs.Count > 0)
                {
                    MusicSearchCacheService.Instance.SetPlaylistSongs(playlist.Id, neteaseSongs);
                    _playerService.PlayAll(neteaseSongs);
                }
            }
            catch
            {
            }

            IsLoadingPlaylist = false;
            LoadingPlaylistName = "";
        }

        private void ToggleFavorite(Song song)
        {
            if (song == null) return;
            _libraryService.EnsureSongInLibrary(song);
            _libraryService.ToggleSongFavorite(song.Id);
        }

        private void OpenAddToPlaylistDialog(Song song)
        {
            if (song == null) return;
            _selectedSongForPlaylist = song;
            _libraryService.EnsureSongInLibrary(song);
            AvailablePlaylists = _libraryService.GetAllPlaylists();
            IsAddToPlaylistDialogOpen = true;
        }

        private async Task SelectPlaylistForSongAsync(Playlist playlist)
        {
            if (playlist == null || _selectedSongForPlaylist == null) return;
            await _libraryService.EnsureSongInLibraryAsync(_selectedSongForPlaylist);
            await _libraryService.AddSongToPlaylistAsync(playlist.Id, _selectedSongForPlaylist.Id);
            IsAddToPlaylistDialogOpen = false;
            _selectedSongForPlaylist = null;
        }
    }
}
