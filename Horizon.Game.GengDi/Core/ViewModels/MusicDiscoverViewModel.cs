using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Enums;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class MusicDiscoverViewModel : ViewModelBase
    {
        private readonly MusicPlayerService _playerService;
        private readonly MusicLibraryService _libraryService;
        private List<Song> _recommendedSongs;
        private List<Song> _hotSongs;
        private List<Song> _newSongs;
        private List<Song> _risingSongs;
        private List<Playlist> _networkPlaylists;
        private MusicRankingType _selectedRankingType = MusicRankingType.Hot;
        private bool _isLoading;
        private bool _isNetworkAvailable;
        private LocalMusicImportViewModel _localImportViewModel;

        public MusicDiscoverViewModel()
        {
            _playerService = MusicPlayerService.Instance;
            _libraryService = MusicLibraryService.Instance;

            PlaySongCommand = new RelayCommand<Song>(PlaySong);
            PlayAllCommand = new RelayCommand<Song>(song => PlaySong(song));
            SelectRankingTypeCommand = new RelayCommand<MusicRankingType>(SelectRankingType);
            PlayPlaylistCommand = new RelayCommand<Playlist>(PlayPlaylist);
            OpenLocalImportDialogCommand = new RelayCommand(OpenLocalImportDialog);
        }

        public List<Song> RecommendedSongs
        {
            get => _recommendedSongs;
            set => SetProperty(ref _recommendedSongs, value);
        }

        public List<Song> HotSongs
        {
            get => _hotSongs;
            set => SetProperty(ref _hotSongs, value);
        }

        public List<Song> NewSongs
        {
            get => _newSongs;
            set => SetProperty(ref _newSongs, value);
        }

        public List<Song> RisingSongs
        {
            get => _risingSongs;
            set => SetProperty(ref _risingSongs, value);
        }

        public List<Playlist> NetworkPlaylists
        {
            get => _networkPlaylists;
            set { SetProperty(ref _networkPlaylists, value); OnPropertyChanged(nameof(HasNetworkPlaylists)); }
        }

        public MusicRankingType SelectedRankingType
        {
            get => _selectedRankingType;
            set => SetProperty(ref _selectedRankingType, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool IsNetworkAvailable
        {
            get => _isNetworkAvailable;
            set => SetProperty(ref _isNetworkAvailable, value);
        }

        public LocalMusicImportViewModel LocalImportViewModel
        {
            get => _localImportViewModel;
            set => SetProperty(ref _localImportViewModel, value);
        }

        public bool HasNetworkPlaylists => _networkPlaylists?.Count > 0;

        public List<Song> CurrentRankingSongs => _selectedRankingType switch
        {
            MusicRankingType.Hot => HotSongs,
            MusicRankingType.New => NewSongs,
            MusicRankingType.Rising => RisingSongs,
            _ => HotSongs
        };

        public ICommand PlaySongCommand { get; }
        public ICommand PlayAllCommand { get; }
        public ICommand SelectRankingTypeCommand { get; }
        public ICommand PlayPlaylistCommand { get; }
        public ICommand OpenLocalImportDialogCommand { get; }

        public async void Initialize()
        {
            IsLoading = true;
            try
            {
                try
                {
                    var selector = new MusicSourceSelector();
                    var newSongsTask = selector.SearchWithAllSources("热门歌曲", 30);
                    var playlistsTask = NeteaseMusicApiService.Instance.GetTopPlaylistsAsync(12);

                    var newSongs = await newSongsTask;
                    var playlists = await playlistsTask;

                    if (newSongs.Count > 0)
                    {
                        IsNetworkAvailable = true;
                        RecommendedSongs = newSongs.Take(6).ToList();
                        HotSongs = newSongs.OrderByDescending(s => s.PlayCount).ToList();
                        NewSongs = newSongs;
                        RisingSongs = newSongs.OrderBy(_ => Guid.NewGuid()).ToList();
                    }

                    if (playlists.Count > 0)
                    {
                        IsNetworkAvailable = true;
                        NetworkPlaylists = playlists;
                    }
                }
                catch
                {
                    IsNetworkAvailable = false;
                }

                if (HotSongs == null || HotSongs.Count == 0)
                {
                    var songs = await _libraryService.GetAllSongsAsync();
                    if (songs == null || songs.Count == 0)
                    {
                        songs = BuildSampleSongs();
                    }
                    RecommendedSongs = songs.Take(6).ToList();
                    HotSongs = songs.OrderByDescending(s => s.PlayCount).ToList();
                    NewSongs = songs.OrderByDescending(s => s.AddedDate).ToList();
                    RisingSongs = songs.OrderBy(_ => Guid.NewGuid()).ToList();
                }

                OnPropertyChanged(nameof(CurrentRankingSongs));
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void OpenLocalImportDialog()
        {
            LocalImportViewModel = new LocalMusicImportViewModel();
            LocalImportViewModel.OpenDialog();
        }

        private void PlaySong(Song song)
        {
            if (song == null) return;
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
            if (songs.Count > 0) { _playerService.PlayAll(songs); return; }

            if (!playlist.Id.StartsWith("netease_pl_")) return;

            if (MusicSearchCacheService.Instance.TryGetPlaylistSongs(playlist.Id, out var cached))
            {
                if (cached.Count > 0) { _playerService.PlayAll(cached); return; }
            }

            try
            {
                var neteaseSongs = await NeteaseMusicApiService.Instance.GetPlaylistSongsWithAudioUrlsAsync(playlist.Id);
                if (neteaseSongs.Count > 0)
                {
                    MusicSearchCacheService.Instance.SetPlaylistSongs(playlist.Id, neteaseSongs);
                    _playerService.PlayAll(neteaseSongs);
                }
            }
            catch { }
        }

        private void SelectRankingType(MusicRankingType type)
        {
            SelectedRankingType = type;
            OnPropertyChanged(nameof(CurrentRankingSongs));
        }

        private static List<Song> BuildSampleSongs()
        {
            var songs = new List<Song>();
            var titles = new[] { "星辰大海", "晚风", "追光者", "起风了", "光年之外", "晴天", "稻香", "七里香", "简单爱", "夜曲" };
            var artists = new[] { "黄霄雲", "陈婧霏", "岑宁儿", "买辣椒也用券", "邓紫棋", "周杰伦", "周杰伦", "周杰伦", "周杰伦", "周杰伦" };

            for (int i = 0; i < titles.Length; i++)
            {
                songs.Add(new Song
                {
                    Id = $"song_{i + 1}",
                    Title = titles[i],
                    ArtistId = $"artist_{(i % 3) + 1}",
                    ArtistName = artists[i],
                    AlbumId = $"album_{(i % 4) + 1}",
                    AlbumName = $"{titles[i]} - Single",
                    Duration = TimeSpan.FromMinutes(3 + i % 3).Add(TimeSpan.FromSeconds(15 + i * 7)),
                    CoverUrl = string.Empty,
                    AudioUrl = string.Empty,
                    AddedDate = DateTime.UtcNow.AddDays(-i * 3),
                    PlayCount = 10000 - i * 800,
                    IsFavorite = i < 3
                });
            }
            return songs;
        }
    }
}
