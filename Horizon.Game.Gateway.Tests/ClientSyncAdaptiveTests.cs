using System;
using System.Reflection;
using System.Threading;
using Arch.Core;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Systems;
using Horizon.Game.Message.Sync.Components;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// Phase C4 验证：自适应插值延迟收敛 + Dead Reckoning 速度衰减。
/// </summary>
public class AdaptiveInterpolationTests : IDisposable
{
    public AdaptiveInterpolationTests()
    {
        // 重置自适应延迟静态字段
        ResetAdaptiveFields();
        SnapshotApplySystem.UseAdaptiveDelay = true;
    }

    public void Dispose()
    {
        ResetAdaptiveFields();
        SnapshotApplySystem.UseAdaptiveDelay = true;
    }

    private static void ResetAdaptiveFields()
    {
        var lockObj = typeof(SnapshotApplySystem)
            .GetField("_adaptiveLock", BindingFlags.NonPublic | BindingFlags.Static);
        var avgField = typeof(SnapshotApplySystem)
            .GetField("_adaptiveAvgInterval", BindingFlags.NonPublic | BindingFlags.Static);
        var jitterField = typeof(SnapshotApplySystem)
            .GetField("_adaptiveJitter", BindingFlags.NonPublic | BindingFlags.Static);
        var lastField = typeof(SnapshotApplySystem)
            .GetField("_adaptiveLastArrivalTimestamp", BindingFlags.NonPublic | BindingFlags.Static);

        avgField?.SetValue(null, 0f);
        jitterField?.SetValue(null, 0f);
        lastField?.SetValue(null, 0L);
    }

    // ─── 自适应延迟计算 ───

    [Fact]
    public void AdaptiveDelay_NoSamples_ReturnsFixedDelay()
    {
        SnapshotApplySystem.FixedInterpolationDelaySeconds = 0.1f;
        var delay = SnapshotApplySystem.AdaptiveInterpolationDelaySeconds;
        Assert.Equal(0.1f, delay, 0.001f);
    }

    [Fact]
    public void AdaptiveDelay_Disabled_ReturnsFixedDelay()
    {
        SnapshotApplySystem.UseAdaptiveDelay = false;
        SnapshotApplySystem.FixedInterpolationDelaySeconds = 0.15f;
        var delay = SnapshotApplySystem.AdaptiveInterpolationDelaySeconds;
        Assert.Equal(0.15f, delay, 0.001f);
    }

    [Fact]
    public void AdaptiveDelay_FixedInterval_ConvergesToInterval()
    {
        // 模拟固定 50ms 间隔的快照到达
        for (int i = 0; i < 20; i++)
        {
            SnapshotApplySystem.RecordSnapshotArrival();
            Thread.Sleep(50);
        }

        var delay = SnapshotApplySystem.AdaptiveInterpolationDelaySeconds;
        // 固定间隔 → jitter ≈ 0，delay ≈ avgInterval ≈ 50ms
        Assert.True(delay >= 0.04f && delay <= 0.08f,
            $"Delay should converge to ~50ms but was {delay * 1000:F1}ms");
    }

    [Fact]
    public void AdaptiveDelay_ClampedTo_Min50ms_Max200ms()
    {
        // 直接设置极端 avgInterval 值来测试 clamp
        var avgField = typeof(SnapshotApplySystem)
            .GetField("_adaptiveAvgInterval", BindingFlags.NonPublic | BindingFlags.Static);
        var jitterField = typeof(SnapshotApplySystem)
            .GetField("_adaptiveJitter", BindingFlags.NonPublic | BindingFlags.Static);

        // 极低间隔 → 应 clamp 到 50ms
        avgField?.SetValue(null, 0.01f); // 10ms
        jitterField?.SetValue(null, 0f);
        Assert.Equal(0.05f, SnapshotApplySystem.AdaptiveInterpolationDelaySeconds, 0.001f);

        // 极高间隔 → 应 clamp 到 200ms
        avgField?.SetValue(null, 0.5f); // 500ms
        jitterField?.SetValue(null, 0f);
        Assert.Equal(0.2f, SnapshotApplySystem.AdaptiveInterpolationDelaySeconds, 0.001f);
    }

    [Fact]
    public void AdaptiveDelay_HighJitter_IncreasesDelay()
    {
        var avgField = typeof(SnapshotApplySystem)
            .GetField("_adaptiveAvgInterval", BindingFlags.NonPublic | BindingFlags.Static);
        var jitterField = typeof(SnapshotApplySystem)
            .GetField("_adaptiveJitter", BindingFlags.NonPublic | BindingFlags.Static);

        // avg=50ms, jitter=30ms → target = 50 + 2*30 = 110ms
        avgField?.SetValue(null, 0.05f);
        jitterField?.SetValue(null, 0.03f);
        var delay = SnapshotApplySystem.AdaptiveInterpolationDelaySeconds;
        Assert.Equal(0.11f, delay, 0.001f);
    }
}

/// <summary>
/// Phase C4 验证：Dead Reckoning 速度衰减（200ms 后开始衰减，500ms 完全停止）。
/// </summary>
public class DeadReckoningDecayTests
{
    private World CreateWorldWithEntity(out Entity entity, float velX, float velY, float timeSinceSnapshot)
    {
        var world = World.Create();
        var interp = new InterpolatedTransformComponent
        {
            X = 0f, Y = 0f, Z = 0f,
            TargetX = 0f, TargetY = 0f, TargetZ = 0f,
            StartX = 0f, StartY = 0f, StartZ = 0f,
            Alpha = 1f, // 已到达目标，进入 dead reckoning
            TimeSinceLastSnapshot = timeSinceSnapshot,
        };
        var movement = new MovementStateAuthComponent
        {
            VelocityXZ_X = velX,
            VelocityXZ_Y = velY,
        };
        entity = world.Create(interp, movement);
        return world;
    }

