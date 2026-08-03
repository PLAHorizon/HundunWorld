using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    /// <summary>
    /// 搜索结果分类标签枚举。
    /// </summary>
    public enum SearchResultTab
    {
        Song,
        Playlist,
        Artist
    }

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
        private List<string> _searchHistory;
        private SearchResultTab _selectedResultTab;
        private string _searchKeyword;
        private List<ArtistGroup> _artistResults;

        public MusicSearchViewModel()
        {
            _libraryService = MusicLibraryService.Instance;
            _playerService = MusicPlayerService.Instance;

            // 热门搜索词（静态）
            var hotKeywords = new List<string>
            {
                "星河入梦", "古风合集", "深夜电台", "云水谣", "助眠白噪音",
                "长河落日", "民谣", "影视原声", "山川客", "轻音乐"
            };
            HotSearchKeywords = hotKeywords;
            // 构建带排名的展示项，前 3 个标记为 top
            HotSearchItems = hotKeywords
                .Select((k, i) => new HotSearchItem { Keyword = k, Rank = i + 1, IsTop = i < 3 })
                .ToList();

            // 历史搜索（初始示例数据）
            SearchHistory = new List<string>
            {
                "星河入梦", "云水谣", "古风", "助眠白噪音", "山川客", "长河落日"
            };

            SelectedResultTab = SearchResultTab.Song;

            SearchCommand = new AsyncRelayCommand(SearchAsync);
            PlaySongCommand = new RelayCommand<Song>(PlaySong);
            PlayPlaylistCommand = new RelayCommand<Playlist>(PlayPlaylist);
            ClearSearchCommand = new RelayCommand(() => { SearchText = string.Empty; HasSearched = false; });
            ToggleFavoriteCommand = new RelayCommand<Song>(ToggleFavorite);
            AddToPlaylistCommand = new RelayCommand<Song>(OpenAddToPlaylistDialog);
            SelectPlaylistForSongCommand = new AsyncRelayCommand<Playlist>(SelectPlaylistForSongAsync);
            CloseAddToPlaylistDialogCommand = new RelayCommand(() => IsAddToPlaylistDialogOpen = false);

            // 用关键词搜索（点击热搜/历史 chip 时触发）
            SearchKeywordCommand = new RelayCommand<string>(SearchByKeyword);
            // 清空历史搜索
            ClearHistoryCommand = new RelayCommand(ClearHistory);
            // 切换结果分类标签
            SelectResultTabCommand = new RelayCommand<SearchResultTab>(SelectResultTab);

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

        /// <summary>热门搜索词原始列表。</summary>
        public List<string> HotSearchKeywords { get; }

        /// <summary>带排名与 top 标记的热搜展示项。</summary>
        public List<HotSearchItem> HotSearchItems { get; }

        /// <summary>历史搜索列表。</summary>
        public List<string> SearchHistory
        {
            get => _searchHistory;
            set => SetProperty(ref _searchHistory, value);
        }

        /// <summary>是否存在历史搜索。</summary>
        public bool HasSearchHistory => SearchHistory?.Count > 0;

        /// <summary>当前选中的结果分类标签。</summary>
        public SearchResultTab SelectedResultTab
        {
            get => _selectedResultTab;
            set
            {
                if (SetProperty(ref _selectedResultTab, value))
                {
                    OnPropertyChanged(nameof(IsSongTab));
                    OnPropertyChanged(nameof(IsPlaylistTab));
                    OnPropertyChanged(nameof(IsArtistTab));
                }
            }
        }

        public bool IsSongTab => SelectedResultTab == SearchResultTab.Song;
        public bool IsPlaylistTab => SelectedResultTab == SearchResultTab.Playlist;
        public bool IsArtistTab => SelectedResultTab == SearchResultTab.Artist;

        /// <summary>当前搜索词（用于结果计数展示）。</summary>
        public string SearchKeyword
        {
            get => _searchKeyword;
            set => SetProperty(ref _searchKeyword, value);
        }

        /// <summary>按艺术家分组的结果（歌手标签页数据）。</summary>
        public List<ArtistGroup> ArtistResults
        {
            get => _artistResults;
            set => SetProperty(ref _artistResults, value);
        }

        public bool HasSongResults => SongResults?.Count > 0;
        public bool HasUserPlaylistResults => UserPlaylistResults?.Count > 0;
        public bool HasNetworkPlaylistResults => NetworkPlaylistResults?.Count > 0;
        public bool HasNetworkSongResults => NetworkSongResults?.Count > 0;
        public bool HasArtistResults => ArtistResults?.Count > 0;
        public bool HasNoResults => HasSearched && !HasSongResults && !HasUserPlaylistResults && !HasNetworkPlaylistResults && !HasNetworkSongResults && !IsSearching;

        /// <summary>歌曲结果总数（本地 + 网络）。</summary>
        public int SongResultCount => (SongResults?.Count ?? 0) + (NetworkSongResults?.Count ?? 0);

        /// <summary>歌单结果总数（我的 + 网络）。</summary>
        public int PlaylistResultCount => (UserPlaylistResults?.Count ?? 0) + (NetworkPlaylistResults?.Count ?? 0);

        /// <summary>歌手结果总数。</summary>
        public int ArtistResultCount => ArtistResults?.Count ?? 0;

        /// <summary>所有结果数之和。</summary>
        public int TotalResultCount => SongResultCount + PlaylistResultCount + ArtistResultCount;

        public ICommand SearchCommand { get; }
        public ICommand PlaySongCommand { get; }
        public ICommand PlayPlaylistCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand ToggleFavoriteCommand { get; }
        public ICommand AddToPlaylistCommand { get; }
        public ICommand SelectPlaylistForSongCommand { get; }
        public ICommand CloseAddToPlaylistDialogCommand { get; }

        public RelayCommand<string> SearchKeywordCommand { get; }
        public RelayCommand ClearHistoryCommand { get; }
        public RelayCommand<SearchResultTab> SelectResultTabCommand { get; }

        /// <summary>用指定关键词发起搜索（热搜/历史 chip 触发）。</summary>
        private async void SearchByKeyword(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return;
            SearchText = keyword;
            await SearchAsync();
        }

        /// <summary>清空历史搜索。</summary>
        private void ClearHistory()
        {
            SearchHistory = new List<string>();
            OnPropertyChanged(nameof(HasSearchHistory));
        }

        /// <summary>切换结果分类标签。</summary>
        private void SelectResultTab(SearchResultTab tab)
        {
            SelectedResultTab = tab;
        }

        private async Task SearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return;

            // 记录当前搜索词用于结果计数展示
            SearchKeyword = SearchText;
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
                AddToHistory(SearchText);
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
            AddToHistory(SearchText);
        }

        /// <summary>搜索成功后将关键词加入历史（去重，最多保留 10 条）。</summary>
        private void AddToHistory(string keyword)
        {
            var history = new List<string>(SearchHistory ?? new List<string>());
            history.Remove(keyword);
            history.Insert(0, keyword);
            if (history.Count > 10) history = history.Take(10).ToList();
            SearchHistory = history;
            OnPropertyChanged(nameof(HasSearchHistory));
        }

        private void NotifyResultChanged()
        {
            // 构建按艺术家分组的结果
            var allSongs = new List<Song>();
            if (NetworkSongResults != null) allSongs.AddRange(NetworkSongResults);
            if (SongResults != null) allSongs.AddRange(SongResults);
            ArtistResults = allSongs
                .Where(s => s != null)
                .GroupBy(s => s.DisplayArtist)
                .Select(g => new ArtistGroup { ArtistName = g.Key, Songs = g.ToList() })
                .ToList();

            OnPropertyChanged(nameof(HasSongResults));
            OnPropertyChanged(nameof(HasUserPlaylistResults));
            OnPropertyChanged(nameof(HasNetworkPlaylistResults));
            OnPropertyChanged(nameof(HasNetworkSongResults));
            OnPropertyChanged(nameof(HasArtistResults));
            OnPropertyChanged(nameof(HasNoResults));
            OnPropertyChanged(nameof(SongResultCount));
            OnPropertyChanged(nameof(PlaylistResultCount));
            OnPropertyChanged(nameof(ArtistResultCount));
            OnPropertyChanged(nameof(TotalResultCount));
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

    /// <summary>热搜展示项（带排名与 top 标记）。</summary>
    public class HotSearchItem
    {
        public string Keyword { get; set; }
        public int Rank { get; set; }
        public string RankText => Rank.ToString();
        /// <summary>是否为前 3 名（高亮显示）。</summary>
        public bool IsTop { get; set; }
    }

    /// <summary>艺术家分组结果。</summary>
    public class ArtistGroup
    {
        public string ArtistName { get; set; }
        public List<Song> Songs { get; set; }
        public int SongCount => Songs?.Count ?? 0;
    }
}
