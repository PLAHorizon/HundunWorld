using System.Threading.Tasks;
using Orleans;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Server;

namespace Horizon.Orleans.Interface.World;

/// <summary>
/// P-F3：<see cref="IPlayerSessionGrain.ReceiveInputAndForwardAsync"/> 的聚合返回值：
/// 输入接受结果 + 即时构建的 InputAck，一次跨进程 RPC 同时带回，
/// 避免 Gateway 为拿 ACK 再发一次跨进程调用。
/// </summary>
[GenerateSerializer]
public sealed class InputForwardResult
{
    /// <summary>输入接受结果（Accepted/Duplicate/Invalid/TooOld）。</summary>
    [Id(0)]
    public InputAcceptResult Result { get; set; }

    /// <summary>基于本次输入构建的 ACK（echo 为输入包的 ClientTick）。</summary>
    [Id(1)]
    public InputAckPacket Ack { get; set; } = null!;
}

/// <summary>
/// 玩家会话 Grain 契约（P1-a 引入）。<br/>
/// 每条长连接 × 每个角色 对应一个 <see cref="IPlayerSessionGrain"/>；
/// Grain Primary Key = CharacterId（与 <see cref="ICharacterGrain"/> 同源，便于反查）。
/// </summary>
/// <remarks>
/// 本接口仅暴露 P1-a 必需操作；P2/P6 会继续扩展（AOI 范围查询、resume 续连等）。
/// 实现侧的权威状态机请参见 <see cref="PlayerSessionState"/>（纯逻辑层、可 mock）。
/// </remarks>
[global::Orleans.CodeGeneration.Version(1)]
public interface IPlayerSessionGrain : IGrainWithIntegerKey
{
    /// <summary>
    /// 会话首次握手；服务器据此记录客户端自报的基线/patch 版本，
    /// 后续 snapshot 与 diff 推送以此为起点做最小化裁剪。
    /// </summary>
    /// <returns>true 表示接受；false 表示参数非法（调用方应发 error event 并断开）。</returns>
    Task<bool> HandshakeAsync(HandshakePacket packet, int baselineVersion, int worldPatchVersion, long lastAppliedDiffSeq);

    /// <summary>
    /// 接收一个 <see cref="InputPacket"/>。
    /// 由 Gateway 在 sync dispatch 中解码后调用，本身不做回包（ACK 通过 <see cref="BuildInputAckAsync"/> 定频聚合下发）。
    /// </summary>
    Task<InputAcceptResult> ReceiveInputAsync(InputPacket packet);

    /// <summary>
    /// 上行转发效率优化（P-F3）：单条跨进程 RPC 完成“接收输入 → 转发权威模拟层 → 构建 ACK”。
    /// 替代原先 Gateway 依次的三次跨进程调用（ReceiveInputAsync → SubmitInputAsync → BuildInputAckAsync），
    /// 将输入上行链路的跨进程 RTT 从 3 次降为 1 次，显著降低输入→ACK 延迟。
    /// </summary>
    /// <remarks>
    /// 转发在 silo 内部以 grain→grain 调用完成（<c>IZoneShardGrain.SubmitInputAsync</c> 为纯同步方法，
    /// 且 ZoneShardGrain 不会回调 PlayerSessionGrain，无 Orleans 非重入死锁风险）。
    /// </remarks>
    /// <param name="packet">客户端输入包。</param>
    /// <param name="zoneShardKey">目标 ZoneShardGrain 主键（由 Gateway 侧 shardRouter 解析，保持路由单一来源）。</param>
    /// <param name="predictedEndX">客户端预测终点 X（ECS Z-up）。</param>
    /// <param name="predictedEndY">客户端预测终点 Y。</param>
    /// <param name="predictedEndZ">客户端预测终点 Z。</param>
    Task<InputForwardResult> ReceiveInputAndForwardAsync(
        InputPacket packet, long zoneShardKey, float predictedEndX, float predictedEndY, float predictedEndZ);

    /// <summary>
    /// 推进服务器 tick（由所属 Zone 的模拟循环调用）。
    /// </summary>
    Task AdvanceServerTickAsync(long serverTick);

    /// <summary>
    /// 拉取当前可发的 <see cref="InputAckPacket"/>；由 Gateway 的发包节流器调用。
    /// </summary>
    Task<InputAckPacket> BuildInputAckAsync(long echoClientTick = 0);

    /// <summary>
    /// 增量订阅一组 ChunkCell Morton 键（AOI 扩展）。返回实际新增条目数。
    /// </summary>
    Task<int> SubscribeChunksAsync(ulong[] mortonKeys);

    /// <summary>
    /// 增量退订一组 ChunkCell Morton 键（AOI 收缩）。返回实际移除条目数。
    /// </summary>
    Task<int> UnsubscribeChunksAsync(ulong[] mortonKeys);

    /// <summary>
    /// 处理 <see cref="ReconnectResumePacket"/>；调用方（Gateway）根据 <see cref="ResumeDecision"/>
    /// 决定 "发增量 / 发全量 chunk / 要求 patch / 踢回登录"。
    /// </summary>
    Task<ResumeDecision> ResumeAsync(ReconnectResumePacket packet, long serverHeadDiffSeq, int serverWorldPatchVersion);
}
