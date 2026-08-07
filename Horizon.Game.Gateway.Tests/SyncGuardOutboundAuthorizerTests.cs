using System;
using Horizon.Game.ECS.Arch.SyncGuard.Contracts;
using Horizon.Game.Message.Sync;
using HundunWorld.Game.SyncGuard;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 资格判定核心（OutboundSyncAuthorizer）单元测试：
/// 覆盖 spec 5.1.1 / 5.2.1 / 5.3.1 全部验收条件。
/// </summary>
public class OutboundSyncAuthorizerTests
{
    private const ulong LocalPlayerId = 1001;
    private const ulong BoundSummonId = 2001;
    private const ulong BoundPetId = 2002;
    private const ulong RemotePlayerId = 3001;
    private const ulong NpcId = 3002;
    private const ulong MonsterId = 3003;
    private const ulong FakeBindingId = 4001;

    private static SendRequestContext Request(ulong entityId) =>
        new(entityId, SyncPacketKind.Input, DateTimeOffset.UtcNow);

    private sealed class FakeReporter : ISendViolationReporter
    {
        public int ViolationCount;
        public SendViolationInfo Last;

        public void ReportViolation(in SendViolationInfo violation)
        {
            ViolationCount++;
            Last = violation;
        }
    }

    /// <summary>构造一个已具备本地资格、含若干绑定关系的授权器。</summary>
    private static (OutboundSyncAuthorizer Authorizer, FakeReporter Reporter, BindingRelationshipRegistry Registry, LocalSendEligibilityState Eligibility) Build(
        bool localEligible = true,
        Action<LocalSendEligibilityState, BindingRelationshipRegistry>? setup = null)
    {
        var registry = new BindingRelationshipRegistry();
        var eligibility = new LocalSendEligibilityState(registry);
        var reporter = new FakeReporter();

        // 构造资格状态：模拟"连接 + 握手 + 身份确立"链路。
        if (localEligible)
        {
            eligibility.OnConnectionChanged(true);          // ConnectedHandshakePending
            eligibility.OnHandshakeChanged(true);           // Established
            eligibility.OnLocalIdentityChanged(true);       // Established（幂等）
        }

        // 登记绑定关系（需在 Established 之后登记，使资格联动派生有效）。
        registry.RegisterBinding(BoundSummonId, LocalPlayerId, BindingType.Summon);
        registry.RegisterBinding(BoundPetId, LocalPlayerId, BindingType.Pet);

        var authorizer = new OutboundSyncAuthorizer(
            registry,
            eligibility,
            reporter,
            isLocalPlayerEntity: id => id == LocalPlayerId,
            getLocalPlayerEntityId: () => LocalPlayerId,
            getLocalPlayerEntityCount: () => 1);

        setup?.Invoke(eligibility, registry);
        return (authorizer, reporter, registry, eligibility);
    }

    // ─── 本地角色放行 ───

    [Fact]
    public void Authorize_LocalPlayer_Allows()
    {
        var (authorizer, reporter, _, _) = Build();
        var verdict = authorizer.Authorize(Request(LocalPlayerId));

        Assert.True(verdict.Allowed);
        Assert.Equal(EntitySendCategory.LocalPlayer, verdict.Category);
        Assert.Equal(0, reporter.ViolationCount);
    }

    // ─── 远程角色 / NPC / 怪物拒绝 ───

    [Theory]
    [InlineData(RemotePlayerId)]
    [InlineData(NpcId)]
    [InlineData(MonsterId)]
    public void Authorize_RemoteNpcMonster_Denies(ulong entityId)
    {
        var (authorizer, reporter, _, _) = Build();
        var verdict = authorizer.Authorize(Request(entityId));

        Assert.False(verdict.Allowed);
        Assert.Equal(EntitySendCategory.Unqualified, verdict.Category);
        Assert.Equal(SendRejectReason.NotLocalPlayer, verdict.Reason);
        Assert.Equal(1, reporter.ViolationCount);
    }

    // ─── 绑定实体（唤物/宠物）放行 ───

    [Fact]
    public void Authorize_BoundSummon_Allows()
    {
        var (authorizer, reporter, _, _) = Build();
        var verdict = authorizer.Authorize(Request(BoundSummonId));

        Assert.True(verdict.Allowed);
        Assert.Equal(EntitySendCategory.BoundEntity, verdict.Category);
        Assert.Equal(0, reporter.ViolationCount);
    }

    [Fact]
    public void Authorize_BoundPet_Allows()
    {
        var (authorizer, reporter, _, _) = Build();
        var verdict = authorizer.Authorize(Request(BoundPetId));

        Assert.True(verdict.Allowed);
        Assert.Equal(EntitySendCategory.BoundEntity, verdict.Category);
        Assert.Equal(0, reporter.ViolationCount);
    }

    // ─── 绑定失效拒绝 ───

    [Fact]
    public void Authorize_InvalidatedBinding_Denies()
    {
        var (authorizer, reporter, registry, _) = Build();
        registry.InvalidateBinding(BoundSummonId, BindingInvalidateReason.Dismissed);
        registry.InvalidateBinding(BoundPetId, BindingInvalidateReason.Died);

        var verdict = authorizer.Authorize(Request(BoundSummonId));

        Assert.False(verdict.Allowed);
        Assert.Equal(SendRejectReason.BindingInvalid, verdict.Reason);
        Assert.Equal(1, reporter.ViolationCount);
    }

    // ─── 身份冒充（绑定实体携带本地角色身份）拒绝 ───

