using System;
using Horizon.Game.GengDi.Enums;

namespace Horizon.Game.GengDi.Models
{
    public class DownloadTask
    {
        [LiteDB.BsonId]
        public string Id { get; set; }
        public string GameId { get; set; }
        public string GameName { get; set; }
        public long TotalSize { get; set; }
        public long DownloadedSize { get; set; }
        public DownloadStatus Status { get; set; }
        public double Progress { get; set; }
        public double Speed { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// 任务类别：游戏安装下载、游戏更新下载、或客户端更新下载。
        /// </summary>
        public DownloadTaskKind Kind { get; set; } = DownloadTaskKind.GameInstall;

        /// <summary>
        /// 源 URL，持久化后可在恢复 / 断点续传时复用，无需调用方重新提供。
        /// </summary>
        public string SourceUrl { get; set; }

        /// <summary>
        /// 目标保存路径（完成后的最终文件路径，下载过程中使用 {SavePath}.partial）。
        /// </summary>
        public string SavePath { get; set; }

        /// <summary>
        /// 更新类下载所对应的目标版本号，用于与 PendingUpdateItem 关联。
        /// </summary>
        public string TargetVersion { get; set; }

        /// <summary>
        /// 可选的包哈希（SHA-256 十六进制小写），下载完成时会进行校验，不一致则视为失败。
        /// </summary>
        public string ExpectedHash { get; set; }

        /// <summary>
        /// 最近一次下载失败原因，供安装流程和 UI 显示具体提示。
        /// </summary>
        public string ErrorMessage { get; set; }

        [LiteDB.BsonIgnore]
        public bool HasKnownTotalSize => TotalSize > 0;

        [LiteDB.BsonIgnore]
        public bool IsProgressIndeterminate => Status == DownloadStatus.Downloading && !HasKnownTotalSize;

        [LiteDB.BsonIgnore]
        public string ProgressText => HasKnownTotalSize
            ? $"{Progress:F0}%"
            : Status switch
            {
                DownloadStatus.Completed => "100%",
                DownloadStatus.Paused => "已暂停",
                DownloadStatus.Failed => "失败",
                DownloadStatus.Cancelled => "已取消",
                _ => "处理中"
            };

        [LiteDB.BsonIgnore]
        public string TransferStatusText => HasKnownTotalSize
            ? $"已下载 {DownloadedSize:N0}/{TotalSize:N0} bytes"
            : $"已下载 {DownloadedSize:N0} bytes";
    }
}
