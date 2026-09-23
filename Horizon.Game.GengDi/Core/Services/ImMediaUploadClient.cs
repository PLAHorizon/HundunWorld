using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Horizon.Share.VMs;

namespace Horizon.Game.GengDi.Core.Services
{
    /// <summary>
    /// IM 聊天媒体上传客户端：将本地文件/图片/语音上传到 WebApi ImMedia 服务，
    /// 返回可直接访问的完整 URL（baseUrl + 相对路径），作为消息 mediaUrl 下发，
    /// 保证接收端（PC/鸿蒙/移动）都能下载访问。
    /// </summary>
    public static class ImMediaUploadClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        /// <summary>文件大小上限（与服务端 MaxFileBytes 对齐）</summary>
        private const long MaxFileBytes = 100 * 1024 * 1024;

        private static readonly Lazy<HttpClient> LazyHttp = new(() => new HttpClient(
            SslConfiguration.CreateTestEnvironmentHandler())
        {
            Timeout = TimeSpan.FromMinutes(5)
        });

        private static HttpClient Http => LazyHttp.Value;

        /// <summary>
        /// 上传本地文件到 IM 媒体服务。
        /// 返回完整远程 URL；上传失败返回 null（调用方回退本地路径，仅本机可打开）。
        /// </summary>
        /// <param name="localPath">本地文件路径</param>
        /// <param name="displayName">原始文件名（保留扩展名供服务端校验与打开方式匹配）</param>
        public static async Task<string> UploadAsync(string localPath, string displayName)
        {
            if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
            {
                return null;
            }

            try
            {
                var fileInfo = new FileInfo(localPath);
                if (fileInfo.Length == 0 || fileInfo.Length > MaxFileBytes)
                {
                    return null;
                }

                var fileName = string.IsNullOrWhiteSpace(displayName)
                    ? Path.GetFileName(localPath)
                    : displayName;

                var baseUrl = GatewayDiscoveryService.GetWebApiBaseUrl().TrimEnd('/');
                var url = $"{baseUrl}/ImMedia/upload?fileName={Uri.EscapeDataString(fileName ?? string.Empty)}";

                await using var stream = File.OpenRead(localPath);
                using var content = new StreamContent(stream);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                var token = AccountService.GetAccessToken();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                using var response = await Http.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[ImMediaUpload] HTTP {(int)response.StatusCode}：{localPath}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var vm = JsonSerializer.Deserialize<ResultVM<string>>(json, JsonOptions);
                if (vm == null || !vm.IsSuccess || string.IsNullOrWhiteSpace(vm.Data))
                {
                    System.Diagnostics.Debug.WriteLine($"[ImMediaUpload] 服务端拒绝：{vm?.ErrorMessage}");
                    return null;
                }

                return $"{baseUrl}{vm.Data}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImMediaUpload] 上传失败：{localPath}，{ex.Message}");
                return null;
            }
        }
    }
}
