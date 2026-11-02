using System;
using System.Collections.Generic;
using Horizon.Game.Message.Sync;
using Horizon.Game.Sim;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// SimWorld 翻越权威状态机回归：受理条件（起点贴合/冷却/不叠加/限值）、
/// 逐帧位置与共享曲线一致、结束时落点与朝向、期间不产生 Correction、积压输入被消费并回 ack。
/// <para>
/// 这条链是客户端预测与服务端权威共用的唯一推进点（DS 与 ZoneShardGrain 都走 SimWorld），
/// 所以判据必须落在 SimWorld 本身，而不是某个宿主层的包装上。
/// </para>
/// </summary>
public class TraversalWorldTests
{
    private const float Dt = 1f / 60f;
    private const ulong EntityId = 42UL;

    private static SimWorld NewWorld(float cooldownSeconds = 0.35f)
        => new SimWorld(new MovementValidator.Options
        {
            TickDtSeconds = Dt,
            Traversal = new TraversalLimits { CooldownSeconds = cooldownSeconds },
        });

    private static SimWorldEntity Spawn(SimWorld world, float x = 0f, float y = 0f, float z = 0f)
    {
        world.AddOrUpdateEntity(EntityId, x, y, z, 6f);
        var e = world.GetEntity(EntityId);
        Assert.NotNull(e);
        return e!;
    }

    private static TraversalPlan Plan(float sx = 0f, float sy = 0f, float sz = 0f)
        => new TraversalPlan
        {
            StartX = sx,
            StartY = sy,
            StartZ = sz,
            EndX = sx + 80f,
            EndY = sy + 60f,
            EndZ = sz + 90f,
            Duration = 0.6f,
            ArcHeight = 15f,
            EndYaw = 0.5f,
        };

    [Fact]
    public void TryBeginTraversal_ValidPlan_Accepted()
    {
        var world = NewWorld();
        Spawn(world);
        Assert.Equal(TraversalRejectReason.None, world.TryBeginTraversal(EntityId, Plan(), 100));
        Assert.True(world.IsTraversalActive(EntityId));
    }

    [Fact]
    public void TryBeginTraversal_UnknownEntity_Rejected()
    {
        var world = NewWorld();
        Assert.Equal(TraversalRejectReason.StartTooFar, world.TryBeginTraversal(999UL, Plan(), 100));
    }

    [Fact]
    public void TryBeginTraversal_WhileActive_RejectedAsAlreadyActive()
    {
        var world = NewWorld();
        Spawn(world);
        Assert.Equal(TraversalRejectReason.None, world.TryBeginTraversal(EntityId, Plan(), 100));
        Assert.Equal(TraversalRejectReason.AlreadyActive, world.TryBeginTraversal(EntityId, Plan(), 101));
    }

    [Fact]
    public void TryBeginTraversal_StartFarFromAuthority_Rejected()
    {
        var world = NewWorld();
        Spawn(world);
        // 权威位置在原点，计划从 1.5m 外开始。
        Assert.Equal(TraversalRejectReason.StartTooFar,
            world.TryBeginTraversal(EntityId, Plan(sx: 150f), 100));
        Assert.False(world.IsTraversalActive(EntityId));
    }

    [Fact]
    public void TryBeginTraversal_AfterFinish_CooldownThenAccepted()
    {
        const float cooldown = 0.35f;
        var world = NewWorld(cooldown);
        var e = Spawn(world);
        Assert.Equal(TraversalRejectReason.None, world.TryBeginTraversal(EntityId, Plan(), 100));

        long tick = 100;
        while (world.IsTraversalActive(EntityId) && tick < 200)
            world.Step(++tick);

        Assert.False(world.IsTraversalActive(EntityId));
        var endTick = e.LastTraversalEndTick;

        // 冷却内拒绝。
        Assert.Equal(TraversalRejectReason.Cooldown, world.TryBeginTraversal(EntityId, Plan(e.X, e.Y, e.Z), endTick + 1));

        // 冷却后（ceil(0.35 / (1/60)) = 21 tick）可再次受理。
        var afterCooldown = endTick + (long)MathF.Ceiling(cooldown / Dt);
        Assert.Equal(TraversalRejectReason.None, world.TryBeginTraversal(EntityId, Plan(e.X, e.Y, e.Z), afterCooldown));
    }

