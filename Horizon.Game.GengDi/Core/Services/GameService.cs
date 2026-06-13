using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Data.Repositories;
using GameModel = Horizon.Game.GengDi.Models.GameInfo;

namespace Horizon.Game.GengDi.Core.Services
{
    public class GameService
    {
        private static GameService _instance;
        private static readonly object _lock = new object();

        public event EventHandler<UpdateProgress> UpdateProgressChanged;

        private readonly GameRepository _gameRepository;
        private readonly DownloadTaskRepository _downloadTaskRepository;
        private readonly object _cacheLock;
        private List<GameModel> _cachedGames;
        private DateTime _lastCacheUpdate;
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5); // 缓存5分钟

        public static GameService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new GameService();
                        }
                    }
                }
                return _instance;
            }
        }

        private GameService()
        {
            _gameRepository = new GameRepository();
            _downloadTaskRepository = new DownloadTaskRepository();
            _cacheLock = new object();
            _cachedGames = new List<GameModel>();
            _lastCacheUpdate = DateTime.MinValue;
        }

        public async Task InitializeAsync()
        {
            // 游戏初始化逻辑
            await Task.CompletedTask;
        }

        public List<GameModel> GetAllGames()
        {
            lock (_cacheLock)
            {
                if (DateTime.Now - _lastCacheUpdate > _cacheExpiry || _cachedGames.Count == 0)
                {
                    _cachedGames = _gameRepository.GetAll();
                    _lastCacheUpdate = DateTime.Now;
                }

                return _cachedGames.ToList();
            }
        }

        public async Task<List<GameModel>> GetAllGamesAsync()
        {
            lock (_cacheLock)
            {
                if (DateTime.Now - _lastCacheUpdate <= _cacheExpiry && _cachedGames.Count > 0)
                {
                    return _cachedGames.ToList();
                }
            }

            var games = await ClientAsyncDispatcher.RunLiteDbAsync(() => _gameRepository.GetAll()).ConfigureAwait(false);
            lock (_cacheLock)
            {
                _cachedGames = games.ToList();
                _lastCacheUpdate = DateTime.Now;
                return _cachedGames.ToList();
            }
        }

        public List<GameModel> GetInstalledGames()
        {
            // 从缓存中过滤，避免再次查询数据库
            var allGames = GetAllGames();
            return allGames.Where(g => g.IsInstalled).ToList();
        }

        public async Task<List<GameModel>> GetInstalledGamesAsync()
        {
            var allGames = await GetAllGamesAsync().ConfigureAwait(false);
            return allGames.Where(game => game.IsInstalled).ToList();
        }

        public List<GameModel> GetGamesByCategory(string category)
        {
            // 从缓存中过滤，避免再次查询数据库
            var allGames = GetAllGames();
            return allGames.Where(g => g.Category == category).ToList();
        }

        public GameModel GetGameById(string id)
        {
            // 从缓存中查找，避免再次查询数据库
            var allGames = GetAllGames();
            return allGames.FirstOrDefault(g => g.Id == id);
        }

        // 当游戏状态发生变化时，清除缓存
        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _cachedGames = new List<GameModel>();
                _lastCacheUpdate = DateTime.MinValue;
            }
        }

        public Task AddGamesAsync(IEnumerable<GameModel> games)
        {
            return ClientAsyncDispatcher.RunLiteDbAsync(() =>
            {
                foreach (var game in games)
                {
                    _gameRepository.Add(game);
                }
            });
        }

        public async Task InstallGameAsync(GameModel game)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));

            // 防御式校验：仅允许推荐游戏下载 / 安装。UI 层已用 CanInstall 做初筛，此处为最后一道门闩。
            if (!game.IsRecommended)
            {
                throw new InvalidOperationException("仅允许从推荐游戏列表中下载 / 安装此游戏。");
            }
            if (string.IsNullOrWhiteSpace(game.DownloadUrl))
            {
                throw new InvalidOperationException("推荐游戏条目缺少 DownloadUrl，无法启动下载。");
            }

            var download = DownloadService.Instance;
            var install = InstallService.Instance;

            try
            {
                game.LastOperationError = string.Empty;
                game.State = Horizon.Game.GengDi.Enums.GameLifecycleState.Downloading;
                await ClientAsyncDispatcher.RunLiteDbAsync(() => _gameRepository.Update(game)).ConfigureAwait(false);
                ClearCache();

                // --- 下载阶段 ---
                var savePath = download.BuildDefaultPackageSavePath(game.Name ?? game.Id);
                var task = await download.StartDownloadAsync(new DownloadRequest
                {
                    GameId = game.Id,
                    GameName = game.Name,
                    DownloadUrl = game.DownloadUrl,
                    SavePath = savePath,
                    Kind = Horizon.Game.GengDi.Enums.DownloadTaskKind.GameInstall
                }).ConfigureAwait(false);

                var finished = await download.WaitForCompletionAsync(task.Id).ConfigureAwait(false);
                if (finished.Status != Horizon.Game.GengDi.Enums.DownloadStatus.Completed)
                {
                    game.State = finished.Status == Horizon.Game.GengDi.Enums.DownloadStatus.Cancelled
                        ? Horizon.Game.GengDi.Enums.GameLifecycleState.NotInstalled
                        : Horizon.Game.GengDi.Enums.GameLifecycleState.Failed;
                    game.LastOperationError = finished.Status switch
                    {
                        Horizon.Game.GengDi.Enums.DownloadStatus.Cancelled => string.Empty,
                        Horizon.Game.GengDi.Enums.DownloadStatus.Failed => string.IsNullOrWhiteSpace(finished.ErrorMessage)
                            ? "安装包下载失败，请检查网络连接或下载源后重试。"
                            : finished.ErrorMessage,
                        _ => "安装包下载未完成，请重试。"
                    };
                    await ClientAsyncDispatcher.RunLiteDbAsync(() => _gameRepository.Update(game)).ConfigureAwait(false);
                    ClearCache();
                    return;
                }

                // --- 安装阶段 ---
                game.State = Horizon.Game.GengDi.Enums.GameLifecycleState.Installing;
                game.LastOperationError = string.Empty;
                var installRoot = await AppSettingsService.Instance.GetResolvedInstallDirectoryAsync().ConfigureAwait(false);
                game.InstallationPath = Path.Combine(installRoot,game.Name ?? Guid.NewGuid().ToString("N"));
                await ClientAsyncDispatcher.RunLiteDbAsync(() => _gameRepository.Update(game)).ConfigureAwait(false);

                var ok = await install.InstallFromPackageAsync(game, finished.SavePath, default).ConfigureAwait(false);
                if (!ok)
                {
                    game.State = Horizon.Game.GengDi.Enums.GameLifecycleState.Failed;
                    game.IsInstalled = false;
                    if (string.IsNullOrWhiteSpace(game.LastOperationError))
                    {
                        game.LastOperationError = "安装失败，请检查磁盘空间、文件权限或安装包完整性后重试。";
                    }
                    await ClientAsyncDispatcher.RunLiteDbAsync(() => _gameRepository.Update(game)).ConfigureAwait(false);
                    ClearCache();
                    return;
                }

                game.IsInstalled = true;
                game.State = Horizon.Game.GengDi.Enums.GameLifecycleState.Installed;
                game.LastOperationError = string.Empty;
                if (string.IsNullOrEmpty(game.Version))
                {
                    game.Version = string.IsNullOrEmpty(game.LatestVersion) ? "1.0.0" : game.LatestVersion;
                }
                await ClientAsyncDispatcher.RunLiteDbAsync(() => _gameRepository.Update(game)).ConfigureAwait(false);
                ClearCache();
            }
            catch (Exception ex)
            {
                game.State = Horizon.Game.GengDi.Enums.GameLifecycleState.Failed;
                game.IsInstalled = false;
                game.LastOperationError = string.IsNullOrWhiteSpace(ex.Message)
                    ? "安装失败，请稍后重试。"
                    : ex.Message;
                await ClientAsyncDispatcher.RunLiteDbAsync(() => _gameRepository.Update(game)).ConfigureAwait(false);
                ClearCache();
                throw;
            }
        }

        public async Task UpdateGameAsync(GameModel game)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));
            if (game.State != Horizon.Game.GengDi.Enums.GameLifecycleState.Installed)
            {
                return;
            }

            try
            {
                game.State = Horizon.Game.GengDi.Enums.GameLifecycleState.Updating;
                await ClientAsyncDispatcher.RunLiteDbAsync(() => _gameRepository.Update(game)).ConfigureAwait(false);
                ClearCache();

                var progress = new Progress<UpdateProgress>(p => UpdateProgressChanged?.Invoke(this, p));
                var ok = await UpdateService.Instance.ApplyPendingUpdatesAsync(game, progress).ConfigureAwait(false);

                game.State = ok
                    ? Horizon.Game.GengDi.Enums.GameLifecycleState.Installed
                    : Horizon.Game.GengDi.Enums.GameLifecycleState.Failed;
                await ClientAsyncDispatcher.RunLiteDbAsync(() => _gameRepository.Update(game)).ConfigureAwait(false);
                ClearCache();
            }
            catch (Exception ex)
            {
                game.State = Horizon.Game.GengDi.Enums.GameLifecycleState.Failed;
                await ClientAsyncDispatcher.RunLiteDbAsync(() => _gameRepository.Update(game)).ConfigureAwait(false);
                ClearCache();
                throw new Exception($"游戏更新失败: {ex.Message}", ex);
            }
        }

        public async Task<bool> UninstallGameAsync(GameModel game)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));

            try
            {
                game.State = Horizon.Game.GengDi.Enums.GameLifecycleState.Uninstalling;
                await ClientAsyncDispatcher.RunLiteDbAsync(() => _gameRepository.Update(game)).ConfigureAwait(false);
                ClearCache();

                // (1) 先清除本地数据记录：下载任务、待更新列表
                var pendingRepo = new PendingUpdateRepository();
                await ClientAsyncDispatcher.RunLiteDbAsync(() =>
                {
                    _downloadTaskRepository.DeleteByGameId(game.Id);
                    pendingRepo.ClearForGame(game.Id);
                }).ConfigureAwait(false);

                // (2) 物理删除游戏文件（带 3 次重试，容忍 Windows 文件锁）
                var cleanupSucceeded = true;
                if (!string.IsNullOrWhiteSpace(game.InstallationPath) && Directory.Exists(game.InstallationPath))
                {
                    var deleted = false;
                    await ClientAsyncDispatcher.RunBackgroundAsync(() =>
                    {
                        for (var attempt = 0; attempt < 3; attempt++)
                        {
                            try
                            {
                                Directory.Delete(game.InstallationPath, true);
                                deleted = true;
                                return;
                            }
                            catch (IOException)
                            {
                                System.Threading.Thread.Sleep(250);
                            }
                            catch (UnauthorizedAccessException)
                            {
                                System.Threading.Thread.Sleep(250);
                            }
                        }
                    }).ConfigureAwait(false);

                    if (!deleted && Directory.Exists(game.InstallationPath))
                    {
                        Debug.WriteLine($"[GameService] 卸载游戏后无法删除目录: {game.InstallationPath}");
                        cleanupSucceeded = false;
                    }
                }

                // (3) 清理 Windows 注册表卸载项（非 Windows 为 no-op）
                try { RegistryCleanupService.Remove(game); } catch (Exception ex) { Debug.WriteLine($"[GameService] {nameof(UninstallGameAsync)}: {ex.Message}"); }

                // (4) 重置 GameInfo 运行时状态
                game.IsInstalled = false;
                game.InstallationPath = null;
                game.Version = null;
                game.HasPendingUpdate = false;
                game.State = Horizon.Game.GengDi.Enums.GameLifecycleState.NotInstalled;
                await ClientAsyncDispatcher.RunLiteDbAsync(() => _gameRepository.Update(game)).ConfigureAwait(false);
                ClearCache();

                return cleanupSucceeded;
            }
            catch (Exception ex)
            {
                game.State = Horizon.Game.GengDi.Enums.GameLifecycleState.Failed;
                await ClientAsyncDispatcher.RunLiteDbAsync(() => _gameRepository.Update(game)).ConfigureAwait(false);
                ClearCache();
                throw new Exception($"游戏卸载失败: {ex.Message}", ex);
            }
        }

        public async Task StartGame(GameModel game)
        {
            try
            {
                await StartGameAsync(game).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"游戏启动失败: {ex.Message}");
                throw;
            }
        }

        public async Task StartGameAsync(GameModel game)
        {
            // 状态闸门：仅在 Installed 且磁盘上有安装目录时才启动，避免下载中途或安装尚未完成便被误触发。
            if (game == null
                || game.State != Horizon.Game.GengDi.Enums.GameLifecycleState.Installed
                || !game.IsInstalled
                || string.IsNullOrWhiteSpace(game.InstallationPath)
                || !Directory.Exists(game.InstallationPath))
            {
                return;
            }

            var passportId = AccountService.GetPassportId();
            var authToken = AccountService.GetGameAuthToken();
            var gameId = game.EffectiveGameId;
            var userId = await AccountService.GetOrRegisterGameUserIdAsync(gameId, game.AreaId, game.ServerId).ConfigureAwait(false);

            // 若 HTTP 流程未能获取到游戏用户，通过 Game Gateway 发起主动构建
            if (userId <= 0 && !string.IsNullOrWhiteSpace(passportId) && !string.IsNullOrWhiteSpace(authToken))
            {
                System.Diagnostics.Debug.WriteLine($"[GameService] HTTP流程未找到游戏用户，尝试通过Game Gateway构建: PassportId={passportId}, GameId={gameId}");
                userId = await GameGatewayClient.Instance.BuildGameUserAsync(
                    passportId, gameId, game.AreaId, game.ServerId, authToken).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(passportId) || string.IsNullOrWhiteSpace(authToken) || userId <= 0)
            {
                return;
            }

            // 将 GameModel 写成 INI 文件覆盖到游戏安装目录，供 UE5 客户端启动时读取
            try
            {
                // 在生成 INI 前刷新网关发现缓存，确保写入最新的 IM / Game 网关地址。
                await GatewayDiscoveryService.RefreshAsync(force: true).ConfigureAwait(false);
                GameModelIniWriter.Write(game, passportId, authToken, userId);
                System.Diagnostics.Debug.WriteLine($"[GameService] GameModel INI 已写入: {System.IO.Path.Combine(game.InstallationPath, GameModelIniWriter.IniFileName)}");
            }
            catch (Exception iniEx)
            {
                System.Diagnostics.Debug.WriteLine($"[GameService] 写入 GameModel INI 失败: {iniEx.Message}");
                // INI 写入失败不阻断游戏启动，命令行参数仍然有效
            }

            var executableName = !string.IsNullOrWhiteSpace(game.ExecutableName) ? game.ExecutableName : game.Id;
            var executablePath = Path.Combine(game.InstallationPath, executableName + ".exe");

            if (!File.Exists(executablePath))
            {
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                WorkingDirectory = game.InstallationPath
            };
            psi.ArgumentList.Add($"--passport-id={passportId}");
            psi.ArgumentList.Add($"--game-id={gameId}");
            psi.ArgumentList.Add($"--app-type={game.AppType}");
            psi.ArgumentList.Add($"--area-id={game.AreaId}");
            psi.ArgumentList.Add($"--server-id={game.ServerId}");
            psi.ArgumentList.Add($"--user-id={userId}");
            psi.Environment["HORIZON_AUTH_TOKEN"] = authToken;

            var process = Process.Start(psi);
            if (process != null)
            {
                game.State = Horizon.Game.GengDi.Enums.GameLifecycleState.Running;
                
                // 将状态更新同步到数据库和 UI（假设上层是通过轮询或 ClearCache 生效的）
                _ = ClientAsyncDispatcher.RunLiteDbAsync(() =>
                {
                    _gameRepository.Update(game);
                    ClearCache();
                });

                // 异步等待进程退出，不阻塞调用者线程
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await process.WaitForExitAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        process.Dispose();

                        // 进程结束后恢复到 Installed 状态
                        if (game.State == Horizon.Game.GengDi.Enums.GameLifecycleState.Running)
                        {
                            game.State = Horizon.Game.GengDi.Enums.GameLifecycleState.Installed;
                            _ = ClientAsyncDispatcher.RunLiteDbAsync(() =>
                            {
                                _gameRepository.Update(game);
                                ClearCache();
                            });
                        }
                    }
                });
            }
        }

        public void StartGame()
        {
            // 无参数重载方法，用于MainViewModel
        }

        public void PauseGame()
        {
            // 暂停游戏逻辑
        }

        public void ResumeGame()
        {
            // 恢复游戏逻辑
        }

        public void EndGame()
        {
            // 结束游戏逻辑
        }
    }
}
