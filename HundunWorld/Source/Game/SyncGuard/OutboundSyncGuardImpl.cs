using System;
using System.Collections.Immutable;
using Horizon.Game.ECS.Arch.SyncGuard.Contracts;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;

namespace HundunWorld.Game.SyncGuard;

/// <summary>
/// <see cref="IOutboundSyncGuard"/> 的实现：注入 <see cref="NetworkManager"/> 的兜底守卫。
/// <para>
/// 实体同步类上行帧在真正入网前必须已通过 <see cref="IOutboundSyncAuthorizer"/> 授权；
/// 未授权帧被拦截并触发"旁路绕过"告警（spec 5.4.3 异常 1）。
/// 无法推导 <paramref name="senderEntityId"/> 时按无资格处理（拒绝）。
/// </para>
/// </summary>
public sealed class OutboundSyncGuardImpl : IOutboundSyncGuard
{
    private readonly IOutboundSyncAuthorizer _authorizer;
    private readonly ISendViolationReporter _violationReporter;

    /// <summary>当前由 GuardSyncSender 授权放行的包集合（线程安全，防并发旁路误拦）。</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<SyncFrameMessage, byte> _approvedFrames = new();

    /// <summary>连接层同步包豁免集合（spec 1.4 职责边界 / 5.5.1 规则 6）：
    /// 握手、重连恢复、AOI 订阅协商、快照重同步请求均为连接/会话层消息，
    /// 不依赖实体身份资格，直接放行，不纳入实体同步类上行管控、不触发旁路告警。
    /// 使用 ImmutableHashSet 显式表达不可变契约，静态初始化后只读访问线程安全。</summary>
    private static readonly ImmutableHashSet<byte> ConnectionLayerPacketKinds = ImmutableHashSet.Create<byte>(
        (byte)SyncPacketKind.Handshake,
        (byte)SyncPacketKind.ReconnectResume,
        (byte)SyncPacketKind.SubscriptionUpdate,
        (byte)SyncPacketKind.BaselineResyncRequest);

    public OutboundSyncGuardImpl(IOutboundSyncAuthorizer authorizer, ISendViolationReporter violationReporter)
    {
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        _violationReporter = violationReporter ?? throw new ArgumentNullException(nameof(violationReporter));
    }

    /// <summary>
    /// 标记一条由 GuardSyncSender 授权放行的帧（发送前调用）。
    /// </summary>
    public void MarkApproved(SyncFrameMessage frame)
    {
        if (frame != null)
        {
            _approvedFrames.TryAdd(frame, 0);
        }
    }

    /// <summary>
    /// 消费授权标记（发送完成后调用，清理缓存）。
    /// </summary>
    public void ConsumeApproved(SyncFrameMessage frame)
    {
        if (frame != null)
        {
            _approvedFrames.TryRemove(frame, out _);
        }
    }

    /// <inheritdoc />
    public bool TryApprove(SyncFrameMessage syncFrame, ulong senderEntityId)
    {
        // 连接层消息豁免（spec 1.4 职责边界、5.5.1 规则 6、5.6.1 规则 2）：
        // 握手/重连恢复/AOI 订阅协商/快照重同步请求均为连接/会话层消息，
        // 是身份确立的前置动作，不依赖实体同步类上行资格，直接放行。
        if (syncFrame != null && ConnectionLayerPacketKinds.Contains(syncFrame.PacketKind))
        {
            return true;
        }

        // 已由 GuardSyncSender 授权放行的帧直接通过。
        if (syncFrame != null && _approvedFrames.ContainsKey(syncFrame))
        {
            return true;
        }

        // 未授权帧：执行旁路检测（spec 5.4.3 异常 1）。
        ReportBypass(syncFrame, senderEntityId);
        return false;
    }

    private void ReportBypass(SyncFrameMessage syncFrame, ulong senderEntityId)
    {
        var category = senderEntityId != 0
            ? _authorizer.ClassifyEntity(senderEntityId)
            : EntitySendCategory.Unqualified;

        var violation = new SendViolationInfo(
            senderEntityId,
            category,
            SendRejectReason.BypassDetected,
            DateTimeOffset.UtcNow);

        _violationReporter.ReportViolation(in violation);
        System.Diagnostics.Debug.WriteLine($"[OutboundSyncGuard] 旁路绕过拦截: Sender={senderEntityId}, PacketKind={syncFrame?.PacketKind}");
    }
}