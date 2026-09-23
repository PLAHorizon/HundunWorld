using System;
using System.Collections.Generic;
using Horizon.Game.Core.Sim.Server;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.Gateway.Services;

/// <summary>控制面处理结果（供宿主写回应答与记录指标）。</summary>
public readonly struct DsControlResult
{
    /// <summary>是否受理成功。</summary>
    public bool Ok { get; init; }

    /// <summary>失败原因（成功时为空串）。</summary>
    public string Reason { get; init; }

    /// <summary>是否需要向对端回写 <see cref="DsControlAck"/>（快照批次等单向消息无需应答）。</summary>
    public bool ExpectsAck { get; init; }

    /// <summary>便捷构造成功结果。</summary>
    public static DsControlResult Success() => new DsControlResult { Ok = true, Reason = string.Empty, ExpectsAck = true };

    /// <summary>便捷构造失败结果。</summary>
    public static DsControlResult Failure(string reason) => new DsControlResult { Ok = false, Reason = reason, ExpectsAck = true };
}

/// <summary>
/// DS 上行控制面处理逻辑（MMORPG 开发计划 M2.1 —— 消灭缺口 G3「DS 注册无传输通道」）。
/// <para>
/// <b>背景</b>：<see cref="DSRegistry"/> 的 <c>RegisterOrUpdate</c>/<c>Heartbeat</c>/<c>Unregister</c>
/// 在此之前**全仓无调用方**——它们只是被设计出来却从未被接线。DS 因此无法被网关发现，
/// 路由表恒空，<c>DsSyncDataPlane.TryRouteAsync</c> 永远返回 <c>no_ds</c> 并回退 Orleans。
/// 本类把这套注册表真正接到线上。
/// </para>
/// <para>
/// <b>为什么是独立类而不是直接写在网络回调里</b>：控制面的语义（幂等 upsert、令牌校验、
/// 未知 DS 的心跳容错、灰度开关拒绝）是**纯逻辑**，与 TouchSocket 的 IO 回调生命周期无关。
/// 拆开后可在单元测试里完整覆盖，无需起真实 socket——这与项目既有的
/// "协议/编排逻辑可测、IO 薄壳不可测" 分层一致。
/// </para>
/// <para>
/// <b>线程安全</b>：<see cref="DSRegistry"/> 自身对全部公开方法加锁；本类不持有可变共享状态
/// （计数器为诊断用途，允许弱一致）。
/// </para>
/// </summary>
public sealed class DsControlPlane
{
    private readonly DSRegistry _registry;
    private readonly DsDownlinkIngress _downlink;
    private readonly ILogger<DsControlPlane>? _logger;
    private readonly Func<bool> _isEnabled;
    private readonly string _expectedToken;
    private readonly IDsGameplayEventSink? _gameplaySink;

    /// <summary>累计受理的跨层玩法事件数（击杀/掉落/玩家死亡）。</summary>
    public long GameplayEventCount { get; private set; }

    /// <summary>累计因无接收端（业务层未接线）而丢弃的玩法事件数。</summary>
    public long DroppedGameplayEventCount { get; private set; }

    /// <summary>累计受理的注册数。</summary>
    public long RegisterCount { get; private set; }

    /// <summary>累计受理的心跳数。</summary>
    public long HeartbeatCount { get; private set; }

    /// <summary>累计受理的摘除数。</summary>
    public long UnregisterCount { get; private set; }

    /// <summary>累计因令牌不匹配被拒的次数（安全指标）。</summary>
    public long TokenRejectedCount { get; private set; }

    /// <summary>累计因灰度开关关闭被拒的次数。</summary>
    public long DisabledRejectedCount { get; private set; }

    /// <summary>累计因 DS 未注册而丢弃的心跳数。</summary>
    public long UnknownDsHeartbeatCount { get; private set; }

