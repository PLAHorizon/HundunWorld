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

    /// <summary>
    /// 累计因**入站队列溢出**而未处理的帧数（DS 卡顿，内存护栏）。
    /// <para>与 <see cref="InputWithoutSessionCount"/> **刻意分两个数**：两者在玩家那边的表现一模一样
    /// （动了没反应），但成因完全不同——一个是"这台机器跑不过来"，另一个是"这台 DS 根本不认识这个会话"
    /// （典型现场：同一个 shard 上起了两台 DS，网关把会话判给另一台、却把输入帧发给这台）。
    /// 合成一格之后两种故障同形，只能靠猜（见 [[assert-mechanism-and-effect-separately]] 的反方向：
    /// 一个计数器装两种成因＝没有成因）。</para>
    /// </summary>
    public long DroppedFrameCount { get; private set; }

    /// <summary>
    /// 累计"没有握手记录的上行输入"被丢弃的帧数。
    /// <para>这条以前挂在 <see cref="DroppedFrameCount"/> 上且**完全静默**（溢出那一路每 100 条还会念一次），
    /// 实测形状是：客户端有画面、DS 摘要 <c>玩家=0 收帧=1753 丢帧=1753</c>、日志里一条握手行都没有
    /// ⇒ 排查的人只能猜"是不是内容包没装对"。</para>
    /// </summary>
    public long InputWithoutSessionCount { get; private set; }

    /// <summary>
    /// 同一会话两次战斗上行之间至少要隔这么多 tick（默认 1 秒）：
    /// 客户端理论上每帧都能发一条，不限流就是把"刷包打怪"敞开。
    /// <para>刻意放在数据面（不可信输入的边界）而不是 <c>DsWorldSession</c> 里：
    /// 会话内测试直接提交动作时不该被一层节拍挡住，判据要能单独表达"结算对不对"。</para>
    /// </summary>
    public const long MinCombatActionIntervalTicks = 60;

    /// <summary>累计受理的战斗上行包数。</summary>
    public long CombatActionCount { get; private set; }

    /// <summary>被边界限流挡下的战斗上行数（"我明明在砍"的第一归因）。</summary>
    public long CombatActionRateLimitedCount { get; private set; }

    /// <summary>会话不存在（未握手/已离开）而被丢弃的战斗上行数。</summary>
    public long CombatActionRejectedNoSessionCount { get; private set; }

    /// <summary>结算命中（伤害真的落到了目标身上）。</summary>
    public long CombatActionHitCount { get; private set; }

    /// <summary>结算判成够不着（客户端与权威侧的距离/射程口径不一致的第一归因）。</summary>
    public long CombatActionOutOfRangeCount { get; private set; }

    /// <summary>结算判成非法（目标号不存在、不可攻击等）。</summary>
    public long CombatActionInvalidCount { get; private set; }

    /// <summary>结算判成目标正在回位免伤（整段回家路上都打不到）。</summary>
    public long CombatActionEvadingCount { get; private set; }

    /// <summary>结算返回了未预期的档位。</summary>
    public long CombatActionOtherOutcomeCount { get; private set; }

    /// <summary>收到的客户端拾取请求数。</summary>
    public long CorpseLootRequestsSeen { get; private set; }

    /// <summary>当场拒掉的拾取请求数（窗口/距离/会话不过）。</summary>
    public long CorpseLootRejected { get; private set; }

    /// <summary>收到的翻越请求数（与后续受理/拒绝成对；只有这一格增长说明裁决没跑）。</summary>
    public long TraversalRequestsSeen { get; private set; }

    /// <summary>每个会话最后一次被受理的战斗上行 tick（由 tick 线程独占访问）。</summary>
    private readonly Dictionary<long, long> CombatActionLastTick = new();

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
                    case Horizon.Game.Message.Sync.CombatActionPacket cb:
                        boundCharacterIds.Add((int)cb.AttackerId);
                        break;
                    case Horizon.Game.Message.Sync.TraversalRequestPacket tp:
                        boundCharacterIds.Add((int)tp.CharacterId);
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

                // 坐标系修复：握手 InitialX/Y/Z 是 Flax Y-up（来自 CharacterInfo.Position，
                // 服务端下发前已换成 Flax 空间；客户端地面对齐后的 Y 也是 Flax 上下）。
                // SimWorld 与 ZoneShard 同源，约定 ECS Z-up——Orleans 通道的对偶换轴在
                // RegisterEntityAsync 内部完成，DS 入口此前漏做，导致"上下"被当成"前后"种进
                // 模拟世界：重登恢复/远程角色都会错一个轴，且错值会随写回缓存逐轮放大。
                // 快照输出侧（BuildPlayerDelta 的 ECS→Flax）保持不变，此处补齐输入侧配对偶变换：
                // ecsX=flaxX（左右）, ecsY=flaxZ（前后）, ecsZ=flaxY（上下）。
                var ecsX = handshake.InitialX;
                var ecsY = handshake.InitialZ;
                var ecsZ = handshake.InitialY;

                if (!_world.AddPlayer(sessionId, characterId, ecsX, ecsY, ecsZ, out var reason,
                        nickname: handshake.Nickname, level: handshake.Level, title: handshake.Title,
                        faction: handshake.Faction))
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
                if (!_world.TryGetPlayer(sessionId, out _))
                {
                    // 这台 DS 根本没有这个会话：握手从没到过这里。典型现场是**同一个 shard 上起了两台 DS**
                    // （本地开发期很容易撞）：网关把会话判给另一台，却把该玩家的输入帧也发给这台，
                    // 于是客户端有画面、这台 DS 恒 `玩家=0`，而现场只看到一个"丢帧"数在涨。
                    // 判据问的是世界自己那份会话表（`TryGetPlayer`），不在这里另立一条"谁算有会话"的规则。
                    InputWithoutSessionCount++;
                    if (InputWithoutSessionCount == 1 || InputWithoutSessionCount % 100 == 1)
                    {
                        _log?.Invoke($"[DataPlane] 警告: 收到没有会话的输入, 累计 {InputWithoutSessionCount} 帧, " +
                                     $"CharacterId={input.CharacterId}, 这台 DS 当前玩家数 {_world.PlayerCount}" +
                                     "（成因不是卡顿：这台没被握手过——查是不是另一台 DS 也注册了同一个 shard）");
                    }
                    return;
                }
                if (!_world.SubmitInput(sessionId, input))
                {
                    // 会话在、这一帧没被接受（重复 clientTick 由世界自己那份计数）。
                    DroppedFrameCount++;
                }
                return;
            }

            case Horizon.Game.Message.Sync.TraversalRequestPacket traversal:
            {
                // 翻越：计划合法性/冷却/起点贴合由 SimWorld.TryBeginTraversal 一处裁决；
                // 结果包由会话直接排进该会话下行事件，客户端据此开始或取消本地预测。
                var sessionId = (long)traversal.CharacterId;
                TraversalRequestsSeen++;
                _world.HandleTraversalRequestFromClient(sessionId, traversal);
                return;
            }

            case Horizon.Game.Message.Sync.CorpseLootTakeRequestPacket lootTake:
            {
                // 尸体拾取：距离在这里量（DS 才知道权威坐标），窗口/幂等归权威侧。
                // 本帧不受战斗动作的节流约束：它是"点一下取一件"，不是可连发的攻击。
                var takeStatus = _world.HandleCorpseLootTakeFromClient(
                    (long)lootTake.CharacterId, lootTake.CorpseEntityId, lootTake.SlotIndex, out var denial);
                CorpseLootRequestsSeen++;
                // 拒绝回执由会话直接排进该会话的下行事件（不必再经上行绕一圈）；
                // 这里只计数——"当场拒了但玩家没看到提示"时先看这两格。
                if (denial is not null || takeStatus != CorpseLootTakeStatus.Taken) CorpseLootRejected++;
                return;
            }

            case Horizon.Game.Message.Sync.CombatActionPacket combat:
            {
                // 战斗上行：在这之前 DS 的数据面**只认移动**（InputPacket/握手/重连恢复），
                // 而客户端的攻击走的是 Orleans 的 CombatGrain——那只认识 CharacterGrain 的玩家。
                // 于是住在 DS 世界里的怪物（有 HP、会索敌、会读条、会掉东西的那一份）
                // 在实机里根本挨不到打：玩家看不到血条掉、怪不会死、掉落与经验全都不发生。
                // 会话 id 用发起者的 characterId（与 InputPacket 同源）；真正的攻方实体由
                // 世界按会话自己取（SubmitCombatAction 里用的是 player.EntityId），不采信包里的 AttackerId。
                var sessionId = (long)combat.AttackerId;

                // 限流对**所有**战斗动作生效（含新加的打断）：按 ActionKind 列白名单的话，
                // 每加一种动作就是一次"不在表里 = 每帧都能发"的漏口——列清单迟早漏改。
                {
                    // "从没见过这个会话"= 不受限（用 TryGetValue 的分支直接放行；
                    // 拿 long.MinValue 当"很久以前"会在 ServerTick - MinValue 上溢出，
                    // 结果第一发动作反而被判成"来得太快"——实测过：三条用例全卡在计数 0）。
                    if (CombatActionLastTick.TryGetValue(sessionId, out var last)
                        && _world.ServerTick - last < MinCombatActionIntervalTicks)
                    {
                        // 限流必须计数：不然"我明明在砍却不动"会被读成服务器的错。
                        CombatActionRateLimitedCount++;
                        return;
                    }
                    CombatActionLastTick[sessionId] = _world.ServerTick;
                }

                // 结果分档必须念出来：只数"收到了多少刀"时，"怪不掉血"会被读成客户端或渲染的问题，
                // 而真相常在这三档里（无效目标 / 够不着 / 命中但零伤害）。
                var combatResolution = _world.SubmitCombatAction(sessionId, combat);
                switch (combatResolution.Outcome)
                {
                    case Horizon.Game.Sim.CombatOutcome.Hit: CombatActionHitCount++; break;
                    case Horizon.Game.Sim.CombatOutcome.OutOfRange: CombatActionOutOfRangeCount++; break;
                    case Horizon.Game.Sim.CombatOutcome.Invalid: CombatActionInvalidCount++; break;
                    case Horizon.Game.Sim.CombatOutcome.Evading: CombatActionEvadingCount++; break;
                    default: CombatActionOtherOutcomeCount++; break;
                }
                CombatActionCount++;
                if (!_world.TryGetPlayer(sessionId, out _))
                {
                    // 没握手的会话（断线重连竞态）：与 InputPacket 同等待遇——不刷错误日志，但计数可见。
                    CombatActionRejectedNoSessionCount++;
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
