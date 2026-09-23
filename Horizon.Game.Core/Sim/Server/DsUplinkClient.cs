using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>DS 上行客户端配置。</summary>
public sealed class DsUplinkClientOptions
{
    /// <summary>网关主机。</summary>
    public string GatewayHost { get; set; } = "127.0.0.1";

    /// <summary>网关 DS 上行端口（对应 <c>GatewayOptions.DsUplinkPort</c>）。</summary>
    public int GatewayPort { get; set; }

    /// <summary>本 DS 唯一标识。</summary>
    public string DsId { get; set; } = "ds-0";

    /// <summary>承载的 shard 集合。</summary>
    public long[] ShardIds { get; set; } = new long[] { 0 };

    /// <summary>本 DS 对网关可达的主机（网关据此把上行帧转发过来）。</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>本 DS 数据面监听端口。</summary>
    public int ListenPort { get; set; }

    /// <summary>区域标识（跨区盲路由防护）。</summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>共享令牌（对应 <c>GatewayOptions.DsControlToken</c>）。</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>心跳间隔（秒）。默认 30s——网关侧 90s（3×）判过期。</summary>
    public int HeartbeatSeconds { get; set; } = 30;

    /// <summary>控制应答等待超时（毫秒）。</summary>
    public int AckTimeoutMs { get; set; } = 5000;

    /// <summary>连接/重连退避基数（毫秒）。</summary>
    public int ReconnectBackoffMs { get; set; } = 1000;

    /// <summary>连接/重连退避上限（毫秒）。</summary>
    public int ReconnectBackoffMaxMs { get; set; } = 30000;

    /// <summary>从环境变量与命令行填充（宿主入口用）。</summary>
    public static DsUplinkClientOptions FromEnvironment(string dsId, long[] shardIds, int listenPort, string region, string host)
        => new DsUplinkClientOptions
        {
            DsId = dsId,
            ShardIds = shardIds,
            ListenPort = listenPort,
            Region = region,
            Host = host,
            GatewayHost = Environment.GetEnvironmentVariable("HORIZON_GATEWAY_HOST") ?? "127.0.0.1",
            GatewayPort = int.TryParse(Environment.GetEnvironmentVariable("HORIZON_GATEWAY_DS_UPLINK_PORT"), out var p) ? p : 0,
            Token = Environment.GetEnvironmentVariable("HORIZON_DS_CONTROL_TOKEN") ?? string.Empty,
        };
}

/// <summary>
/// DS → 网关上行客户端（MMORPG 开发计划 M2.4 / P3）。
/// <para>
/// 与网关侧 <c>DsUplinkServer</c> 配对：建立**一条长连接**，承载控制信令（注册/心跳/摘除）
/// 与权威快照回传（<see cref="DsSnapshotBatch"/>）。这是让 DS "被网关发现"的唯一途径——
/// 在该链路存在之前，<c>DSRegistry</c> 永远为空（缺口 G3）。
/// </para>
/// <para>
/// <b>为什么用 BCL socket 而非 TouchSocket</b>：DS 是独立进程，用
/// <see cref="TcpClient"/> 可以零框架耦合地把宿主移植到任意 .NET 环境
/// （含 UE UnrealSharp 托管域），也让分帧/重连/应答匹配等逻辑保持可单测。
/// 网关侧的监听仍然用 TouchSocket，与本类通过**线上字节**对接——两端只共享
/// <see cref="DsUplinkCodec"/> / <see cref="DsFrameReader"/>，不共享运行时类型。
/// </para>
/// <para>
/// <b>失败语义</b>：所有发送方法在网络异常时返回 <c>false</c> / 抛
/// <see cref="InvalidOperationException"/> 而不静默吞掉——DS 必须能感知"网关联系不上"
/// 并停止对外提供服务（否则会产生"玩家连着网关、网关没有路由表、玩家卡死"的黑洞）。
/// </para>
/// </summary>
public sealed class DsUplinkClient : IDsDownlinkSender, IDsGameplayEventSender, IAsyncDisposable
{
    private readonly DsUplinkClientOptions _options;
    private readonly Action<string>? _log;

    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private Task? _readLoop;

