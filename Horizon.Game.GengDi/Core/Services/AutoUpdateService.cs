using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Models;
using Newtonsoft.Json;

namespace Horizon.Game.GengDi.Core.Services;

/// <summary>
/// 客户端自动更新服务。
/// 负责检查远程版本、下载更新包并触发安装程序。
/// </summary>
public sealed class AutoUpdateService
{
    /// <summary>当前客户端版本号。</summary>
    public const string CurrentVersion = "1.0.0";

    /// <summary>默认更新检查地址（可通过 <see cref="UpdateCheckUrl"/> 属性覆盖）。</summary>
    private const string DefaultUpdateCheckUrl = "https://update.hundunworld.com/client/version.json";

    private static readonly Lazy<AutoUpdateService> s_instance = new(() => new AutoUpdateService());
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _checkGate = new(1, 1);

    private AutoUpdateService()
    {
        _httpClient = new HttpClient(SslConfiguration.CreateTestEnvironmentHandler())
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", $"HundunWorld-Client/{CurrentVersion}");
    }

    /// <summary>获取单例实例。</summary>
    public static AutoUpdateService Instance => s_instance.Value;

    /// <summary>更新检查地址（空时使用默认值）。</summary>
    public string UpdateCheckUrl { get; set; } = DefaultUpdateCheckUrl;

    /// <summary>
    /// 下载进度变化事件。参数为 0–100 之间的百分比。
    /// </summary>
    public event EventHandler<double> DownloadProgressChanged;

    /// <summary>
    /// 最近一次检查更新所发现的新版本信息。
    /// 若已是最新版或尚未完成检查，则为 <c>null</c>。
    /// </summary>
    public AppUpdateInfo LatestUpdateInfo { get; private set; }

    /// <summary>
    /// 检查是否有可用的新版本。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// 若有新版本，返回 <see cref="AppUpdateInfo"/>；
    /// 若已是最新版，返回 <c>null</c>。
    /// </returns>
    public async Task<AppUpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        await _checkGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var url = string.IsNullOrWhiteSpace(UpdateCheckUrl) ? DefaultUpdateCheckUrl : UpdateCheckUrl;
            var json = await _httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            var info = JsonConvert.DeserializeObject<AppUpdateInfo>(json);

            if (info == null || string.IsNullOrWhiteSpace(info.LatestVersion))
            {
                return null;
            }

