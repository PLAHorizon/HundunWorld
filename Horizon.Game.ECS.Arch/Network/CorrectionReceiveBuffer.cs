using Horizon.Game.Core.Sim;
using Horizon.Game.Message.Sync.Components;
using MemoryPack;

namespace Horizon.Game.ECS.Arch.Network;

/// <summary>
/// 服务器→客户端的位置修正包（P1-b）。<br/>
/// 当 <see cref="MovementValidator"/> 发现客户端预测位置与权威回放偏差超过阈值时下发；
/// 客户端 <c>ReconciliationSystem</c> 据此强制吸附 <see cref="AuthTransformComponent"/>。
/// </summary>
/// <remarks>
/// 本包与 <see cref="Horizon.Game.Message.Sync.SyncPacket"/> 体系独立：
/// <list type="bullet">
///   <item>correction 走**可靠 + 高优先级**通道，不受 snapshot tick 限制。</item>
///   <item>在 P6-a 的 <c>SyncDispatcher</c> 落地时，会以 <c>EventPacket</c> 的负载形式下发，
///   避免 <see cref="Horizon.Game.Message.Sync.SyncPacket"/> union 再次扩容。</item>
/// </list>
/// </remarks>
[MemoryPackable]
public partial class CorrectionPacket
{
    /// <summary>目标实体的网络 ID。</summary>
    [MemoryPackOrder(0)]
    public ulong EntityId { get; set; }

    /// <summary>下发时的服务器 tick，客户端按此排序应用。</summary>
    [MemoryPackOrder(1)]
    public long ServerTick { get; set; }

    /// <summary>权威 X。</summary>
    [MemoryPackOrder(2)]
    public float CorrectedX { get; set; }

    /// <summary>权威 Y。</summary>
    [MemoryPackOrder(3)]
    public float CorrectedY { get; set; }

    /// <summary>权威 Z。</summary>
    [MemoryPackOrder(4)]
    public float CorrectedZ { get; set; }

    /// <summary>权威 Z 方向速度（便于客户端继续推演）。</summary>
    [MemoryPackOrder(5)]
    public float CorrectedVz { get; set; }

    /// <summary>偏差（米）；仅用于客户端日志、不参与吸附计算。</summary>
    [MemoryPackOrder(6)]
    public float DriftMeters { get; set; }

    /// <summary>修正原因（便于反外挂后台做聚合告警）。</summary>
    [MemoryPackOrder(7)]
    public CorrectionReason Reason { get; set; }

    /// <summary>
    /// 服务端本次权威回放处理到的最后一个客户端 tick（含）。
    /// <para>
    /// 客户端 <c>ReconciliationSystem</c> 据此：
    /// <list type="number">
    ///   <item>调用 <c>InputHistoryBuffer.ClearUpTo</c> 清理已确认输入；</item>
    ///   <item>重放时仅取 <c>ClientTick &gt; LastProcessedClientTick</c> 的输入。</item>
    /// </list>
    /// 修复"吸附+重放导致角色无法移动"：原实现 Correction 不携带此字段，
    /// 客户端重放时 <c>GetFromTick(0)</c> 返回所有历史输入（含已确认），
    /// 角色从权威位置飞出极远 → 下一帧 drift 巨大 → 再次 Correction → 死循环。
    /// </para>
    /// </summary>
    [MemoryPackOrder(8)]
    public long LastProcessedClientTick { get; set; }
}

/// <summary>修正触发原因。</summary>
public enum CorrectionReason : byte
{
    /// <summary>未知/保留。</summary>
    Unknown = 0,
    /// <summary>客户端预测漂移超出阈值（常见情况：高 RTT、丢包、浮点累积）。</summary>
    PredictionDrift = 1,
    /// <summary>客户端速度超出硬性上限（疑似外挂或网络抖动）。</summary>
    SpeedHackSuspected = 2,
    /// <summary>客户端试图穿越不可行走区域（本包暂未使用；P1-b 之后的碰撞校验会启用）。</summary>
    CollisionOverride = 3,
    JumpCountExceeded = 4,
}

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
