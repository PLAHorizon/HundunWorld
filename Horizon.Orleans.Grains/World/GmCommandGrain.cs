using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Core.Security;
using Horizon.Game.Message.Network;
using Horizon.Model.GameModel;
using Horizon.Orleans.Interface.World;

namespace Horizon.Orleans.Grains.World;

/// <summary>
/// P3.3 GM 命令 Grain 实现（全局单例）。<br/>
/// 角色管理、公告邮件、审计日志。
/// </summary>
public sealed class GmCommandGrain : Grain, IGmCommandGrain
{
    private readonly ILogger<GmCommandGrain> _logger;

    /// <summary>封禁记录（characterId → 封禁信息）。</summary>
    private readonly Dictionary<long, BanRecord> _banRecords = new();

    /// <summary>
    /// 持久层保留的审计条数上限（环形，丢最旧）。审计与授权同存 Redis，
    /// 是为了让 Grain 去激活 / Silo 重启后 <c>/audit</c> 仍答得出最近的 GM 动作，
    /// 而不是返回空表——空表会被误读成"没人做过"。长期归档依赖 Silo 日志。
    /// </summary>
    private const int MaxDurableAuditEntries = 500;

    /// <summary>GM 运维操作所需权限位（与"世界编排"位分列，见 <see cref="ScriptPermissionFlags"/>）。</summary>
    private const uint GmOpsFlag = (uint)ScriptPermissionFlags.GmOps;

    /// <summary>授权热层（Orleans Redis 持久化）。</summary>
    private readonly IPersistentState<GmScriptAuthorityState> _authority;

    /// <summary>授权权威层（SQLServer）。未注册时授权面退化为"仅热层 + 重启丢失"，裁决仍 fail-closed。</summary>
    private readonly IDataContext<GameEntityContext, GmScriptGrantEntity, long>? _grantContext;

    /// <summary>角色权威位置来源（由 ZoneShardGrain tick 写入，**不是**客户端上报）。</summary>
    private readonly Horizon.Game.Core.Sim.Server.ICharacterPositionStore? _positionStore;

    private readonly ScriptedSpawnOptions _scriptOptions;
    private readonly byte[]? _signingKey;
    private readonly ScriptAuthorityCore _scriptCore;

    /// <summary>
    /// 本 Grain 已向各 shard 放行的脚本实体计数（粗略预算护栏）。
    /// <para>
    /// 它看不到静态刷怪点与他人实例，因此**只是防洪兜底**，不是全局权威计数；
    /// DS 侧另有 <c>MaxEntities</c> 与区域上限做硬约束。P1 接入 DS 回报后可替换为真实值。
    /// </para>
    /// </summary>
    private readonly Dictionary<long, int> _shardIssuedCount = new();

    public GmCommandGrain(
        ILogger<GmCommandGrain> logger,
        [PersistentState("gmscriptauthority", "GameStore")] IPersistentState<GmScriptAuthorityState> authority,
        IDataContext<GameEntityContext, GmScriptGrantEntity, long>? grantContext = null,
        Horizon.Game.Core.Sim.Server.ICharacterPositionStore? positionStore = null,
        ScriptedSpawnOptions? scriptOptions = null)
    {
        _logger = logger;
        _authority = authority;
        _grantContext = grantContext;
        _positionStore = positionStore;
        _scriptOptions = scriptOptions ?? ScriptedSpawnOptions.Default;

        // 密钥缺失时保持 null：裁决核据此一律拒绝（fail-closed），绝不"跳过验签"。
        if (!string.IsNullOrWhiteSpace(_scriptOptions.SigningKeyBase64))
        {
            try
            {
                _signingKey = Convert.FromBase64String(_scriptOptions.SigningKeyBase64);
            }
            catch (FormatException)
            {
                _logger.LogError("ScriptedSpawn:SigningKeyBase64 不是合法 Base64，脚本授权将全部拒绝。");
            }
        }

        _scriptCore = new ScriptAuthorityCore(_scriptOptions, DefaultScriptTemplateCatalog.Instance);
    }

    /// <summary>热层为空时从权威层重建，保证 Redis 失效/首次部署后授权不丢。</summary>
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await _authority.ReadStateAsync();

