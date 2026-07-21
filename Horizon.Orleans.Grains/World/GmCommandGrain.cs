using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Horizon.Orleans.Interface.World;

namespace Horizon.Orleans.Grains.World;

/// <summary>
/// P3.3 GM 命令 Grain 实现（全局单例）。<br/>
/// 角色管理、公告邮件、审计日志。
/// </summary>
public sealed class GmCommandGrain : Grain, IGmCommandGrain
{
    private readonly ILogger<GmCommandGrain> _logger;

    /// <summary>审计日志（内存缓存，生产环境应持久化到数据库）。</summary>
    private readonly List<GmAuditEntry> _auditLogs = new();

    /// <summary>封禁记录（characterId → 封禁信息）。</summary>
    private readonly Dictionary<long, BanRecord> _banRecords = new();

    private const int MaxAuditLogSize = 10000;

    public GmCommandGrain(ILogger<GmCommandGrain> logger)
    {
        _logger = logger;
    }

    public Task<GmCharacterInfo?> QueryCharacterAsync(long characterId)
    {
        // TODO: 通过 ICharacterGrain.GetCharacterInfoAsync 获取完整角色信息
        // 当前返回基础信息（封禁状态）
        _banRecords.TryGetValue(characterId, out var ban);

        var info = new GmCharacterInfo
        {
            CharacterId = characterId,
            Name = $"Character_{characterId}", // TODO: 从 CharacterGrain 获取
            Level = 0,
            Gold = 0,
            IsOnline = false, // TODO: 从 SessionGrain 获取
            IsBanned = ban != null && (ban.Expiry == null || ban.Expiry > DateTime.UtcNow),
            BanReason = ban?.Reason,
            BanExpiry = ban?.Expiry,
            CreateTime = DateTime.UtcNow,
            LastLoginTime = DateTime.UtcNow,
        };

        return Task.FromResult<GmCharacterInfo?>(info);
    }

    public async Task<GmOperationResult> BanCharacterAsync(long characterId, string reason, DateTime? expiry, long gmId)
    {
        var operationId = Guid.NewGuid().ToString("N");

        _banRecords[characterId] = new BanRecord
        {
            CharacterId = characterId,
            Reason = reason,
            Expiry = expiry,
            GmId = gmId,
            BanTime = DateTime.UtcNow,
        };

        await RecordAuditLogAsync(new GmAuditEntry
        {
            OperationId = operationId,
            GmId = gmId,
            OperationType = "BanCharacter",
            TargetCharacterId = characterId,
            Details = $"Reason: {reason}, Expiry: {expiry?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Permanent"}",
        });

        _logger.LogWarning(
            "角色封禁。CharacterId={CharacterId}, Reason={Reason}, Expiry={Expiry}, GmId={GmId}",
            characterId, reason, expiry, gmId);

