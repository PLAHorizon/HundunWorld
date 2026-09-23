using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Services;

/// <summary>
/// Sprint 2 — Gateway→DS 上行 TCP 连接池。按 <c>(Host, Port)</c> 维护可复用长连接，
/// 消除 <see cref="DsSyncDataPlane"/> 旧路径「每个上行包 new TcpClient() → ConnectAsync → WriteAsync → Dispose」
/// 带来的三次握手 + TIME_WAIT 开销（60Hz × N 玩家场景下 DS 路径最大的延迟瓶颈）。
/// <para>
/// DS 侧 <c>DsDataPlaneHost</c> 已支持长连接循环读帧（<c>AcceptTcpClientAsync</c> → <c>ClientLoopAsync</c>
/// → <c>DsFrameReader.TryReadFrame</c>），因此本网关侧连接池**无需 DS 端任何改动**即可直接复用长连接。
/// </para>
/// </summary>
/// <remarks>
/// 设计要点：
/// <list type="bullet">
/// <item><description>线程安全：多路由线程可并发调用 <see cref="RentAsync"/>/<see cref="Return"/>/<see cref="Evict"/>。</description></item>
/// <item><description>每 endpoint 上限 <see cref="MaxPerEndpoint"/> 条连接（默认 4），达到上限时 <see cref="RentAsync"/> 返回 <c>null</c>，
/// 由调用方走一次性直连 fallback（不因池满载而丢包）。</description></item>
/// <item><description>单连接写保护：<see cref="PooledConnection.SendAsync"/> 内部持 <see cref="SemaphoreSlim"/>(1,1) 写锁，
/// 防止多路由线程并发写同一连接导致帧交叉。</description></item>
/// <item><description>健康探测：<see cref="RentAsync"/> 复用空闲连接前用 <c>Socket.Poll(0, SelectRead)</c> 探测对端是否已关闭（FIN/RST），
/// 失效连接直接丢弃并计入 <see cref="ReconnectCount"/>。</description></item>
/// <item><description>空闲回收：每个 endpoint 惰性创建一个后台 <see cref="Timer"/>（周期 30s），关闭超过 <see cref="IdleTimeout"/>
/// （默认 60s）无写操作的空闲连接，避免长期占用 DS 侧连接槽位。</description></item>
/// <item><description>熔断联动：<see cref="DsSyncDataPlane"/> 判定某 DS 连续失败触发熔断时调用 <see cref="Evict"/> 清空该 endpoint
/// 的池连接，冷却期结束恢复探测时重新建连。</description></item>
/// </list>
/// 注册为 DI 单例（<c>services.AddSingleton&lt;DsTcpConnectionPool&gt;()</c>），容器关闭时自动 <see cref="Dispose"/>。
/// </remarks>
public sealed class DsTcpConnectionPool : IDisposable
{
    /// <summary>空闲回收扫描周期（每个 endpoint 独立一个 Timer）。</summary>
    private static readonly TimeSpan IdleSweepInterval = TimeSpan.FromSeconds(30);

    /// <summary>Key = (Host, Port) endpoint；Value = 该 endpoint 的连接池。</summary>
    private readonly ConcurrentDictionary<(string Host, int Port), EndpointPool> _pools = new();

    private volatile bool _disposed;

    /// <summary>每个 endpoint 允许维持的最大连接数（活跃 + 空闲）。默认 4；超过则 <see cref="RentAsync"/> 返回 <c>null</c>。</summary>
    public int MaxPerEndpoint { get; set; } = 4;

    /// <summary>空闲连接最长存活时间：超过该时长无写操作即被后台扫描关闭。默认 60s。</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>新建连接超时（毫秒）。默认 2000ms，与 <see cref="DsSyncDataPlane"/> 旧路径的连接超时一致。</summary>
    public int ConnectTimeoutMs { get; set; } = 2000;

    /// <summary>当前已借出（活跃使用中）的连接总数，跨所有 endpoint。仅应通过 <see cref="Interlocked"/> 读写。</summary>
    public long ActiveCount;

    /// <summary>当前空闲待命的连接总数，跨所有 endpoint。仅应通过 <see cref="Interlocked"/> 读写。</summary>
    public long IdleCount;

