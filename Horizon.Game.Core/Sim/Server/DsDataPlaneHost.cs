using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Message.Sync;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>
/// DS 数据面监听（MMORPG 开发计划 M2.2 / P3）—— 接收网关转发来的客户端上行帧。
/// <para>
/// 线上格式与网关侧 <c>DsSyncDataPlane</c> 完全对齐：<c>[4B 大端长度][SyncPacketCodec 原始帧]</c>。
/// 本类只做"接字节 → 切帧 → 解码 → 入队"，真正的模拟由宿主在**单一 tick 线程**上消费队列完成。
/// </para>
/// <para>
/// <b>为什么要过队列而不是在回调里直接改世界</b>：<see cref="DsWorldSession"/> 明确是非线程安全的
/// 单线程结构（与 Orleans grain 的 turn 模型同构）。网络回调线程直接改世界会立刻引入数据竞争，
/// 而且竞争只在生产高负载下才暴露——正是本项目历史上"偶发不同步"类缺陷的温床。
/// 队列把并发边界收敛到一个明确的接缝上。
/// </para>
/// <para>
/// <b>会话标识约定</b>：本架构中网关 fanout 用 <c>characterId</c> 作为 sessionId
/// （见 <c>ConnectionManagerSessionRegistry</c>），因此 DS 侧同样以
/// <c>sessionId == characterId</c> 记账，快照受众直接就是角色 ID，网关可原样解析。
/// </para>
/// </summary>
public sealed class DsDataPlaneHost : IAsyncDisposable
{
    private readonly DsWorldSession _world;
    private readonly int _listenPort;
    private readonly string _listenAddress;
    private readonly Action<string>? _log;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    private int _connectionSeq;
    private int _activeConnections;
    private readonly ConcurrentDictionary<int, byte> _loggedUnknownKinds = new();

    private readonly ConcurrentQueue<SyncPacket> _inbound = new();

    /// <summary>创建数据面监听。</summary>
    /// <param name="world">目标世界会话（由宿主 tick 线程消费队列）。</param>
    /// <param name="listenPort">监听端口（须与注册时上报的 Port 一致）。</param>
    /// <param name="listenAddress">监听地址。</param>
    /// <param name="log">日志回调。</param>
    public DsDataPlaneHost(DsWorldSession world, int listenPort, string listenAddress = "0.0.0.0", Action<string>? log = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _listenPort = listenPort;
        _listenAddress = listenAddress;
        _log = log;
    }

    /// <summary>累计接收的帧数。</summary>
    public long ReceivedFrameCount { get; private set; }

    /// <summary>累计解码失败的帧数。</summary>
    public long DecodeFailureCount { get; private set; }

    /// <summary>累计因连接丢弃而未处理的帧数。</summary>
    public long DroppedFrameCount { get; private set; }

    /// <summary>当前待处理队列长度（背压指标）。</summary>
    public int PendingCount => _inbound.Count;

