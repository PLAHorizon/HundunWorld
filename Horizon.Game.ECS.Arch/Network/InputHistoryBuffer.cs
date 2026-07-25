using System.Collections.Generic;
using Horizon.Game.Message.Sync;

namespace Horizon.Game.ECS.Arch.Network;

/// <summary>
/// 客户端输入历史记录环形缓冲区（容量 256），用于预测回滚时重播未确认的输入。
/// </summary>
/// <remarks>
/// 由 <c>LocalSimulationSystem</c> 写入，<c>ReconciliationSystem</c> 读取和清理。
/// 使用 lock 保证线程安全，容量满时覆盖最旧的输入。
/// </remarks>
public sealed class InputHistoryBuffer
{
    /// <summary>全局唯一实例。</summary>
    public static readonly InputHistoryBuffer Instance = new();

    private const int Capacity = 256;

    private readonly InputPacket[] _buffer = new InputPacket[Capacity];
    private readonly object _lock = new();

    /// <summary>环形缓冲区起始索引。</summary>
    private int _head;

    /// <summary>缓冲区中元素数量。</summary>
    private int _count;

    private InputHistoryBuffer() { }

    /// <summary>
    /// 当前缓冲区中的输入包数量。
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _count;
            }
        }
    }

    /// <summary>
    /// 添加一个 <see cref="InputPacket"/> 到缓冲区。
    /// </summary>
    /// <param name="packet">要添加的输入包。</param>
    public void Add(InputPacket packet)
    {
        lock (_lock)
        {
            var index = (_head + _count) % Capacity;
            _buffer[index] = packet;

            if (_count < Capacity)
            {
                _count++;
            }
            else
            {
                _head = (_head + 1) % Capacity;
            }
        }
    }

    /// <summary>
    /// 获取 ClientTick 大于指定 tick 的所有输入包。
    /// </summary>
    /// <param name="tick">参考 tick，返回 ClientTick &gt; tick 的输入包。</param>
    /// <returns>满足条件的输入包集合，按 tick 升序排列。</returns>
    public IEnumerable<InputPacket> GetFromTick(long tick)
    {
        lock (_lock)
        {
            var result = new List<InputPacket>();
            for (int i = 0; i < _count; i++)
            {
                var index = (_head + i) % Capacity;
                if (_buffer[index].ClientTick > tick)
                {
                    result.Add(_buffer[index]);
                }
            }
            return result;
        }
    }

    /// <summary>
    /// 零分配版本：将 ClientTick 大于指定 tick 的输入包写入调用方提供的缓冲区。
    /// 避免热路径（ReconciliationSystem 每帧调用）上的 List 分配与 GC 压力。
    /// </summary>
    /// <param name="tick">参考 tick，返回 ClientTick &gt; tick 的输入包。</param>
    /// <param name="destination">调用方提供的复用缓冲区，本方法先 Clear 再写入。</param>
    /// <returns>写入的输入包数量。</returns>
    public int GetFromTick(long tick, List<InputPacket> destination)
    {
        lock (_lock)
        {
            destination.Clear();
            for (int i = 0; i < _count; i++)
            {
                var index = (_head + i) % Capacity;
                if (_buffer[index].ClientTick > tick)
                {
                    destination.Add(_buffer[index]);
                }
            }
            return destination.Count;
        }
    }

    /// <summary>
    /// 清除所有 ClientTick 小于等于指定 tick 的输入包。
    /// </summary>
    /// <param name="tick">清理阈值，ClientTick &lt;= tick 的输入将被移除。</param>
    public void ClearUpTo(long tick)
    {
        lock (_lock)
        {
            while (_count > 0)
            {
                if (_buffer[_head].ClientTick <= tick)
                {
                    _head = (_head + 1) % Capacity;
                    _count--;
                }
                else
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 清空缓冲区中的所有输入包。
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _head = 0;
            _count = 0;
        }
    }
}
