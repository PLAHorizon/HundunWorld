using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core.Sim.Server;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchSocket.Core;
using TouchSocket.Sockets;

namespace Horizon.Game.Gateway.Services;

/// <summary>
/// DS 上行服务（MMORPG 开发计划 M2.1/M2.2）—— 网关侧监听 DS 的注册/心跳/快照回传连接。
/// <para>
/// <b>本类只做 IO 薄壳</b>：把 <c>[4B 大端长度][kind+MemoryPack 负载]</c> 的字节流
/// 切成完整帧，交给 <see cref="DsControlPlane"/> 处理，再把应答写回。
/// 全部分帧逻辑在 <see cref="DsFrameReader"/>（纯函数，已被
/// <c>DsUplinkProtocolTests</c> 覆盖），全部控制语义在 <see cref="DsControlPlane"/>
/// （纯逻辑，已被 <c>DsControlPlaneTests</c> 覆盖）——本类只承担"接字节、调逻辑、发应答"。
/// </para>
/// <para>
/// <b>启用条件</b>：<c>GatewayOptions.UseDS == true</c> 且 <c>DsUplinkPort &gt; 0</c>。
/// 任一不满足即完全不启动监听（默认配置下零行为变化，符合「新增并行通道」纪律）。
/// </para>
/// <para>
/// <b>连接模型</b>：一个 DS 一条长连接；注册后该连接同时承载快照回传。
/// 连接断开时**不立即摘除**注册表条目——由 DS 自身的重连注册或
/// <c>DSRegistryMaintenanceHostedService</c> 的 90s 过期兜底，
/// 避免网络抖动导致路由表频繁抖动（抖动会让玩家在两个权威之间反复横跳）。
/// </para>
/// </summary>
public sealed class DsUplinkServer : IHostedService, Horizon.Game.Core.Sim.Server.IDsSessionNotifier, Horizon.Game.Core.Sim.Server.IDsScriptDispatcher, IAsyncDisposable
{
    private readonly IOptionsMonitor<Configuration.GatewayOptions> _options;
    private readonly DsControlPlane _controlPlane;
    private readonly ILogger<DsUplinkServer> _logger;
    private readonly TouchSocket.Core.ILog _touchSocketLogger;

    private TcpService? _service;
    private bool _started;

    /// <param name="options">网关配置（逐调用读取，支持热切换）。</param>
    /// <param name="controlPlane">控制面逻辑。</param>
    /// <param name="logger">网关日志器。</param>
    /// <param name="touchSocketLogger">TouchSocket 内部日志器（与既有 GameNetworkServer 同源注入）。</param>
    /// <param name="registry">DS 路由表（按 shard 解析目标 DS，用于反向下发会话结束通知）。</param>
    public DsUplinkServer(
        IOptionsMonitor<Configuration.GatewayOptions> options,
        DsControlPlane controlPlane,
        DSRegistry registry,
        CorpseLootUplinkHandler corpseLoot,
        ILogger<DsUplinkServer> logger,
        TouchSocket.Core.ILog touchSocketLogger)
    {
        _corpseLoot = corpseLoot ?? throw new ArgumentNullException(nameof(corpseLoot));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _controlPlane = controlPlane ?? throw new ArgumentNullException(nameof(controlPlane));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _touchSocketLogger = touchSocketLogger ?? throw new ArgumentNullException(nameof(touchSocketLogger));
    }

    /// <summary>DS 路由表。</summary>
    private readonly DSRegistry _registry;
    private readonly CorpseLootUplinkHandler _corpseLoot;

    /// <summary>
    /// dsId → 该 DS 的上行连接。注册时建立，连接关闭时清理。
    /// <para>用于把「会话结束」通知反向下发给正确的 DS——这是本通道**首次**由网关主动发起下发。</para>
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ITcpSessionClient> _clientsByDsId = new();

    /// <summary>累计下发的会话结束通知数。</summary>
    public long SessionEndNoticeCount { get; private set; }

