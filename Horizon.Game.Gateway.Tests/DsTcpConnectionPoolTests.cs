using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Gateway.Services;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// Sprint 2 — DsTcpConnectionPool 单元测试。
/// <para>
/// 覆盖：连接复用、写失败标记、并发写不乱帧、Evict 清池、池上限、健康探测。
/// 使用 FakeDsPersistent（循环读帧的环回 TCP 监听器）模拟 DS 长连接接收端。
/// </para>
/// </summary>
public class DsTcpConnectionPoolTests
{
    /// <summary>
    /// 持久化 FakeDs：循环接受多条连接，每条连接循环读帧（[4B BE 长度][负载]）。
    /// 与 DsRoutingTests.FakeDs（一次性监听）不同，本助手兼容连接池的长连接复用语义。
    /// </summary>
    private sealed class FakeDsPersistent : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentQueue<byte[]> _frames = new();
        private readonly ConcurrentBag<TcpClient> _clients = new();
        private readonly Task _acceptTask;

        public int Port { get; }
        public int FrameCount => _frames.Count;

        public FakeDsPersistent()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptTask = AcceptLoopAsync();
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    _clients.Add(client);
                    _ = ClientLoopAsync(client);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { break; }
            }
        }

        private async Task ClientLoopAsync(TcpClient client)
        {
            try
            {
                var stream = client.GetStream();
                var lenBuf = new byte[4];
                while (!_cts.IsCancellationRequested)
                {
                    await stream.ReadExactlyAsync(lenBuf, _cts.Token);
                    var len = BinaryPrimitives.ReadInt32BigEndian(lenBuf);
                    if (len <= 0 || len > 16 * 1024 * 1024) break;
                    var body = new byte[len];
                    await stream.ReadExactlyAsync(body, _cts.Token);
                    _frames.Enqueue(body);
                }
            }
            catch { /* 连接关闭/取消 — 正常退出 */ }
        }

        /// <summary>关闭所有已接受的连接（LingerState(0) 发 RST 而非 FIN，确保对端立即感知）。</summary>
        public void CloseAllConnections()
        {
            foreach (var client in _clients)
            {
                try
                {
                    // 强制 RST：LingerState(true, 0) 使 Close 发送 RST 而非 FIN，
                    // 对端 Poll/Write 立即失败（环回接口上 FIN 可能延迟感知）。
                    client.LingerState = new LingerOption(true, 0);
                    client.Close();
                }
                catch { }
            }
        }

        /// <summary>尝试取出一帧（非阻塞）。</summary>
        public bool TryDequeueFrame(out byte[]? frame) => _frames.TryDequeue(out frame);

        /// <summary>等待至少 count 帧到达（带超时）。</summary>
        public async Task WaitForFramesAsync(int count, int timeoutMs = 5000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (_frames.Count < count && sw.ElapsedMilliseconds < timeoutMs)
            {
                await Task.Delay(10);
            }
        }

        public ValueTask DisposeAsync()
        {
            _cts.Cancel();
            CloseAllConnections();
            _listener.Stop();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>构造 [4B BE 长度][负载] 帧。</summary>
    private static byte[] MakeFrame(byte[] payload)
    {
        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(0, 4), payload.Length);
        payload.CopyTo(frame.AsSpan(4));
        return frame;
    }

    private static byte[] MakePayload(int id, int size = 64)
    {
        var payload = new byte[size];
        payload[0] = (byte)(id & 0xFF);
        payload[1] = (byte)((id >> 8) & 0xFF);
        for (var i = 2; i < size; i++) payload[i] = (byte)(i + id);
        return payload;
    }

    // ---------- 1. 连接复用 ----------

    [Fact]
    public async Task RentReturnRent_ReusesSameConnection()
    {
        await using var ds = new FakeDsPersistent();
        using var pool = new DsTcpConnectionPool { MaxPerEndpoint = 4 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var conn1 = await pool.RentAsync("127.0.0.1", ds.Port, cts.Token);
        Assert.NotNull(conn1);
        Assert.Equal(1, Interlocked.Read(ref pool.ActiveCount));
        Assert.Equal(0, Interlocked.Read(ref pool.IdleCount));

        // 发送一帧验证连接可用
        await conn1!.SendAsync(MakeFrame(MakePayload(1)), cts.Token);

        // 归还
        pool.Return("127.0.0.1", ds.Port, conn1);
        Assert.Equal(0, Interlocked.Read(ref pool.ActiveCount));
        Assert.Equal(1, Interlocked.Read(ref pool.IdleCount));

        // 再次租借 → 应拿到同一连接（引用相等）
        var conn2 = await pool.RentAsync("127.0.0.1", ds.Port, cts.Token);
        Assert.NotNull(conn2);
        Assert.Same(conn1, conn2);
        Assert.Equal(1, Interlocked.Read(ref pool.ActiveCount));
        Assert.Equal(0, Interlocked.Read(ref pool.IdleCount));

        pool.Return("127.0.0.1", ds.Port, conn2!);
    }

    // ---------- 2. 写失败标记 IsBroken ----------

    [Fact]
    public async Task SendAsync_AfterRemoteClose_MarksBrokenAndIncrementsWriteFailure()
    {
        await using var ds = new FakeDsPersistent();
        using var pool = new DsTcpConnectionPool { MaxPerEndpoint = 4 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var conn = await pool.RentAsync("127.0.0.1", ds.Port, cts.Token);
        Assert.NotNull(conn);

        // 先发一帧确认连接已建立（服务端已 Accept）
        await conn!.SendAsync(MakeFrame(MakePayload(1)), cts.Token);
        await ds.WaitForFramesAsync(1, timeoutMs: 2000);

        // 远端强制 RST 关闭
        ds.CloseAllConnections();
        await Task.Delay(300); // 等待 RST 传播到客户端 TCP 栈

        // 大负载写入（256KB）：超过内核发送缓冲区，必须实际发送 → RST 状态下必失败。
        // 小负载在 Windows 环回上可能仅复制到缓冲区就返回成功（不触发实际发送）。
        var largePayload = new byte[256 * 1024];
        largePayload[0] = 0xDE; largePayload[1] = 0xAD;
        var largeFrame = MakeFrame(largePayload);

        var writeFailuresBefore = Interlocked.Read(ref pool.WriteFailureCount);
        Exception? caught = null;
        for (var attempt = 0; attempt < 5 && caught is null; attempt++)
        {
            try
            {
                await conn.SendAsync(largeFrame, cts.Token);
                await Task.Delay(150);
            }
            catch (Exception ex) { caught = ex; }
        }

        Assert.NotNull(caught);
        Assert.True(conn.IsBroken);
        Assert.True(Interlocked.Read(ref pool.WriteFailureCount) > writeFailuresBefore);

        // 归还 broken 连接 → 直接 Dispose，不入池
        pool.Return("127.0.0.1", ds.Port, conn);
        Assert.Equal(0, Interlocked.Read(ref pool.IdleCount));
    }

    // ---------- 3. 并发写不乱帧 ----------

    [Fact]
    public async Task ConcurrentSends_FramesArriveIntact()
    {
        await using var ds = new FakeDsPersistent();
        using var pool = new DsTcpConnectionPool { MaxPerEndpoint = 4 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var conn = await pool.RentAsync("127.0.0.1", ds.Port, cts.Token);
        Assert.NotNull(conn);

        const int threadCount = 8;
        const int framesPerThread = 20;
        var tasks = new Task[threadCount];

        for (var t = 0; t < threadCount; t++)
        {
            var threadId = t;
            tasks[t] = Task.Run(async () =>
            {
                for (var f = 0; f < framesPerThread; f++)
                {
                    var payload = MakePayload(threadId * 1000 + f, 128);
                    await conn!.SendAsync(MakeFrame(payload), cts.Token);
                }
            });
        }

        await Task.WhenAll(tasks);

        // 等待所有帧到达
        var totalFrames = threadCount * framesPerThread;
        await ds.WaitForFramesAsync(totalFrames, timeoutMs: 5000);

        // 验证帧完整性：每帧负载的前 2 字节编码了 id，且长度正确
        var received = new ConcurrentBag<byte[]>();
        while (ds.TryDequeueFrame(out var frame))
        {
            if (frame is not null) received.Add(frame);
        }

        Assert.Equal(totalFrames, received.Count);
        foreach (var payload in received)
        {
            Assert.Equal(128, payload.Length);
            // 验证 id 编码一致性（前 2 字节 = id 的低/高字节）
            var id = payload[0] | (payload[1] << 8);
            Assert.True(id >= 0 && id < threadCount * 1000 + framesPerThread,
                $"帧 id {id} 超出预期范围");
        }

        pool.Return("127.0.0.1", ds.Port, conn!);
    }

    // ---------- 4. Evict 清池 ----------

    [Fact]
    public async Task Evict_DisposesAllIdleConnections()
    {
        await using var ds = new FakeDsPersistent();
        using var pool = new DsTcpConnectionPool { MaxPerEndpoint = 4 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // 租借 2 条连接
        var conn1 = await pool.RentAsync("127.0.0.1", ds.Port, cts.Token);
        var conn2 = await pool.RentAsync("127.0.0.1", ds.Port, cts.Token);
        Assert.NotNull(conn1);
        Assert.NotNull(conn2);

        // 归还 → 进入空闲池
        pool.Return("127.0.0.1", ds.Port, conn1!);
        pool.Return("127.0.0.1", ds.Port, conn2!);
        Assert.Equal(2, Interlocked.Read(ref pool.IdleCount));

        // Evict → 清空
        pool.Evict("127.0.0.1", ds.Port);
        Assert.Equal(0, Interlocked.Read(ref pool.IdleCount));

        // 再次 Rent → 应创建新连接（非复用旧的）
        var conn3 = await pool.RentAsync("127.0.0.1", ds.Port, cts.Token);
        Assert.NotNull(conn3);
        Assert.NotSame(conn1, conn3);
        Assert.NotSame(conn2, conn3);

        pool.Return("127.0.0.1", ds.Port, conn3!);
    }

    // ---------- 5. 池上限 ----------

    [Fact]
    public async Task MaxPerEndpoint_ThirdRent_ReturnsNull()
    {
        await using var ds = new FakeDsPersistent();
        using var pool = new DsTcpConnectionPool { MaxPerEndpoint = 2 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var conn1 = await pool.RentAsync("127.0.0.1", ds.Port, cts.Token);
        var conn2 = await pool.RentAsync("127.0.0.1", ds.Port, cts.Token);
        Assert.NotNull(conn1);
        Assert.NotNull(conn2);

        // 第 3 次 → 达到上限 → 返回 null
        var conn3 = await pool.RentAsync("127.0.0.1", ds.Port, cts.Token);
        Assert.Null(conn3);
        Assert.Equal(1, Interlocked.Read(ref pool.RentFailureCount));

        // 归还一条后可再租
        pool.Return("127.0.0.1", ds.Port, conn1!);
        var conn4 = await pool.RentAsync("127.0.0.1", ds.Port, cts.Token);
        Assert.NotNull(conn4);

        pool.Return("127.0.0.1", ds.Port, conn2!);
        pool.Return("127.0.0.1", ds.Port, conn4!);
    }

    // ---------- 6. 健康探测：远端关闭后 Rent 丢弃死连接 ----------

    [Fact]
    public async Task RentAsync_DetectsDeadConnection_DiscardsAndCreatesNew()
    {
        await using var ds = new FakeDsPersistent();
        using var pool = new DsTcpConnectionPool { MaxPerEndpoint = 4 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // 租借并归还 → 连接进入空闲池
        var conn1 = await pool.RentAsync("127.0.0.1", ds.Port, cts.Token);
        Assert.NotNull(conn1);
        // 发一帧确认服务端已 Accept（避免 CloseAllConnections 时服务端尚未拿到 client）
        await conn1!.SendAsync(MakeFrame(MakePayload(7)), cts.Token);
        await ds.WaitForFramesAsync(1, timeoutMs: 2000);

        pool.Return("127.0.0.1", ds.Port, conn1);
        Assert.Equal(1, Interlocked.Read(ref pool.IdleCount));

        // 远端强制 RST 关闭 → 空闲连接变为死连接
        ds.CloseAllConnections();
        await Task.Delay(500); // 充分等待 RST 传播到客户端 TCP 栈

        var reconnectBefore = Interlocked.Read(ref pool.ReconnectCount);

        // 再次 Rent → 健康探测发现死连接 → 丢弃 → 新建
        var conn2 = await pool.RentAsync("127.0.0.1", ds.Port, cts.Token);
        Assert.NotNull(conn2);
        Assert.NotSame(conn1, conn2);
        Assert.Equal(reconnectBefore + 1, Interlocked.Read(ref pool.ReconnectCount));

        // 新连接可用
        await conn2!.SendAsync(MakeFrame(MakePayload(99)), cts.Token);
        await ds.WaitForFramesAsync(1, timeoutMs: 3000);
        Assert.True(ds.TryDequeueFrame(out var frame));
        Assert.NotNull(frame);

        pool.Return("127.0.0.1", ds.Port, conn2);
    }
}