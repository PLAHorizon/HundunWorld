using System;
using Newtonsoft.Json;

namespace Horizon.Game.GengDi.Models;

/// <summary>
/// 描述一次可用的客户端更新。
/// </summary>
public sealed class AppUpdateInfo
{
    /// <summary>最新版本号（如 "1.1.0"）。</summary>
    [JsonProperty("latestVersion")]
    public string LatestVersion { get; set; } = string.Empty;

    /// <summary>更新包的下载地址。</summary>
    [JsonProperty("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>更新说明（发布说明）。</summary>
    [JsonProperty("releaseNotes")]
    public string ReleaseNotes { get; set; } = string.Empty;

    /// <summary>发布时间（UTC）。</summary>
    [JsonProperty("releaseDate")]
    public DateTime ReleaseDate { get; set; }

    /// <summary>是否为强制更新。</summary>
    [JsonProperty("isMandatory")]
    public bool IsMandatory { get; set; }

    /// <summary>
    /// 安装包的 SHA-256 哈希值（十六进制，不区分大小写）。
    /// 若非空，则在执行安装前校验文件完整性，防止供应链攻击。
    /// </summary>
    [JsonProperty("sha256")]
    public string Sha256 { get; set; } = string.Empty;
}
