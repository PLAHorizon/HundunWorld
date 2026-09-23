using System;
using System.IO;
using System.Threading.Tasks;

using Horizon.Share.VMs;
using Horizon.WebApi.Configs;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Horizon.WebApi.Controllers
{
    /// <summary>
    /// IM 聊天媒体上传（图片/语音/视频/文件）。
    /// 客户端以原始二进制体 POST（Content-Type 按媒体类型），文件名经 fileName 查询参数传递；
    /// 落盘到运行目录 Aessets/im/{yyyyMMdd}/（Startup 已映射为 /Aessets 静态资源），
    /// 返回可直接访问的相对 URL，客户端拼接 baseUrl 后作为消息 mediaUrl 下发。
    /// </summary>
    [ApiGroup(ApiGroupName.Basic)]
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class ImMediaController : ControllerBase
    {
        /// <summary>图片/音频上限（10MB）</summary>
        private const long MaxMediaBytes = 10 * 1024 * 1024;

        /// <summary>视频/文档/压缩包等共享文件上限（100MB）</summary>
        private const long MaxFileBytes = 100 * 1024 * 1024;

        /// <summary>按大小上限分级的扩展名（图片/音频）</summary>
        private static readonly string[] AllowedMediaExtensions =
        {
            ".jpg", ".jpeg", ".png", ".webp", ".gif",
            ".m4a", ".aac", ".mp3", ".wav", ".amr"
        };

        /// <summary>按大小上限分级的扩展名（视频/文档/压缩包等共享文件）</summary>
        private static readonly string[] AllowedFileExtensions =
        {
            ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".flv", ".webm", ".m4v",
            ".pdf", ".txt", ".md", ".log", ".csv", ".json", ".xml", ".html",
            ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".rtf", ".odt", ".ods",
            ".zip", ".rar", ".7z", ".tar", ".gz",
            ".apk", ".har"
        };

        private readonly ILogger<ImMediaController> _logger;

        public ImMediaController(ILogger<ImMediaController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 上传聊天媒体（图片/语音/视频/文件）。请求体为原始字节，fileName 用于扩展名校验与落盘命名。
        /// </summary>
        [HttpPost("upload")]
        public async Task<ResultVM<string>> UploadAsync([FromQuery] string? fileName = null)
        {
            var result = new ResultVM<string>();
            try
            {
                var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
                var maxBytes = MaxMediaBytes;
                if (Array.IndexOf(AllowedMediaExtensions, ext) >= 0)
                {
                    maxBytes = MaxMediaBytes;
                }
                else if (Array.IndexOf(AllowedFileExtensions, ext) >= 0)
                {
                    maxBytes = MaxFileBytes;
                }
                else
                {
                    result.ErrorMessage = "不支持的文件格式";
                    HttpContext.Response.StatusCode = 400;
                    return result;
                }

                if (Request.ContentLength is > MaxFileBytes)
                {
                    result.ErrorMessage = $"文件超过 {MaxFileBytes / (1024 * 1024)}MB 上限";
                    HttpContext.Response.StatusCode = 413;
                    return result;
                }

                var dateDir = DateTime.Now.ToString("yyyyMMdd");
                var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Aessets", "im", dateDir);
                Directory.CreateDirectory(dir);

                var savedName = $"{Guid.NewGuid():N}{ext}";
                var fullPath = Path.Combine(dir, savedName);
                await using (var fs = System.IO.File.Create(fullPath))
                {
                    await Request.Body.CopyToAsync(fs);
                }

                var fileInfo = new FileInfo(fullPath);
                if (fileInfo.Length == 0 || fileInfo.Length > maxBytes)
                {
                    fileInfo.Delete();
                    result.ErrorMessage = $"文件内容无效或超过 {maxBytes / (1024 * 1024)}MB 上限";
                    HttpContext.Response.StatusCode = 413;
                    return result;
                }

                result.Data = $"/Aessets/im/{dateDir}/{savedName}";
                result.IsSuccess = true;
                _logger.LogInformation("IM 媒体上传成功: {Url} ({Size} bytes)", result.Data, fileInfo.Length);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IM 媒体上传失败");
                result.ErrorMessage = "媒体上传失败";
                return result;
            }
        }
    }
}
