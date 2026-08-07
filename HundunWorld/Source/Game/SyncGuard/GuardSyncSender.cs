using System;
using System.Threading.Tasks;
using Horizon.Game.ECS.Arch.SyncGuard.Contracts;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;

namespace HundunWorld.Game.SyncGuard;

/// <summary>
/// 受控发送服务：实体同步类上行的唯一合法发送入口。
/// <para>
/// 发送前统一调用 <see cref="IOutboundSyncAuthorizer.Authorize"/> 完成资格校验；
/// 放行 → 调用注入的发送委托（生产环境绑定 <see cref="NetworkManager.SendAsync(SyncFrameMessage)"/>）；
/// 拒绝 → 静默拦截（零网络流量、零补偿性消息）并输出违规告警（spec 5.4.1 规则 1、4.1.4）。
/// </para>
/// </summary>
public sealed class GuardSyncSender : IGuardSyncSender
{
    private readonly IOutboundSyncAuthorizer _authorizer;
    private readonly ISendViolationReporter _violationReporter;

    /// <summary>底层发送委托（生产装配为 NetworkManager.SendAsync，测试可注入替身）。</summary>
    private readonly Func<SyncFrameMessage, Task<bool>> _sendAsync;

    /// <summary>兜底守卫实例（可空；非空时发送前标记授权放行，避免被误判旁路绕过）。</summary>
    private readonly IOutboundSyncGuard? _outboundGuard;

    /// <summary>授权拒绝累计次数（限频诊断日志用，跨 ECS 线程/网络线程原子递增）。</summary>
    private long _deniedAttemptCount;

    /// <summary>
    /// 初始化受控发送服务。
    /// </summary>
    /// <param name="authorizer">集中式资格判定组件。</param>
    /// <param name="violationReporter">违规告警上报。</param>
    /// <param name="sendAsync">底层发送委托（生产绑定 NetworkManager.SendAsync）。</param>
    /// <param name="outboundGuard">兜底守卫实例（可选，用于标记授权放行）。</param>
    public GuardSyncSender(
        IOutboundSyncAuthorizer authorizer,
        ISendViolationReporter violationReporter,
        Func<SyncFrameMessage, Task<bool>> sendAsync,
        IOutboundSyncGuard? outboundGuard = null)
    {
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        _violationReporter = violationReporter ?? throw new ArgumentNullException(nameof(violationReporter));
        _sendAsync = sendAsync ?? throw new ArgumentNullException(nameof(sendAsync));
        _outboundGuard = outboundGuard;
    }

    /// <inheritdoc />
    public async Task<bool> SendLocalAsync(SyncPacket packet, ulong localCharacterId)
    {
        // 身份绑定校验：以本地角色身份发送时，消息必须携带本地角色身份（spec 5.1.1 规则 5）。
        if (!TryExtractCarriedEntityId(packet, out var carriedId))
        {
            carriedId = localCharacterId;
        }

        if (carriedId != localCharacterId)
        {
            ReportIdentityMismatch(localCharacterId, carriedId);
            return false;
        }

        var verdict = Authorize(localCharacterId, packet.Kind);
        if (!verdict.Allowed)
        {
            // 修复（静置断线后输入断流 — "无法移动"诊断）：
            // 资格拒绝此前完全静默（仅 Debug.WriteLine，Release 下不可见），且每次移动帧都走
            // 该路径时无法定位拒绝根因。对高频的 InputPacket 做限频告警：
            // 首 3 次无条件输出，之后每 120 次输出一次，避免刷屏。
            var attempt = System.Threading.Interlocked.Increment(ref _deniedAttemptCount);
            if (attempt <= 3 || attempt % 120 == 1)
            {
                FlaxEngine.Debug.LogWarning(
                    $"[GuardSyncSender] 上行被授权拒绝: Kind={packet.Kind}, EntityId={localCharacterId}, " +
                    $"Reason={verdict.Reason}, Category={verdict.Category}, TotalDenied={attempt}");
            }
            return false;
        }

        return await SendEncodedAsync(packet);
    }

