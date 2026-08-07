using System;
using System.Collections.Generic;
using Horizon.Game.ECS.Arch.SyncGuard.Contracts;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;
using HundunWorld.Game.Network;
using HundunWorld.Game.SyncGuard;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 连接层豁免（OutboundSyncGuardImpl 对握手/重连恢复/订阅更新/快照重同步等连接层消息直接放行）单元测试：
/// 覆盖 spec 1.4 职责边界、5.5.1 规则 6 验收 a/b。
/// </summary>
public class OutboundSyncGuardExemptionTests : IDisposable
{
    private const ulong LocalPlayerId = 1001;

    public OutboundSyncGuardExemptionTests()
    {
        ClientSyncMetrics.Reset();
    }

    public void Dispose()
    {
        ClientSyncMetrics.Reset();
    }

    private sealed class FakeReporter : ISendViolationReporter
    {
        public int ViolationCount;
        public readonly List<SendViolationInfo> Violations = new();

        public void ReportViolation(in SendViolationInfo violation)
        {
            ViolationCount++;
            Violations.Add(violation);
        }
    }

    private static OutboundSyncGuardImpl Build(out FakeReporter reporter)
    {
        reporter = new FakeReporter();
        var authorizer = new TestAuthorizer();
        return new OutboundSyncGuardImpl(authorizer, reporter);
    }

    private sealed class TestAuthorizer : IOutboundSyncAuthorizer
    {
        public SyncSendVerdict Authorize(in SendRequestContext request) =>
            SyncSendVerdict.Allow(EntitySendCategory.LocalPlayer);

        public EntitySendCategory ClassifyEntity(ulong entityId) => EntitySendCategory.LocalPlayer;
    }

    private static SyncFrameMessage ReconnectResumeFrame() => new()
    {
        Frame = new byte[8],
        PacketKind = (byte)SyncPacketKind.ReconnectResume,
    };

    private static SyncFrameMessage InputFrame() => new()
    {
        Frame = new byte[8],
        PacketKind = (byte)SyncPacketKind.Input,
    };

    private static SyncFrameMessage HandshakeFrame() => new()
    {
        Frame = new byte[8],
        PacketKind = (byte)SyncPacketKind.Handshake,
    };

    private static SyncFrameMessage SubscriptionUpdateFrame() => new()
    {
        Frame = new byte[8],
        PacketKind = (byte)SyncPacketKind.SubscriptionUpdate,
    };

    private static SyncFrameMessage BaselineResyncRequestFrame() => new()
    {
        Frame = new byte[8],
        PacketKind = (byte)SyncPacketKind.BaselineResyncRequest,
    };

    [Fact]
    public void ReconnectResumeFrame_Exempted_NoBypassAlert()
    {
        // 验收 a：重连恢复协商消息直接放行，不触发旁路告警（spec 5.5.1 规则 6）。
        var guard = Build(out var reporter);

        var approved = guard.TryApprove(ReconnectResumeFrame(), LocalPlayerId);

        Assert.True(approved);
        Assert.Equal(0, reporter.ViolationCount);
    }

    [Fact]
    public void ReconnectResumeFrame_Unmarked_StillExempted()
    {
        // 即使未 MarkApproved，ReconnectResume 仍被豁免（连接层消息）。
        var guard = Build(out var reporter);

        var approved = guard.TryApprove(ReconnectResumeFrame(), 0);

        Assert.True(approved);
        Assert.Equal(0, reporter.ViolationCount);
    }

    [Fact]
    public void HandshakeFrame_Unmarked_Exempted_NoBypassAlert()
    {
        // 连接层握手包直接放行，不触发旁路告警（修复 sync-handshake 拦截根因）。
        var guard = Build(out var reporter);

        var approved = guard.TryApprove(HandshakeFrame(), 0);

        Assert.True(approved);
        Assert.Equal(0, reporter.ViolationCount);
    }

    [Fact]
    public void SubscriptionUpdateFrame_Unmarked_Exempted()
    {
        // AOI 订阅协商为连接层消息，直接放行。
        var guard = Build(out var reporter);

        var approved = guard.TryApprove(SubscriptionUpdateFrame(), 0);

        Assert.True(approved);
        Assert.Equal(0, reporter.ViolationCount);
    }

    [Fact]
    public void BaselineResyncRequestFrame_Unmarked_Exempted()
    {
        // 快照重同步请求为连接层控制消息，直接放行。
        var guard = Build(out var reporter);

        var approved = guard.TryApprove(BaselineResyncRequestFrame(), 0);

        Assert.True(approved);
        Assert.Equal(0, reporter.ViolationCount);
    }

    [Fact]
    public void HandshakeFrame_Marked_StillExempted()
    {
        // 豁免判定优先于 MarkApproved：已标记的握手包仍走豁免分支（防止未来调整判定顺序时回归）。
        var guard = Build(out var reporter);
        var frame = HandshakeFrame();
        guard.MarkApproved(frame);

        var approved = guard.TryApprove(frame, 0);

        Assert.True(approved);
        Assert.Equal(0, reporter.ViolationCount);
    }

    [Fact]
    public void HandshakeFrame_NonZeroSender_Exempted()
    {
        // 豁免判定只看 PacketKind，不依赖 senderEntityId：非 0 发送者的握手包仍放行。
        var guard = Build(out var reporter);

        var approved = guard.TryApprove(HandshakeFrame(), LocalPlayerId);

        Assert.True(approved);
        Assert.Equal(0, reporter.ViolationCount);
    }

    [Fact]
    public void InputFrame_Unmarked_StillBlockedWithBypassAlert()
    {
        // 验收 b：其余实体同步类上行帧仍走既有守卫（不弱化）。
        var guard = Build(out var reporter);

        var approved = guard.TryApprove(InputFrame(), LocalPlayerId);

        Assert.False(approved);
        Assert.Equal(1, reporter.ViolationCount);
        Assert.Equal(SendRejectReason.BypassDetected, reporter.Violations[0].Reason);
    }

    [Fact]
    public void NullFrame_BlockedAsBypass()
    {
        var guard = Build(out var reporter);

        var approved = guard.TryApprove(null, LocalPlayerId);

        Assert.False(approved);
        Assert.Equal(1, reporter.ViolationCount);
    }
}