using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Controls;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class GamesViewModel : ViewModelBase
    {
        private const string SampleGameDownloadUrl = "https://codeload.github.com/PLAHorizon/ExHyperV/zip/refs/heads/main";
        private const string DeprecatedSampleDownloadHost = "github.com/PLAHorizon/LongAI";
        private const string DeprecatedSampleCodeloadHost = "codeload.github.com/PLAHorizon/LongAI";

        private static readonly IReadOnlyDictionary<string, GameInfo> SampleGameMetadata =
            BuildSampleGames().ToDictionary(game => game.Id, StringComparer.Ordinal);
        private readonly GameService _gameService;
        private readonly AsyncRelayCommand<GameInfo> _installGameCommand;
        private readonly AsyncRelayCommand<GameInfo> _updateGameCommand;
        private readonly AsyncRelayCommand<GameInfo> _uninstallGameCommand;
        private readonly AsyncRelayCommand<GameInfo> _startGameCommand;
        private List<GameInfo> _allGames;
        private List<GameInfo> _installedGames;
        private GameInfo _selectedGame;
        private bool _isLoading;
        private string _loadingMessage;
        private bool _isInitialized;
        private double _updateProgressPercent;

        public List<GameInfo> AllGames
        {
            get => _allGames;
            set => SetProperty(ref _allGames, value);
        }

        public List<GameInfo> InstalledGames
        {
            get => _installedGames;
            set => SetProperty(ref _installedGames, value);
        }

        public GameInfo SelectedGame
        {
            get => _selectedGame;
            set
            {
                // 忽略 UI (ListBox) 因焦点切换等导致的 null 赋值
                // 防止两个列表框公用一个绑定时，一个未选中的列表通过双向绑定清空此值
                if (value != null)
                {
                    SetProperty(ref _selectedGame, value);
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    _installGameCommand.RaiseCanExecuteChanged();
                    _updateGameCommand.RaiseCanExecuteChanged();
                    _uninstallGameCommand.RaiseCanExecuteChanged();
                    _startGameCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string LoadingMessage
        {
            get => _loadingMessage;
            set => SetProperty(ref _loadingMessage, value);
        }

        public double UpdateProgressPercent
        {
            get => _updateProgressPercent;
            set => SetProperty(ref _updateProgressPercent, value);
        }

        public ICommand InstallGameCommand { get; }
        public ICommand UpdateGameCommand { get; }
        public ICommand UninstallGameCommand { get; }
        public ICommand StartGameCommand { get; }

        public GamesViewModel()
        {
            _gameService = GameService.Instance;
            _allGames = new List<GameInfo>();
            _installedGames = new List<GameInfo>();
            
            _installGameCommand = new AsyncRelayCommand<GameInfo>(InstallGameAsync, CanMutateGame);
            _updateGameCommand = new AsyncRelayCommand<GameInfo>(UpdateGameAsync, CanMutateGame);
            _uninstallGameCommand = new AsyncRelayCommand<GameInfo>(UninstallGameAsync, CanMutateGame);
            _startGameCommand = new AsyncRelayCommand<GameInfo>(StartGameAsync, CanMutateGame);

            _gameService.UpdateProgressChanged += OnUpdateProgressChanged;

            InstallGameCommand = _installGameCommand;
            UpdateGameCommand = _updateGameCommand;
            UninstallGameCommand = _uninstallGameCommand;
            StartGameCommand = _startGameCommand;
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized)
            {
                return;
            }

            _isInitialized = true;
            await LoadGames();
        }

        public async Task LoadGames()
        {
            IsLoading = true;
            LoadingMessage = "加载游戏列表...";
            var selectedGameId = SelectedGame?.Id;

            try
            {
                // 异步加载游戏数据，避免阻塞UI线程
                AllGames = await _gameService.GetAllGamesAsync();
                InstalledGames = await _gameService.GetInstalledGamesAsync();
                ApplySampleMetadata(AllGames);
                ApplySampleMetadata(InstalledGames);

                // 如果没有游戏，添加一些示例游戏
                if (AllGames.Count == 0)
                {
                    await AddSampleGames();
                }
                
                // 重新绑定所选游戏，确保按钮与错误提示使用最新状态。
                if (AllGames.Count > 0)
                {
                    var newSelectedGame = !string.IsNullOrWhiteSpace(selectedGameId)
                        ? AllGames.Find(game => string.Equals(game.Id, selectedGameId, StringComparison.Ordinal))
                            ?? AllGames[0]
                        : AllGames[0];
                    
                    SetProperty(ref _selectedGame, newSelectedGame, nameof(SelectedGame));
                }
                else
                {
                    SetProperty(ref _selectedGame, null, nameof(SelectedGame));
                }
            }
            catch (Exception)
            {
                LoadingMessage = "加载游戏失败";
            }
            finally
            {
                IsLoading = false;
                LoadingMessage = string.Empty;
            }
        }

        private async Task AddSampleGames()
        {
            // 在后台线程执行数据库操作，避免阻塞UI线程
            var sampleGames = BuildSampleGames();

            await _gameService.AddGamesAsync(sampleGames);
            GameService.Instance.ClearCache();

            // 重新加载游戏列表
            await LoadGames();
        }

        private static void ApplySampleMetadata(IEnumerable<GameInfo> games)
        {
            foreach (var game in games)
            {
                if (game == null || string.IsNullOrWhiteSpace(game.Id) || !SampleGameMetadata.TryGetValue(game.Id, out var sampleGame))
                {
                    continue;
                }

                game.CoverImage ??= sampleGame.CoverImage;
                game.IconImage ??= sampleGame.IconImage;
                game.PopularityBadge ??= sampleGame.PopularityBadge;
                game.OnlinePlayerCount = game.OnlinePlayerCount > 0 ? game.OnlinePlayerCount : sampleGame.OnlinePlayerCount;
                game.PassportLoginCount = game.PassportLoginCount > 0 ? game.PassportLoginCount : sampleGame.PassportLoginCount;
                game.CharacterEnterCount = game.CharacterEnterCount > 0 ? game.CharacterEnterCount : sampleGame.CharacterEnterCount;

                if (string.IsNullOrWhiteSpace(game.DownloadUrl)
                    || game.DownloadUrl.IndexOf(DeprecatedSampleDownloadHost, StringComparison.OrdinalIgnoreCase) >= 0
                    || game.DownloadUrl.IndexOf(DeprecatedSampleCodeloadHost, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    game.DownloadUrl = sampleGame.DownloadUrl;
                }
            }
        }

        private static List<GameInfo> BuildSampleGames()
        {
            return new List<GameInfo>
            {
                new GameInfo
                {
                    Id = "1",
                    Name = "Horizon Adventure",
                    Description = "一款开放世界冒险游戏，探索神秘的大陆，与各种生物战斗，完成任务。",
                    CoverImage = "https://trae-api-cn.mchost.guru/api/ide/v1/text_to_image?prompt=horizon%20adventure%20game%20cover%20art%20fantasy%20open%20world&image_size=landscape_16_9",
                    IconImage = "https://trae-api-cn.mchost.guru/api/ide/v1/text_to_image?prompt=horizon%20adventure%20game%20icon%20fantasy%20compass&image_size=square",
                    Developer = "Horizon Studios",
                    Publisher = "Horizon Studios",
                    ReleaseDate = new DateTime(2023, 12, 15),
                    Category = "Adventure",
                    PopularityBadge = "超热",
                    OnlinePlayerCount = 823,
                    PassportLoginCount = 28456,
                    CharacterEnterCount = 30128,
                    IsInstalled = false,
                    IsRecommended = true,
                    DownloadUrl = SampleGameDownloadUrl,
                    GameId = 1001,
                    AppType = 369,
                    AreaId = 1,
                    ServerId = 1,
                    Screenshots = new List<string>
                    {
                        "https://trae-api-cn.mchost.guru/api/ide/v1/text_to_image?prompt=horizon%20adventure%20game%20screenshot%201%20fantasy%20landscape&image_size=landscape_16_9",
                        "https://trae-api-cn.mchost.guru/api/ide/v1/text_to_image?prompt=horizon%20adventure%20game%20screenshot%202%20combat%20scene&image_size=landscape_16_9"
                    }
                },
                new GameInfo
                {
                    Id = "2",
                    Name = "Space Explorer",
                    Description = "一款太空探索游戏，驾驶宇宙飞船探索未知的星系，发现新的星球和文明。",
                    CoverImage = "https://trae-api-cn.mchost.guru/api/ide/v1/text_to_image?prompt=space%20explorer%20game%20cover%20art%20sci-fi%20space%20ship&image_size=landscape_16_9",
                    IconImage = "https://trae-api-cn.mchost.guru/api/ide/v1/text_to_image?prompt=space%20explorer%20game%20icon%20spaceship%20badge&image_size=square",
                    Developer = "Stellar Games",
                    Publisher = "Stellar Games",
                    ReleaseDate = new DateTime(2024, 3, 10),
                    Category = "Sci-Fi",
                    PopularityBadge = "热门",
                    OnlinePlayerCount = 4820,
                    PassportLoginCount = 19876,
                    CharacterEnterCount = 21330,
                    IsInstalled = false,
                    IsRecommended = true,
                    DownloadUrl = SampleGameDownloadUrl,
                    GameId = 1002,
                    AppType = 369,
                    AreaId = 1,
                    ServerId = 1,
                    Screenshots = new List<string>
                    {
                        "https://trae-api-cn.mchost.guru/api/ide/v1/text_to_image?prompt=space%20explorer%20game%20screenshot%201%20space%20station&image_size=landscape_16_9",
                        "https://trae-api-cn.mchost.guru/api/ide/v1/text_to_image?prompt=space%20explorer%20game%20screenshot%202%20planet%20surface&image_size=landscape_16_9"
                    }
                },
                new GameInfo
                {
                    Id = "3",
                    Name = "Racing Master",
                    Description = "一款竞速游戏，驾驶各种高性能赛车在不同的赛道上比赛，挑战极限速度。",
                    CoverImage = "https://trae-api-cn.mchost.guru/api/ide/v1/text_to_image?prompt=racing%20master%20game%20cover%20art%20sports%20car%20racing&image_size=landscape_16_9",
                    IconImage = "https://trae-api-cn.mchost.guru/api/ide/v1/text_to_image?prompt=racing%20master%20game%20icon%20racing%20helmet&image_size=square",
                    Developer = "Speed Studios",
                    Publisher = "Speed Studios",
                    ReleaseDate = new DateTime(2023, 9, 5),
                    Category = "Racing",
                    PopularityBadge = "新秀",
                    OnlinePlayerCount = 12786,
                    PassportLoginCount = 41234,
                    CharacterEnterCount = 43782,
                    IsInstalled = false,
                    IsRecommended = true,
                    DownloadUrl = SampleGameDownloadUrl,
                    GameId = 1003,
                    AppType = 369,
                    AreaId = 1,
                    ServerId = 1,
                    Screenshots = new List<string>
                    {
                        "https://trae-api-cn.mchost.guru/api/ide/v1/text_to_image?prompt=racing%20master%20game%20screenshot%201%20city%20track&image_size=landscape_16_9",
                        "https://trae-api-cn.mchost.guru/api/ide/v1/text_to_image?prompt=racing%20master%20game%20screenshot%202%20night%20racing&image_size=landscape_16_9"
                    }
                }
            };
        }

        public async Task InstallGameAsync(GameInfo game)
        {
            IsLoading = true;
            LoadingMessage = "安装游戏中...";

            try
            {
                await _gameService.InstallGameAsync(game);
                await LoadGames();
            }
            catch (Exception)
            {
                LoadingMessage = "安装失败";
            }
            finally
            {
                IsLoading = false;
                LoadingMessage = string.Empty;
            }
        }

        public async Task UpdateGameAsync(GameInfo game)
        {
            IsLoading = true;
            LoadingMessage = "更新游戏中...";

            try
            {
                await _gameService.UpdateGameAsync(game);
                await LoadGames();
            }
            catch (Exception)
            {
                LoadingMessage = "更新失败";
            }
            finally
            {
                IsLoading = false;
                LoadingMessage = string.Empty;
            }
        }

        public async Task UninstallGameAsync(GameInfo game)
        {
            if (game == null) return;

            // 卸载前弹出确认对话框；用户取消则直接返回，不对本地数据做任何变更。
            var confirmed = await Controls.ConfirmDialog.ShowAsync(
                "确认卸载",
                $"卸载{game.Name ?? game.Id}后，本地游戏文件、存档记录和下载缓存都会被清除，是否继续？",
                primaryText: "卸载",
                cancelText: "取消").ConfigureAwait(true);
            if (!confirmed)
            {
                return;
            }

            IsLoading = true;
            LoadingMessage = "卸载游戏中...";

            try
            {
                var cleanupOk = await _gameService.UninstallGameAsync(game);
                await LoadGames();
                if (!cleanupOk)
                {
                    ToastService.Instance.Warning("游戏已卸载，但部分文件无法删除，请手动清理");
                }
            }
            catch (Exception)
            {
                LoadingMessage = "卸载失败";
            }
            finally
            {
                IsLoading = false;
                LoadingMessage = string.Empty;
            }
        }

        public async Task StartGameAsync(GameInfo game)
        {
            if (game == null) return;

            IsLoading = true;
            LoadingMessage = "启动游戏中...";

            try
            {
                await _gameService.StartGame(game);
                await LoadGames();
            }
            catch (Exception)
            {
                LoadingMessage = "游戏启动失败";
            }
            finally
            {
                IsLoading = false;
                LoadingMessage = string.Empty;
            }
        }

        public void SelectGame(GameInfo game)
        {
            SetProperty(ref _selectedGame, game, nameof(SelectedGame));
        }

        private void OnUpdateProgressChanged(object sender, UpdateProgress progress)
        {
            UpdateProgressPercent = progress.OverallPercent;
            LoadingMessage = $"更新游戏中... {progress.OverallPercent:F0}%";
        }

        private bool CanMutateGame()
        {
            return !IsLoading;
        }
    }
}
