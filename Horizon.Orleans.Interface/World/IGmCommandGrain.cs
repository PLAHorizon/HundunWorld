using System;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;
using Orleans;

namespace Horizon.Orleans.Interface.World;

/// <summary>
/// P3.3 GM 命令 Grain 契约（全局单例）。<br/>
/// 负责：角色查询/封禁/补偿、公告/邮件管理、经济数据看板、异常行为审计，
/// 以及**脚本化世界生成（怪物/NPC/特殊事件）的权威授权裁决**。
/// </summary>
/// <remarks>
/// <para>
/// <b>Version(2)</b>：新增脚本授权方法。升版后 <b>Silo 与网关必须同批构建部署</b>，
/// 新旧混跑会 <c>TypeLoadException</c>；部署前需先终止旧 Silo 进程。
/// </para>
/// <para>
/// <b>身份口径（重要）</b>：所有 <c>gmId</c> / <c>operatorId</c> 必须由调用方（网关或 WebApi）
/// 从<b>已验签的 AuthToken</b> 或<b>管理会话</b>解出后传入。Grain 不信任 body 自报身份，
/// 并对每个操作校验该 id 的权限位（历史版本"零鉴权、全靠调用方自觉"的口径已废弃）。
/// </para>
/// </remarks>
[global::Orleans.CodeGeneration.Version(2)]
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

    // --- 脚本化世界生成授权（DS 怪物/NPC/特殊事件） ---

    /// <summary>读取某操作者的脚本授权快照（含配额余量口径由裁决侧给出，这里只给授权位与到期）。</summary>
    Task<GmScriptGrantSnapshot?> GetScriptPermissionsAsync(long operatorId);

    /// <summary>
    /// 授予脚本权限。<paramref name="callerId"/> 自身权限位必须覆盖
    /// <paramref name="flags"/>。调用者权限不足时直接拒绝（不凭空造权）。
    /// </summary>
    Task<GmOperationResult> GrantScriptAsync(long operatorId, ScriptPermissionFlags flags, TimeSpan ttl, long callerId, string reason);

    /// <summary>回收脚本权限。</summary>
    Task<GmOperationResult> RevokeScriptAsync(long operatorId, long callerId, string reason);

    /// <summary>
    /// **唯一裁决点**：权限位 → 模板白名单 → 冷却 → 配额 → 距离 → shard 归属 → 实体预算，
    /// 全部通过后扣预算、落审计并签发一次性 HMAC 令牌。
    /// <para>网关与 DS 都不得自行判断"谁有权"；DS 只验签。</para>
    /// </summary>
    Task<GmScriptVerdict> TryConsumeScriptAsync(GmScriptTicket ticket);

    /// <summary>查询脚本操作审计（按操作者过滤）。</summary>
    Task<GmAuditEntry[]> QueryScriptAuditAsync(long? operatorId, int limit);
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
