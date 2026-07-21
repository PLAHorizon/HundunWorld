using System.Collections.Concurrent;
using Horizon.Game.Message.Sync;

namespace Horizon.Game.ECS.Arch.Network;

/// <summary>
/// 线程安全的输入包发送队列：存放等待通过网络层发送到服务器的 <see cref="InputPacket"/> 和 <see cref="CombatActionPacket"/>。
/// </summary>
/// <remarks>
/// 使用 <see cref="ConcurrentQueue{T}"/> 保证 ECS 线程（生产者）与网络 IO 线程（消费者）的安全通信。
/// 静态单例，整个进程共享。
/// </remarks>
public sealed class InputSendQueue
{
    /// <summary>全局唯一的队列实例。</summary>
    public static readonly InputSendQueue Instance = new();

    private readonly ConcurrentQueue<InputPacket> _queue = new();

    /// <summary>P1.4：战斗动作包队列。</summary>
    private readonly ConcurrentQueue<CombatActionPacket> _combatQueue = new();

    /// <summary>私有构造函数，确保单例。</summary>
    private InputSendQueue() { }

    /// <summary>
    /// 将一个 <see cref="InputPacket"/> 入队，等待网络层消费。
    /// </summary>
    /// <param name="packet">要发送的输入包。</param>
    public void Enqueue(InputPacket packet)
    {
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
}
