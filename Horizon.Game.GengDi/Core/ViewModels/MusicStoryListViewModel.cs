using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class MusicStoryListViewModel : ViewModelBase
    {
        private readonly MusicLibraryService _libraryService;
        private readonly MusicStoryService _storyService;
        private List<Song> _storySongs;
        private bool _isLoading;

        // ===== 内联详情相关字段 =====
        private bool _isDetailOpen;
        private Song _selectedStorySong;
        private MusicStory _currentStory;
        private bool _isDetailLoading;
        private List<Song> _relatedSongs;
        private List<int> _waveformPlayedBars;
        private List<int> _waveformRemainingBars;

        public MusicStoryListViewModel()
        {
            _libraryService = MusicLibraryService.Instance;
            _storyService = MusicStoryService.Instance;
            LoadStorySongsCommand = new AsyncRelayCommand(LoadStorySongsAsync);
            // 点击卡片：加载故事并显示内联详情
            OpenStoryDetailCommand = new AsyncRelayCommand<Song>(OpenStoryDetailAsync);
            // 关闭内联详情
            CloseDetailCommand = new RelayCommand(CloseDetail);
            _ = LoadStorySongsAsync();
        }

        public List<Song> StorySongs
        {
            get => _storySongs;
            set => SetProperty(ref _storySongs, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool HasNoStories => StorySongs == null || StorySongs.Count == 0;

        public ICommand LoadStorySongsCommand { get; }

        // ================================================================
        //  内联详情区属性
        // ================================================================

        /// <summary>是否显示内联详情视图（显示在列表下方）</summary>
        public bool IsDetailOpen
        {
            get => _isDetailOpen;
            set => SetProperty(ref _isDetailOpen, value);
        }

        /// <summary>选中的故事歌曲</summary>
        public Song SelectedStorySong
        {
            get => _selectedStorySong;
            set => SetProperty(ref _selectedStorySong, value);
        }

        /// <summary>当前故事详情</summary>
        public MusicStory CurrentStory
        {
            get => _currentStory;
            set
            {
                if (SetProperty(ref _currentStory, value))
                {
                    // 故事变化时刷新所有派生属性
                    OnPropertyChanged(nameof(HasDetailStory));
                    OnPropertyChanged(nameof(DetailTitle));
                    OnPropertyChanged(nameof(DetailArtist));
                    OnPropertyChanged(nameof(DetailSummary));
                    OnPropertyChanged(nameof(DetailSections));
                    OnPropertyChanged(nameof(DetailDate));
                }
            }
        }

        /// <summary>详情加载中</summary>
        public bool IsDetailLoading
        {
            get => _isDetailLoading;
            set => SetProperty(ref _isDetailLoading, value);
        }

        /// <summary>是否有故事内容（Sections 非空）</summary>
        public bool HasDetailStory => _currentStory != null
            && _currentStory.Sections != null
            && _currentStory.Sections.Count > 0;

        /// <summary>详情标题（优先取故事标题，回退到歌曲标题）</summary>
        public string DetailTitle => _currentStory?.SongTitle ?? _selectedStorySong?.Title ?? "";

        /// <summary>详情艺术家</summary>
        public string DetailArtist => _currentStory?.ArtistName ?? _selectedStorySong?.DisplayArtist ?? "";

        /// <summary>详情摘要（副标题）</summary>
        public string DetailSummary => _currentStory?.Summary ?? "";

        /// <summary>故事作者（默认"龙"）</summary>
        public string DetailAuthor => "龙";

        /// <summary>发布日期文本</summary>
        public string DetailDate
        {
            get
            {
                if (_currentStory != null && _currentStory.FetchedAt != default)
                {
                    return _currentStory.FetchedAt.ToString("yyyy-MM-dd");
                }
                return "2025-06-15";
            }
        }

        /// <summary>阅读量文本</summary>
        public string DetailReadCount => "3.2万";

        /// <summary>详情正文节点列表</summary>
        public List<MusicStorySection> DetailSections =>
            _currentStory?.Sections ?? new List<MusicStorySection>();

        /// <summary>关联歌曲推荐（取前3个非当前选中歌曲）</summary>
        public List<Song> RelatedSongs
        {
            get => _relatedSongs;
            set => SetProperty(ref _relatedSongs, value);
        }

        /// <summary>波形条-已播放部分高度列表（主色）</summary>
        public List<int> WaveformPlayedBars
        {
            get => _waveformPlayedBars;
            set => SetProperty(ref _waveformPlayedBars, value);
        }

        /// <summary>波形条-未播放部分高度列表（灰色）</summary>
        public List<int> WaveformRemainingBars
        {
            get => _waveformRemainingBars;
            set => SetProperty(ref _waveformRemainingBars, value);
        }

        /// <summary>点击卡片：打开内联详情</summary>
        public AsyncRelayCommand<Song> OpenStoryDetailCommand { get; }

        /// <summary>关闭内联详情</summary>
        public ICommand CloseDetailCommand { get; }

        private async Task LoadStorySongsAsync()
        {
            IsLoading = true;
            var allSongs = _libraryService.GetAllSongs();
            var storySongs = new List<Song>();
            var batchSize = 10;

            for (int i = 0; i < allSongs.Count; i += batchSize)
            {
                var batch = allSongs.Skip(i).Take(batchSize);
                var tasks = batch.Select(async song =>
                {
                    var story = await _storyService.GetStoryAsync(song);
                    return (Song: song, HasStory: story != null && story.Sections.Count > 0);
                });

                var results = await Task.WhenAll(tasks);
                foreach (var result in results)
                {
                    if (result.HasStory)
                    {
                        storySongs.Add(result.Song);
                    }
                }

                if (storySongs.Count >= 50) break;
            }

            StorySongs = storySongs.Take(50).ToList();
            IsLoading = false;
        }

        /// <summary>点击故事卡片：设置选中歌曲、打开详情、加载故事</summary>
        private async Task OpenStoryDetailAsync(Song song)
        {
            if (song == null) return;

            SelectedStorySong = song;
            IsDetailOpen = true;
            IsDetailLoading = true;
            CurrentStory = null;

            // 刷新依赖 SelectedStorySong 的派生属性（标题/艺术家在故事加载前先回退到歌曲信息）
            OnPropertyChanged(nameof(DetailTitle));
            OnPropertyChanged(nameof(DetailArtist));

            // 计算关联歌曲推荐（排除当前歌曲，取前3）
            RelatedSongs = StorySongs?
                .Where(s => s.Id != song.Id)
                .Take(3)
                .ToList() ?? new List<Song>();

            // 生成静态波形条数据（共 24 条，高度 6~28 随机，前 9 条为已播放主色，后 15 条为未播放灰色）
            var random = new System.Random(song?.GetHashCode() ?? 0);
            var played = new List<int>();
            for (int i = 0; i < 9; i++)
            {
                played.Add(random.Next(6, 29));
            }
            var remaining = new List<int>();
            for (int i = 0; i < 15; i++)
            {
                remaining.Add(random.Next(6, 29));
            }
            WaveformPlayedBars = played;
            WaveformRemainingBars = remaining;

            try
            {
                CurrentStory = await _storyService.GetStoryAsync(song);
            }
            catch
            {
                CurrentStory = null;
            }

            IsDetailLoading = false;
        }

        /// <summary>关闭内联详情</summary>
        private void CloseDetail()
        {
            IsDetailOpen = false;
        }
    }
}