    /// <summary>创建控制面。</summary>
    /// <param name="registry">DS 注册表（路由表来源）。</param>
    /// <param name="downlink">下行入口（快照批次落地）。</param>
    /// <param name="isEnabled">启用判定：通常为 <c>GatewayOptions.UseDS</c>（逐调用读取，支持热切换）。</param>
    /// <param name="expectedToken">期望的共享令牌；空串/null = 不校验（仅适用于本机回环部署）。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="gameplaySink">跨层玩法事件接收端（转 Orleans 业务层）；null = 事件被计数丢弃。</param>
    public DsControlPlane(
        DSRegistry registry,
        DsDownlinkIngress downlink,
        Func<bool> isEnabled,
        string? expectedToken = null,
        ILogger<DsControlPlane>? logger = null,
        IDsGameplayEventSink? gameplaySink = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _downlink = downlink ?? throw new ArgumentNullException(nameof(downlink));
        _isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
        _expectedToken = expectedToken ?? string.Empty;
        _logger = logger;
        _gameplaySink = gameplaySink;
    }

    /// <summary>
    /// 处理 DS 注册（幂等 upsert）。
    /// <para>校验顺序刻意固定：开关 → 令牌 → 字段合法性 → 落地。
    /// 开关优先可在灰度关闭时立即停止新 DS 接入而不必改令牌。</para>
    /// </summary>
    public DsControlResult HandleRegister(DsRegisterRequest? request)
    {
        if (!_isEnabled())
        {
            DisabledRejectedCount++;
            return DsControlResult.Failure("ds_channel_disabled");
        }

        if (request is null)
        {
            return DsControlResult.Failure("null_request");
        }

        if (!IsTokenValid(request.Token))
        {
            TokenRejectedCount++;
            _logger?.LogWarning("DsControlPlane：DS 注册令牌不匹配，已拒绝。DsId={DsId}", request.DsId);
            return DsControlResult.Failure("bad_token");
        }

        if (string.IsNullOrWhiteSpace(request.DsId))
        {
            return DsControlResult.Failure("missing_dsid");
        }

        if (string.IsNullOrWhiteSpace(request.Host))
        {
            return DsControlResult.Failure("missing_host");
        }

        if (request.Port <= 0 || request.Port > 65535)
        {
            return DsControlResult.Failure("bad_port");
        }

        var shardIds = request.ShardIds ?? Array.Empty<long>();

        try
        {
            _registry.RegisterOrUpdate(request.DsId, shardIds, request.Host, request.Port, request.Region);
        }
        catch (ArgumentException ex)
        {
            // DSRegistry 的参数校验（空 DsId / 非法端口）在此转为协议级失败，不向上抛。
            return DsControlResult.Failure("invalid_argument:" + ex.ParamName);
        }

        RegisterCount++;
        _logger?.LogInformation(
            "DsControlPlane：DS 已注册。DsId={DsId} Shards={Shards} Host={Host}:{Port} Region={Region}",
            request.DsId, string.Join(',', shardIds), request.Host, request.Port,
            string.IsNullOrEmpty(request.Region) ? "<none>" : request.Region);

        return DsControlResult.Success();
    }

    /// <summary>
    /// 处理心跳续约。
    /// <para>对**未注册**的 DS 返回成功但不做任何事：DS 可能先于注册被网关重启驱逐，
    /// 此时回错误会让 DS 侧误以为自身 ID 非法而放弃；返回 <c>Ok</c> 让 DS 继续按周期发送，
    /// 由它自己的注册重试逻辑补齐（心跳不应承担注册职责）。</para>
    /// </summary>
    public DsControlResult HandleHeartbeat(DsIdentityRequest? request)
    {
        if (!_isEnabled())
        {
            DisabledRejectedCount++;
            return DsControlResult.Failure("ds_channel_disabled");
        }

        if (request is null)
        {
            return DsControlResult.Failure("null_request");
        }

        if (!IsTokenValid(request.Token))
        {
            TokenRejectedCount++;
            return DsControlResult.Failure("bad_token");
        }

        if (string.IsNullOrWhiteSpace(request.DsId))
        {
            return DsControlResult.Failure("missing_dsid");
        }

        var before = _registry.CountAlive();
        _registry.Heartbeat(request.DsId);

        // 无法直接区分"续约成功"与"DS 不存在"（Heartbeat 对未知 ID 静默无操作），
        // 用 Snapshot 做一次廉价存在性判定——心跳频率仅 30s/DS，开销可忽略。
        var exists = false;
        foreach (var e in _registry.Snapshot())
        {
            if (string.Equals(e.DsId, request.DsId, StringComparison.Ordinal)) { exists = true; break; }
        }

        if (!exists)
        {
            UnknownDsHeartbeatCount++;
            _logger?.LogDebug("DsControlPlane：收到未注册 DS 的心跳（忽略）。DsId={DsId}", request.DsId);
            return DsControlResult.Success();
        }

        HeartbeatCount++;
        _ = before; // 保留局部变量便于将来接指标
        return DsControlResult.Success();
    }

