using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Sync;
using Horizon.Game.Sim;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// DS 权威翻越闭环：请求 → 裁决回执 → 权威曲线推进 → 落点 → 冷却，以及拒绝档位。
/// <para>
/// DS 与 ZoneShardGrain 共用 <see cref="SimWorld.TryBeginTraversal"/>；
/// 这里钉住的是"宿主层真的把裁决结果发给客户端、并且真的推进了权威位置"，
/// 避免出现"回执说受理、世界没动"的分裂。
/// </para>
/// </summary>
public class DsTraversalTests
{
    private const long SessionId = 3001L;
    private const float Dt = 1f / 60f;

    private static DsWorldSession NewWorld()
    {
        var world = new DsWorldSession(new DsWorldSessionOptions
        {
            ShardId = 0,
            TicksPerSecond = 60,
            SnapshotIntervalTicks = 60,
            PlayerDefaultMaxHp = 100,
        });
        Assert.True(world.AddPlayer(SessionId, (ulong)SessionId, 0f, 0f, 0f, out var reason), reason);
        return world;
    }

    private static TraversalRequestPacket Request(in TraversalPlan plan, long clientTick = 1L)
        => new TraversalRequestPacket
        {
            CharacterId = (ulong)SessionId,
            ClientTick = clientTick,
            StartX = plan.StartX,
            StartY = plan.StartY,
            StartZ = plan.StartZ,
            EndX = plan.EndX,
            EndY = plan.EndY,
            EndZ = plan.EndZ,
            Duration = plan.Duration,
            ArcHeight = plan.ArcHeight,
            EndYaw = plan.EndYaw,
            CurveVersion = TraversalMath.Version,
        };

    private static TraversalPlan Plan()
        => new TraversalPlan
        {
            StartX = 0f,
            StartY = 0f,
            StartZ = 0f,
            EndX = 80f,
            EndY = 60f,
            EndZ = 90f,
            Duration = 0.6f,
            ArcHeight = 12f,
            EndYaw = 0.5f,
        };

    [Fact]
    public void Request_Accepted_AdvancesAuthorityAlongCurve_AndQueuesResult()
    {
        var world = NewWorld();
        var plan = Plan();

        var reason = world.HandleTraversalRequestFromClient(SessionId, Request(plan));
        Assert.Equal(TraversalRejectReason.None, reason);
        Assert.Equal(1, world.TraversalRequestsAccepted);
        Assert.Equal(0, world.TraversalRequestsRejected);

        var entity = world.GetAuthoritativeEntity(SessionId);
        Assert.NotNull(entity);
        Assert.True(entity!.TraversalActive);

        // 回执必须进本 tick 的下行事件；只改世界不回复执会让客户端一直猜。
        var firstTick = world.Tick();
        var result = firstTick.Events
            .Select(e => e.Packet)
            .OfType<TraversalResultPacket>()
            .SingleOrDefault();
        Assert.NotNull(result);
        Assert.True(result!.Accepted);
        Assert.Equal(0, result.Status);
        Assert.Equal((ulong)SessionId, result.CharacterId);
        Assert.Equal(1L, result.ClientTick);

        // 第一拍同时排空事件并推进了一次世界：曲线已走了 1 个 dt，对账从下一拍继续。
        float expectedElapsed = Dt;
        int ticks = 1;
        while (entity.TraversalActive && ticks < 120)
        {
            world.Tick();
            ticks++;
            expectedElapsed += Dt;
            var expected = TraversalMath.Sample(plan, expectedElapsed);
            Assert.Equal(expected.X, entity.X, 5);
            Assert.Equal(expected.Y, entity.Y, 5);
            Assert.Equal(expected.Z, entity.Z, 5);
            Assert.Equal(0f, entity.Vz);
        }

        Assert.False(entity.TraversalActive);
        Assert.Equal(plan.EndX, entity.X);
        Assert.Equal(plan.EndY, entity.Y);
        Assert.Equal(plan.EndZ, entity.Z);
        Assert.Equal(plan.EndYaw, entity.Yaw);
    }

    [Fact]
    public void Request_UnknownSession_RejectedAsAuthorityUnavailable()
    {
        var world = NewWorld();
        var packet = Request(Plan());
        packet.CharacterId = 999999UL;

        var reason = world.HandleTraversalRequestFromClient(999999L, packet);
        Assert.Equal(TraversalRejectReason.AuthorityUnavailable, reason);
        Assert.Equal(1, world.TraversalRequestsRejected);

        var result = world.Tick().Events
            .Select(e => e.Packet)
            .OfType<TraversalResultPacket>()
            .SingleOrDefault();
        Assert.NotNull(result);
        Assert.False(result!.Accepted);
        Assert.Equal((byte)TraversalRejectReason.AuthorityUnavailable, result.Status);
    }

    [Fact]
    public void Request_InvalidPlan_RejectedWithReason_AndWorldStaysPut()
    {
        var world = NewWorld();
        var tooTall = new TraversalPlan
        {
            StartX = 0f, StartY = 0f, StartZ = 0f,
            EndX = 80f, EndY = 60f, EndZ = 350f,
            Duration = 0.6f, ArcHeight = 0.1f, EndYaw = 0f,
        };

        var reason = world.HandleTraversalRequestFromClient(SessionId, Request(tooTall));
        Assert.Equal(TraversalRejectReason.HeightOutOfRange, reason);

        var entity = world.GetAuthoritativeEntity(SessionId);
        Assert.False(entity!.TraversalActive);
        Assert.Equal(0f, entity.X);

        var result = world.Tick().Events
            .Select(e => e.Packet)
            .OfType<TraversalResultPacket>()
            .SingleOrDefault();
        Assert.NotNull(result);
        Assert.False(result!.Accepted);
        Assert.Equal((byte)TraversalRejectReason.HeightOutOfRange, result.Status);
    }

    [Fact]
    public void Request_WrongCurveVersion_Rejected()
    {
        var world = NewWorld();
        var packet = Request(Plan());
        packet.CurveVersion = TraversalMath.Version + 1;

        var reason = world.HandleTraversalRequestFromClient(SessionId, packet);
        Assert.Equal(TraversalRejectReason.InvalidNumbers, reason);
        Assert.False(world.GetAuthoritativeEntity(SessionId)!.TraversalActive);
    }

    [Fact]
    public void Request_WhileActive_OrDuringCooldown_Rejected()
    {
        var world = NewWorld();
        var plan = Plan();
        Assert.Equal(TraversalRejectReason.None, world.HandleTraversalRequestFromClient(SessionId, Request(plan)));
        Assert.Equal(TraversalRejectReason.AlreadyActive,
            world.HandleTraversalRequestFromClient(SessionId, Request(plan, 2L)));

        var entity = world.GetAuthoritativeEntity(SessionId)!;
        int ticks = 0;
        while (entity.TraversalActive && ticks < 120)
        {
            world.Tick();
            ticks++;
        }
        Assert.False(entity.TraversalActive);

        // 刚结束：冷却内再提必拒。
        var immediate = new TraversalPlan
        {
            StartX = entity.X, StartY = entity.Y, StartZ = entity.Z,
            EndX = entity.X + 80f, EndY = entity.Y + 60f, EndZ = entity.Z + 90f,
            Duration = 0.6f, ArcHeight = 0.1f, EndYaw = 0f,
        };
        Assert.Equal(TraversalRejectReason.Cooldown,
            world.HandleTraversalRequestFromClient(SessionId, Request(immediate, 3L)));
    }
}