        if (_authority.State.Operators.Count == 0 && _grantContext != null)
        {
            var now = DateTime.UtcNow;
            var query = await _grantContext.QueryAsync(e => e.RevokedAtUtc == null && e.ExpiresAtUtc > now);
            foreach (var entity in query)
            {
                _authority.State.Operators[entity.OperatorCharacterId] = new GmScriptGrantState
                {
                    Flags = (uint)entity.Flags,
                    GrantedAtUtc = entity.GrantedAtUtc,
                    ExpiresAtUtc = entity.ExpiresAtUtc,
                    GrantedBy = entity.GrantedBy,
                    Reason = entity.Reason,
                };
            }

            if (_authority.State.Operators.Count > 0)
                await _authority.WriteStateAsync();

            _logger.LogInformation("脚本授权热层已从权威层重建，操作者数={Count}", _authority.State.Operators.Count);
        }

        base.OnActivateAsync(cancellationToken);
    }

    public async Task<GmCharacterInfo?> QueryCharacterAsync(long characterId)
    {
        await AssertGmAsync(characterId);
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

        return info;
    }

    public async Task<GmOperationResult> BanCharacterAsync(long characterId, string reason, DateTime? expiry, long gmId)
    {
        await AssertGmAsync(gmId);
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
        await AssertGmAsync(gmId);
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
        await AssertGmAsync(gmId);
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
        await AssertGmAsync(gmId);
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
        await AssertGmAsync(gmId);
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
        await AssertGmAsync(gmId);
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
        await AssertGmAsync(gmId);
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

    /// <summary>
    /// 落一条审计。**写进持久层而不是进程内字段**：本 Grain 会被 Orleans 按需去激活，
    /// 进程内列表会在无人察觉时清零，让"谁在什么时候授予/造怪"变成查不到。
    /// <para>
    /// 调用点已处于各自的写路径里（授予/回收随后还会再写一次状态），多一次
    /// <c>WriteStateAsync</c> 的代价换来"任何一条被拒绝的 GM 动作也留痕"——
    /// 拒绝记录恰恰是最需要事后核查的那一类。
    /// </para>
    /// </summary>
    public async Task RecordAuditLogAsync(GmAuditEntry entry)
    {
        await EnsureLoadedAsync();

        var log = _authority.State.AuditLog;
        log.Add(entry);
        if (log.Count > MaxDurableAuditEntries)
            log.RemoveRange(0, log.Count - MaxDurableAuditEntries);

        await _authority.WriteStateAsync();
    }

    public async Task<GmAuditEntry[]> QueryAuditLogsAsync(DateTime? from, DateTime? to, long? gmId, int limit)
    {
        await EnsureLoadedAsync();

        var query = _authority.State.AuditLog.AsEnumerable();

        if (from.HasValue)
            query = query.Where(e => e.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(e => e.Timestamp <= to.Value);
        if (gmId.HasValue)
            query = query.Where(e => e.GmId == gmId.Value);

        return query.OrderByDescending(e => e.Timestamp).Take(Math.Max(1, limit)).ToArray();
    }

    // ==================== 脚本化世界生成授权 ====================

    /// <inheritdoc />
    public async Task<GmScriptGrantSnapshot?> GetScriptPermissionsAsync(long operatorId)
    {
        await EnsureLoadedAsync();

        if (!_authority.State.Operators.TryGetValue(operatorId, out var state))
            return null;

        var now = DateTime.UtcNow;
        return new GmScriptGrantSnapshot
        {
            OperatorCharacterId = operatorId,
            Flags = state.Flags,
            GrantedAtUtc = state.GrantedAtUtc,
            ExpiresAtUtc = state.ExpiresAtUtc,
            GrantedBy = state.GrantedBy,
            Reason = state.Reason,
            IsExpired = now > state.ExpiresAtUtc,
        };
    }

    /// <inheritdoc />
    public async Task<GmOperationResult> GrantScriptAsync(
        long operatorId, ScriptPermissionFlags flags, TimeSpan ttl, long callerId, string reason)
    {
        var operationId = Guid.NewGuid().ToString("N");

        // 调用者自身必须持 GmOps，且其权限位必须覆盖要授予的位（不能凭空造权）。
        var caller = await LoadOperatorAsync(callerId);
        var isGmHolder = (caller.Flags & GmOpsFlag) != 0;

        // 引导通道：解决"第一个 GM 由谁授予"。仅当 callerId 被显式列在配置
        // ScriptedSpawn:BootstrapAdminIds 中才生效（引导路径不检查调用者已有位，但仍强制落审计）。
        var isBootstrapAdmin = Array.IndexOf(_scriptOptions.BootstrapAdminIds, callerId) >= 0;

        if (!isGmHolder && !isBootstrapAdmin)
            return await DenyAsync(operationId, callerId, "GrantScript", operatorId, "调用者无 GmOps 权限。");

        if ((caller.Flags & (uint)flags) != (uint)flags && !isBootstrapAdmin)
            return await DenyAsync(operationId, callerId, "GrantScript", operatorId, "调用者权限不足以授予该组合。");

        if (isBootstrapAdmin)
        {
            _logger.LogWarning(
                "经由引导管理员授予脚本权限。Caller={Caller}, Operator={Operator}, Flags={Flags}——" +
                "正式授权应迁入数据库后清空 ScriptedSpawn:BootstrapAdminIds。",
                callerId, operatorId, flags);
        }

        var nowUtc = DateTime.UtcNow;
        var expires = ttl <= TimeSpan.Zero ? DateTime.MaxValue : nowUtc.Add(ttl);

        await PersistGrantAsync(operatorId, (uint)flags, callerId, nowUtc, expires, reason, revokedAtUtc: null);

        _authority.State.Operators[operatorId] = new GmScriptGrantState
        {
            Flags = (uint)flags,
            GrantedAtUtc = nowUtc,
            ExpiresAtUtc = expires,
            GrantedBy = callerId,
            Reason = reason ?? string.Empty,
        };
        await _authority.WriteStateAsync();

        await RecordAuditLogAsync(new GmAuditEntry
        {
            OperationId = operationId,
            GmId = callerId,
            OperationType = "GrantScript",
            TargetCharacterId = operatorId,
            Details = $"Flags: {flags}, ExpiresUtc: {expires:yyyy-MM-dd HH:mm:ss}, Reason: {reason}",
        });

        _logger.LogWarning(
            "脚本授权授予。Operator={Operator}, Flags={Flags}, Caller={Caller}, Expires={Expires}",
            operatorId, flags, callerId, expires);

        return new GmOperationResult { Success = true, OperationId = operationId };
    }

    /// <inheritdoc />
    public async Task<GmOperationResult> RevokeScriptAsync(long operatorId, long callerId, string reason)
    {
        var operationId = Guid.NewGuid().ToString("N");

        var caller = await LoadOperatorAsync(callerId);
        if ((caller.Flags & GmOpsFlag) == 0 && Array.IndexOf(_scriptOptions.BootstrapAdminIds, callerId) < 0)
            return await DenyAsync(operationId, callerId, "RevokeScript", operatorId, "调用者无 GmOps 权限。");

        await PersistGrantAsync(operatorId, 0, callerId, DateTime.UtcNow, DateTime.UtcNow, reason, DateTime.UtcNow);
        _authority.State.Operators.Remove(operatorId);
        await _authority.WriteStateAsync();

        await RecordAuditLogAsync(new GmAuditEntry
        {
            OperationId = operationId,
            GmId = callerId,
            OperationType = "RevokeScript",
            TargetCharacterId = operatorId,
            Details = $"Reason: {reason}",
        });

        return new GmOperationResult { Success = true, OperationId = operationId };
    }

    /// <inheritdoc />
    public async Task<GmScriptVerdict> TryConsumeScriptAsync(GmScriptTicket ticket)
    {
        if (ticket == null)
            return new GmScriptVerdict { Allowed = false, Reason = ScriptRejectReason.InvalidArgument, Message = "请求为空。" };

        await EnsureLoadedAsync();

        var stored = _authority.State.Operators.TryGetValue(ticket.OperatorCharacterId, out var s)
            ? s
            : new GmScriptGrantState();

        var state = MapToCore(stored);

        // 操作者权威位置：来自位置存储（Redis 热层，由 DS 位置回写 / ZoneShardGrain 写入）。
        // **不在这里判缺失**：位置缺失该不该拒、拒哪一类操作，是裁决核的规则（ScriptAuthorityCore 第 2.5 步），
        // Grain 只负责如实报告"有没有"。在此重复一条规则就会出现"网关一套、Grain 一套"的口径漂移。
        var position = _positionStore != null
            ? await _positionStore.GetPositionAsync(ticket.OperatorCharacterId)
            : null;

        var ctx = new ScriptEvalContext
        {
            Ticket = ticket,
            OperatorX = position?.X ?? 0f,
            OperatorY = position?.Y ?? 0f,
            OperatorZ = position?.Z ?? 0f,
            OperatorPositionKnown = position != null,
            OperatorShardId = ticket.OperatorShardId,
            ShardScriptedEntityCount = _shardIssuedCount.TryGetValue(ticket.OperatorShardId, out var issued) ? issued : 0,
            ShardTriggerZoneCount = 0, // 触发区域计数由 DS 侧硬顶把关；此处留 0 不做二次限流。
            NowUtc = DateTime.UtcNow,
            NowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        var decision = _scriptCore.Evaluate(state, ctx, _signingKey);

        if (decision.Allowed)
        {
            // 回写热层（冷却/配额/计数都是被裁决消耗的状态）。
            _authority.State.Operators[ticket.OperatorCharacterId] = MapFromCore(state);
            await _authority.WriteStateAsync();

            if (ticket.Op == ScriptOp.Spawn)
                _shardIssuedCount[ticket.OperatorShardId] =
                    (_shardIssuedCount.TryGetValue(ticket.OperatorShardId, out var n) ? n : 0) + (decision.Token?.Count ?? 1);
        }

        await RecordAuditLogAsync(new GmAuditEntry
        {
            OperationId = decision.Token != null ? decision.Token.Nonce.ToString("N") : Guid.NewGuid().ToString("N"),
            GmId = ticket.OperatorCharacterId,
            OperationType = $"Script.{ticket.Op}.{ticket.Kind}",
            TargetCharacterId = ticket.OperatorCharacterId,
            Details = $"{(decision.Allowed ? "受理" : "拒绝")}:{decision.Message} Template={ticket.TemplateKey} Shard={ticket.OperatorShardId}",
        });

        return new GmScriptVerdict
        {
            Allowed = decision.Allowed,
            Reason = decision.Reason,
            Message = decision.Message,
            Token = decision.Token,
            RemainingBudget = decision.RemainingBudget,
            EffectiveFlags = decision.Allowed ? decision.EffectiveFlags : state.Flags,
            OperationId = decision.Token?.Nonce.ToString("N") ?? string.Empty,
        };
    }

    /// <inheritdoc />
    /// <summary>
    /// 查脚本相关审计。<b>默认只看脚本面</b>（OperationType 以 Script 开头），
    /// 不传 <paramref name="operatorId"/> 时不再回落到"全部 GM 动作"——否则
    /// <c>/audit</c> 会把封禁/邮件这类无关 GM 操作混进脚本文案里，看起来像脚本授权记录。
    /// </summary>
    public async Task<GmAuditEntry[]> QueryScriptAuditAsync(long? operatorId, int limit)
    {
        await EnsureLoadedAsync();

        var query = _authority.State.AuditLog
            .Where(e => e.OperationType.StartsWith("Script", StringComparison.Ordinal)
                        || e.OperationType is "GrantScript" or "RevokeScript");
        if (operatorId.HasValue)
            query = query.Where(e => e.GmId == operatorId.Value || e.TargetCharacterId == operatorId.Value);

        var capped = limit <= 0 ? 50 : Math.Min(limit, MaxDurableAuditEntries);
        return query.OrderByDescending(e => e.Timestamp).Take(capped).ToArray();
    }

    /// <summary>把热层授权投影为裁决核的纯数据结构。</summary>
    private static ScriptOperatorState MapToCore(GmScriptGrantState s) => new()
    {
        Flags = s.Flags,
        ExpiresAtUtc = s.ExpiresAtUtc == default ? DateTime.MaxValue : s.ExpiresAtUtc,
        GrantedAtUtc = s.GrantedAtUtc,
        GrantedBy = s.GrantedBy,
        Reason = s.Reason,
        LastAcceptedUnixMs = s.LastAcceptedUnixMs,
        QuotaWindowStartUnixMs = s.QuotaWindowStartUnixMs,
        QuotaWindowUsed = s.QuotaWindowUsed,
        IssuedTokenCounter = s.IssuedTokenCounter,
    };

    /// <summary>裁决核消耗后的状态回写热层。</summary>
    private static GmScriptGrantState MapFromCore(ScriptOperatorState s) => new()
    {
        Flags = s.Flags,
        ExpiresAtUtc = s.ExpiresAtUtc,
        GrantedAtUtc = s.GrantedAtUtc,
        GrantedBy = s.GrantedBy,
        Reason = s.Reason,
        LastAcceptedUnixMs = s.LastAcceptedUnixMs,
        QuotaWindowStartUnixMs = s.QuotaWindowStartUnixMs,
        QuotaWindowUsed = s.QuotaWindowUsed,
        IssuedTokenCounter = s.IssuedTokenCounter,
    };

    /// <summary>
    /// GM 运维操作的统一鉴权：调用者必须持 <see cref="ScriptPermissionFlags.GmOps"/>。
    /// 这是本 Grain 从"零鉴权、全靠调用方自觉"改造后的硬约束。
    /// </summary>
    private async Task AssertGmAsync(long gmId, uint required = GmOpsFlag)
    {
        var caller = await LoadOperatorAsync(gmId);
        if ((caller.Flags & required) != required)
        {
            _logger.LogWarning("GM 操作被拒绝。GmId={GmId}, 需要位={Required}, 实际={Actual}", gmId, required, caller.Flags);
            throw new InvalidOperationException($"GM 权限不足：需要 {required}。");
        }
    }

    private async Task<GmScriptGrantState> LoadOperatorAsync(long operatorId)
    {
        await EnsureLoadedAsync();
        return _authority.State.Operators.TryGetValue(operatorId, out var s) ? s : new GmScriptGrantState();
    }

    /// <summary>Orleans 会在属性访问前自动加载，但显式调用可保证首次访问的时序确定。</summary>
    private async Task EnsureLoadedAsync()
    {
        if (!_authority.RecordExists && !_authority.State.Operators.Any())
            await _authority.ReadStateAsync();
    }

    private async Task<GmOperationResult> DenyAsync(
        string operationId, long callerId, string operation, long target, string message)
    {
        await RecordAuditLogAsync(new GmAuditEntry
        {
            OperationId = operationId,
            GmId = callerId,
            OperationType = operation,
            TargetCharacterId = target,
            Details = $"拒绝：{message}",
        });
        _logger.LogWarning("GM 授权操作被拒绝。Operation={Operation}, Caller={Caller}, Target={Target}, Message={Message}",
            operation, callerId, target, message);
        return new GmOperationResult { Success = false, ErrorMessage = message, OperationId = operationId };
    }

    /// <summary>
    /// 写权威层（SQLServer）。顺序固定"先表后热层"：表写失败就不刷新热层，
    /// 以免出现"热层已生效但重启后消失"的假象。
    /// </summary>
    private async Task PersistGrantAsync(
        long operatorId, uint flags, long callerId, DateTime grantedAtUtc, DateTime expiresAtUtc, string? reason, DateTime? revokedAtUtc)
    {
        if (_grantContext == null) return;

        var existing = await _grantContext.QueryFirstOrDefaultAsync(e => e.OperatorCharacterId == operatorId, true);
        if (existing == null)
        {
            await _grantContext.AddAsync(new GmScriptGrantEntity
            {
                OperatorCharacterId = operatorId,
                Flags = flags,
                GrantedBy = callerId,
                GrantedAtUtc = grantedAtUtc,
                ExpiresAtUtc = expiresAtUtc,
                RevokedAtUtc = revokedAtUtc,
                Reason = reason ?? string.Empty,
            });
            return;
        }

        existing.Flags = flags;
        existing.GrantedBy = callerId;
        existing.GrantedAtUtc = grantedAtUtc;
        existing.ExpiresAtUtc = expiresAtUtc;
        existing.RevokedAtUtc = revokedAtUtc;
        existing.Reason = reason ?? string.Empty;
        await _grantContext.UpdateAsync(existing, existing.Id);
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
