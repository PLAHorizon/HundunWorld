using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
        private ObservableCollection<PlaylistSongEntry> _playlistSongs;
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

            // 批量选择相关命令
            SelectAllCommand = new RelayCommand(SelectAll);
            DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync);
            AddSelectedToQueueCommand = new RelayCommand(AddSelectedToQueue);
            ToggleSongSelectionCommand = new RelayCommand<PlaylistSongEntry>(ToggleSongSelection);

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

        /// <summary>当前歌单的歌曲条目集合（带序号与选中状态）。</summary>
        public ObservableCollection<PlaylistSongEntry> PlaylistSongs
        {
            get => _playlistSongs;
            set
            {
                if (SetProperty(ref _playlistSongs, value))
                {
                    OnPropertyChanged(nameof(TotalDurationText));
                    OnPropertyChanged(nameof(HasSongs));
                    OnSongSelectionChanged();
                }
            }
        }

        /// <summary>当前已选中的歌曲数量（派生属性）。</summary>
        public int SelectedCount => PlaylistSongs?.Count(x => x.IsSelected) ?? 0;

        /// <summary>是否处于批量操作模式：存在选中项时为 true（派生属性）。</summary>
        public bool IsBatchMode => SelectedCount > 0;

        /// <summary>当前歌单是否存在歌曲（派生属性，用于空状态提示）。</summary>
        public bool HasSongs => PlaylistSongs != null && PlaylistSongs.Count > 0;

        /// <summary>歌单总时长文本，格式"N 小时 N 分"（派生属性）。</summary>
        public string TotalDurationText
        {
            get
            {
                if (PlaylistSongs == null || PlaylistSongs.Count == 0) return "0 分";
                var total = TimeSpan.FromSeconds(PlaylistSongs.Sum(e => e.Song.Duration.TotalSeconds));
                var hours = (int)total.TotalHours;
                var mins = total.Minutes;
                return hours > 0 ? $"{hours} 小时 {mins} 分" : $"{mins} 分";
            }
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

        /// <summary>全选/取消全选当前歌单歌曲。</summary>
        public ICommand SelectAllCommand { get; }
        /// <summary>删除所有选中的歌曲。</summary>
        public ICommand DeleteSelectedCommand { get; }
        /// <summary>将选中歌曲添加到播放队列。</summary>
        public ICommand AddSelectedToQueueCommand { get; }
        /// <summary>切换单个歌曲条目的选中状态。</summary>
        public ICommand ToggleSongSelectionCommand { get; }

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
                var songs = await _libraryService.GetPlaylistSongsAsync(playlist.Id);
                BuildPlaylistSongs(songs);
            }
            else
            {
                PlaylistSongs = null;
            }
        }

        /// <summary>将原始歌曲列表包装为带序号与选中状态的条目集合。</summary>
        private void BuildPlaylistSongs(List<Song> songs)
        {
            // 解除旧集合的事件订阅，避免内存泄漏
            if (_playlistSongs != null)
            {
                foreach (var entry in _playlistSongs)
                    entry.PropertyChanged -= OnEntryPropertyChanged;
            }

            var entries = new ObservableCollection<PlaylistSongEntry>();
            if (songs != null)
            {
                int index = 1;
                foreach (var song in songs)
                {
                    var entry = new PlaylistSongEntry { Index = index++, Song = song };
                    entry.PropertyChanged += OnEntryPropertyChanged;
                    entries.Add(entry);
                }
            }
            PlaylistSongs = entries;
        }

        /// <summary>歌曲条目属性变化时同步刷新选中相关派生属性。</summary>
        private void OnEntryPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlaylistSongEntry.IsSelected))
            {
                OnSongSelectionChanged();
            }
        }

        /// <summary>刷新选中数量与批量模式状态（供外部/绑定调用）。</summary>
        public void OnSongSelectionChanged()
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(IsBatchMode));
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
            var songs = await _libraryService.GetPlaylistSongsAsync(SelectedPlaylist.Id);
            BuildPlaylistSongs(songs);
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
            var songs = await _libraryService.GetPlaylistSongsAsync(SelectedPlaylist.Id);
            BuildPlaylistSongs(songs);
            Playlists = await _libraryService.GetAllPlaylistsAsync();
            AvailableSongs = AvailableSongs.Where(s => s.Id != song.Id).ToList();
        }

        private void ToggleSongFavorite(Song song)
        {
            if (song == null) return;
            _libraryService.ToggleSongFavorite(song.Id);
            if (PlaylistSongs != null && SelectedPlaylist != null)
            {
                BuildPlaylistSongs(_libraryService.GetPlaylistSongs(SelectedPlaylist.Id));
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
                var songs = await _libraryService.GetPlaylistSongsAsync(SelectedPlaylist.Id);
                BuildPlaylistSongs(songs);
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

        /// <summary>全选/取消全选切换：若已全部选中则取消全选，否则全选。</summary>
        private void SelectAll()
        {
            if (PlaylistSongs == null || PlaylistSongs.Count == 0) return;
            var allSelected = PlaylistSongs.All(x => x.IsSelected);
            foreach (var entry in PlaylistSongs)
            {
                entry.IsSelected = !allSelected;
            }
            OnSongSelectionChanged();
        }

        /// <summary>删除所有选中的歌曲。</summary>
        private async Task DeleteSelectedAsync()
        {
            if (PlaylistSongs == null || SelectedPlaylist == null) return;
            var selected = PlaylistSongs.Where(x => x.IsSelected).Select(x => x.Song).ToList();
            if (selected.Count == 0) return;
            foreach (var song in selected)
            {
                _libraryService.RemoveSongFromPlaylist(SelectedPlaylist.Id, song.Id);
            }
            var songs = await _libraryService.GetPlaylistSongsAsync(SelectedPlaylist.Id);
            BuildPlaylistSongs(songs);
            Playlists = await _libraryService.GetAllPlaylistsAsync();
        }

        /// <summary>将选中歌曲添加到播放队列。</summary>
        private void AddSelectedToQueue()
        {
            if (PlaylistSongs == null) return;
            var selected = PlaylistSongs.Where(x => x.IsSelected).Select(x => x.Song).ToList();
            if (selected.Count == 0) return;
            foreach (var song in selected)
            {
                _playerService.Queue.AddSong(song);
            }
        }

        /// <summary>切换单个歌曲条目的选中状态。</summary>
        private void ToggleSongSelection(PlaylistSongEntry entry)
        {
            if (entry == null) return;
            entry.IsSelected = !entry.IsSelected;
            OnSongSelectionChanged();
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
