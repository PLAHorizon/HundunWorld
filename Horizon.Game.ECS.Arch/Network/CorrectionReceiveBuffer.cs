using Horizon.Game.Core.Sim;

namespace Horizon.Game.ECS.Arch.Network;

/// <summary>
/// 线程安全的 CorrectionPacket 接收缓冲区。
/// </summary>
/// <remarks>
/// 网络线程写入（收到服务器修正包后放入），ECS 线程读取（ReconciliationSystem 消费）。
/// 每次 TryTake 会取出并清空缓冲区，确保每个修正包只被处理一次。
/// </remarks>
public sealed class CorrectionReceiveBuffer
{
    /// <summary>全局唯一实例。</summary>
    public static readonly CorrectionReceiveBuffer Instance = new();

    private readonly object _lock = new();

    private CorrectionPacket? _packet;

    private CorrectionReceiveBuffer() { }

    /// <summary>
    /// 尝试取出并清空最新的 CorrectionPacket。
    /// </summary>
    /// <param name="packet">取出的修正包。</param>
    /// <returns>成功取出时返回 <c>true</c>，无数据时返回 <c>false</c>。</returns>
    public bool TryTake(out CorrectionPacket? packet)
    {
        lock (_lock)
        {
            packet = _packet;
            _packet = null;
            return packet != null;
        }
    }

    /// <summary>
    /// 将一个 CorrectionPacket 放入缓冲区（覆盖旧值）。
    /// </summary>
    /// <param name="packet">要放入的修正包。</param>
    public void Add(CorrectionPacket packet)
    {
        lock (_lock)
        {
            _packet = packet;
        }
    }

    /// <summary>
    /// 清空缓冲区中待处理的修正包（断线/重连场景使用）。
    /// 避免断线期间积压的旧修正在重连后触发无意义的位置吸附。
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _packet = null;
        }
    }
}
