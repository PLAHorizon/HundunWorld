using System.Collections.Concurrent;
using System.Threading;
using Horizon.Game.Message.Sync;

namespace Horizon.Game.ECS.Arch.Network;

/// <summary>
/// 线程安全的 <see cref="SnapshotPacket"/> 接收队列。
/// </summary>
/// <remarks>
/// 网络线程写入（收到服务器快照后放入），ECS 线程读取（<see cref="Systems.SnapshotApplySystem"/> 消费）。
/// 使用 <see cref="ConcurrentQueue{SnapshotPacket}"/> 保证跨线程安全通信。
/// 静态单例，整个进程共享。
/// </remarks>
public sealed class SnapshotReceiveBuffer
{
    /// <summary>全局唯一的队列实例。</summary>
    public static readonly SnapshotReceiveBuffer Instance = new();

    private readonly ConcurrentQueue<SnapshotPacket> _queue = new();

    // 累计统计：用于诊断"曾经入队但已被消费"的数量，避免 SnapshotApplySystem drain 后 Count=0 被误判为链路断裂。
    private long _totalEnqueued;
    private long _totalDequeued;

    // 队列溢出丢弃统计：DropOldest 策略下累计被丢弃的最旧包数量。
    private long _droppedByOverflowCount;

    // 限频告警：每 10 秒最多输出一次溢出告警日志，避免刷屏。
    private long _lastOverflowWarnTicks;
    private static readonly long OverflowWarnIntervalTicks = TimeSpan.FromSeconds(10).Ticks;

    private SnapshotReceiveBuffer() { }

    /// <summary>
    /// 队列软上限，超过此值时采用 DropOldest 策略丢弃最旧包。
    /// 默认 1024，可根据业务场景调整。
    /// </summary>
    public int MaxQueueSize { get; set; } = 1024;

    /// <summary>
    /// 将一个 <see cref="SnapshotPacket"/> 入队。
    /// </summary>
    /// <param name="packet">要入队的快照包。</param>
    public void Enqueue(SnapshotPacket packet)
    {
        if (_queue.Count >= MaxQueueSize)
        {
            // 队列已满：丢弃最旧包以腾出空间（DropOldest 策略）
            _queue.TryDequeue(out _);
            Interlocked.Increment(ref _droppedByOverflowCount);
            LogOverflowWarn();
        }
        _queue.Enqueue(packet);
        Interlocked.Increment(ref _totalEnqueued);
    }

    /// <summary>
    /// 限频输出队列溢出告警日志：每 10 秒最多一次。
    /// 输出到 Console.Error（不阻塞主线程，区别于 Console.WriteLine）。
    /// </summary>
    private void LogOverflowWarn()
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastOverflowWarnTicks);
        if (now - last < OverflowWarnIntervalTicks) return;
        // CAS 更新：确保仅有一个线程成功输出告警，避免并发刷屏
        if (Interlocked.CompareExchange(ref _lastOverflowWarnTicks, now, last) != last) return;
        Console.Error.WriteLine($"[SnapshotReceiveBuffer] 队列溢出丢弃最旧包：QueueCount={_queue.Count}, MaxSize={MaxQueueSize}, TotalDropped={Interlocked.Read(ref _droppedByOverflowCount)}");
    }

    /// <summary>
    /// 尝试从队列中取出一个 <see cref="SnapshotPacket"/>。
    /// </summary>
    /// <param name="packet">取出的快照包。</param>
    /// <returns>成功取出时返回 <c>true</c>，队列为空时返回 <c>false</c>。</returns>
    public bool TryDequeue(out SnapshotPacket packet)
    {
        if (_queue.TryDequeue(out packet!))
        {
            Interlocked.Increment(ref _totalDequeued);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 当前队列中待处理的快照包数量。
    /// 注意：SnapshotApplySystem 每帧 drain 整个队列，所以本字段在 ECS tick 后总是 0；
    /// 诊断链路是否联通应优先看 <see cref="TotalEnqueued"/> 与 <see cref="TotalDequeued"/>。
    /// </summary>
    public int Count => _queue.Count;

    /// <summary>累计入队数（从不重置）。诊断 fanout 链路是否联通的可靠指标。</summary>
    public long TotalEnqueued => Interlocked.Read(ref _totalEnqueued);

    /// <summary>累计出队数（从不重置）。等于 SnapshotApplySystem 已消费的快照包总数。</summary>
    public long TotalDequeued => Interlocked.Read(ref _totalDequeued);

    /// <summary>
    /// 队列溢出时累计丢弃的最旧包数量（从不重置）。
    /// 当 <see cref="Count"/> 达到 <see cref="MaxQueueSize"/> 时采用 DropOldest 策略，丢弃最旧包以腾出空间。
    /// </summary>
    public long DroppedByOverflowCount => Interlocked.Read(ref _droppedByOverflowCount);

    /// <summary>
    /// 清空队列中所有待处理的快照包（断线/重连场景使用）。
    /// 避免断线期间积压的旧快照在重连后污染状态。
    /// </summary>
    public void ClearQueue()
    {
        while (_queue.TryDequeue(out _)) { }
    }
}
