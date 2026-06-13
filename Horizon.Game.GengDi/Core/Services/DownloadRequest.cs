using System.IO;

namespace Horizon.Game.GengDi.Core.Services
{
    /// <summary>
    /// <see cref="DownloadService"/> 对外暴露的下载启动参数，封装 URL、保存路径、任务类别及可选的哈希校验信息。
    /// </summary>
    public sealed class DownloadRequest
    {
        public string GameId { get; set; }
        public string GameName { get; set; }
        public string DownloadUrl { get; set; }
        public string SavePath { get; set; }
        public Horizon.Game.GengDi.Enums.DownloadTaskKind Kind { get; set; } = Horizon.Game.GengDi.Enums.DownloadTaskKind.GameInstall;
        public string TargetVersion { get; set; }

        /// <summary>
        /// 可选的 SHA-256 哈希（十六进制小写）。若提供，下载完成后会校验；不匹配则视为 Failed。
        /// </summary>
        public string ExpectedHash { get; set; }

        /// <summary>
        /// 自描述：<c>{SavePath}.partial</c>，用于断点续传。
        /// </summary>
        public string PartialPath => string.IsNullOrEmpty(SavePath) ? null : SavePath + ".partial";
    }
}
