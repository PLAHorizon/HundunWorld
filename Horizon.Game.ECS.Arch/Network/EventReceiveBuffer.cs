using System.Collections.Concurrent;
using System.Threading;
using Horizon.Game.Message.Sync;

namespace Horizon.Game.ECS.Arch.Network;

/// <summary>
/// 线程安全的 <see cref="EventPacket"/> 接收队列（带溢出保护）。
/// </summary>
/// <remarks>
/// 网络线程写入（收到服务器事件包后放入），ECS 线程读取（<see cref="Systems.EventApplySystem"/> 消费）。
/// 使用 <see cref="ConcurrentQueue{EventPacket}"/> 保证跨线程安全通信。
/// 静态单例，整个进程共享。
/// <para>
/// 溢出保护：队列超过 <see cref="MaxQueueSize"/> 时采用 DropOldest 策略丢弃最旧包，
/// 防止网络突发（如批量技能事件）导致内存无限膨胀。
/// </para>
/// </remarks>
public sealed class EventReceiveBuffer
{
    /// <summary>全局唯一的队列实例。</summary>
    public static readonly EventReceiveBuffer Instance = new();

    private readonly ConcurrentQueue<EventPacket> _queue = new();

    // 累计统计
    private long _totalEnqueued;
    private long _totalDequeued;
    private long _droppedByOverflowCount;

    private EventReceiveBuffer() { }

    /// <summary>
    /// 队列软上限，超过此值时采用 DropOldest 策略丢弃最旧包。
    /// 默认 512，事件包通常较小但可能突发（批量技能/伤害）。
    /// </summary>
    public int MaxQueueSize { get; set; } = 512;

    /// <summary>
    /// 将一个 <see cref="EventPacket"/> 入队。
    /// </summary>
    /// <param name="packet">要入队的事件包。</param>
    public void Enqueue(EventPacket packet)
    {
        if (_queue.Count >= MaxQueueSize)
        {
            // 队列已满：丢弃最旧包以腾出空间（DropOldest 策略）
            _queue.TryDequeue(out _);
            Interlocked.Increment(ref _droppedByOverflowCount);
        }
        _queue.Enqueue(packet);
        Interlocked.Increment(ref _totalEnqueued);
    }

    /// <summary>
    /// 尝试从队列中取出一个 <see cref="EventPacket"/>。
    /// </summary>
    /// <param name="packet">取出的事件包。</param>
    /// <returns>成功取出时返回 <c>true</c>，队列为空时返回 <c>false</c>。</returns>
    public bool TryDequeue(out EventPacket packet)
    {
        if (_queue.TryDequeue(out packet!))
        {
            Interlocked.Increment(ref _totalDequeued);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 当前队列中待处理的事件包数量。
    /// </summary>
    public int Count => _queue.Count;

    /// <summary>累计入队数（从不重置）。</summary>
    public long TotalEnqueued => Interlocked.Read(ref _totalEnqueued);

    /// <summary>累计出队数（从不重置）。</summary>
    public long TotalDequeued => Interlocked.Read(ref _totalDequeued);

    /// <summary>队列溢出时累计丢弃的最旧包数量。</summary>
    public long DroppedByOverflowCount => Interlocked.Read(ref _droppedByOverflowCount);

    /// <summary>
    /// 清空队列中所有待处理的事件包（断线/重连场景使用）。
    /// </summary>
    public void ClearQueue()
    {
        while (_queue.TryDequeue(out _)) { }
    }
}
