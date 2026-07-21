using System;
using System.Threading.Tasks;
using Orleans;

namespace Horizon.Orleans.Interface.World;

/// <summary>
/// P3.3 GM 命令 Grain 契约（全局单例）。<br/>
/// 负责：角色查询/封禁/补偿、公告/邮件管理、经济数据看板、异常行为审计。
/// </summary>
[global::Orleans.CodeGeneration.Version(1)]
public interface IGmCommandGrain : IGrainWithIntegerKey
{
    // --- 角色管理 ---

    /// <summary>查询角色信息。</summary>
    Task<GmCharacterInfo?> QueryCharacterAsync(long characterId);

    /// <summary>封禁角色。</summary>
    Task<GmOperationResult> BanCharacterAsync(long characterId, string reason, DateTime? expiry, long gmId);

    /// <summary>解封角色。</summary>
    Task<GmOperationResult> UnbanCharacterAsync(long characterId, long gmId);

    /// <summary>发放补偿（金币/物品/经验）。</summary>
    Task<GmOperationResult> GrantCompensationAsync(long characterId, GmCompensation compensation, long gmId);

    /// <summary>传送角色到指定位置。</summary>
    Task<GmOperationResult> TeleportCharacterAsync(long characterId, float x, float y, float z, long gmId);

    // --- 公告/邮件 ---

    /// <summary>发送全服公告。</summary>
    Task<GmOperationResult> BroadcastAnnouncementAsync(string content, GmAnnouncementType type, long gmId);

    /// <summary>发送邮件给指定角色。</summary>
    Task<GmOperationResult> SendMailAsync(long characterId, string title, string content, GmMailAttachment[]? attachments, long gmId);

    /// <summary>发送全服邮件。</summary>
    Task<GmOperationResult> SendGlobalMailAsync(string title, string content, GmMailAttachment[]? attachments, long gmId);

    // --- 审计日志 ---

    /// <summary>记录 GM 操作日志。</summary>
    Task RecordAuditLogAsync(GmAuditEntry entry);

    /// <summary>查询审计日志。</summary>
    Task<GmAuditEntry[]> QueryAuditLogsAsync(DateTime? from, DateTime? to, long? gmId, int limit);
}

/// <summary>GM 角色信息。</summary>
[GenerateSerializer]
public sealed class GmCharacterInfo
{
    [Id(0)] public long CharacterId { get; set; }
    [Id(1)] public string Name { get; set; } = string.Empty;
    [Id(2)] public int Level { get; set; }
    [Id(3)] public long Gold { get; set; }
    [Id(4)] public bool IsOnline { get; set; }
    [Id(5)] public bool IsBanned { get; set; }
    [Id(6)] public string? BanReason { get; set; }
    [Id(7)] public DateTime? BanExpiry { get; set; }
    [Id(8)] public DateTime CreateTime { get; set; }
    [Id(9)] public DateTime LastLoginTime { get; set; }
    [Id(10)] public long CurrentZoneShardId { get; set; }
}

/// <summary>GM 操作结果。</summary>
[GenerateSerializer]
public sealed class GmOperationResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string ErrorMessage { get; set; } = string.Empty;
    [Id(2)] public string OperationId { get; set; } = string.Empty;
}

/// <summary>GM 补偿内容。</summary>
[GenerateSerializer]
public sealed class GmCompensation
{
    [Id(0)] public long GoldAmount { get; set; }
    [Id(1)] public long ExpAmount { get; set; }
    [Id(2)] public GmItemGrant[] Items { get; set; } = Array.Empty<GmItemGrant>();
    [Id(3)] public string Reason { get; set; } = string.Empty;
}

/// <summary>GM 物品发放。</summary>
[GenerateSerializer]
public sealed class GmItemGrant
{
    [Id(0)] public int ItemId { get; set; }
    [Id(1)] public int Count { get; set; }
}

/// <summary>GM 邮件附件。</summary>
[GenerateSerializer]
public sealed class GmMailAttachment
{
    [Id(0)] public int ItemId { get; set; }
    [Id(1)] public int Count { get; set; }
    [Id(2)] public long GoldAmount { get; set; }
}

/// <summary>公告类型。</summary>
[GenerateSerializer]
public enum GmAnnouncementType : byte
{
    /// <summary>普通公告。</summary>
    Normal = 0,
    /// <summary>系统维护。</summary>
    Maintenance = 1,
    /// <summary>紧急通知。</summary>
    Emergency = 2,
    /// <summary>活动公告。</summary>
    Event = 3,
}

/// <summary>GM 审计日志条目。</summary>
[GenerateSerializer]
public sealed class GmAuditEntry
{
    [Id(0)] public string OperationId { get; set; } = Guid.NewGuid().ToString("N");
    [Id(1)] public long GmId { get; set; }
    [Id(2)] public string OperationType { get; set; } = string.Empty;
    [Id(3)] public long TargetCharacterId { get; set; }
    [Id(4)] public string Details { get; set; } = string.Empty;
    [Id(5)] public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    [Id(6)] public string IpAddress { get; set; } = string.Empty;
}