    [Fact]
    public void Step_AdvancesAlongSharedCurve_AndLandsExactly()
    {
        var world = NewWorld();
        var e = Spawn(world);
        var plan = Plan();
        Assert.Equal(TraversalRejectReason.None, world.TryBeginTraversal(EntityId, plan, 100));

        float expectedElapsed = 0f;
        long tick = 100;
        var seen = new List<(float X, float Y, float Z)>();
        while (world.IsTraversalActive(EntityId) && tick < 200)
        {
            world.Step(++tick);
            expectedElapsed += Dt;
            var expected = TraversalMath.Sample(plan, expectedElapsed);
            seen.Add((e.X, e.Y, e.Z));
            Assert.Equal(expected.X, e.X, 5);
            Assert.Equal(expected.Y, e.Y, 5);
            // 最后一步会显式落点，采样在 t≥1 时本来就等于终点，因此同一判据也成立。
            Assert.Equal(expected.Z, e.Z, 5);
            Assert.Equal(0f, e.Vz);
            Assert.True(e.IsGrounded);
            Assert.Equal(0, e.JumpCount);
        }

        // 终点必须逐位等于计划终点（不是"接近"）。
        Assert.Equal(plan.EndX, e.X);
        Assert.Equal(plan.EndY, e.Y);
        Assert.Equal(plan.EndZ, e.Z);
        Assert.Equal(plan.EndYaw, e.Yaw);
        Assert.True(seen.Count >= 30, $"翻越步数异常少：{seen.Count}");
    }

    [Fact]
    public void Step_DuringTraversal_ProducesNoCorrection_AndConsumesInputs()
    {
        var world = NewWorld();
        var e = Spawn(world);
        Assert.Equal(TraversalRejectReason.None, world.TryBeginTraversal(EntityId, Plan(), 100));

        // 翻越期间客户端照常发输入（含向前跑）：不得改变曲线位置，也不该被判漂移。
        world.SubmitInput(EntityId, new InputPacket
        {
            CharacterId = EntityId,
            ClientTick = 7,
            MoveX = 1f,
            MoveY = 1f,
            MaxSpeed = 6f,
            PredictedEndX = 0.5f,
            PredictedEndY = 0.5f,
            PredictedEndZ = 0.5f,
        }, 0.5f, 0.5f, 0.5f);

        var steps = world.Step(101);
        var step = steps[0];
        Assert.Null(step.Correction);
        Assert.Equal(7L, e.LastProcessedClientTick);
        Assert.True(step.HadInput);
        Assert.Equal(7L, step.LastProcessedClientTick);
        Assert.Equal(0, e.PendingInputs.Count);
        // 位置仍在曲线起点附近的第一个采样点，而不是被 MoveX=1 拉走 0.1m。
        var expected = TraversalMath.Sample(Plan(), Dt);
        Assert.Equal(expected.X, e.X, 5);
        Assert.Equal(expected.Y, e.Y, 5);
    }

    [Fact]
    public void Step_AfterTraversal_ResumesNormalValidation()
    {
        var world = NewWorld();
        var e = Spawn(world);
        Assert.Equal(TraversalRejectReason.None, world.TryBeginTraversal(EntityId, Plan(), 100));

        long tick = 100;
        while (world.IsTraversalActive(EntityId) && tick < 200)
            world.Step(++tick);

        // 结束后普通输入必须照常推进（不是被翻越分支永久卡住）。
        var startX = e.X;
        world.SubmitInput(EntityId, new InputPacket
        {
            CharacterId = EntityId,
            ClientTick = 20,
            MoveX = 1f,
            MoveY = 0f,
            MaxSpeed = 6f,
            PredictedEndX = startX + 6f * Dt,
            PredictedEndY = e.Y,
            PredictedEndZ = e.Z,
        }, startX + 6f * Dt, e.Y, e.Z);

        world.Step(++tick);
        Assert.True(e.X > startX, $"翻越结束后普通移动没有生效：{startX} -> {e.X}");
        Assert.False(e.TraversalActive);
    }

    [Fact]
    public void TryBeginTraversal_InvalidPlan_RejectedWithSameReasonsAsMath()
    {
        var world = NewWorld();
        Spawn(world);

        var tooTall = Plan();
        tooTall = new TraversalPlan
        {
            StartX = tooTall.StartX, StartY = tooTall.StartY, StartZ = tooTall.StartZ,
            EndX = tooTall.EndX, EndY = tooTall.EndY, EndZ = tooTall.StartZ + 350f,
            Duration = tooTall.Duration, ArcHeight = tooTall.ArcHeight, EndYaw = tooTall.EndYaw,
        };
        Assert.Equal(TraversalRejectReason.HeightOutOfRange, world.TryBeginTraversal(EntityId, tooTall, 100));

        var tooSlow = new TraversalPlan
        {
            StartX = 0f, StartY = 0f, StartZ = 0f,
            EndX = 0.8f, EndY = 0.6f, EndZ = 0.9f,
            Duration = 5f, ArcHeight = 0.1f, EndYaw = 0f,
        };
        Assert.Equal(TraversalRejectReason.InvalidDuration, world.TryBeginTraversal(EntityId, tooSlow, 100));
        Assert.False(world.IsTraversalActive(EntityId));
    }
}
