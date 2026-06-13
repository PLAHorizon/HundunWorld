using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Server;
using Horizon.Orleans.Interface.World;

namespace Horizon.Orleans.Grains.World;

/// <summary>
/// <see cref="IPlayerSessionGrain"/> 的最小可用实现（P1-a）。<br/>
/// 内部委托给 <see cref="PlayerSessionState"/>；本 grain 目前是"瞬态 grain"，不落盘——
/// 会话状态在断线后由 <see cref="IPlayerSessionGrain.ResumeAsync"/> 依据客户端上报的版本向量重建，
/// 无需持久化（对齐计划中的"Transient Entities 不进 Diff Log"）。
/// </summary>
/// <remarks>
/// 后续 PR 会扩展：
/// <list type="bullet">
///   <item>P2-a：AOI 范围查询 + Gateway 本地扇出订阅表同步。</item>
///   <item>P4-b：从 <see cref="ReconnectResumePacket"/> 的版本向量做版本仲裁。</item>
/// </list>
/// </remarks>
public class PlayerSessionGrain : Grain, IPlayerSessionGrain
{
    private readonly ILogger<PlayerSessionGrain> _logger;
    private readonly PlayerSessionState _state = new();

    public PlayerSessionGrain(ILogger<PlayerSessionGrain> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("PlayerSessionGrain {CharacterId} 激活。", this.GetPrimaryKeyLong());
        return base.OnActivateAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> HandshakeAsync(HandshakePacket packet, int baselineVersion, int worldPatchVersion, long lastAppliedDiffSeq)
    {
        if (packet is null) return Task.FromResult(false);
        var ok = _state.ApplyHandshake(baselineVersion, worldPatchVersion, lastAppliedDiffSeq);
        if (ok)
        {
            _logger.LogDebug(
                "PlayerSession {CharacterId} 握手完成: baseline={Baseline}, patch={Patch}, diffSeq={DiffSeq}",
                this.GetPrimaryKeyLong(), baselineVersion, worldPatchVersion, lastAppliedDiffSeq);
        }
        return Task.FromResult(ok);
    }

    /// <inheritdoc />
    public Task<InputAcceptResult> ReceiveInputAsync(InputPacket packet)
    {
        var result = _state.AcceptInput(packet);
        if (result != InputAcceptResult.Accepted && result != InputAcceptResult.Duplicate)
        {
            // 仅在真正异常路径（Invalid / TooOld）记一条 debug，避免正常重传刷屏。
            _logger.LogDebug(
                "PlayerSession {CharacterId} 输入被拒: {Result}, clientTick={Tick}",
                this.GetPrimaryKeyLong(), result, packet?.ClientTick ?? -1);
        }
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task AdvanceServerTickAsync(long serverTick)
    {
        _state.AdvanceServerTick(serverTick);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<InputAckPacket> BuildInputAckAsync(long echoClientTick = 0)
    {
        // 先消费缓冲以推进 LastProcessedClientTick，模拟权威模拟 drain（真正的模拟在 Zone grain）。
        _state.ConsumeInputs();
        return Task.FromResult(_state.BuildInputAck(echoClientTick));
    }

    /// <inheritdoc />
    public Task<int> SubscribeChunksAsync(ulong[] mortonKeys)
    {
        var added = mortonKeys is null || mortonKeys.Length == 0
            ? 0
            : _state.SubscribeChunks(mortonKeys);
        return Task.FromResult(added);
    }

    /// <inheritdoc />
    public Task<int> UnsubscribeChunksAsync(ulong[] mortonKeys)
    {
        var removed = mortonKeys is null || mortonKeys.Length == 0
            ? 0
            : _state.UnsubscribeChunks(mortonKeys);
        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public Task<ResumeDecision> ResumeAsync(ReconnectResumePacket packet, long serverHeadDiffSeq, int serverWorldPatchVersion)
    {
        if (packet is null) throw new ArgumentNullException(nameof(packet));
        var decision = _state.ApplyReconnect(packet, serverHeadDiffSeq, serverWorldPatchVersion);
        _logger.LogInformation(
            "PlayerSession {CharacterId} 重连决策: {Decision}, clientDiffSeq={ClientSeq}, serverHead={ServerHead}",
            this.GetPrimaryKeyLong(), decision, packet.LastAppliedDiffSeq, serverHeadDiffSeq);
        return Task.FromResult(decision);
    }
}
