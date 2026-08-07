using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.ECS.Arch.SyncGuard.Contracts;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;
using HundunWorld.Game.Network;
using HundunWorld.Game.SyncGuard;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 受控发送（GuardSyncSender）、违规告警限频（SendViolationMonitor）、
/// 兜底守卫（OutboundSyncGuardImpl）单元测试：覆盖 spec 5.4.1 / 5.4.3 / 4.4.3。
/// </summary>
public class GuardSyncSenderTests : IDisposable
{
    private const ulong LocalPlayerId = 1001;
    private const ulong BoundPetId = 2002;

    public GuardSyncSenderTests()
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

    private sealed class FakeMetricsSink
    {
        public int SendCount;
        public readonly List<SyncFrameMessage> Sent = new();
    }

    private static (GuardSyncSender Sender, FakeReporter Reporter, FakeMetricsSink Sink, OutboundSyncGuardImpl Guard, IOutboundSyncAuthorizer Authorizer) Build(
        bool localEligible = true,
        bool registerBinding = true)
    {
        var registry = new BindingRelationshipRegistry();
        var eligibility = new LocalSendEligibilityState(registry);
        var reporter = new FakeReporter();
        var sink = new FakeMetricsSink();

        if (localEligible)
        {
            eligibility.OnConnectionChanged(true);
            eligibility.OnHandshakeChanged(true);
            eligibility.OnLocalIdentityChanged(true);
        }

        if (registerBinding)
        {
            registry.RegisterBinding(BoundPetId, LocalPlayerId, BindingType.Pet);
        }

        var authorizer = new OutboundSyncAuthorizer(
            registry, eligibility, reporter,
            isLocalPlayerEntity: id => id == LocalPlayerId,
            getLocalPlayerEntityId: () => LocalPlayerId,
            getLocalPlayerEntityCount: () => 1);

        var guard = new OutboundSyncGuardImpl(authorizer, reporter);
        var sender = new GuardSyncSender(
            authorizer, reporter,
            sendAsync: frame => { sink.SendCount++; sink.Sent.Add(frame); return Task.FromResult(true); },
            outboundGuard: guard);

        return (sender, reporter, sink, guard, authorizer);
    }

    private static InputPacket InputPacket(ulong characterId) => new()
    {
        ClientTick = 1,
        InputBits = 0,
        CharacterId = characterId,
    };

    private static CombatActionPacket CombatPacket(ulong attackerId) => new()
    {
        AttackerId = attackerId,
        TargetId = 0,
        ActionKind = CombatActionKind.NormalAttack,
        ClientTick = 1,
    };

    // ─── 本地角色放行调用发送 ───

    [Fact]
    public async Task SendLocalAsync_LocalPlayer_Sends()
    {
        var (sender, reporter, sink, _, _) = Build();
        var ok = await sender.SendLocalAsync(InputPacket(LocalPlayerId), LocalPlayerId);

        Assert.True(ok);
        Assert.Equal(1, sink.SendCount);
        Assert.Equal(0, reporter.ViolationCount);
    }

    // ─── 绑定实体放行调用发送 ───

    [Fact]
    public async Task SendBoundEntityAsync_ValidBinding_Sends()
    {
        var (sender, reporter, sink, _, _) = Build();
        var ok = await sender.SendBoundEntityAsync(CombatPacket(BoundPetId), BoundPetId);

        Assert.True(ok);
        Assert.Equal(1, sink.SendCount);
        Assert.Equal(0, reporter.ViolationCount);
    }

    // ─── 远程实体拒绝 → 零流量 ───

    [Fact]
    public async Task SendLocalAsync_RemoteId_DeniedZeroTraffic()
    {
        var (sender, reporter, sink, _, _) = Build();

        // 以远程角色 ID 冒充本地角色发送：消息携带身份与授权上下文不一致 → 拒绝。
        var ok = await sender.SendLocalAsync(InputPacket(9001), LocalPlayerId);

        Assert.False(ok);
        Assert.Equal(0, sink.SendCount);
        Assert.True(reporter.ViolationCount >= 1);
    }

    // ─── 绑定失效拒绝 → 零流量 ───

    [Fact]
    public async Task SendBoundEntityAsync_InvalidatedBinding_DeniedZeroTraffic()
    {
        var registry = new BindingRelationshipRegistry();
        var eligibility = new LocalSendEligibilityState(registry);
        eligibility.OnConnectionChanged(true);
        eligibility.OnHandshakeChanged(true);
        eligibility.OnLocalIdentityChanged(true);
        registry.RegisterBinding(BoundPetId, LocalPlayerId, BindingType.Pet);
        registry.InvalidateBinding(BoundPetId, BindingInvalidateReason.Unbound);

        var reporter = new FakeReporter();
        var sink = new FakeMetricsSink();
        var authorizer = new OutboundSyncAuthorizer(
            registry, eligibility, reporter,
            isLocalPlayerEntity: id => id == LocalPlayerId,
            getLocalPlayerEntityId: () => LocalPlayerId,
            getLocalPlayerEntityCount: () => 1);

        var sender = new GuardSyncSender(
            authorizer, reporter,
            sendAsync: frame => { sink.SendCount++; sink.Sent.Add(frame); return Task.FromResult(true); });

        var ok = await sender.SendBoundEntityAsync(CombatPacket(BoundPetId), BoundPetId);

        Assert.False(ok);
        Assert.Equal(0, sink.SendCount);
        Assert.True(reporter.ViolationCount >= 1);
    }