    /// <summary>启动监听。</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_listenPort <= 0)
        {
            _log?.Invoke("DsDataPlaneHost：ListenPort<=0，数据面未启动。");
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var addr = IPAddress.TryParse(_listenAddress, out var parsed) ? parsed : IPAddress.Any;
        _listener = new TcpListener(addr, _listenPort);
        _listener.Start();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token), CancellationToken.None);
        _log?.Invoke($"DsDataPlaneHost：数据面已监听 {_listenAddress}:{_listenPort}");
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
                break; // 监听关闭/取消
            }

            var seq = Interlocked.Increment(ref _connectionSeq);
            var activeCount = Interlocked.Increment(ref _activeConnections);
            var remoteEp = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
            _log?.Invoke($"[DataPlane] 新连接 #{seq} from {remoteEp}, 当前活跃: {activeCount}");

            _ = Task.Run(() => ClientLoopAsync(client, seq, ct), CancellationToken.None);
        }
    }

    private async Task ClientLoopAsync(TcpClient client, int seq, CancellationToken ct)
    {
        var reader = new DsFrameReader();
        var buffer = ArrayPool<byte>.Shared.Rent(16384);
        var startTimestamp = Stopwatch.GetTimestamp();
        var boundCharacterIds = new HashSet<int>();
        long connFrameCount = 0;
        var gracefulClose = false;

        try
        {
            client.NoDelay = true;
            using var stream = client.GetStream();

            while (!ct.IsCancellationRequested)
            {
                int read;
                try
                {
                    read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"[DataPlane] 连接 #{seq} 读异常: {ex.GetType().Name}: {ex.Message}");
                    break;
                }

                if (read <= 0)
                {
                    gracefulClose = true;
                    break;
                }

                reader.Append(buffer.AsSpan(0, read));
                while (reader.TryReadFrame(out var payload))
                {
                    ReceivedFrameCount++;
                    connFrameCount++;
                    DecodeAndEnqueue(payload, boundCharacterIds);
                    reader.ConsumeFrame();
                }

                if (reader.OversizedFrameCount > 0)
                {
                    DecodeFailureCount += reader.OversizedFrameCount;
                    _log?.Invoke($"DsDataPlaneHost：收到非法长度前缀帧（累计 {reader.OversizedFrameCount}），已丢弃缓冲。");
                }
            }
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            _log?.Invoke($"[DataPlane] 连接 #{seq} 处理异常(存活 {elapsed.TotalSeconds:F1}s): {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            var activeCount = Interlocked.Decrement(ref _activeConnections);
            var closeReason = gracefulClose ? "对端关闭" : "异常断开";
            var charIds = boundCharacterIds.Count > 0 ? string.Join(",", boundCharacterIds) : "(无)";
            _log?.Invoke($"[DataPlane] 连接 #{seq} 断开({closeReason}), 存活 {elapsed.TotalSeconds:F1}s, 收帧 {connFrameCount}, 会话: {charIds}, 当前活跃: {activeCount}");

            ArrayPool<byte>.Shared.Return(buffer);
            try { client.Dispose(); } catch { /* 关闭期忽略 */ }
        }
    }

    private void DecodeAndEnqueue(ReadOnlySpan<byte> frame, HashSet<int>? boundCharacterIds = null)
    {
        try
        {
            var packet = SyncPacketCodec.Decode(frame);

            // 跟踪连接绑定的 characterId（用于断开日志）
            if (boundCharacterIds is not null)
            {
                switch (packet)
                {
                    case HandshakePacket hs:
                        boundCharacterIds.Add((int)hs.LocalCharacterId);
                        break;
                    case InputPacket ip:
                        boundCharacterIds.Add((int)ip.CharacterId);
                        break;
                    case ReconnectResumePacket rp:
                        boundCharacterIds.Add((int)rp.LocalCharacterId);
                        break;
                }
            }

            // 有界化：DS 卡顿时不能让上行无界堆积（内存爆炸比丢包更糟）。
            if (_inbound.Count > 4096)
            {
                DroppedFrameCount++;
                // 丢帧日志：首次 + 每 100 次采样
                if (DroppedFrameCount == 1 || DroppedFrameCount % 100 == 1)
                {
                    _log?.Invoke($"[DataPlane] 警告: 入站队列溢出, 累计丢帧 {DroppedFrameCount}, 当前积压 {_inbound.Count}");
                }
                return;
            }
            _inbound.Enqueue(packet);
        }
        catch (Exception)
        {
            // 不可信网络输入：坏帧只丢自己，不影响同连接后续帧。
            DecodeFailureCount++;
        }
    }

    /// <summary>尝试取出一帧待处理上行包（由宿主 tick 线程调用）。</summary>
    public bool TryDequeue(out SyncPacket packet) => _inbound.TryDequeue(out packet!);

    /// <summary>
    /// 在 tick 线程上一次性排空上行队列并应用到世界。
    /// </summary>
    /// <param name="maxPerTick">单 tick 处理上限，防止"积压洪峰"让本 tick 超帧预算。</param>
    /// <returns>实际处理帧数。</returns>
    public int DrainIntoWorld(int maxPerTick = 512)
    {
        var processed = 0;
        while (processed < maxPerTick && _inbound.TryDequeue(out var packet))
        {
            Apply(packet);
            processed++;
        }
        return processed;
    }

    /// <summary>把单个上行包应用到世界（由 tick 线程调用）。</summary>
    public void Apply(SyncPacket packet)
    {
        switch (packet)
        {
            case HandshakePacket handshake:
            {
                var characterId = handshake.LocalCharacterId;
                var sessionId = (long)characterId;
                if (!_world.AddPlayer(sessionId, characterId, handshake.InitialX, handshake.InitialY, handshake.InitialZ, out var reason))
                {
                    _log?.Invoke($"DsDataPlaneHost：握手被拒。CharacterId={characterId} Reason={reason}");
                }
                else
                {
                    _log?.Invoke($"[DataPlane] 握手成功 CharacterId={characterId}, 当前玩家: {_world.PlayerCount}");
                }
                return;
            }

            case InputPacket input:
            {
                var sessionId = (long)input.CharacterId;
                if (!_world.SubmitInput(sessionId, input))
                {
                    // 未握手的输入：静默丢弃（可能是断线重连竞态），不视为错误刷日志。
                    DroppedFrameCount++;
                }
                return;
            }

            case ReconnectResumePacket resume:
            {
                var sessionId = (long)resume.LocalCharacterId;
                if (!_world.TryGetPlayer(sessionId, out _))
                {
                    _log?.Invoke($"DsDataPlaneHost：收到未知会话的重连恢复请求。CharacterId={resume.LocalCharacterId}");
                }
                else
                {
                    // 已知会话重连恢复：客户端已重建本地 ECS 世界（实体全清），
                    // 登记下一快照节拍补发专属强制 Spawn 全量（否则重连后远端实体不再出现）。
                    _world.ScheduleInitialSpawn(sessionId);
                    _log?.Invoke($"[DataPlane] 重连恢复：已登记首包 Spawn 补发。CharacterId={resume.LocalCharacterId}");
                }
                return;
            }

            default:
            {
                // 其他包型（交互/场景对象等）在 M4 及后续里程碑接入；当前静默忽略。
                var kind = (int)packet.Kind;
                if (_loggedUnknownKinds.TryAdd(kind, 0))
                {
                    _log?.Invoke($"[DataPlane] 收到未处理包型 Kind={packet.Kind}({kind}), 已忽略(首次记录)");
                }
                return;
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try { _cts?.Cancel(); } catch { /* 忽略 */ }
        try { _listener?.Stop(); } catch { /* 忽略 */ }

        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { /* 忽略 */ }
        }

        _listener = null;
        _cts?.Dispose();
        _cts = null;
        _acceptLoop = null;
    }
}
