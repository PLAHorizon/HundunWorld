using Horizon.IM.Core.Adapters;
using Horizon.IM.Message.Enums;
using Horizon.IM.Message.Network;

using Microsoft.Extensions.Logging;

using Orleans;

namespace Horizon.IM.Core.Handlers;

/// <summary>
/// 通话信令处理器：转发语音/视频通话信令到接收方用户的 Grain。
/// 服务端不持久化通话数据，仅负责：
/// 1. 校验信令发送者身份（必须与连接鉴权用户一致）；
/// 2. 通过接收方 Grain 完成忙线判定与实时推送；
/// 3. 向信令发送者返回确认应答（含忙线等失败原因）。
/// </summary>
public class IMCallHandler : IMMessageHandlerBase
{
    public IMCallHandler(
        ILogger<IMMessageHandlerBase> logger,
        IClusterClient clusterClient,
        IMMessageAdapter adapter)
        : base(logger, clusterClient, adapter)
    {
    }

    public override List<IMMessageType> MessageTypes { get; } = new()
    {
        IMMessageType.CallSignal
    };

    public override IMServiceType ServiceType => IMServiceType.Call;

    public override async Task<(bool IsSuccess, IMMessagePacket? MessagePacket)> RouteHandlerAsync(IMMessagePacket message)
    {
        if (message.Body is not IMCallSignalMessage signal)
        {
            return (false, CreateErrorPacket(message, IMErrorCode.Unknown, "无效的通话信令"));
        }

        if (signal.SenderId == 0 || signal.ReceiverId == 0)
        {
            return (false, CreateErrorPacket(message, IMErrorCode.Unknown, "通话信令缺少发送者或接收者"));
        }

        // 防止伪造他人身份发起/操纵通话：信令发送者必须与当前连接的鉴权用户一致
        if (message.Header.UserId != 0 && signal.SenderId != message.Header.UserId)
        {
            Logger.LogWarning(
                "通话信令发送者身份与连接用户不一致: SignalSender={SignalSender}, ConnectionUser={ConnectionUser}",
                signal.SenderId, message.Header.UserId);
            return (false, CreateErrorPacket(message, IMErrorCode.PermissionDenied, "不允许代表其他用户发送通话信令"));
        }

        if (signal.SenderId == signal.ReceiverId)
        {
            return (false, CreateErrorPacket(message, IMErrorCode.Unknown, "不能向自己发起通话"));
        }

        try
        {
            var ack = await GetUserGrain(signal.ReceiverId)
                .ReceiveCallSignalAsync(signal)
                .ConfigureAwait(false);

            return (true, CreateResponsePacket(message, ack));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "转发通话信令失败: CallId={CallId}, SignalType={SignalType}, Sender={SenderId}, Receiver={ReceiverId}",
                signal.CallId, signal.SignalType, signal.SenderId, signal.ReceiverId);
            return (false, CreateErrorPacket(message, IMErrorCode.Unknown, "通话信令转发失败，请稍后重试"));
        }
    }
}
