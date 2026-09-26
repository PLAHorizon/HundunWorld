using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Horizon.Game.Core.Sim.Server;

namespace Horizon.DsHost;

/// <summary>无头 DS 宿主配置。</summary>
public sealed class DsHostOptions
{
    /// <summary>本 DS 承载的 shard 集合。</summary>
    public long[] ShardIds { get; set; } = new long[] { 0 };

    /// <summary>本 DS 唯一标识。</summary>
    public string DsId { get; set; } = "ds-local-0";

    /// <summary>模拟频率（Hz）。</summary>
    public int TicksPerSecond { get; set; } = 60;

    /// <summary>数据面监听端口（承接网关转发的客户端上行帧）。</summary>
    public int ListenPort { get; set; } = 19870;

    /// <summary>数据面监听地址。</summary>
    public string ListenAddress { get; set; } = "0.0.0.0";

    /// <summary>对网关可达的主机名（写入注册表）。</summary>
    public string AdvertiseHost { get; set; } = "127.0.0.1";

    /// <summary>区域标识。</summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>刷怪点配置（空 = 世界内无怪，仅同步玩家）。</summary>
    public System.Collections.Generic.List<Horizon.Game.Sim.SpawnPointConfig> SpawnPoints { get; set; } = new();

    /// <summary>单 tick 上行处理上限（防止积压洪峰超帧预算）。</summary>
    public int MaxInboundPerTick { get; set; } = 512;

    /// <summary>
    /// 掉落提供者（MMORPG 开发计划 M4.3 收尾）。
    /// <para>
    /// null = 使用 <c>DeterministicLootProvider</c>（内置极简掉落表，无需 Orleans）；
    /// 生产注入 <c>OrleansLootProvider</c>（配置来自 <c>LootTableGrain</c>，本地确定性掷骰）。
    /// </para>
    /// </summary>
    public IDsLootProvider? LootProvider { get; set; }

    /// <summary>
    /// 是否启用 AOI 分桶快照裁剪（Sprint 3）。默认 false = 走原有全量快照路径（向后兼容）。
    /// <para>true 时按玩家可见 chunk 分组裁剪快照，把下行实体量从 O(N²) 降到 O(N×K)。</para>
    /// </summary>
    public bool EnableAoiFilter { get; set; } = false;

    /// <summary>AOI chunk 边长（米）。默认 16，与 Orleans <c>ZoneShardGrain</c> 一致。</summary>
    public int AoiChunkSize { get; set; } = 16;

    /// <summary>AOI 视野半径（chunk 数）。默认 28（≈448m），与 Orleans 握手路径一致。</summary>
    public int AoiRadiusChunks { get; set; } = 28;

    /// <summary>
    /// 单 DS 软容量上限（会话数）。超过时 <see cref="DsWorldSession.AddPlayer"/> 拒绝新玩家，
    /// 返回 reason="shard_full"，客户端/网关可据此重路由到其他 DS。默认 5000。
    /// </summary>
    public int MaxSessionsPerDs { get; set; } = 5000;
}

/// <summary>
/// 无头 DS 宿主编排（MMORPG 开发计划 P3）—— 把"世界模拟 + 数据面 + 控制面"串成一个 tick 循环。
/// <para>
/// 与 <c>Program.cs</c> 分离，使整条 tick 链路（收包 → 模拟 → 编码 → 下行）可以在单元测试里
/// 用假 sender 端到端驱动，无需真实 socket 与真实网关。
/// </para>
/// <para>
/// <b>时钟</b>：使用"固定步长 + 追赶"的调度——每 tick 目标间隔 <c>1/TicksPerSecond</c>，
/// 落后则立即补 tick（但单轮补 tick 数设上限，避免长时间停顿后"死亡螺旋"）。
/// 权威模拟必须由本进程独占驱动，不依赖 UE 渲染帧（既有文档列为风险 N3：双时钟漂移）。
/// </para>
/// </summary>
public sealed class DsHostRunner : IAsyncDisposable
{
    /// <summary>单轮最多补的 tick 数（防止停顿后死亡螺旋）。</summary>
    private const int MaxCatchUpTicksPerLoop = 8;

