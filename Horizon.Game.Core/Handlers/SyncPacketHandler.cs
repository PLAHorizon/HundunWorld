using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Server;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Horizon.Game.Core.Handlers;

/// <summary>
/// 实时同步包处理器：在现有 HorizonMessagePacket 通道内处理 SyncPacketCodec 编码帧。
/// </summary>
public sealed class SyncPacketHandler : MessageHandlerBase
{
    /// <summary>当前服务器基线版本（与游戏二进制版本一一对应）。</summary>
    private const int ServerBaselineVersion = 1;

    /// <summary>当前服务器世界补丁版本。</summary>
    private const int ServerWorldPatchVersion = 1;

    private long _characterId;

    public SyncPacketHandler(ILogger<MessageHandlerBase> logger, IClusterClient clusterClient, HorizonMessageAdapter adapter)
        : base(logger, clusterClient, adapter)
    {
    }

    public override List<MessageType> MessageTypes => new() { MessageType.SyncPacket };

    public override ServiceType ServiceType => ServiceType.Game;

    public override async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message)
    {
        if (message.Body is not SyncFrameMessage syncFrame || syncFrame.Frame.Length == 0)
        {
            Logger.LogWarning("收到空实时同步帧。MessageId={MessageId}", message.Header.MessageId);
            return (false, CreateSyncResponse(new WorldPatchManifestPacket()));
        }

        var packet = SyncPacketCodec.Decode(syncFrame.Frame);
        if (packet.ProtocolVersion != SyncProtocolVersion.Current)
        {
            Logger.LogWarning(
                "实时同步协议版本不匹配。ClientVersion={ClientVersion}, ServerVersion={ServerVersion}, Kind={Kind}",
                packet.ProtocolVersion,
                SyncProtocolVersion.Current,
                packet.Kind);
        }

        try
        {
            var response = packet switch
            {
                HandshakePacket handshake => await HandleHandshakeAsync(handshake),
                InputPacket input => await HandleInputAsync(input),
                ReconnectResumePacket resume => await HandleReconnectAsync(resume),
                _ => await HandleInputAsync(null),
            };

            return (true, CreateSyncResponse(response));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "处理实时同步包失败。Kind={Kind}, MessageId={MessageId}",
                packet.Kind,
                message.Header.MessageId);

            return (false, CreateSyncResponse(new WorldPatchManifestPacket()));
        }
    }

    /// <summary>
    /// 处理握手包：初始化玩家会话并返回世界补丁清单。
    /// </summary>
    private async Task<SyncPacket> HandleHandshakeAsync(HandshakePacket handshake)
    {
        var characterId = (long)handshake.LocalCharacterId;
        _characterId = characterId;

        Logger.LogInformation(
            "Sync握手开始。CharacterId={CharacterId}, ClientTick={ClientTick}",
            characterId,
            handshake.InitialClientTick);

        var sessionGrain = _clusterClient.GetGrain<IPlayerSessionGrain>(characterId);

        var handshakeSuccess = await sessionGrain.HandshakeAsync(
            handshake,
            ServerBaselineVersion,
            ServerWorldPatchVersion,
            lastAppliedDiffSeq: 0);

        if (!handshakeSuccess)
        {
            Logger.LogWarning("Sync握手失败，参数被拒绝。CharacterId={CharacterId}", characterId);
            return new WorldPatchManifestPacket();
        }

        Logger.LogInformation("Sync握手成功。CharacterId={CharacterId}", characterId);

        return new WorldPatchManifestPacket
        {
            BaselineVersion = ServerBaselineVersion,
            WorldPatchVersion = ServerWorldPatchVersion,
            ManifestUrl = string.Empty,
            ManifestSha256 = string.Empty,
            PatchCutoverDiffSeq = 0,
        };
    }

    /// <summary>
    /// 处理输入包：接收客户端输入并生成确认包。
    /// </summary>
    private async Task<SyncPacket> HandleInputAsync(InputPacket? input)
    {
        if (input is null)
        {
            return new InputAckPacket
            {
                LastProcessedClientTick = 0,
                ServerTick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                EchoClientTick = 0,
            };
        }

        if (_characterId == 0)
        {
            Logger.LogWarning("输入处理失败：未握手。ClientTick={ClientTick}", input.ClientTick);
            return new InputAckPacket
            {
                LastProcessedClientTick = input.ClientTick,
                ServerTick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                EchoClientTick = input.ClientTick,
            };
        }

        var sessionGrain = _clusterClient.GetGrain<IPlayerSessionGrain>(_characterId);

        var acceptResult = await sessionGrain.ReceiveInputAsync(input);

        if (acceptResult == InputAcceptResult.Invalid)
        {
            Logger.LogWarning("输入包被拒绝（无效）。CharacterId={CharacterId}, ClientTick={ClientTick}", _characterId, input.ClientTick);
        }
        else if (acceptResult == InputAcceptResult.TooOld)
        {
            Logger.LogDebug("输入包被拒绝（过期）。CharacterId={CharacterId}, ClientTick={ClientTick}", _characterId, input.ClientTick);
        }

        var ackPacket = await sessionGrain.BuildInputAckAsync(echoClientTick: input.ClientTick);

        Logger.LogDebug(
            "输入处理完成。CharacterId={CharacterId}, ClientTick={ClientTick}, ServerTick={ServerTick}, LastProcessed={LastProcessed}, Result={Result}",
            _characterId,
            input.ClientTick,
            ackPacket.ServerTick,
            ackPacket.LastProcessedClientTick,
            acceptResult);

        return ackPacket;
    }

    /// <summary>
    /// 处理重连恢复包：根据客户端状态决定恢复策略。
    /// </summary>
    private async Task<SyncPacket> HandleReconnectAsync(ReconnectResumePacket resume)
    {
        var characterId = (long)resume.LocalCharacterId;

        Logger.LogInformation(
            "断线重连恢复开始。CharacterId={CharacterId}, BaselineVersion={BaselineVersion}, WorldPatchVersion={WorldPatchVersion}, LastAppliedDiffSeq={LastAppliedDiffSeq}",
            characterId,
            resume.BaselineVersion,
            resume.WorldPatchVersion,
            resume.LastAppliedDiffSeq);

        var sessionGrain = _clusterClient.GetGrain<IPlayerSessionGrain>(characterId);

        var serverHeadDiffSeq = 0L;
        var decision = await sessionGrain.ResumeAsync(resume, serverHeadDiffSeq, ServerWorldPatchVersion);

        SyncPacket response = decision switch
        {
            ResumeDecision.ResumeIncremental => BuildIncrementalResume(resume, serverHeadDiffSeq),
            ResumeDecision.RequireLauncherPatch => BuildLauncherPatchRequiredResponse(),
            ResumeDecision.ResendFullChunks => BuildFullChunksResendResponse(resume),
            ResumeDecision.ForceReLogin => BuildForceReLoginResponse(),
            _ => new WorldPatchManifestPacket(),
        };

        Logger.LogInformation(
            "断线重连恢复完成。CharacterId={CharacterId}, Decision={Decision}",
            characterId,
            decision);

        return response;
    }

    /// <summary>
    /// 构建增量恢复响应。
    /// </summary>
    private WorldPatchManifestPacket BuildIncrementalResume(ReconnectResumePacket resume, long serverHeadDiffSeq)
    {
        return new WorldPatchManifestPacket
        {
            BaselineVersion = resume.BaselineVersion,
            WorldPatchVersion = resume.WorldPatchVersion,
            ManifestUrl = string.Empty,
            ManifestSha256 = string.Empty,
            PatchCutoverDiffSeq = resume.LastAppliedDiffSeq,
        };
    }

    /// <summary>
    /// 构建需要启动器补丁的响应。
    /// </summary>
    private WorldPatchManifestPacket BuildLauncherPatchRequiredResponse()
    {
        return new WorldPatchManifestPacket
        {
            BaselineVersion = ServerBaselineVersion,
            WorldPatchVersion = ServerWorldPatchVersion,
            ManifestUrl = string.Empty,
            ManifestSha256 = string.Empty,
            PatchCutoverDiffSeq = 0,
        };
    }

    /// <summary>
    /// 构建全量重发响应。
    /// </summary>
    private WorldPatchManifestPacket BuildFullChunksResendResponse(ReconnectResumePacket resume)
    {
        return new WorldPatchManifestPacket
        {
            BaselineVersion = resume.BaselineVersion,
            WorldPatchVersion = resume.WorldPatchVersion,
            ManifestUrl = string.Empty,
            ManifestSha256 = string.Empty,
            PatchCutoverDiffSeq = resume.LastAppliedDiffSeq,
        };
    }

    /// <summary>
    /// 构建强制重新登录响应。
    /// </summary>
    private WorldPatchManifestPacket BuildForceReLoginResponse()
    {
        return new WorldPatchManifestPacket
        {
            BaselineVersion = 0,
            WorldPatchVersion = 0,
            ManifestUrl = string.Empty,
            ManifestSha256 = string.Empty,
            PatchCutoverDiffSeq = 0,
        };
    }

    /// <summary>
    /// 将同步包编码为 HorizonMessagePacket。
    /// </summary>
    private HorizonMessagePacket CreateSyncResponse(SyncPacket packet)
    {
        SyncPacketCodec.Encode(packet, out var frame, out var frameLength);
        try
        {
            var payload = new byte[frameLength];
            Buffer.BlockCopy(frame, 0, payload, 0, frameLength);

            var message = new SyncFrameMessage
            {
                Frame = payload,
                PacketKind = (byte)packet.Kind,
                ProtocolVersion = packet.ProtocolVersion,
            };

            return CreateHorizonMessage(message);
        }
        finally
        {
            SyncPacketCodec.ReturnFrame(frame);
        }
    }
}