    /// <summary>处理优雅摘除（DS 主动下线）。</summary>
    public DsControlResult HandleUnregister(DsIdentityRequest? request)
    {
        if (request is null)
        {
            return DsControlResult.Failure("null_request");
        }

        if (!IsTokenValid(request.Token))
        {
            TokenRejectedCount++;
            return DsControlResult.Failure("bad_token");
        }

        if (string.IsNullOrWhiteSpace(request.DsId))
        {
            return DsControlResult.Failure("missing_dsid");
        }

        // 摘除**不受灰度开关限制**：即使通道已关闭，也必须允许 DS 把自己从路由表里摘掉，
        // 否则开关关闭瞬间残留在表中的条目会造成路由黑洞（直到 90s 过期）。
        _registry.Unregister(request.DsId);
        UnregisterCount++;
        _logger?.LogInformation("DsControlPlane：DS 已摘除。DsId={DsId}", request.DsId);

        return DsControlResult.Success();
    }

    /// <summary>
    /// 处理 DS 上报的权威快照批次（无需应答）。
    /// <para>通道关闭时直接丢弃：DS 不应该在灰度关闭后继续驱动客户端状态。</para>
    /// </summary>
    public DsControlResult HandleSnapshotBatch(DsSnapshotBatch? batch)
    {
        if (!_isEnabled())
        {
            DisabledRejectedCount++;
            return new DsControlResult { Ok = false, Reason = "ds_channel_disabled", ExpectsAck = false };
        }

        var accepted = _downlink.Ingest(batch);
        return new DsControlResult { Ok = true, Reason = string.Empty, ExpectsAck = false, };
    }

    /// <summary>
    /// 处理 DS 上报的跨层玩法事件批次（击杀/掉落/玩家死亡）。
    /// <para>
    /// 语义要点：
    /// <list type="bullet">
    ///   <item>通道关闭时**整体丢弃**——灰度回切后 DS 不应再影响业务层状态。</item>
    ///   <item>单个事件转换失败不阻断同批其他事件（与下行注入同一条容错纪律）。</item>
    ///   <item>无接收端时**计数丢弃而非抛异常**：业务层接线前，DS 仍应能正常跑完整个战斗闭环，
    ///         否则"先上 DS、后接业务"的灰度顺序将无法成立。</item>
    /// </list>
    /// </para>
    /// </summary>
    public DsControlResult HandleGameplayEventBatch(DsGameplayEventBatch? batch)
    {
        if (!_isEnabled())
        {
            DisabledRejectedCount++;
            return new DsControlResult { Ok = false, Reason = "ds_channel_disabled", ExpectsAck = false };
        }

        if (batch?.Events is null || batch.Events.Length == 0)
        {
            return new DsControlResult { Ok = true, Reason = string.Empty, ExpectsAck = false };
        }

        for (var i = 0; i < batch.Events.Length; i++)
        {
            var dto = batch.Events[i];
            if (dto is null) continue;

            if (_gameplaySink is null)
            {
                DroppedGameplayEventCount++;
                continue;
            }

            try
            {
                var evt = dto.ToCoreEvent();
                _gameplaySink.Publish(in evt);
                GameplayEventCount++;
            }
            catch (Exception ex)
            {
                // 业务层异常不能反噬实时链路：DS 的战斗结算已经在客户端生效了，
                // 一条掉落事件没入包是可补偿的（幂等键 + 事后对账），让网关挂掉不是。
                DroppedGameplayEventCount++;
                _logger?.LogWarning(ex,
                    "DsControlPlane：投递玩法事件失败（已丢弃）。Kind={Kind} Actor={Actor} Item={Item}",
                    dto.Kind, dto.ActorCharacterId, dto.ItemId);
            }
        }

        return new DsControlResult { Ok = true, Reason = string.Empty, ExpectsAck = false };
    }

    private bool IsTokenValid(string? token)
    {
        if (string.IsNullOrEmpty(_expectedToken)) return true; // 未配置令牌 = 不校验
        return string.Equals(token ?? string.Empty, _expectedToken, StringComparison.Ordinal);
    }
}