    [Fact]
    public void Authorize_BoundEntityPretendingLocal_Denies()
    {
        var (authorizer, reporter, _, _) = Build();

        // 绑定实体被误标为本地玩家：isLocalPlayerEntity 返回 true 时按本地角色判定，
        // 但其 EntityId 与真实本地身份不一致 → LocalPlayerDuplicated。
        var badAuthorizer = new OutboundSyncAuthorizer(
            Build().Registry, Build().Eligibility, new FakeReporter(),
            isLocalPlayerEntity: id => id == BoundSummonId || id == LocalPlayerId,
            getLocalPlayerEntityId: () => LocalPlayerId,
            getLocalPlayerEntityCount: () => 1);

        var verdict = badAuthorizer.Authorize(Request(BoundSummonId));

        Assert.False(verdict.Allowed);
        Assert.Equal(SendRejectReason.LocalPlayerDuplicated, verdict.Reason);
    }

    // ─── 伪造/无效绑定拒绝 ───

    [Fact]
    public void Authorize_FakeBinding_Denies()
    {
        var (authorizer, reporter, _, _) = Build();
        var verdict = authorizer.Authorize(Request(FakeBindingId));

        Assert.False(verdict.Allowed);
        Assert.Equal(SendRejectReason.NotLocalPlayer, verdict.Reason);
    }

    // ─── 本地角色重复拒绝 ───

    [Fact]
    public void Authorize_DuplicatedLocalPlayer_Denies()
    {
        var (_, _, registry, eligibility) = Build();
        var reporter = new FakeReporter();

        var authorizer = new OutboundSyncAuthorizer(
            registry, eligibility, reporter,
            isLocalPlayerEntity: id => id == LocalPlayerId || id == BoundPetId,
            getLocalPlayerEntityId: () => LocalPlayerId,
            getLocalPlayerEntityCount: () => 2);

        // BoundPetId 被误标为第二个本地玩家：仅真实身份 LocalPlayerId 放行。
        var badVerdict = authorizer.Authorize(Request(BoundPetId));
        Assert.False(badVerdict.Allowed);
        Assert.Equal(SendRejectReason.LocalPlayerDuplicated, badVerdict.Reason);

        var goodVerdict = authorizer.Authorize(Request(LocalPlayerId));
        Assert.True(goodVerdict.Allowed);
    }

    // ─── 前置条件未满足拒绝 ───

    [Fact]
    public void Authorize_HandshakePending_Denies()
    {
        // 未握手（未 Established）→ 本地角色也拒绝。
        var (authorizer, reporter, _, _) = Build(localEligible: false);
        var verdict = authorizer.Authorize(Request(LocalPlayerId));

        Assert.False(verdict.Allowed);
        Assert.Equal(SendRejectReason.IdentityNotEstablished, verdict.Reason);
    }

    [Fact]
    public void Authorize_BoundEntityWithoutLocalEligibility_Denies()
    {
        var (authorizer, reporter, _, _) = Build(localEligible: false);
        var verdict = authorizer.Authorize(Request(BoundPetId));

        Assert.False(verdict.Allowed);
        Assert.Equal(SendRejectReason.IdentityNotEstablished, verdict.Reason);
    }

    // ─── 同一请求重复判定结论一致（确定性） ───

    [Fact]
    public void Authorize_Deterministic_RepeatedSameVerdict()
    {
        var (authorizer, _, _, _) = Build();

        var v1 = authorizer.Authorize(Request(BoundPetId));
        var v2 = authorizer.Authorize(Request(BoundPetId));

        Assert.Equal(v1.Allowed, v2.Allowed);
        Assert.Equal(v1.Category, v2.Category);
        Assert.Equal(v1.Reason, v2.Reason);
    }

    // ─── 分类（ClassifyEntity） ───

    [Fact]
    public void ClassifyEntity_ReturnsExpectedCategory()
    {
        var (authorizer, _, _, _) = Build();

        Assert.Equal(EntitySendCategory.LocalPlayer, authorizer.ClassifyEntity(LocalPlayerId));
        Assert.Equal(EntitySendCategory.BoundEntity, authorizer.ClassifyEntity(BoundSummonId));
        Assert.Equal(EntitySendCategory.BoundEntity, authorizer.ClassifyEntity(BoundPetId));
        Assert.Equal(EntitySendCategory.Unqualified, authorizer.ClassifyEntity(RemotePlayerId));
        Assert.Equal(EntitySendCategory.Unqualified, authorizer.ClassifyEntity(0));
    }

    // ─── 绑定归属非本地角色拒绝 ───

    [Fact]
    public void Authorize_BindingOwnedByRemote_Denies()
    {
        var (_, _, registry, eligibility) = Build();

        // 登记一条归属非本地角色的绑定（远程玩家名下的召唤物）。
        registry.RegisterBinding(FakeBindingId, RemotePlayerId, BindingType.Summon);

        var reporter = new FakeReporter();
        var authorizer = new OutboundSyncAuthorizer(
            registry, eligibility, reporter,
            isLocalPlayerEntity: id => id == LocalPlayerId,
            getLocalPlayerEntityId: () => LocalPlayerId,
            getLocalPlayerEntityCount: () => 1);

        var verdict = authorizer.Authorize(Request(FakeBindingId));

        Assert.False(verdict.Allowed);
        Assert.Equal(SendRejectReason.BindingNotOwnedByLocal, verdict.Reason);
        Assert.Equal(1, reporter.ViolationCount);
    }
}