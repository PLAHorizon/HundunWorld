using Horizon.Game.Message.Sync;

namespace Horizon.Game.ECS.Arch.Network;

/// <summary>
/// 线程安全的 InputAckPacket 接收缓冲区（覆盖式）。
/// </summary>
/// <remarks>
/// 网络线程写入（收到服务器 ACK 后放入），ECS 线程读取（ReconciliationSystem 消费）。
/// 采用覆盖策略，始终保留最新一条 InputAckPacket。
/// </remarks>
public sealed class InputAckReceiveBuffer
{
    /// <summary>全局唯一实例。</summary>
    public static readonly InputAckReceiveBuffer Instance = new();

    private readonly object _lock = new();

    private InputAckPacket? _latest;

    private InputAckReceiveBuffer() { }

    /// <summary>
    /// 当前最新的 InputAckPacket，可能为 null。
    /// </summary>
    public InputAckPacket? Latest
    {
        get
        {
            lock (_lock)
            {
                return _latest;
            }
        }
        set
        {
            lock (_lock)
            {
                _latest = value;
            }
        }
    }

    /// <summary>
    /// 尝试取出并清空最新的 InputAckPacket。
    /// </summary>
    /// <param name="packet">取出的输入确认包。</param>
    /// <returns>成功取出时返回 <c>true</c>，无数据时返回 <c>false</c>。</returns>
    public bool TryTake(out InputAckPacket? packet)
    {
        lock (_lock)
        {
            packet = _latest;
            _latest = null;
            return packet != null;
        }
    }

    /// <summary>
    /// 清空缓冲区中待处理的 ACK 包（断线/重连场景使用）。
    /// 避免重连后消费旧会话的 ACK 导致 InputHistoryBuffer 被错误清理。
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _latest = null;
        }
    }
}