    // ─── 身份绑定不一致拒绝（绑定实体冒充本地角色身份） ───

    [Fact]
    public async Task SendLocalAsync_BoundEntityPretendsLocal_Rejected()
    {
        var (sender, reporter, sink, _, _) = Build();

        // 调用 SendLocalAsync 但 packet 携带绑定实体 ID：消息身份 ≠ 授权上下文身份。
        var ok = await sender.SendLocalAsync(InputPacket(BoundPetId), LocalPlayerId);

        Assert.False(ok);
        Assert.Equal(0, sink.SendCount);
        Assert.True(reporter.ViolationCount >= 1);
    }

    // ─── 告警限频：同一实体同一原因每秒最多 1 条日志 ───

    [Fact]
    public void SendViolationMonitor_RateLimitsLogs()
    {
        var monitor = new SendViolationMonitor { MetricsEnabled = true };
        var info = new SendViolationInfo(9001, EntitySendCategory.Unqualified,
            SendRejectReason.NotLocalPlayer, DateTimeOffset.UtcNow);

        // 高频连续上报（同一实体+同一原因）：内部限频（不直接断言日志条数，
        // 断言计数指标持续累加且不抛出异常）。
        for (int i = 0; i < 100; i++)
        {
            monitor.ReportViolation(in info);
        }

        Assert.Equal(100, monitor.TotalViolations);
        Assert.Equal(100, ClientSyncMetrics.OutboundViolationCount);
    }

    // ─── 兜底守卫：旁路绕过拦截 ───

    [Fact]
    public void OutboundSyncGuard_UnapprovedFrame_RejectsAsBypass()
    {
        var (_, reporter, _, guard, _) = Build();

        var frame = new SyncFrameMessage { Frame = new byte[8], PacketKind = (byte)SyncPacketKind.Input };
        var approved = guard.TryApprove(frame, 9001);

        Assert.False(approved);
        Assert.Equal(1, reporter.ViolationCount);
        Assert.Equal(SendRejectReason.BypassDetected, reporter.Violations[0].Reason);
    }

    [Fact]
    public void OutboundSyncGuard_ApprovedFrame_Allows()
    {
        var (_, reporter, _, guard, _) = Build();

        var frame = new SyncFrameMessage { Frame = new byte[8], PacketKind = (byte)SyncPacketKind.Input };
        guard.MarkApproved(frame);

        var approved = guard.TryApprove(frame, LocalPlayerId);

        Assert.True(approved);
        Assert.Equal(0, reporter.ViolationCount);

        guard.ConsumeApproved(frame);
    }

    // ─── 兜底守卫：无法推导身份 → 拒绝 ───

    [Fact]
    public void OutboundSyncGuard_UnknownSender_Rejects()
    {
        var (_, reporter, _, guard, _) = Build();

        var frame = new SyncFrameMessage { Frame = new byte[8], PacketKind = (byte)SyncPacketKind.Input };
        var approved = guard.TryApprove(frame, 0);

        Assert.False(approved);
        Assert.Equal(1, reporter.ViolationCount);
    }

    // ─── 守卫异常不扩散：授权器抛异常 → GuardSyncSender 返回 false ───

    [Fact]
    public async Task GuardSyncSender_AuthorizerThrows_ReturnsFalseNoThrow()
    {
        var registry = new BindingRelationshipRegistry();
        var eligibility = new LocalSendEligibilityState(registry);
        eligibility.OnConnectionChanged(true);
        eligibility.OnHandshakeChanged(true);
        eligibility.OnLocalIdentityChanged(true);

        var reporter = new FakeReporter();
        var authorizer = new ThrowingAuthorizer();

        var sender = new GuardSyncSender(
            authorizer, reporter,
            sendAsync: _ => Task.FromResult(true));

        var ok = await sender.SendLocalAsync(InputPacket(LocalPlayerId), LocalPlayerId);

        Assert.False(ok);
        Assert.True(reporter.ViolationCount >= 1);
    }

    private sealed class ThrowingAuthorizer : IOutboundSyncAuthorizer
    {
        public SyncSendVerdict Authorize(in SendRequestContext request) =>
            throw new InvalidOperationException("判定内部异常");

        public EntitySendCategory ClassifyEntity(ulong entityId) =>
            throw new InvalidOperationException("分类内部异常");
    }
}