    /// <inheritdoc />
    public async Task<bool> SendBoundEntityAsync(SyncPacket packet, ulong boundEntityId)
    {
        // 身份绑定校验：以绑定实体身份发送时，消息必须携带绑定实体自身身份（spec 5.2.1 规则 4、5.3.1 规则 3）。
        if (!TryExtractCarriedEntityId(packet, out var carriedId))
        {
            carriedId = boundEntityId;
        }

        if (carriedId != boundEntityId)
        {
            ReportIdentityMismatch(boundEntityId, carriedId);
            return false;
        }

        var verdict = Authorize(boundEntityId, packet.Kind);
        if (!verdict.Allowed)
        {
            return false;
        }

        return await SendEncodedAsync(packet);
    }

    private SyncSendVerdict Authorize(ulong entityId, SyncPacketKind kind)
    {
        try
        {
            var request = new SendRequestContext(entityId, kind, DateTimeOffset.UtcNow);
            return _authorizer.Authorize(in request);
        }
        catch (Exception ex)
        {
            // 内部授权异常 → 返回拒绝 + 告警，不抛异常扩散到发送通道与本地角色运行（spec 5.4.3 异常 2）。
            System.Diagnostics.Debug.WriteLine($"[GuardSyncSender] 授权异常: {ex.Message}");
            var violation = new SendViolationInfo(entityId, EntitySendCategory.Unqualified,
                SendRejectReason.InternalError, DateTimeOffset.UtcNow);
            _violationReporter.ReportViolation(in violation);
            return SyncSendVerdict.Deny(EntitySendCategory.Unqualified, SendRejectReason.InternalError);
        }
    }

    private void ReportIdentityMismatch(ulong expected, ulong actual)
    {
        var violation = new SendViolationInfo(actual, EntitySendCategory.Unqualified,
            SendRejectReason.IdentityMismatch, DateTimeOffset.UtcNow);
        _violationReporter.ReportViolation(in violation);
        System.Diagnostics.Debug.WriteLine($"[GuardSyncSender] 身份绑定不一致拒绝: Expected={expected}, Carried={actual}");
    }

    private async Task<bool> SendEncodedAsync(SyncPacket packet)
    {
        try
        {
            SyncPacketCodec.Encode(packet, out var frame, out var frameLength);
            try
            {
                var payload = new byte[frameLength];
                System.Buffer.BlockCopy(frame, 0, payload, 0, frameLength);

                var syncFrame = new SyncFrameMessage
                {
                    Frame = payload,
                    PacketKind = (byte)packet.Kind,
                    ProtocolVersion = packet.ProtocolVersion,
                };

                // 标记为已授权放行，避免被 NetworkManager 兜底守卫误判为旁路绕过（spec 5.4.3 异常 1）。
                if (_outboundGuard is OutboundSyncGuardImpl guard)
                {
                    guard.MarkApproved(syncFrame);
                }

                var result = await _sendAsync(syncFrame);

                if (_outboundGuard is OutboundSyncGuardImpl consumeGuard)
                {
                    consumeGuard.ConsumeApproved(syncFrame);
                }
                return result;
            }
            finally
            {
                SyncPacketCodec.ReturnFrame(frame);
            }
        }
        catch (Exception ex)
        {
            // 编码/发送异常：返回 false + 告警，不扩散（spec 5.4.3 异常 2）。
            System.Diagnostics.Debug.WriteLine($"[GuardSyncSender] 发送失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 从同步包提取其携带的发起实体 ID（InputPacket.CharacterId / CombatActionPacket.AttackerId）。
    /// 无法提取时返回 false。
    /// </summary>
    private static bool TryExtractCarriedEntityId(SyncPacket packet, out ulong entityId)
    {
        switch (packet)
        {
            case InputPacket input:
                entityId = input.CharacterId;
                return true;

            case CombatActionPacket combat:
                entityId = combat.AttackerId;
                return true;

            default:
                entityId = 0;
                return false;
        }
    }
}
