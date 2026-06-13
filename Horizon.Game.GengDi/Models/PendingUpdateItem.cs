using System;

namespace Horizon.Game.GengDi.Models
{
    /// <summary>
    /// 待更新版本列表中的单个条目，描述从 <c>FromVersion</c> 升级到 <c>ToVersion</c> 所需要拉取的补丁包。
    /// 以 "{GameId}:{ToVersion}" 为主键持久化到 LiteDB 本地库中，<c>ApplyPendingUpdatesAsync</c> 按 <c>OrderIndex</c> 依次应用。
    /// </summary>
    public class PendingUpdateItem
    {
        /// <summary>
        /// 主键，格式："{GameId}:{ToVersion}"。
        /// </summary>
        [LiteDB.BsonId]
        public string Id { get; set; }

        /// <summary>
        /// 游戏 ID，与 <see cref="GameInfo.Id"/> 对应。
        /// </summary>
        public string GameId { get; set; }

        /// <summary>
        /// 升级前版本号（空字符串表示最小可用版本）。
        /// </summary>
        public string FromVersion { get; set; }

        /// <summary>
        /// 升级后版本号。
        /// </summary>
        public string ToVersion { get; set; }

        /// <summary>
        /// 该补丁包的下载 URL。
        /// </summary>
        public string DownloadUrl { get; set; }

        /// <summary>
        /// 可选的补丁包哈希（SHA-256 十六进制小写），用于下载后的完整性校验。
        /// </summary>
        public string PackageHash { get; set; }

        /// <summary>
        /// 是否已应用。应用成功后置 true，用户中途取消后保持原值，保证断点续更。
        /// </summary>
        public bool Applied { get; set; }

        /// <summary>
        /// 在列表中的顺序索引，<c>UpdateService.ApplyPendingUpdatesAsync</c> 按升序依次应用。
        /// </summary>
        public int OrderIndex { get; set; }

        /// <summary>
        /// 条目创建时间（UTC），便于排错与过期判断。
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public static string BuildId(string gameId, string toVersion)
        {
            return $"{gameId}:{toVersion}";
        }
    }
}