    private readonly DsHostOptions _options;
    private readonly IDsDownlinkSender? _sender;
    private readonly IDsGameplayEventSender? _gameplaySender;
    private readonly Action<string>? _log;

    private readonly DsWorldSession _world;
    private readonly DsDataPlaneHost _dataPlane;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    // ── Sprint 4：Tick 编码/发送流水线化 ──
    // Stage 1-3（drain + simulate + encode）仍在 tick 线程同步执行（保持确定性），
    // Stage 4（send）异步化：编码后的批次写入有界 Channel，后台发送泵消费。
    // 这样 tick 线程不会被网络 IO 阻塞，快照节拍更稳定。
    private readonly Channel<DsSnapshotBatch> _sendChannel = Channel.CreateBounded<DsSnapshotBatch>(
        new BoundedChannelOptions(3) { FullMode = BoundedChannelFullMode.DropOldest });
    private Task? _sendPumpTask;

    /// <summary>
    /// 发送泵是否正在运行（即批次是否真的会被异步消费）。
    /// <para>
    /// <b>为什么需要这个标志</b>：<see cref="RunSingleTickAsync"/> 有两条调用形态——
    /// 自驱宿主的 tick 循环（泵在跑，投递即可返回）与"未启动即单步"（测试、外部宿主启动前的预热）
    /// ——后者如果照旧投递进 Channel，**没有任何消费者**，批次会静默堆积到 Channel 满后被
    /// <see cref="BoundedChannelFullMode.DropOldest"/> 丢弃，表现为"模拟在跑但快照永不发出"。
    /// </para>
    /// <para>
    /// 因此只有当泵确实在运行时才走异步投递；否则必须**同步 await 发送**，
    /// 让 <see cref="RunSingleTickAsync"/> 返回时"批次已发出"这一语义成立。
    /// </para>
    /// </summary>
    private volatile bool _sendPumpRunning;

    /// <summary>创建宿主。</summary>
    /// <param name="options">配置。</param>
    /// <param name="sender">下行发送端（生产为 <see cref="DsUplinkClient"/>；测试可传假实现或 null）。</param>
    /// <param name="log">日志回调。</param>
    /// <param name="gameplaySender">跨层玩法事件发送端；null 时尝试把 <paramref name="sender"/> 当作它使用（<c>DsUplinkClient</c> 两者都实现）。</param>
    public DsHostRunner(
        DsHostOptions options,
        IDsDownlinkSender? sender,
        Action<string>? log = null,
        IDsGameplayEventSender? gameplaySender = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _sender = sender;
        _gameplaySender = gameplaySender;
        _log = log;

        _world = new DsWorldSession(new DsWorldSessionOptions
        {
            ShardId = _options.ShardIds.Length > 0 ? _options.ShardIds[0] : 0,
            TicksPerSecond = _options.TicksPerSecond,
            EnableAoiFilter = _options.EnableAoiFilter,
            AoiChunkSize = _options.AoiChunkSize,
            AoiRadiusChunks = _options.AoiRadiusChunks,
            MaxSessions = _options.MaxSessionsPerDs,
        }, _options.LootProvider);

        if (_options.SpawnPoints.Count > 0)
        {
            _world.ConfigureSpawnPoints(_options.SpawnPoints);
        }

        _dataPlane = new DsDataPlaneHost(_world, _options.ListenPort, _options.ListenAddress, log);
    }

    /// <summary>权威世界（测试/诊断可见）。</summary>
    public DsWorldSession World => _world;

    /// <summary>数据面（测试/诊断可见）。</summary>
    public DsDataPlaneHost DataPlane => _dataPlane;

    /// <summary>累计执行的 tick 数。</summary>
    public long TicksExecuted { get; private set; }

    /// <summary>累计发送的下行批次数。</summary>
    public long BatchesSent { get; private set; }

    /// <summary>累计失败的下行批次数。</summary>
    public long BatchesFailed { get; private set; }

    /// <summary>累计处理的上行帧数。</summary>
    public long InboundProcessed { get; private set; }

    /// <summary>累计产生的跨层玩法事件数（击杀/掉落/玩家死亡）。</summary>
    public long GameplayEventsProduced { get; private set; }