    /// <inheritdoc cref="Horizon.Game.Core.Sim.Server.IDsSessionNotifier.NotifySessionEnded"/>
    public bool NotifySessionEnded(long shardId, System.Collections.Generic.IReadOnlyList<long> sessionIds)
    {
        if (sessionIds is null || sessionIds.Count == 0) return false;

        var opts = _options.CurrentValue;
        if (!opts.UseDS) return false;

        if (_registry.TryResolve(shardId, opts.Region, opts.StrictEmptyRegionMode, out var ep)
            != DsResolveStatus.Ok)
        {
            return false; // 无可用 DS：静默跳过（断线清理不能被 DS 可用性阻塞）
        }

        if (!_clientsByDsId.TryGetValue(ep.DsId, out var client))
        {
            return false;
        }

        try
        {
            var frame = Horizon.Game.Core.Sim.Server.DsUplinkCodec.EncodeFrame(
                Horizon.Game.Core.Sim.Server.DsUplinkMessageKind.SessionEnd,
                new Horizon.Game.Core.Sim.Server.DsSessionEndNotice
                {
                    SessionIds = System.Linq.Enumerable.ToArray(sessionIds),
                });

            // 非阻塞下发：本方法是同步接口（由断线清理路径调用），
            // 在其中同步等待 async 写入有死锁风险（延续任务可能回到同一上下文）。
            // 失败只记日志——**断线清理绝不能被 DS 的可用性阻塞**。
            _ = client.SendAsync(frame).ContinueWith(
                t => _logger.LogWarning(t.Exception, "DsUplinkServer：下发会话结束通知失败。DsId={DsId}", ep.DsId),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);

            SessionEndNoticeCount += sessionIds.Count;
            _logger.LogDebug("DsUplinkServer：已向 DS 下发会话结束通知。DsId={DsId} Sessions={Count}", ep.DsId, sessionIds.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DsUplinkServer：下发会话结束通知失败。DsId={DsId}", ep.DsId);
            return false;
        }
    }

    /// <summary>累计下发的脚本生成指令数（诊断）。</summary>
    public long ScriptDispatchCount { get; private set; }

    /// <summary>
    /// 下发玩家权威等级/累计经验（击杀经验已落进 CharacterGrain 之后）。
    /// <para>不接这条：经验真的加进了 Grain，但 DS 的玩家快照里 Exp 恒为 0，
    /// 客户端经验条永远停在登录值。</para>
    /// </summary>
    public bool NotifyPlayerProgress(long shardId, long characterId, int level, long exp)
    {
        if (characterId <= 0) return false;

        var opts = _options.CurrentValue;
        if (!opts.UseDS) return false;

        if (_registry.TryResolve(shardId, opts.Region, opts.StrictEmptyRegionMode, out var ep)
            != DsResolveStatus.Ok)
        {
            return false;
        }

        if (!_clientsByDsId.TryGetValue(ep.DsId, out var client)) return false;

        try
        {
            var frame = Horizon.Game.Core.Sim.Server.DsUplinkCodec.EncodeFrame(
                Horizon.Game.Core.Sim.Server.DsUplinkMessageKind.PlayerProgress,
                new Horizon.Game.Core.Sim.Server.DsPlayerProgressNotice
                {
                    CharacterId = characterId,
                    Level = level,
                    Exp = exp,
                });

            _ = client.SendAsync(frame).ContinueWith(
                t => _logger.LogWarning(t.Exception, "DsUplinkServer：下发玩家进度失败。DsId={DsId}", ep.DsId),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);

            PlayerProgressNoticeCount++;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DsUplinkServer：下发玩家进度失败。DsId={DsId}", ep.DsId);
            return false;
        }
    }

    /// <summary>累计下发的玩家进度通知数。</summary>
    public long PlayerProgressNoticeCount { get; private set; }

    /// <inheritdoc cref="Horizon.Game.Core.Sim.Server.IDsScriptDispatcher.DispatchAsync"/>
    public async System.Threading.Tasks.Task<bool> DispatchAsync(
        long shardId, Horizon.Game.Message.Network.ScriptGrantToken token)
    {
        if (token is null) return false;

        var opts = _options.CurrentValue;
        if (!opts.UseDS) return false;

        if (_registry.TryResolve(shardId, opts.Region, opts.StrictEmptyRegionMode, out var ep)
            != DsResolveStatus.Ok)
        {
            _logger.LogWarning("DsUplinkServer：脚本指令无可用 DS。ShardId={ShardId}", shardId);
            return false;
        }

        if (!_clientsByDsId.TryGetValue(ep.DsId, out var client))
        {
            _logger.LogWarning("DsUplinkServer：DS 连接未建立，脚本指令无法下发。DsId={DsId}", ep.DsId);
            return false;
        }

        try
        {
            var frame = Horizon.Game.Core.Sim.Server.DsUplinkCodec.EncodeFrame(
                Horizon.Game.Core.Sim.Server.DsUplinkMessageKind.ScriptedSpawn,
                new Horizon.Game.Core.Sim.Server.DsScriptedSpawnEnvelope { Token = token });

            // 这里 await：脚本指令的"已受理"必须建立在真实写入之上（与尽力而为的断线清理不同）。
            await client.SendAsync(frame);

            ScriptDispatchCount++;
            _logger.LogInformation(
                "DsUplinkServer：已下发脚本生成指令。DsId={DsId} Operator={Operator} Kind={Kind} Shard={ShardId}",
                ep.DsId, token.OperatorCharacterId, token.Kind, shardId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DsUplinkServer：下发脚本生成指令失败。DsId={DsId}", ep.DsId);
            return false;
        }
    }

    /// <summary>累计处理的帧数（诊断）。</summary>
    public long ReceivedFrameCount { get; private set; }

    /// <summary>累计因帧非法/超长被丢弃的次数。</summary>
    public long DroppedFrameCount { get; private set; }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;
        if (!opts.UseDS)
        {
            _logger.LogInformation("DsUplinkServer：UseDS=false，DS 上行通道未启动（零行为变化）。");
            return;
        }

        if (opts.DsUplinkPort <= 0)
        {
            _logger.LogInformation("DsUplinkServer：DsUplinkPort<=0，DS 上行通道未配置，未启动。");
            return;
        }

        // 每个连接一份分帧缓冲：键为 TouchSocket 的连接 Id。
        var readers = new System.Collections.Concurrent.ConcurrentDictionary<string, DsFrameReader>();

        var service = new TcpService();
        service.Connected = (client, e) =>
        {
            readers[client.Id] = new DsFrameReader();
            _logger.LogInformation("DsUplinkServer：DS 连接建立。Id={Id} Remote={Ip}:{Port}", client.Id, client.IP, client.Port);
            return Task.CompletedTask;
        };
        service.Closed = (client, e) =>
        {
            readers.TryRemove(client.Id, out _);

            // 清理 dsId → 连接映射（遍历比对连接对象，避免为每个 dsId 维护反向索引）
            foreach (var kv in _clientsByDsId)
            {
                if (ReferenceEquals(kv.Value, client)) _clientsByDsId.TryRemove(kv.Key, out _);
            }

            _logger.LogInformation("DsUplinkServer：DS 连接关闭。Id={Id}（注册表条目由心跳过期兜底摘除）", client.Id);
            return Task.CompletedTask;
        };
        service.Received = async (client, e) =>
        {
            if (!readers.TryGetValue(client.Id, out var reader))
            {
                reader = new DsFrameReader();
                readers[client.Id] = reader;
            }

            reader.Append(e.Memory.Span);

            while (reader.TryReadFrame(out var payload))
            {
                // 复制出负载：TryReadFrame 返回的是内部缓冲切片，ConsumeFrame 会覆盖它。
                var owned = ArrayPool<byte>.Shared.Rent(payload.Length);
                try
                {
                    payload.CopyTo(owned);
                    await HandleFrameAsync(client, owned.AsMemory(0, payload.Length)).ConfigureAwait(false);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(owned);
                }

                reader.ConsumeFrame();
                ReceivedFrameCount++;
            }

            if (reader.OversizedFrameCount > 0)
            {
                DroppedFrameCount += reader.OversizedFrameCount;
                _logger.LogWarning(
                    "DsUplinkServer：连接 {Id} 收到非法长度前缀帧（累计 {Count}），已丢弃缓冲。",
                    client.Id, reader.OversizedFrameCount);
            }
        };

        var config = new TouchSocketConfig()
            .SetListenIPHosts(new IPHost[] { new IPHost($"{opts.DsUplinkListenAddress}:{opts.DsUplinkPort}") })
            .ConfigureContainer(container => container.AddLogger(_touchSocketLogger))
            .ConfigurePlugins(_ => { });

        await service.SetupAsync(config).ConfigureAwait(false);
        await service.StartAsync().ConfigureAwait(false);

        _service = service;
        _started = true;

        _logger.LogInformation(
            "DsUplinkServer：DS 上行通道已启动，监听 {Addr}:{Port}（令牌校验={Token}）",
            opts.DsUplinkListenAddress, opts.DsUplinkPort,
            string.IsNullOrEmpty(opts.DsControlToken) ? "关闭" : "开启");
    }

    /// <summary>处理单个完整帧（分派到控制面并写回应答）。</summary>
    private async Task HandleFrameAsync(ITcpSessionClient client, ReadOnlyMemory<byte> payloadMemory)
    {
        var payload = payloadMemory.Span;
        if (payload.Length < 1)
        {
            DroppedFrameCount++;
            return;
        }

        var kind = (DsUplinkMessageKind)payload[0];

        switch (kind)
        {
            case DsUplinkMessageKind.Register:
            {
                if (!DsUplinkCodec.TryDecode<DsRegisterRequest>(payload, out _, out var req))
                {
                    DroppedFrameCount++;
                    return;
                }
                var result = _controlPlane.HandleRegister(req);

                // 记录 dsId → 连接：会话结束通知需要按 dsId 反向下发到正确的 DS。
                if (result.Ok && req.DsId is { Length: > 0 })
                {
                    _clientsByDsId[req.DsId] = client;
                }

                await SendAckAsync(client, result).ConfigureAwait(false);
                return;
            }

            case DsUplinkMessageKind.Heartbeat:
            {
                if (!DsUplinkCodec.TryDecode<DsIdentityRequest>(payload, out _, out var req))
                {
                    DroppedFrameCount++;
                    return;
                }
                var result = _controlPlane.HandleHeartbeat(req);
                await SendAckAsync(client, result).ConfigureAwait(false);
                return;
            }

            case DsUplinkMessageKind.Unregister:
            {
                if (!DsUplinkCodec.TryDecode<DsIdentityRequest>(payload, out _, out var req))
                {
                    DroppedFrameCount++;
                    return;
                }
                var result = _controlPlane.HandleUnregister(req);
                await SendAckAsync(client, result).ConfigureAwait(false);
                return;
            }

            case DsUplinkMessageKind.SnapshotBatch:
            {
                if (!DsUplinkCodec.TryDecode<DsSnapshotBatch>(payload, out _, out var batch))
                {
                    DroppedFrameCount++;
                    return;
                }
                _controlPlane.HandleSnapshotBatch(batch); // 单向，无应答
                return;
            }

            case DsUplinkMessageKind.GameplayEvent:
            {
                if (!DsUplinkCodec.TryDecode<DsGameplayEventBatch>(payload, out _, out var batch))
                {
                    DroppedFrameCount++;
                    return;
                }
                _controlPlane.HandleGameplayEventBatch(batch); // 单向，无应答
                return;
            }

            case DsUplinkMessageKind.CorpseLootRegister:
            {
                if (!DsUplinkCodec.TryDecode<DsCorpseLootRegister>(payload, out _, out var register) || register is null)
                {
                    DroppedFrameCount++;
                    return;
                }
                await _corpseLoot.HandleRegisterAsync(register).ConfigureAwait(false);
                return;
            }

            case DsUplinkMessageKind.CorpseLootGone:
            {
                if (!DsUplinkCodec.TryDecode<DsCorpseLootGone>(payload, out _, out var gone) || gone is null)
                {
                    DroppedFrameCount++;
                    return;
                }
                await _corpseLoot.HandleGoneAsync(gone).ConfigureAwait(false);
                return;
            }

            case DsUplinkMessageKind.CorpseLootForward:
            {
                if (!DsUplinkCodec.TryDecode<DsCorpseLootForward>(payload, out _, out var forward) || forward is null)
                {
                    DroppedFrameCount++;
                    return;
                }
                var reply = await _corpseLoot.HandleForwardAsync(forward).ConfigureAwait(false);
                try
                {
                    var frame = DsUplinkCodec.EncodeFrame(DsUplinkMessageKind.CorpseLootResult, reply);
                    await client.SendAsync(frame).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // 回执写失败：DS 那边会按超时丢弃这条请求；这里必须留下日志，否则"点了没反应"无从解释。
                    _logger.LogWarning(ex, "DsUplinkServer：写回尸体拾取回执失败。Id={Id}", client.Id);
                }
                return;
            }

            case DsUplinkMessageKind.EntityDescriptor:
            {
                if (!DsUplinkCodec.TryDecode<DsEntityDescriptorBatch>(payload, out _, out var batch))
                {
                    DroppedFrameCount++;
                    return;
                }
                _controlPlane.HandleEntityDescriptorBatch(batch); // 单向，无应答
                return;
            }

            default:
                // 未知 kind：前向兼容——旧网关遇到新 DS 的新消息只跳过，不断连。
                DroppedFrameCount++;
                return;
        }
    }

    private async Task SendAckAsync(ITcpSessionClient client, DsControlResult result)
    {
        if (!result.ExpectsAck) return;

        try
        {
            var ack = new DsControlAck { Ok = result.Ok, Reason = result.Reason };
            var frame = DsUplinkCodec.EncodeFrame(DsUplinkMessageKind.ControlAck, ack);
            await client.SendAsync(frame).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 应答失败不应影响控制面已生效的状态变更（注册表已更新）。
            _logger.LogWarning(ex, "DsUplinkServer：写回控制应答失败。Id={Id}", client.Id);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_started || _service is null) return;

        try
        {
            await _service.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DsUplinkServer：停止时发生异常（忽略）。");
        }
        finally
        {
            _started = false;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_service is not null)
        {
            try { await _service.StopAsync().ConfigureAwait(false); } catch { /* 关闭期忽略 */ }
            _service.Dispose();
            _service = null;
        }
    }
}