    /// <summary><see cref="RentAsync"/> 因达到 <see cref="MaxPerEndpoint"/> 上限或新建连接失败而返回 <c>null</c> 的累计次数。</summary>
    public long RentFailureCount;

    /// <summary>复用空闲连接时经健康探测发现对端已关闭（需丢弃重建）的累计次数。</summary>
    public long ReconnectCount;

    /// <summary><see cref="PooledConnection.SendAsync"/> 写入失败（抛异常）的累计次数。</summary>
    public long WriteFailureCount;

    /// <summary>
    /// 租借一条连接。返回的 <see cref="PooledConnection"/> 用完必须调用 <see cref="Return"/> 归还（以便复用）。
    /// 池中无可用空闲连接时新建；达到 <see cref="MaxPerEndpoint"/> 上限或新建失败时返回 <c>null</c>，
    /// 调用方（<see cref="DsSyncDataPlane"/>）应据此走一次性直连 fallback，不因池满载/故障而丢包。
    /// </summary>
    /// <param name="host">DS 主机。</param>
    /// <param name="port">DS 端口。</param>
    /// <param name="ct">取消令牌（通常来自 <see cref="DsSyncDataPlane"/> 的连接超时 CTS，与内部 <see cref="ConnectTimeoutMs"/> 取先到者）。</param>
    public async Task<PooledConnection?> RentAsync(string host, int port, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Host 不能为空。", nameof(host));
        if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port), "端口必须在 1–65535。");

        var maxPerEndpoint = MaxPerEndpoint;
        var connectTimeoutMs = ConnectTimeoutMs;

        var endpointPool = _pools.GetOrAdd((host, port), static _ => new EndpointPool());
        EnsureIdleSweeper(endpointPool);

        // ① 优先复用空闲连接：逐条取出做健康探测，失效的直接丢弃（计入重连次数），健康的直接借出。
        while (endpointPool.Idle.TryTake(out var idle))
        {
            Interlocked.Decrement(ref IdleCount);

            if (IsHealthy(idle))
            {
                idle.Touch();
                Interlocked.Increment(ref ActiveCount);
                return idle;
            }

            idle.Dispose();
            Interlocked.Decrement(ref endpointPool.TotalCount);
            Interlocked.Increment(ref ReconnectCount);
        }

        // ② 无可用空闲连接 → 尝试新建（原子 CAS 递增 TotalCount，防止并发 RentAsync 突破 MaxPerEndpoint）。
        while (true)
        {
            var currentTotal = Volatile.Read(ref endpointPool.TotalCount);
            if (currentTotal >= maxPerEndpoint)
            {
                Interlocked.Increment(ref RentFailureCount);
                return null;
            }
            if (Interlocked.CompareExchange(ref endpointPool.TotalCount, currentTotal + 1, currentTotal) == currentTotal)
                break;
        }

        TcpClient? client = null;
        try
        {
            client = new TcpClient();
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(connectTimeoutMs);
            await client.ConnectAsync(host, port, connectCts.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 新建失败（含连接超时/拒绝/外部取消）：回滚 TotalCount 预占，计入租借失败，返回 null 让调用方走 fallback。
            client?.Dispose();
            Interlocked.Decrement(ref endpointPool.TotalCount);
            Interlocked.Increment(ref RentFailureCount);
            return null;
        }

        var conn = new PooledConnection(client, onWriteFailure: () => Interlocked.Increment(ref WriteFailureCount));
        Interlocked.Increment(ref ActiveCount);
        return conn;
    }

    /// <summary>
    /// 归还连接到池中。<see cref="PooledConnection.IsBroken"/> 为 true（写入失败，或调用方主动标记）时直接 Dispose 不再复用；
    /// 否则放回该 endpoint 的空闲队列等待下次租借。
    /// </summary>
    public void Return(string host, int port, PooledConnection conn)
    {
        if (conn is null) return;

        if (_disposed)
        {
            conn.Dispose();
            Interlocked.Decrement(ref ActiveCount);
            return;
        }

        if (conn.IsBroken)
        {
            // 连接已损坏：直接释放，不再入池。若该 endpoint 仍在池中（未被 Evict/Dispose 移除），
            // 同步扣减其 TotalCount，保证限流计数与实际存活连接数一致。
            if (_pools.TryGetValue((host, port), out var brokenEndpointPool))
            {
                Interlocked.Decrement(ref brokenEndpointPool.TotalCount);
            }
            conn.Dispose();
            Interlocked.Decrement(ref ActiveCount);
            return;
        }

        if (!_pools.TryGetValue((host, port), out var endpointPool))
        {
            // 该 endpoint 已被 Evict/Dispose 移除（对应的 EndpointPool 实例已丢弃，TotalCount 无需再维护）：
            // 直接释放连接，不再入池。
            conn.Dispose();
            Interlocked.Decrement(ref ActiveCount);
            return;
        }

        conn.Touch();
        endpointPool.Idle.Add(conn);
        Interlocked.Decrement(ref ActiveCount);
        Interlocked.Increment(ref IdleCount);
    }

    /// <summary>
    /// 熔断联动：清空指定 endpoint 的所有**空闲**池连接（供 <see cref="DsSyncDataPlane"/> 在熔断触发瞬间调用）。
    /// <para>
    /// 已借出（活跃）的连接不会被强制中断（调用方可能正在写入），但由于池条目已被移除，
    /// 这些连接归还时（<see cref="Return"/>）会因找不到对应 endpoint 而被直接 Dispose，不会重新入池。
    /// </para>
    /// </summary>
    public void Evict(string host, int port)
    {
        if (_pools.TryRemove((host, port), out var endpointPool))
        {
            endpointPool.DisposeSweeper();
            while (endpointPool.Idle.TryTake(out var conn))
            {
                conn.Dispose();
                Interlocked.Decrement(ref IdleCount);
            }
        }
    }

    /// <summary>释放所有 endpoint 的空闲连接与后台扫描 Timer；已借出的活跃连接不受影响，归还时会被 Dispose（见 <see cref="Return"/>）。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var kv in _pools)
        {
            kv.Value.DisposeSweeper();
            while (kv.Value.Idle.TryTake(out var conn))
            {
                conn.Dispose();
                Interlocked.Decrement(ref IdleCount);
            }
        }
        _pools.Clear();
    }

    /// <summary>惰性创建该 endpoint 的空闲回收 Timer（双重检查加锁，避免并发 RentAsync 重复创建）。</summary>
    private void EnsureIdleSweeper(EndpointPool endpointPool)
    {
        if (endpointPool.IdleSweeper is null)
        {
            lock (endpointPool)
            {
                endpointPool.IdleSweeper ??= new Timer(_ => SweepIdle(endpointPool), null, IdleSweepInterval, IdleSweepInterval);
            }
        }
    }

    /// <summary>空闲回收扫描：排空 <see cref="EndpointPool.Idle"/>，关闭超过 <see cref="IdleTimeout"/> 未使用的连接，其余重新入队。</summary>
    private void SweepIdle(EndpointPool endpointPool)
    {
        if (_disposed) return;
        // 防重入：Timer 回调理论上可能与上一次尚未结束的回调重叠（周期远大于扫描耗时，几乎不会发生，仍加防护）。
        if (Interlocked.CompareExchange(ref endpointPool.SweepInProgress, 1, 0) != 0) return;
        try
        {
            var idleTimeout = IdleTimeout;
            var now = DateTime.UtcNow;
            var keep = new List<PooledConnection>();

            while (endpointPool.Idle.TryTake(out var conn))
            {
                if (now - conn.LastUsedUtc > idleTimeout)
                {
                    conn.Dispose();
                    Interlocked.Decrement(ref IdleCount);
                    Interlocked.Decrement(ref endpointPool.TotalCount);
                }
                else
                {
                    keep.Add(conn);
                }
            }

            foreach (var conn in keep)
            {
                endpointPool.Idle.Add(conn);
            }
        }
        finally
        {
            Interlocked.Exchange(ref endpointPool.SweepInProgress, 0);
        }
    }

    /// <summary>
    /// 健康探测：<c>Socket.Poll(0, SelectMode.SelectRead)</c> 返回 true 且 <c>Available == 0</c>，
    /// 表示对端已发送 FIN/RST 且无待读数据 → 连接已被对端关闭，不可复用。
    /// </summary>
    private static bool IsHealthy(PooledConnection conn)
    {
        try
        {
            var socket = conn.Socket;
            if (socket is null || !socket.Connected) return false;
            if (socket.Poll(0, SelectMode.SelectRead))
            {
                // 有可读事件：Available == 0 → 对端已关闭；否则是罕见的残留可读数据（本连接为纯写入路径），仍视为健康。
                return socket.Available != 0;
            }
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>单个 endpoint（Host, Port）的连接池内部状态。</summary>
    private sealed class EndpointPool
    {
        /// <summary>空闲待命连接队列（并发安全，TryTake 保证同一连接不会被两个线程同时取出）。</summary>
        public ConcurrentBag<PooledConnection> Idle { get; } = new();

        /// <summary>该 endpoint 当前存活连接总数（活跃 + 空闲），仅通过 <see cref="Interlocked"/> 读写，用于 <see cref="DsTcpConnectionPool.MaxPerEndpoint"/> 限流。</summary>
        public int TotalCount;

        /// <summary>空闲回收 Timer（惰性创建，见 <see cref="DsTcpConnectionPool.EnsureIdleSweeper"/>）。</summary>
        public Timer? IdleSweeper;

        /// <summary>空闲回收扫描重入防护标志（0 = 空闲，1 = 扫描中）。</summary>
        public int SweepInProgress;

        /// <summary>停止并释放空闲回收 Timer。</summary>
        public void DisposeSweeper()
        {
            IdleSweeper?.Dispose();
            IdleSweeper = null;
        }
    }
}

/// <summary>
/// 池化连接包装：封装单条复用的 <see cref="TcpClient"/> 长连接，提供带写锁的 <see cref="SendAsync"/>。
/// </summary>
/// <remarks>
/// 由 <see cref="DsTcpConnectionPool.RentAsync"/> 创建并借出，使用完毕后调用方（<see cref="DsSyncDataPlane"/>）
/// 必须调用 <see cref="DsTcpConnectionPool.Return"/> 归还以便复用；不应由调用方直接 <see cref="Dispose"/>，
/// 归还逻辑会根据 <see cref="IsBroken"/> 决定复用还是释放。
/// </remarks>
public sealed class PooledConnection : IDisposable, IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Action? _onWriteFailure;
    private bool _disposed;

    /// <summary>
    /// 连接是否已损坏（<see cref="SendAsync"/> 写入抛异常，或调用方在归还前主动标记）。
    /// 损坏的连接经 <see cref="DsTcpConnectionPool.Return"/> 归还时不会被复用，而是直接释放。
    /// </summary>
    public bool IsBroken { get; internal set; }

    /// <summary>最近一次成功写入（或被租借/归还刷新）的 UTC 时间，供空闲回收扫描判定是否超时。</summary>
    public DateTime LastUsedUtc { get; private set; }

    /// <summary>底层 Socket，仅供 <see cref="DsTcpConnectionPool"/> 内部做健康探测（<c>Poll</c>）使用。</summary>
    internal Socket? Socket => _client.Client;

    internal PooledConnection(TcpClient client, Action? onWriteFailure)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _stream = client.GetStream();
        _onWriteFailure = onWriteFailure;
        LastUsedUtc = DateTime.UtcNow;
    }

    /// <summary>刷新最近使用时间（租借/归还时调用，避免刚被使用的连接被空闲扫描误判超时回收）。</summary>
    internal void Touch() => LastUsedUtc = DateTime.UtcNow;

    /// <summary>
    /// 带写锁发送完整帧（<see cref="SemaphoreSlim"/>(1,1) 保证同一连接同一时刻只有一个写入者，防止多路由线程并发写导致帧交叉）。
    /// 写入失败时标记 <see cref="IsBroken"/> 并上报写入失败指标，异常继续向上抛出由调用方处理（通常触发 fallback + 熔断计数）。
    /// </summary>
    public async Task SendAsync(byte[] payload, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (payload is null) throw new ArgumentNullException(nameof(payload));

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(payload, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
            LastUsedUtc = DateTime.UtcNow;
        }
        catch
        {
            IsBroken = true;
            _onWriteFailure?.Invoke();
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _writeLock.Dispose();
        _stream.Dispose();
        _client.Dispose();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