    /// <summary>累计成功上送业务层的玩法事件数。</summary>
    public long GameplayEventsSent { get; private set; }

    /// <summary>累计上送失败（或无发送端）的玩法事件数——业务层对账的告警源。</summary>
    public long GameplayEventsFailed { get; private set; }

    /// <summary>上一个快照 tick 的 AOI 分组数（诊断；AOI 关闭或无快照时为 0）。</summary>
    public int LastAoiGroupCount { get; private set; }

    /// <summary>上一个快照 tick 的平均每组实体数（诊断）。</summary>
    public double LastAvgEntitiesPerGroup { get; private set; }

    /// <summary>数据面是否已启动（<see cref="StartAsync"/> 或 <see cref="StartDataPlaneAsync"/> 后为 true）。</summary>
    public bool DataPlaneStarted { get; private set; }

    /// <summary>是否由本类自驱 tick 循环（<see cref="StartAsync"/> 后为 true；外部驱动模式下为 false）。</summary>
    public bool IsSelfDriven { get; private set; }

    /// <summary>
    /// 启动数据面监听与**自驱 tick 循环**（console 宿主 / 集成测试用）。
    /// <para>
    /// ⚠️ <b>外部宿主（UE / Flax / Unity）不要调本方法。</b> 它会起一条内部
    /// <see cref="TickLoopAsync"/>，与外部宿主的循环同时推进同一个非线程安全的
    /// <see cref="DsWorldSession"/>，表现为随机的模拟跳帧与状态损坏。
    /// 外部宿主应调 <see cref="StartDataPlaneAsync"/> 然后再自己按节奏调
    /// <see cref="RunSingleTickAsync"/>（见 <c>HundunWorld/Source/ServerGame/DedicatedServerHost.cs</c>）。
    /// </para>
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await StartDataPlaneAsync(cancellationToken).ConfigureAwait(false);

        IsSelfDriven = true;
        _loop = Task.Run(() => TickLoopAsync(_cts!.Token), CancellationToken.None);

