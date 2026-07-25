using System.Collections.Concurrent;
using System.Threading;
using Horizon.Game.Message.Sync;

namespace Horizon.Game.ECS.Arch.Network;

/// <summary>
/// 线程安全的输入包发送队列（带溢出保护）：存放等待通过网络层发送到服务器的 <see cref="InputPacket"/> 和 <see cref="CombatActionPacket"/>。
/// </summary>
/// <remarks>
/// 使用 <see cref="ConcurrentQueue{T}"/> 保证 ECS 线程（生产者）与网络 IO 线程（消费者）的安全通信。
/// 静态单例，整个进程共享。
/// <para>
/// 溢出保护：队列超过 <see cref="MaxQueueSize"/> 时采用 DropOldest 策略丢弃最旧包，
/// 防止网络阻塞（TCP 缓冲区满）时队列无限膨胀。
/// 输入包具有时效性（60Hz 定频），旧包被新包覆盖是合理的。
/// </para>
/// </remarks>
public sealed class InputSendQueue
{
    /// <summary>全局唯一的队列实例。</summary>
    public static readonly InputSendQueue Instance = new();

    private readonly ConcurrentQueue<InputPacket> _queue = new();

    /// <summary>P1.4：战斗动作包队列。</summary>
    private readonly ConcurrentQueue<CombatActionPacket> _combatQueue = new();

    // 溢出统计
    private long _droppedByOverflowCount;

    /// <summary>私有构造函数，确保单例。</summary>
    private InputSendQueue() { }

    /// <summary>
    /// 队列软上限，超过此值时采用 DropOldest 策略丢弃最旧包。
    /// 默认 256（约 4 秒 @60Hz 的输入量）。
    /// </summary>
    public int MaxQueueSize { get; set; } = 256;

    /// <summary>
    /// 将一个 <see cref="InputPacket"/> 入队，等待网络层消费。
    /// </summary>
    /// <param name="packet">要发送的输入包。</param>
    public void Enqueue(InputPacket packet)
    {
        if (_queue.Count >= MaxQueueSize)
        {
            // 队列已满：丢弃最旧包（输入包具有时效性，旧包已被新包覆盖）
            _queue.TryDequeue(out _);
            Interlocked.Increment(ref _droppedByOverflowCount);
        }
        _queue.Enqueue(packet);
    }

    /// <summary>
    /// P1.4：将一个 <see cref="CombatActionPacket"/> 入队，等待网络层消费。
    /// </summary>
    /// <param name="packet">要发送的战斗动作包。</param>
    public void EnqueueCombatAction(CombatActionPacket packet)
    {
        _combatQueue.Enqueue(packet);
    }

    /// <summary>
    /// 尝试从队列中取出一个 <see cref="InputPacket"/>。
    /// </summary>
    /// <param name="packet">取出的输入包。</param>
    /// <returns>成功取出时返回 <c>true</c>，队列为空时返回 <c>false</c>。</returns>
    public bool TryDequeue(out InputPacket packet)
    {
        return _queue.TryDequeue(out packet!);
    }

    /// <summary>
    /// P1.4：尝试从战斗队列中取出一个 <see cref="CombatActionPacket"/>。
    /// </summary>
    public bool TryDequeueCombat(out CombatActionPacket packet)
    {
        return _combatQueue.TryDequeue(out packet!);
    }

    /// <summary>
    /// 当前队列中待发送的包数量。
    /// </summary>
    public int Count => _queue.Count;

    /// <summary>P1.4：战斗队列待发送数量。</summary>
    public int CombatCount => _combatQueue.Count;

    /// <summary>队列溢出时累计丢弃的最旧包数量。</summary>
    public long DroppedByOverflowCount => Interlocked.Read(ref _droppedByOverflowCount);

    /// <summary>
    /// 清空队列中所有待发送的输入包（断线/重连场景使用）。
    /// </summary>
    public void ClearQueue()
    {
        while (_queue.TryDequeue(out _)) { }
        while (_combatQueue.TryDequeue(out _)) { }
    }
}