    /// <summary>分帧缓冲。**非 readonly**：重连后必须换成全新的实例（残留半帧会污染新连接）。</summary>
    private DsFrameReader _reader = new();
    private readonly ConcurrentQueue<TaskCompletionSource<DsControlAck>> _pendingAcks = new();

    /// <summary>
    /// 连接存活标志。**不能依赖 <c>TcpClient.Connected</c>**：对端静默断开后该属性仍可能为 true
    /// （它只反映最后一次 IO 操作的结果），会导致"以为还连着、实际每次都失败"。
    /// 因此由读循环退出/发送失败来显式置 false。
    /// </summary>
    private volatile bool _connected;

    /// <summary>创建上行客户端。</summary>
    /// <param name="options">配置。</param>
    /// <param name="log">日志回调（可空）。</param>
    public DsUplinkClient(DsUplinkClientOptions options, Action<string>? log = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _log = log;
    }

    /// <summary>当前是否已连接。</summary>
    public bool IsConnected => _connected && _client is not null;

    /// <summary>累计成功建立连接的次数（含重连）。</summary>
    public long ConnectCount { get; private set; }

    /// <summary>累计发送的快照批次数。</summary>
    public long SentBatchCount { get; private set; }

    /// <summary>累计发送的快照条目数。</summary>
    public long SentItemCount { get; private set; }

    /// <summary>累计收到的控制应答数。</summary>
    public long ReceivedAckCount { get; private set; }

    /// <summary>累计收到的会话结束通知数（网关告知"这些玩家已断线"）。</summary>
    public long SessionEndReceivedCount { get; private set; }

    /// <summary>累计因连接不可用而丢弃的快照条目数（监控告警源）。</summary>
    public long DroppedItemCount { get; private set; }

    /// <summary>最近一次收到的失败应答原因（诊断）。</summary>
    public string LastFailureReason { get; private set; } = string.Empty;

