using System.Collections.Concurrent;
using Horizon.Game.Message.Sync;

namespace Horizon.Game.ECS.Arch.Network;

/// <summary>
/// 线程安全的 <see cref="EventPacket"/> 接收队列。
/// </summary>
/// <remarks>
/// 网络线程写入（收到服务器事件包后放入），ECS 线程读取（<see cref="Systems.EventApplySystem"/> 消费）。
/// 使用 <see cref="ConcurrentQueue{EventPacket}"/> 保证跨线程安全通信。
/// 静态单例，整个进程共享。
/// </remarks>
public sealed class EventReceiveBuffer
{
    /// <summary>全局唯一的队列实例。</summary>
    public static readonly EventReceiveBuffer Instance = new();

    private readonly ConcurrentQueue<EventPacket> _queue = new();

    private EventReceiveBuffer() { }

    /// <summary>
    /// 将一个 <see cref="EventPacket"/> 入队。
    /// </summary>
    /// <param name="packet">要入队的事件包。</param>
    public void Enqueue(EventPacket packet)
    {
        _queue.Enqueue(packet);
    }

    /// <summary>
    /// 尝试从队列中取出一个 <see cref="EventPacket"/>。
    /// </summary>
    /// <param name="packet">取出的事件包。</param>
    /// <returns>成功取出时返回 <c>true</c>，队列为空时返回 <c>false</c>。</returns>
    public bool TryDequeue(out EventPacket packet)
    {
        return _queue.TryDequeue(out packet!);
    }

    /// <summary>
    /// 当前队列中待处理的事件包数量。
    /// </summary>
    public int Count => _queue.Count;
}
