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

        // ════════════════════════════════════════════════════════════
        //  UI 显示属性（对应设计稿格式）
        // ══════════════════════════════════════════════════════════════

        [LiteDB.BsonIgnore]
        public bool IsDownloading => Status == DownloadStatus.Downloading;

        [LiteDB.BsonIgnore]
        public bool IsPaused => Status == DownloadStatus.Paused;

        [LiteDB.BsonIgnore]
        public bool IsCompleted => Status == DownloadStatus.Completed;

        [LiteDB.BsonIgnore]
        public bool IsFailed => Status == DownloadStatus.Failed;

        [LiteDB.BsonIgnore]
        public bool IsCancelled => Status == DownloadStatus.Cancelled;

        [LiteDB.BsonIgnore]
        public bool IsPending => Status == DownloadStatus.Pending;

        /// <summary>状态中文显示文本</summary>
        [LiteDB.BsonIgnore]
        public string StatusDisplayText => Status switch
        {
            DownloadStatus.Pending => "等待中",
            DownloadStatus.Downloading => "下载中",
            DownloadStatus.Paused => "已暂停",
            DownloadStatus.Completed => "已完成",
            DownloadStatus.Failed => "失败",
            DownloadStatus.Cancelled => "已取消",
            _ => Status.ToString()
        };

        /// <summary>格式化的速度文本（如 "12.3 MB/s"），非下载中状态返回空</summary>
        [LiteDB.BsonIgnore]
        public string SpeedText => IsDownloading && Speed > 0
            ? FormatSpeed(Speed)
            : string.Empty;

        /// <summary>格式化的剩余时间文本（如 "14 分 22 秒"），无法计算时返回空</summary>
        [LiteDB.BsonIgnore]
        public string RemainingTimeText
        {
            get
            {
                if (!IsDownloading || Speed <= 0 || !HasKnownTotalSize || TotalSize <= DownloadedSize)
                    return string.Empty;

                var remainingBytes = TotalSize - DownloadedSize;
                var remainingSeconds = remainingBytes / Speed;
                return FormatTimeSpan(remainingSeconds);
            }
        }

        /// <summary>格式化的文件大小文本（如 "28.9 GB / 45.2 GB" 或 "18.2 GB"）</summary>
        [LiteDB.BsonIgnore]
        public string FormattedSizeText => HasKnownTotalSize
            ? $"{FormatFileSize(DownloadedSize)} / {FormatFileSize(TotalSize)}"
            : FormatFileSize(DownloadedSize);

        /// <summary>格式化的完成大小文本（如 "18.2 GB"）</summary>
        [LiteDB.BsonIgnore]
        public string FormattedDownloadedSizeText => FormatFileSize(DownloadedSize);

        /// <summary>暂停时间文本（如 "已暂停于 2026-07-25 09:12"）</summary>
        [LiteDB.BsonIgnore]
        public string PausedTimeText => EndTime.HasValue
            ? $"已暂停于 {EndTime:yyyy-MM-dd HH:mm}"
            : "已暂停";

        /// <summary>完成时间文本（如 "完成于 2026-07-24 21:45"）</summary>
        [LiteDB.BsonIgnore]
        public string CompletedTimeText => EndTime.HasValue
            ? $"完成于 {EndTime:yyyy-MM-dd HH:mm}"
            : "已完成";

        /// <summary>
        /// 任务图标键，用于匹配设计稿中每个游戏不同的 Lucide 图标。
        /// 默认按游戏名匹配，未知游戏回退到 "download"。
        /// </summary>
        [LiteDB.BsonIgnore]
        public string IconKey => GameName switch
        {
            "深空突围" => "rocket",
            "苍穹之剑" => "sword",
            "碧海航线" => "anchor",
            "绿野物语" => "trees",
            _ => "download"
        };

        /// <summary>
        /// 渐变索引（0-3），用于每个任务不同的品牌色渐变背景。
        /// 基于 GameName 字符和确定性求和，确保同一游戏始终获得相同渐变。
        /// </summary>
        [LiteDB.BsonIgnore]
        public int GradientIndex
        {
            get
            {
                if (string.IsNullOrEmpty(GameName)) return 0;
                var sum = 0;
                foreach (var c in GameName) sum += c;
                return sum % 4;
            }
        }

        /// <summary>
        /// 将字节大小格式化为人类可读的文件大小文本。
        /// </summary>
        private static string FormatFileSize(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }
            return unitIndex <= 1 ? $"{size:F0} {units[unitIndex]}" : $"{size:F1} {units[unitIndex]}";
        }

        /// <summary>
        /// 将字节/秒格式化为速度文本。
        /// </summary>
        private static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0) return "0 B/s";
            string[] units = { "B/s", "KB/s", "MB/s", "GB/s" };
            double speed = bytesPerSecond;
            int unitIndex = 0;
            while (speed >= 1024 && unitIndex < units.Length - 1)
            {
                speed /= 1024;
                unitIndex++;
            }
            return unitIndex == 0 ? $"{speed:F0} {units[unitIndex]}" : $"{speed:F1} {units[unitIndex]}";
        }

        /// <summary>
        /// 将秒数格式化为时间文本（如 "14 分 22 秒"、"1 时 23 分"）。
        /// </summary>
        private static string FormatTimeSpan(double seconds)
        {
            if (seconds < 1) return "即将完成";
            if (seconds < 60) return $"{Math.Ceiling(seconds)} 秒";
            var totalMinutes = (int)(seconds / 60);
            var remainingSeconds = (int)(seconds % 60);
            if (totalMinutes < 60)
                return remainingSeconds > 0 ? $"{totalMinutes} 分 {remainingSeconds} 秒" : $"{totalMinutes} 分";
            var hours = totalMinutes / 60;
            var minutes = totalMinutes % 60;
            return minutes > 0 ? $"{hours} 时 {minutes} 分" : $"{hours} 时";
        }
    }
}