            var result = IsNewerVersion(info.LatestVersion, CurrentVersion) ? info : null;
            LatestUpdateInfo = result;
            return result;
        }
        finally
        {
            _checkGate.Release();
        }
    }

    /// <summary>
    /// 下载更新包到临时目录，并返回本地文件路径。
    /// 下载过程中通过 <see cref="DownloadProgressChanged"/> 事件上报进度。
    /// </summary>
    /// <param name="updateInfo">更新信息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>下载完成后本地安装包的完整路径。</returns>
    public async Task<string> DownloadUpdatePackageAsync(
        AppUpdateInfo updateInfo,
        CancellationToken cancellationToken = default)
    {
        if (updateInfo == null)
        {
            throw new ArgumentNullException(nameof(updateInfo));
        }

        if (string.IsNullOrWhiteSpace(updateInfo.DownloadUrl))
        {
            throw new ArgumentException("更新下载地址不能为空。", nameof(updateInfo));
        }

        // 安全校验：仅允许 HTTPS 下载，且域名必须为官方更新服务器
        if (!IsAllowedUpdateUrl(updateInfo.DownloadUrl))
        {
            throw new InvalidOperationException($"更新包下载地址不受信任，已拒绝下载：{updateInfo.DownloadUrl}");
        }

        var fileName = BuildInstallerFileName(updateInfo.LatestVersion);
        // 使用唯一子目录避免可预测路径带来的本地安全风险
        var sessionId = Guid.NewGuid().ToString("N")[..8];
        var savePath = Path.Combine(Path.GetTempPath(), $"HundunWorldUpdate-{sessionId}", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);

        using var request = new HttpRequestMessage(HttpMethod.Get, updateInfo.DownloadUrl);
        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0L;

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long downloadedBytes = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            downloadedBytes += bytesRead;

            if (totalBytes > 0)
            {
                var progress = (double)downloadedBytes / totalBytes * 100.0;
                DownloadProgressChanged?.Invoke(this, progress);
            }
        }

        DownloadProgressChanged?.Invoke(this, 100.0);

        // 安全校验：若 manifest 提供了 SHA-256 哈希，则在返回路径前验证文件完整性
        if (!string.IsNullOrWhiteSpace(updateInfo.Sha256))
        {
            var actualHash = await ComputeFileSha256Async(savePath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualHash, updateInfo.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(savePath);
                throw new InvalidOperationException(
                    $"安装包 SHA-256 校验失败，文件可能已被篡改。" +
                    $"预期: {updateInfo.Sha256}，实际: {actualHash}");
            }
        }

        return savePath;
    }

    /// <summary>
    /// 启动已下载的安装包并关闭当前客户端进程。
    /// </summary>
    /// <param name="installerPath">本地安装包路径。</param>
    public void ApplyUpdate(string installerPath)
    {
        if (string.IsNullOrWhiteSpace(installerPath))
        {
            throw new ArgumentException("安装包路径不能为空。", nameof(installerPath));
        }

        if (!File.Exists(installerPath))
        {
            throw new FileNotFoundException("安装包文件不存在。", installerPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true
        };

        // 在 Linux / macOS 上对安装包赋予执行权限
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var chmodInfo = new ProcessStartInfo
                {
                    FileName = "chmod",
                    UseShellExecute = false
                };
                chmodInfo.ArgumentList.Add("+x");
                chmodInfo.ArgumentList.Add(installerPath);
                Process.Start(chmodInfo)?.WaitForExit();
            }
            catch
            {
                // 赋权失败时继续尝试启动
            }
        }

        try
        {
            var launched = Process.Start(startInfo);
            if (launched == null)
            {
                throw new InvalidOperationException("无法启动安装程序进程。");
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"启动安装程序失败：{ex.Message}", ex);
        }

        // 关闭当前应用
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lifetime)
        {
            lifetime.Shutdown();
        }
        else
        {
            Environment.Exit(0);
        }
    }

    /// <summary>
    /// 检查 <paramref name="candidate"/> 是否比 <paramref name="current"/> 更新。
    /// 版本号格式：MAJOR.MINOR.PATCH（忽略额外字段）。
    /// </summary>
    internal static bool IsNewerVersion(string candidate, string current)
    {
        if (!Version.TryParse(candidate, out var parsedCandidate))
        {
            return false;
        }

        if (!Version.TryParse(current, out var parsedCurrent))
        {
            return true;
        }

        return parsedCandidate > parsedCurrent;
    }

    private static string BuildInstallerFileName(string version)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return $"HundunWorld-{version}-setup.exe";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return $"HundunWorld-{version}.dmg";
        }

        return $"HundunWorld-{version}-linux.AppImage";
    }

    /// <summary>
    /// 校验下载地址是否为受信任的官方更新服务器（仅允许 HTTPS + 指定域名）。
    /// </summary>
    private static bool IsAllowedUpdateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        // 仅允许 HTTPS
        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 仅允许官方域名
        var host = uri.Host.ToLowerInvariant();
        return host == "update.hundunworld.com" || host.EndsWith(".hundunworld.com", StringComparison.Ordinal);
    }

    /// <summary>
    /// 异步计算文件的 SHA-256 哈希值，返回十六进制小写字符串。
    /// </summary>
    private static async Task<string> ComputeFileSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        using var sha256 = SHA256.Create();
        var hashBytes = await Task.Run(() => sha256.ComputeHash(stream), cancellationToken).ConfigureAwait(false);

        var sb = new StringBuilder(hashBytes.Length * 2);
        foreach (var b in hashBytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }
}