        _log?.Invoke($"DsHostRunner：已启动（自驱）。ShardIds={string.Join(',', _options.ShardIds)} TicksPerSecond={_options.TicksPerSecond} SpawnPoints={_options.SpawnPoints.Count} MaxSessions={_options.MaxSessionsPerDs}");
    }

    /// <summary>
    /// 只启动数据面监听与后台发送泵，**不自驱 tick 循环**——供外部宿主接管节拍。
    /// <para>
    /// 拆出本方法前，外部宿主只能手动摸 <c>DataPlane.StartAsync</c>，既绕过了
    /// <see cref="_cts"/> 与发送泵的初始化，也让"数据面已启动"这件事有两条不一致的路径。
    /// </para>
    /// <para>
    /// 调用后由宿主反复调 <see cref="RunSingleTickAsync"/> 推进世界，
    /// 停止时调 <see cref="DisposeAsync"/> 释放数据面与发送泵。
    /// </para>
    /// </summary>
    public async Task StartDataPlaneAsync(CancellationToken cancellationToken = default)
    {
        if (DataPlaneStarted) return;

        await _dataPlane.StartAsync(cancellationToken).ConfigureAwait(false);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Sprint 4：后台发送泵（与 tick 解耦，网络 IO 不阻塞模拟节拍）。
        // 外部驱动模式下没有内部 tick 循环，但发送泵仍必须起——
        // 否则 RunSingleTickAsync 写入的批次会静默堆在 Channel 里永不发出。
        _sendPumpTask = Task.Run(() => SendPumpAsync(_cts.Token), CancellationToken.None);
        // 置位必须在起泵之后、tick 开始之前：RunSingleTickAsync 据此选择
        // "异步投递 Channel" 还是 "同步 await 发送"——只有泵真的在跑才走异步。
        _sendPumpRunning = true;

        DataPlaneStarted = true;
        _log?.Invoke($"DsHostRunner：数据面与发送泵已启动（外部驱动，待宿主调 RunSingleTickAsync）。ShardIds={string.Join(',', _options.ShardIds)} TicksPerSecond={_options.TicksPerSecond} SpawnPoints={_options.SpawnPoints.Count} MaxSessions={_options.MaxSessionsPerDs}");
    }

    /// <summary>
    /// 网关通知的「已结束会话」（客户端断线）。**可能在读循环线程上被调用**，
    /// 因此只入队，由 tick 线程消费——世界是非线程安全的。
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentQueue<long> _pendingSessionEnds = new();

    /// <summary>会话输入租约：超过该时长未收到任何输入的会话被兜底回收（见 RunSingleTickAsync 扫描）。</summary>
    private static readonly TimeSpan SessionInputLease = TimeSpan.FromSeconds(120);

    /// <summary>累计因会话来结束而移除的玩家数。</summary>
    public long SessionsRemoved { get; private set; }

    /// <summary>
    /// 会话结束通知入口（由 <c>DsUplinkClient.SessionEndReceived</c> 订阅）。
    /// <para>
    /// 在此之前 DS **永远不知道客户端已断开**：网关清理了自己的连接与映射，
    /// DS 却继续保留该玩家并 20Hz 广播给死会话（实测表现为网关日志持续
    /// <c>Packet dropped: session offline</c>）。本方法让 DS 能真正回收会话。
    /// </para>
    /// </summary>
    public void NotifySessionEnded(long[] sessionIds)
    {
        if (sessionIds is null) return;
        for (var i = 0; i < sessionIds.Length; i++)
        {
            if (sessionIds[i] != 0) _pendingSessionEnds.Enqueue(sessionIds[i]);
        }
    }

    /// <summary>
    /// 与网关的上行链路断开（由 <c>DsUplinkClient.LinkLost</c> 订阅，可能在读循环线程上调用）。
    /// <para>
    /// 网关被强杀/崩溃时不可能再发来任何"会话结束"通知，DS 若继续保留玩家，
    /// 就会对早已消失的会话持续 20Hz 广播，并被后续进入世界的其他客户端
    /// 收到 Spawn 而表现为"服务端残留的离线幽灵角色"。
    /// 链路断开时所有客户端连接必然也已随网关失效（重连后走重新握手），
    /// 因此全量回收是安全且必要的。只入队，由 tick 线程消费（世界非线程安全）。
    /// </para>
    /// </summary>
    public void NotifyLinkLost()
    {
        var ids = _world.GetSessionIds();
        if (ids.Count == 0) return;
        for (var i = 0; i < ids.Count; i++) _pendingSessionEnds.Enqueue(ids[i]);
        _log?.Invoke($"DsHostRunner：与网关链路断开，回收全部 {ids.Count} 个玩家会话（等待 tick 线程移除）。");
    }

    /// <summary>执行恰好一个 tick（测试与"单步驱动"入口）。</summary>
    public async Task<DsTickResult> RunSingleTickAsync()
    {
        // 先处理会话结束（在模拟之前），避免为已断线玩家多跑一帧
        while (_pendingSessionEnds.TryDequeue(out var endedSessionId))
        {
            if (_world.RemovePlayer(endedSessionId))
            {
                SessionsRemoved++;
                _log?.Invoke($"会话结束：已从世界移除玩家 sessionId={endedSessionId}（剩余 {_world.PlayerCount} 人）。");
            }
        }

        // 输入租约兜底扫描（每秒一次）：网关"会话结束"通知丢失（崩溃窗口之外的罕见路径，
        // 如通知帧本身丢失）时，长时间收不到输入的会话也会被回收，杜绝永久幽灵。
        // 阈值 120s 远大于 presence TTL(90s) 与网关 15s 重连宽限期，不会误伤挂机玩家
        //（客户端 InputPacket 为定频上行，在线即有输入）。
        if ((TicksExecuted + 1) % Math.Max(1, _options.TicksPerSecond) == 0)
        {
            var starved = _world.CollectInputStarvedSessions(DateTime.UtcNow, SessionInputLease);
            for (var i = 0; i < starved.Count; i++)
            {
                _pendingSessionEnds.Enqueue(starved[i]);
                _log?.Invoke($"DsHostRunner：会话 {starved[i]} 超过 {SessionInputLease.TotalSeconds:F0}s 无任何输入，投递回收。");
            }
        }

        InboundProcessed += _dataPlane.DrainIntoWorld(_options.MaxInboundPerTick);

        var result = _world.Tick();
        TicksExecuted++;

        // AOI 分桶指标（仅快照 tick 且 AOI 开启时更新；供周期性状态摘要观测裁剪效果）。
        if (result.SnapshotGroups is { Count: > 0 })
        {
            LastAoiGroupCount = result.SnapshotGroups.Count;
            long totalEntities = 0;
            foreach (var g in result.SnapshotGroups) totalEntities += g.Snapshot.Deltas?.Length ?? 0;
            LastAvgEntitiesPerGroup = (double)totalEntities / result.SnapshotGroups.Count;
        }
        else if (result.SnapshotDue)
        {
            LastAoiGroupCount = 0;
            LastAvgEntitiesPerGroup = 0;
        }

        var batch = DsDownlinkEncoder.Encode(result);
        if (batch.Items.Length > 0 && _sender is not null)
        {
            if (_sendPumpRunning)
            {
                // 自驱模式（StartAsync）：非阻塞写入 Channel，后台发送泵异步消费。
                // DropOldest 模式下 TryWrite 恒返回 true（旧批次被挤掉），此处仅做防御性检查。
                if (!_sendChannel.Writer.TryWrite(batch))
                {
                    BatchesFailed++;
                }
            }
            else
            {
                // 单步模式（测试 / 外部宿主启动前）：**同步发送**。
                // 必须同步的原因见 _sendPumpRunning 的注释——没有消费者时投进 Channel
                // 等于把批次丢进黑洞。同步发送让"RunSingleTickAsync 返回 ⇒ 批次已发出"
                // 成为可依赖的契约，这正是 DsHostRunnerTests 的断言前提。
                // 异常吞掉并计数：单批发送失败不该让整条 tick 链路崩掉（与发送泵策略一致）。
                try
                {
                    var ok = await _sender.SendSnapshotBatchAsync(batch, CancellationToken.None).ConfigureAwait(false);
                    if (ok) BatchesSent++;
                    else BatchesFailed++;
                }
                catch (Exception ex)
                {
                    BatchesFailed++;
                    _log?.Invoke($"[DsHost] 同步发送失败: {ex.Message}");
                }
            }
        }

        // 跨层玩法事件（击杀/掉落/玩家死亡）→ 网关 → Orleans 业务层。
        // 与快照分开统计：业务事件丢失与"客户端没看到伤害数字"是两类完全不同的故障，
        // 混在一个计数器里会让排障时无法区分。
        if (result.GameplayEvents.Count > 0)
        {
            var dtoBatch = new DsGameplayEventBatch
            {
                Events = new DsGameplayEventDto[result.GameplayEvents.Count],
            };
            for (var i = 0; i < result.GameplayEvents.Count; i++)
            {
                // 必须先取到局部变量：索引器返回值不是变量，无法以 in 方式按引用传递。
                var evt = result.GameplayEvents[i];
                dtoBatch.Events[i] = DsGameplayEventDto.FromCoreEvent(in evt);
            }

            GameplayEventsProduced += result.GameplayEvents.Count;

            var sender = _gameplaySender ?? (_sender as IDsGameplayEventSender);
            if (sender is not null)
            {
                var ok = await sender.SendGameplayEventsAsync(dtoBatch, CancellationToken.None).ConfigureAwait(false);
                if (ok) GameplayEventsSent += dtoBatch.Events.Length;
                else GameplayEventsFailed += dtoBatch.Events.Length;
            }
            else
            {
                GameplayEventsFailed += dtoBatch.Events.Length;
            }
        }

        return result;
    }

    private async Task TickLoopAsync(CancellationToken ct)
    {
        var ticksPerSecond = _options.TicksPerSecond > 0 ? _options.TicksPerSecond : 60;
        var targetIntervalTicks = Stopwatch.Frequency / ticksPerSecond;
        var nextDeadline = Stopwatch.GetTimestamp();
        var sw = new Stopwatch();

        while (!ct.IsCancellationRequested)
        {
            nextDeadline += targetIntervalTicks;
            var now = Stopwatch.GetTimestamp();
            var waitTicks = nextDeadline - now;

            if (waitTicks > 0)
            {
                var waitMs = (int)Math.Max(1, waitTicks * 1000 / Stopwatch.Frequency);
                try
                {
                    await Task.Delay(waitMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            else
            {
                // 落后了：补 tick，但有上限——否则一次长停顿会让补 tick 越补越落后。
                var behind = -waitTicks / targetIntervalTicks;
                if (behind > MaxCatchUpTicksPerLoop)
                {
                    _log?.Invoke($"DsHostRunner：tick 落后 {behind} 帧，丢弃超出上限的积压（防止死亡螺旋）。");
                    nextDeadline = Stopwatch.GetTimestamp();
                }
            }

            sw.Restart();
            try
            {
                await RunSingleTickAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 单 tick 异常不能杀死整个 DS：记录后继续（否则一个坏输入就能下线整张地图）。
                _log?.Invoke($"DsHostRunner：tick 异常（已跳过）：{ex.Message}");
            }
            sw.Stop();

            var budgetMs = 1000.0 / ticksPerSecond;
            if (sw.Elapsed.TotalMilliseconds > budgetMs * 2 && TicksExecuted % 60 == 0)
            {
                _log?.Invoke($"[perf] tick 耗时 {sw.Elapsed.TotalMilliseconds:F2}ms 超过预算 {budgetMs:F2}ms（ServerTick={_world.ServerTick} 玩家={_world.PlayerCount} 怪={_world.MonsterCount}）");
            }

            // 周期性状态摘要（每 600 tick ≈ 10s@60Hz）
            if (TicksExecuted > 0 && TicksExecuted % 600 == 0)
            {
                _log?.Invoke($"[DsHost] 状态摘要 Ticks={TicksExecuted} 玩家={_world.PlayerCount} 收帧={_dataPlane.ReceivedFrameCount} 丢帧={_dataPlane.DroppedFrameCount} 解码失败={_dataPlane.DecodeFailureCount} 重复输入={_world.DuplicateInputDroppedCount} 批次发送={BatchesSent} 批次失败={BatchesFailed} 事件产出={GameplayEventsProduced} 事件发送={GameplayEventsSent} 会话移除={SessionsRemoved} AoiGroups={LastAoiGroupCount} AvgEntitiesPerGroup={LastAvgEntitiesPerGroup:F1}");
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try { _cts?.Cancel(); } catch { /* 忽略 */ }

        // 先摘标志再排干 Channel：之后任何 RunSingleTickAsync 都会走同步发送路径，
        // 而不是把批次投进一个即将被关闭的 Channel（那会变成静默丢弃）。
        _sendPumpRunning = false;

        // Sprint 4：先标记 Channel 完成，等待发送泵排干剩余批次。
        _sendChannel.Writer.TryComplete();
        if (_sendPumpTask is not null)
        {
            try { await _sendPumpTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { /* 忽略 */ }
        }

        if (_loop is not null)
        {
            try { await _loop.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); } catch { /* 忽略 */ }
        }

        await _dataPlane.DisposeAsync().ConfigureAwait(false);

        _cts?.Dispose();
        _cts = null;
        _loop = null;
        _sendPumpTask = null;
        DataPlaneStarted = false;
        IsSelfDriven = false;
        _sendPumpRunning = false;
    }

    /// <summary>
    /// Sprint 4：后台发送泵——从 Channel 消费已编码的快照批次并异步发送。
    /// <para>
    /// 与 tick 线程解耦：模拟节拍不受网络 IO 延迟影响，快照产出更稳定。
    /// 异常不终止泵（单批失败只计数，下批继续）；取消时自然退出。
    /// </para>
    /// </summary>
    private async Task SendPumpAsync(CancellationToken ct)
    {
        await foreach (var batch in _sendChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            try
            {
                var ok = await _sender!.SendSnapshotBatchAsync(batch, ct).ConfigureAwait(false);
                if (ok) BatchesSent++;
                else BatchesFailed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                BatchesFailed++;
                _log?.Invoke($"[DsHost] 发送泵异常: {ex.Message}");
            }
        }
    }
}
