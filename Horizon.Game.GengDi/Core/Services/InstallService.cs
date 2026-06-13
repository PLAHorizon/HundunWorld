using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Services
{
    /// <summary>
    /// 单个安装步骤的进度快照，供 UI 绑定。
    /// </summary>
    public sealed class InstallProgress
    {
        public GameInfo Game { get; set; }
        public long ProcessedBytes { get; set; }
        public long TotalBytes { get; set; }
        public string CurrentEntry { get; set; }

        /// <summary>
        /// 0..100 的百分比，已做 TotalBytes==0 防御。
        /// </summary>
        public double Percent => TotalBytes > 0 ? Math.Min(100.0, (double)ProcessedBytes / TotalBytes * 100) : 0;
    }

    /// <summary>
    /// 游戏安装 / 更新包解压服务。把下载完成的 <c>.zip</c> 逐条 entry 解压到 <see cref="GameInfo.InstallationPath"/>，
    /// 过程中提供 <c>InstallProgressChanged</c> 事件，完成后触发 <c>InstallCompleted</c>；取消时清理部分解压目录。
    ///
    /// 不依赖额外 NuGet，仅用内置 <see cref="System.IO.Compression.ZipArchive"/>；
    /// 解压使用 <see cref="ZipFileExtensions.ExtractToFile(ZipArchiveEntry, string, bool)"/> 并做路径安全校验，防止 ZipSlip。
    /// </summary>
    public class InstallService
    {
        private static readonly Lazy<InstallService> _instance = new(() => new InstallService());

        /// <summary>
        /// 进程内单例（<see cref="ActiveOperationViewModel"/> 及 <see cref="GameService"/> 均引用）。
        /// </summary>
        public static InstallService Instance => _instance.Value;

        public event EventHandler<InstallProgress> InstallProgressChanged;
        public event EventHandler<GameInfo> InstallCompleted;
        public event EventHandler<(GameInfo Game, Exception Error)> InstallFailed;
        public event EventHandler<GameInfo> InstallCancelled;

        /// <summary>
        /// 从安装包解压并完成安装。返回值表示安装是否成功（true = 完成，false = 被取消 / 失败）。
        /// 覆盖安装：同名文件会被直接替换，以适配补丁包的"覆盖更新"语义。
        /// </summary>
        public async Task<bool> InstallFromPackageAsync(GameInfo game, string packagePath, CancellationToken cancellationToken)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));
            if (string.IsNullOrWhiteSpace(packagePath)) throw new ArgumentException("包路径不能为空", nameof(packagePath));
            if (!File.Exists(packagePath)) throw new FileNotFoundException("安装包不存在", packagePath);

            var installRoot = await AppSettingsService.Instance.GetResolvedInstallDirectoryAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(game.InstallationPath))
            {
                game.InstallationPath = Path.Combine(installRoot, game.Id ?? game.Name ?? Guid.NewGuid().ToString("N"));
            }

            try
            {
                await ClientAsyncDispatcher.RunBackgroundAsync(() =>
                {
                    Directory.CreateDirectory(game.InstallationPath);
                    ExtractZipWithProgress(game, packagePath, cancellationToken);
                }).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                // Windows 下写入注册表卸载项；非 Windows 为 no-op。
                try { RegistryCleanupService.Register(game); } catch { /* 注册失败不影响安装成功判定 */ }

                game.LastOperationError = string.Empty;
                InstallCompleted?.Invoke(this, game);
                return true;
            }
            catch (OperationCanceledException)
            {
                await TryCleanupPartialInstallAsync(game).ConfigureAwait(false);
                InstallCancelled?.Invoke(this, game);
                return false;
            }
            catch (Exception ex)
            {
                await TryCleanupPartialInstallAsync(game).ConfigureAwait(false);
                game.LastOperationError = ex.Message;
                InstallFailed?.Invoke(this, (game, ex));
                return false;
            }
        }

        /// <summary>
        /// 取消安装：清理已解压的部分目录，并将游戏状态回退为未安装。
        /// 调用方在执行取消前应弹出确认 UI，本方法不再询问。
        /// </summary>
        public async Task CancelAsync(GameInfo game)
        {
            if (game == null) return;
            await TryCleanupPartialInstallAsync(game).ConfigureAwait(false);
            InstallCancelled?.Invoke(this, game);
        }

        private void ExtractZipWithProgress(GameInfo game, string packagePath, CancellationToken ct)
        {
            using var archive = ZipFile.OpenRead(packagePath);

            long totalBytes = 0;
            foreach (var entry in archive.Entries)
            {
                if (!string.IsNullOrEmpty(entry.Name))
                {
                    totalBytes += entry.Length;
                }
            }

            long processed = 0;
            var destRootFull = Path.GetFullPath(game.InstallationPath);
            var destRootWithSep = destRootFull + Path.DirectorySeparatorChar;

            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();

                var destPath = Path.GetFullPath(Path.Combine(destRootFull, entry.FullName));
                // ZipSlip 防御：确保目标路径仍位于安装根目录之内。
                // Windows 大小写不敏感、Linux/macOS 大小写敏感；按平台选择比较模式，避免在 Linux 下被大小写变体绕过。
                var comparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                if (!destPath.StartsWith(destRootWithSep, comparison)
                    && !string.Equals(destPath, destRootFull, comparison))
                {
                    throw new InvalidDataException($"非法 zip 条目（目录穿越）：{entry.FullName}");
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    // 目录条目
                    Directory.CreateDirectory(destPath);
                    continue;
                }

                var parent = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                using (var entryStream = entry.Open())
                using (var outStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = entryStream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        outStream.Write(buffer, 0, read);
                        processed += read;

                        InstallProgressChanged?.Invoke(this, new InstallProgress
                        {
                            Game = game,
                            ProcessedBytes = processed,
                            TotalBytes = totalBytes,
                            CurrentEntry = entry.FullName
                        });
                    }
                }
            }

            // 最后再 push 一次 100%。
            InstallProgressChanged?.Invoke(this, new InstallProgress
            {
                Game = game,
                ProcessedBytes = totalBytes,
                TotalBytes = totalBytes,
                CurrentEntry = null
            });
        }

        private static Task TryCleanupPartialInstallAsync(GameInfo game)
        {
            return ClientAsyncDispatcher.RunBackgroundAsync(() =>
            {
                if (string.IsNullOrWhiteSpace(game.InstallationPath) || !Directory.Exists(game.InstallationPath))
                {
                    return;
                }

                for (var attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        Directory.Delete(game.InstallationPath, true);
                        return;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(200);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Thread.Sleep(200);
                    }
                }
            });
        }
    }
}
