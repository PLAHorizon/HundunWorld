using System.Threading.Tasks;
using Orleans;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Server;

namespace Horizon.Orleans.Interface.World;

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