    [Fact]
    public void DeadReckoning_Within200ms_FullSpeed()
    {
        // TimeSinceLastSnapshot = 0.1s (< 0.2s)，速度不衰减
        var world = CreateWorldWithEntity(out var entity, velX: 5f, velY: 0f, timeSinceSnapshot: 0.1f);
        var system = new InterpolationSystem { EnableDeadReckoningDecay = true };

        system.Update(world, TimeSpan.FromSeconds(0.016)); // 1 frame

        var interp = world.Get<InterpolatedTransformComponent>(entity);
        // 应以全速 5 m/s 推进 X：delta = 5 * 0.016 = 0.08
        Assert.True(interp.X > 0.07f, $"X should advance ~0.08 but was {interp.X:F4}");
        Assert.True(interp.X < 0.09f, $"X should advance ~0.08 but was {interp.X:F4}");
        World.Destroy(world);
    }

    [Fact]
    public void DeadReckoning_At350ms_HalfDecay()
    {
        // TimeSinceLastSnapshot = 0.35s → decayFactor = 1 - (0.35-0.2)/(0.5-0.2) = 1 - 0.5 = 0.5
        var world = CreateWorldWithEntity(out var entity, velX: 10f, velY: 0f, timeSinceSnapshot: 0.35f);
        var system = new InterpolationSystem { EnableDeadReckoningDecay = true };

        system.Update(world, TimeSpan.FromSeconds(0.016));

        var interp = world.Get<InterpolatedTransformComponent>(entity);
        // 速度衰减到 50%：delta = 10 * 0.016 * 0.5 = 0.08
        Assert.True(interp.X > 0.06f && interp.X < 0.10f,
            $"X should advance ~0.08 (50% decay) but was {interp.X:F4}");
        World.Destroy(world);
    }

    [Fact]
    public void DeadReckoning_Beyond500ms_NoMovement()
    {
        // TimeSinceLastSnapshot = 0.6s (> 0.5s)，速度完全衰减到 0
        var world = CreateWorldWithEntity(out var entity, velX: 10f, velY: 5f, timeSinceSnapshot: 0.6f);
        var system = new InterpolationSystem { EnableDeadReckoningDecay = true };

        system.Update(world, TimeSpan.FromSeconds(0.016));

        var interp = world.Get<InterpolatedTransformComponent>(entity);
        Assert.Equal(0f, interp.X, 0.001f);
        Assert.Equal(0f, interp.Z, 0.001f);
        World.Destroy(world);
    }

    [Fact]
    public void DeadReckoning_DecayDisabled_FullSpeedAlways()
    {
        // 禁用衰减 → 即使超过 500ms 也全速推进
        var world = CreateWorldWithEntity(out var entity, velX: 10f, velY: 0f, timeSinceSnapshot: 1.0f);
        var system = new InterpolationSystem { EnableDeadReckoningDecay = false };

        system.Update(world, TimeSpan.FromSeconds(0.016));

        var interp = world.Get<InterpolatedTransformComponent>(entity);
        // 全速：delta = 10 * 0.016 = 0.16
        Assert.True(interp.X > 0.15f, $"X should advance ~0.16 (no decay) but was {interp.X:F4}");
        World.Destroy(world);
    }

    [Fact]
    public void DeadReckoning_NewSnapshot_ResetsTimeAndRestoresSpeed()
    {
        // 模拟：先衰减到 0，然后新快照重置 TimeSinceLastSnapshot
        var world = CreateWorldWithEntity(out var entity, velX: 5f, velY: 0f, timeSinceSnapshot: 0.6f);
        var system = new InterpolationSystem { EnableDeadReckoningDecay = true };

        // 第一帧：超过 500ms，不移动
        system.Update(world, TimeSpan.FromSeconds(0.016));
        var interp = world.Get<InterpolatedTransformComponent>(entity);
        Assert.Equal(0f, interp.X, 0.001f);

        // 模拟新快照到达：重置 TimeSinceLastSnapshot = 0, Alpha = 1
        interp.TimeSinceLastSnapshot = 0f;
        interp.Alpha = 1f;
        world.Set(entity, interp);

        // 第二帧：速度恢复
        system.Update(world, TimeSpan.FromSeconds(0.016));
        interp = world.Get<InterpolatedTransformComponent>(entity);
        Assert.True(interp.X > 0.07f, $"X should advance after snapshot reset but was {interp.X:F4}");
        World.Destroy(world);
    }

    [Fact]
    public void Interpolation_IsPaused_NoMovement()
    {
        // IsPaused=true → 不推进任何位置
        var world = CreateWorldWithEntity(out var entity, velX: 10f, velY: 10f, timeSinceSnapshot: 0f);
        var system = new InterpolationSystem { IsPaused = true };

        system.Update(world, TimeSpan.FromSeconds(0.1));

        var interp = world.Get<InterpolatedTransformComponent>(entity);
        Assert.Equal(0f, interp.X, 0.001f);
        Assert.Equal(0f, interp.Z, 0.001f);
        World.Destroy(world);
    }

    [Fact]
    public void Interpolation_BelowThreshold_NoMovement()
    {
        // 速度低于阈值 (0.1 m/s) → 不进行 dead reckoning
        var world = CreateWorldWithEntity(out var entity, velX: 0.05f, velY: 0.05f, timeSinceSnapshot: 0f);
        var system = new InterpolationSystem { EnableDeadReckoningDecay = true };

        system.Update(world, TimeSpan.FromSeconds(0.016));

        var interp = world.Get<InterpolatedTransformComponent>(entity);
        Assert.Equal(0f, interp.X, 0.001f);
        World.Destroy(world);
    }
}