    /// <summary>建立到网关的连接并启动读循环。</summary>
    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_options.GatewayPort <= 0)
        {
            _log?.Invoke("DsUplinkClient：GatewayPort 未配置（<=0），拒绝连接。");
            return false;
        }

        try
        {
            _client = new TcpClient { NoDelay = true };
            await _client.ConnectAsync(_options.GatewayHost, _options.GatewayPort, cancellationToken).ConfigureAwait(false);
            _stream = _client.GetStream();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // 重连场景：必须换新的分帧缓冲与全新的等待队列，避免上一次连接的半帧/悬挂 waiter 污染本次。
            _reader = new DsFrameReader();
            while (_pendingAcks.TryDequeue(out var stale))
            {
                stale.TrySetResult(new DsControlAck { Ok = false, Reason = "reconnected" });
            }

            _connected = true;
            ConnectCount++;
            _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token), CancellationToken.None);
            _log?.Invoke($"DsUplinkClient：已连接网关 {_options.GatewayHost}:{_options.GatewayPort}（第 {ConnectCount} 次）。");
            return true;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"DsUplinkClient：连接网关失败：{ex.Message}");
            Cleanup();
            _connected = false;
            return false;
        }
    }

    /// <summary>发送注册并等待网关应答。</summary>
    public async Task<bool> RegisterAsync(CancellationToken cancellationToken = default)
    {
        var request = new DsRegisterRequest
        {
            DsId = _options.DsId,
            ShardIds = _options.ShardIds,
            Host = _options.Host,
            Port = _options.ListenPort,
            Region = _options.Region,
            Token = _options.Token,
        };

        var ack = await SendWithAckAsync(DsUplinkMessageKind.Register, request, cancellationToken).ConfigureAwait(false);
        if (ack is null)
        {
            LastFailureReason = "register_no_ack";
            return false;
        }

        if (!ack.Ok)
        {
            LastFailureReason = ack.Reason;
            _log?.Invoke($"DsUplinkClient：注册被拒。Reason={ack.Reason}");
            return false;
        }

        _log?.Invoke($"DsUplinkClient：注册成功。DsId={_options.DsId} Shards={string.Join(',', _options.ShardIds)} Port={_options.ListenPort}");
        return true;
    }

    /// <summary>发送心跳并等待应答。</summary>
    public async Task<bool> HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        var ack = await SendWithAckAsync(
            DsUplinkMessageKind.Heartbeat,
            new DsIdentityRequest { DsId = _options.DsId, Token = _options.Token },
            cancellationToken).ConfigureAwait(false);

        return ack is { Ok: true };
    }

    /// <summary>发送优雅摘除（下线）。</summary>
    public async Task<bool> UnregisterAsync(CancellationToken cancellationToken = default)
    {
        var ack = await SendWithAckAsync(
            DsUplinkMessageKind.Unregister,
            new DsIdentityRequest { DsId = _options.DsId, Token = _options.Token },
            cancellationToken).ConfigureAwait(false);

        return ack is { Ok: true };
    }

    /// <summary>
    /// 发送一批跨层玩法事件（击杀/掉落/玩家死亡）给网关转业务层（无应答，单向）。
    /// <para>
    /// 与快照共用同一条连接与同一套分帧：事件与快照都发生在同一 tick 上，
    /// 分两条连接会引入额外的乱序与重连复杂度，收益为零。
    /// </para>
    /// </summary>
    public Task<bool> SendGameplayEventsAsync(DsGameplayEventBatch batch, CancellationToken cancellationToken = default)
    {
        if (batch?.Events is null || batch.Events.Length == 0) return Task.FromResult(true);

        var payload = DsUplinkCodec.EncodePayload(DsUplinkMessageKind.GameplayEvent, batch);
        if (!TrySend(payload))
        {
            DroppedGameplayEventCount += batch.Events.Length;
            return Task.FromResult(false);
        }

        SentGameplayEventCount += batch.Events.Length;
        return Task.FromResult(true);
    }

    /// <summary>累计成功上送的跨层玩法事件数。</summary>
    public long SentGameplayEventCount { get; private set; }

    /// <summary>累计因连接不可用而丢弃的跨层玩法事件数（业务层对账的告警源）。</summary>
    public long DroppedGameplayEventCount { get; private set; }

    /// <summary>发送一批权威下行帧（无应答，单向）。</summary>
    public Task<bool> SendSnapshotBatchAsync(DsSnapshotBatch batch, CancellationToken cancellationToken = default)
    {
        if (batch?.Items is null || batch.Items.Length == 0) return Task.FromResult(true);

        var payload = DsUplinkCodec.EncodePayload(DsUplinkMessageKind.SnapshotBatch, batch);
        if (!TrySend(payload))
        {
            DroppedItemCount += batch.Items.Length;
            return Task.FromResult(false);
        }

        SentBatchCount++;
        SentItemCount += batch.Items.Length;
        return Task.FromResult(true);
    }

    private async Task<DsControlAck?> SendWithAckAsync<T>(DsUplinkMessageKind kind, T body, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<DsControlAck>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingAcks.Enqueue(tcs);

        if (!TrySend(DsUplinkCodec.EncodePayload(kind, body)))
        {
            // 连接不可用：把自己的 waiter 摘掉并立即失败，避免调用方干等到超时，
            // 也避免它被后续某个应答错误地"认领"（应答与请求是 FIFO 配对的）。
            TryDequeueAck(out _);
            LastFailureReason = "send_failed";
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.AckTimeoutMs);
        try
        {
            return await tcs.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LastFailureReason = "ack_timeout";
            return null;
        }
    }

    private bool TryDequeueAck(out TaskCompletionSource<DsControlAck> tcs) => _pendingAcks.TryDequeue(out tcs!);

    private bool TrySend(ReadOnlySpan<byte> payload)
    {
        var stream = _stream;
        if (stream is null) return false;

        try
        {
            var frame = DsUplinkCodec.FramePayload(payload);
            // 同步写：DS 的发送发生在单一 tick 线程上，天然串行；
            // 用一个小的写锁语义（同步 Send）避免引入额外的异步状态机与乱序风险。
            stream.Write(frame, 0, frame.Length);
            stream.Flush();
            return true;
        }
        catch (Exception ex)
        {
            // 发送失败即认为连接已失效：让控制面立刻走重连，而不是继续对着死 socket 发心跳。
            _connected = false;
            _log?.Invoke($"DsUplinkClient：发送失败：{ex.Message}");
            return false;
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16384);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var stream = _stream;
                if (stream is null) break;

                int read;
                try
                {
                    read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    break; // 连接断开/取消
                }

                if (read <= 0) break;

                _reader.Append(buffer.AsSpan(0, read));
                while (_reader.TryReadFrame(out var payload))
                {
                    HandleInbound(payload);
                    _reader.ConsumeFrame();
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            // 读循环退出 = 连接结束。**必须置 _connected=false**：
            // TcpClient.Connected 在对端静默断开后仍可能为 true，只看它会漏判。
            _connected = false;
            // 唤醒所有等待中的应答，避免调用方干等到超时。
            while (_pendingAcks.TryDequeue(out var pending))
            {
                pending.TrySetResult(new DsControlAck { Ok = false, Reason = "connection_closed" });
            }
        }
    }

    /// <summary>
    /// 收到「会话结束」通知（网关 → DS）。**在读循环线程上触发**——
    /// 订阅方负责把工作投递到 tick 线程（世界是非线程安全的）。
    /// </summary>
    public event Action<long[]>? SessionEndReceived;

    private void HandleInbound(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 1) return;

        var kind = (DsUplinkMessageKind)payload[0];

        // 网关 → DS 的会话结束通知（本通道为双向：DS 用它上报，网关用它下发通知）
        if (kind == DsUplinkMessageKind.SessionEnd)
        {
            if (DsUplinkCodec.TryDecode<DsSessionEndNotice>(payload, out _, out var notice)
                && notice?.SessionIds is { Length: > 0 })
            {
                SessionEndReceivedCount += notice.SessionIds.Length;
                try { SessionEndReceived?.Invoke(notice.SessionIds); }
                catch (Exception ex) { _log?.Invoke($"DsUplinkClient：会话结束回调异常：{ex.Message}"); }
            }
            return;
        }

        if (kind != DsUplinkMessageKind.ControlAck) return; // 其余上行消息 DS 不消费

        if (!DsUplinkCodec.TryDecode<DsControlAck>(payload, out _, out var ack) || ack is null) return;

        ReceivedAckCount++;
        if (!ack.Ok) LastFailureReason = ack.Reason;

        if (_pendingAcks.TryDequeue(out var tcs))
        {
            tcs.TrySetResult(ack);
        }
    }

    private void Cleanup()
    {
        try { _stream?.Dispose(); } catch { /* 关闭期忽略 */ }
        try { _client?.Dispose(); } catch { /* 关闭期忽略 */ }
        _stream = null;
        _client = null;
    }

    /// <summary>
    /// 断开当前连接，但**保持对象可复用**（区别于 <see cref="DisposeAsync"/>）。
    /// <para>
    /// 这是重连链路的关键：控制面在心跳失败后调用本方法，然后重新 <see cref="ConnectAsync"/>。
    /// 若此处直接 Dispose，客户端对象即报废，控制面只能退出——那正是
    /// "网关重启后 DS 永不回来"这一缺陷的成因（由 M5.3 混沌用例捕获）。
    /// </para>
    /// </summary>
    public void Disconnect()
    {
        _connected = false;
        try { _cts?.Cancel(); } catch { /* 忽略 */ }

        var loop = _readLoop;
        if (loop is not null)
        {
            try { loop.Wait(TimeSpan.FromSeconds(2)); } catch { /* 忽略 */ }
        }

        Cleanup();

        _cts?.Dispose();
        _cts = null;
        _readLoop = null;

        // 清空等待中的应答：重连后旧请求不再有意义。
        while (_pendingAcks.TryDequeue(out var pending))
        {
            pending.TrySetResult(new DsControlAck { Ok = false, Reason = "disconnected" });
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _connected = false;
        try { _cts?.Cancel(); } catch { /* 忽略 */ }
        if (_readLoop is not null)
        {
            try { await _readLoop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { /* 忽略 */ }
        }
        Cleanup();
        _cts?.Dispose();
        _cts = null;
        _readLoop = null;
    }
}