        // TODO: 如果角色在线，强制下线
        return new GmOperationResult { Success = true, OperationId = operationId };
    }

    public async Task<GmOperationResult> UnbanCharacterAsync(long characterId, long gmId)
    {
        var operationId = Guid.NewGuid().ToString("N");

        _banRecords.Remove(characterId);

        await RecordAuditLogAsync(new GmAuditEntry
        {
            OperationId = operationId,
            GmId = gmId,
            OperationType = "UnbanCharacter",
            TargetCharacterId = characterId,
            Details = "解封",
        });

        _logger.LogInformation("角色解封。CharacterId={CharacterId}, GmId={GmId}", characterId, gmId);
        return new GmOperationResult { Success = true, OperationId = operationId };
    }

    public async Task<GmOperationResult> GrantCompensationAsync(long characterId, GmCompensation compensation, long gmId)
    {
        var operationId = Guid.NewGuid().ToString("N");

        // TODO: 通过 ICharacterGrain 发放金币/经验/物品
        // var characterGrain = GrainFactory.GetGrain<ICharacterGrain>(characterId);
        // await characterGrain.AddGoldAsync(compensation.GoldAmount);
        // await characterGrain.AddExpAsync(compensation.ExpAmount);
        // foreach (var item in compensation.Items)
        //     await characterGrain.AddItemAsync(item.ItemId, item.Count);

        await RecordAuditLogAsync(new GmAuditEntry
        {
            OperationId = operationId,
            GmId = gmId,
            OperationType = "GrantCompensation",
            TargetCharacterId = characterId,
            Details = $"Gold: {compensation.GoldAmount}, Exp: {compensation.ExpAmount}, Items: {compensation.Items.Length}, Reason: {compensation.Reason}",
        });

        _logger.LogInformation(
            "发放补偿。CharacterId={CharacterId}, Gold={Gold}, Exp={Exp}, GmId={GmId}",
            characterId, compensation.GoldAmount, compensation.ExpAmount, gmId);

        return new GmOperationResult { Success = true, OperationId = operationId };
    }

    public async Task<GmOperationResult> TeleportCharacterAsync(long characterId, float x, float y, float z, long gmId)
    {
        var operationId = Guid.NewGuid().ToString("N");

        await RecordAuditLogAsync(new GmAuditEntry
        {
            OperationId = operationId,
            GmId = gmId,
            OperationType = "TeleportCharacter",
            TargetCharacterId = characterId,
            Details = $"Target: ({x:F2}, {y:F2}, {z:F2})",
        });

        _logger.LogInformation(
            "GM 传送。CharacterId={CharacterId}, Target=({X}, {Y}, {Z}), GmId={GmId}",
            characterId, x, y, z, gmId);

        // TODO: 通过 ZoneShardGrain 执行传送
        return new GmOperationResult { Success = true, OperationId = operationId };
    }

    public async Task<GmOperationResult> BroadcastAnnouncementAsync(string content, GmAnnouncementType type, long gmId)
    {
        var operationId = Guid.NewGuid().ToString("N");

        await RecordAuditLogAsync(new GmAuditEntry
        {
            OperationId = operationId,
            GmId = gmId,
            OperationType = "BroadcastAnnouncement",
            Details = $"Type: {type}, Content: {content}",
        });

        _logger.LogInformation("全服公告。Type={Type}, Content={Content}, GmId={GmId}", type, content, gmId);

        // TODO: 通过 Orleans Stream 广播给所有在线玩家
        return new GmOperationResult { Success = true, OperationId = operationId };
    }

    public async Task<GmOperationResult> SendMailAsync(long characterId, string title, string content, GmMailAttachment[]? attachments, long gmId)
    {
        var operationId = Guid.NewGuid().ToString("N");

        await RecordAuditLogAsync(new GmAuditEntry
        {
            OperationId = operationId,
            GmId = gmId,
            OperationType = "SendMail",
            TargetCharacterId = characterId,
            Details = $"Title: {title}, Attachments: {attachments?.Length ?? 0}",
        });

        _logger.LogInformation("发送邮件。CharacterId={CharacterId}, Title={Title}, GmId={GmId}", characterId, title, gmId);

        // TODO: 通过 IMailGrain 发送邮件
        return new GmOperationResult { Success = true, OperationId = operationId };
    }

    public async Task<GmOperationResult> SendGlobalMailAsync(string title, string content, GmMailAttachment[]? attachments, long gmId)
    {
        var operationId = Guid.NewGuid().ToString("N");

        await RecordAuditLogAsync(new GmAuditEntry
        {
            OperationId = operationId,
            GmId = gmId,
            OperationType = "SendGlobalMail",
            Details = $"Title: {title}, Attachments: {attachments?.Length ?? 0}",
        });

        _logger.LogInformation("全服邮件。Title={Title}, GmId={GmId}", title, gmId);

        // TODO: 批量发送给所有角色
        return new GmOperationResult { Success = true, OperationId = operationId };
    }

    public Task RecordAuditLogAsync(GmAuditEntry entry)
    {
        _auditLogs.Add(entry);

        // 限制日志大小
        if (_auditLogs.Count > MaxAuditLogSize)
            _auditLogs.RemoveRange(0, _auditLogs.Count - MaxAuditLogSize);

        return Task.CompletedTask;
    }

    public Task<GmAuditEntry[]> QueryAuditLogsAsync(DateTime? from, DateTime? to, long? gmId, int limit)
    {
        var query = _auditLogs.AsEnumerable();

        if (from.HasValue)
            query = query.Where(e => e.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(e => e.Timestamp <= to.Value);
        if (gmId.HasValue)
            query = query.Where(e => e.GmId == gmId.Value);

        var result = query.OrderByDescending(e => e.Timestamp).Take(limit).ToArray();
        return Task.FromResult(result);
    }

    /// <summary>封禁记录。</summary>
    private sealed class BanRecord
    {
        public long CharacterId { get; init; }
        public string Reason { get; init; } = string.Empty;
        public DateTime? Expiry { get; init; }
        public long GmId { get; init; }
        public DateTime BanTime { get; init; }
    }
}
