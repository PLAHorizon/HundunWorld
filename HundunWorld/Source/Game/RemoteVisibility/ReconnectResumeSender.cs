using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Game.Network;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;
using HundunWorld.Game.Network;
using HundunWorld.Game.RemoteVisibility.Contracts;

namespace HundunWorld.Game.RemoteVisibility;

/// <summary>
/// 重连恢复协商消息（ReconnectResumePacket）的唯一发送器。
/// <para>
/// 以 <see cref="NetworkManager.SendAsync(SyncFrameMessage)"/> 返回值为真实送达依据：
/// 仅真实送达返回 <see cref="ResumeSendResult.Delivered"/> 并输出成功日志（spec 5.6.1 规则 1/6 禁止假成功）；
/// 失败返回 <see cref="ResumeSendResult"/>（Reason 区分场景）且禁止输出"已发送"成功日志。
/// </para>
/// </summary>
public sealed class ReconnectResumeSender : IReconnectResumeSender
{
    private readonly NetworkManager _networkManager;
    private readonly Func<ulong, long> _getLastAppliedTick;

    /// <summary>重连协商发送尝试计数（观测）。</summary>
    public long ResumeSendAttempts => _resumeSendAttempts;
    private long _resumeSendAttempts;

    /// <summary>重连协商真实送达计数（观测）。</summary>
    public long ResumeDeliveredCount => _resumeDeliveredCount;
    private long _resumeDeliveredCount;

    /// <summary>成功日志采样输出计数（限频）。</summary>
    private long _successLogCount;

    /// <summary>
    /// 初始化重连协商发送器。
    /// </summary>
    /// <param name="networkManager">网络管理器（唯一发送出口）。</param>
    /// <param name="getLastAppliedTick">获取客户端最后已应用快照 tick 的委托（默认读取 NetworkManager.LastAppliedServerTick）。</param>
    public ReconnectResumeSender(NetworkManager networkManager, Func<ulong, long>? getLastAppliedTick = null)
    {
        _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        _getLastAppliedTick = getLastAppliedTick ?? new Func<ulong, long>(_ => networkManager.LastAppliedServerTick);
    }

    /// <inheritdoc />
    public bool CanSendNow => _networkManager.CanSendMessage();

    /// <inheritdoc />
    public async Task<ResumeSendResult> SendResumeAsync(ulong characterId, long lastAppliedServerTick)
    {
        if (characterId == 0)
        {
            return new ResumeSendResult(false, ResumeFailReason.ConnectionNotReady);
        }

        Interlocked.Increment(ref _resumeSendAttempts);

        // 前置条件：连接未就绪 → 直接失败（重连窗口期上行隔离，spec 5.6.1 规则 2）。
        if (!CanSendNow)
        {
            return new ResumeSendResult(false, ResumeFailReason.ConnectionNotReady);
        }

        try
        {
            var lastTick = lastAppliedServerTick != 0 ? lastAppliedServerTick : _getLastAppliedTick(characterId);
            var resumePacket = new ReconnectResumePacket
            {
                LocalCharacterId = characterId,
                LastAppliedSnapshotTick = lastTick,
                LastAppliedDiffSeq = 0,
            };

            SyncPacketCodec.Encode(resumePacket, out var frame, out var frameLength);
            try
            {
                var payload = new byte[frameLength];
                System.Buffer.BlockCopy(frame, 0, payload, 0, frameLength);

                var syncFrame = new SyncFrameMessage
                {
                    Frame = payload,
                    PacketKind = (byte)SyncPacketKind.ReconnectResume,
                    ProtocolVersion = resumePacket.ProtocolVersion,
                };

                var delivered = await _networkManager.SendAsync(syncFrame);
                if (delivered)
                {
                    Interlocked.Increment(ref _resumeDeliveredCount);

                    // 成功日志仅在真实送达后输出，携带发送结果来源标记（spec 4.4.5、5.6.1 规则 6）。
                    var cnt = Interlocked.Increment(ref _successLogCount);
                    if (cnt <= 3 || cnt % 10 == 1)
                    {
                        EnhancedLogging.LogInfo($"[Phase C5] ReconnectResumePacket 已真实送达: CharacterId={characterId}, LastTick={lastTick}, Source=SendAsync=true");
                    }

                    return new ResumeSendResult(true);
                }

                // SendAsync 返回 false：连接未就绪或守卫拦截。
                return new ResumeSendResult(false, ResumeFailReason.ConnectionNotReady);
            }
            finally
            {
                SyncPacketCodec.ReturnFrame(frame);
            }
        }
        catch (Exception ex)
        {
            // 发送通道异常 → Failed(ChannelError)，不向调用方抛出（spec 5.6.1 规则 4 异常收敛）。
            System.Diagnostics.Debug.WriteLine($"[ReconnectResumeSender] 协商发送通道异常: {ex.Message}");
            return new ResumeSendResult(false, ResumeFailReason.ChannelError);
        }
    }
}