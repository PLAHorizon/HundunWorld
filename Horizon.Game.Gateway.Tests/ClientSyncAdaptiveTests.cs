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
        // 模拟固定 150ms 间隔的快照到达
        for (int i = 0; i < 20; i++)
        {
            SnapshotApplySystem.RecordSnapshotArrival();
            Thread.Sleep(150);
        }

        var delay = SnapshotApplySystem.AdaptiveInterpolationDelaySeconds;
        // 固定间隔 → jitter ≈ 0，delay ≈ avgInterval ≈ 150ms
        Assert.True(delay >= 0.12f && delay <= 0.20f,
            $"Delay should converge to ~150ms but was {delay * 1000:F1}ms");
    }

    [Fact]
    public void AdaptiveDelay_ClampedTo_Min100ms_Max300ms()
    {
        // 直接设置极端 avgInterval 值来测试 clamp
        var avgField = typeof(SnapshotApplySystem)
            .GetField("_adaptiveAvgInterval", BindingFlags.NonPublic | BindingFlags.Static);
        var jitterField = typeof(SnapshotApplySystem)
            .GetField("_adaptiveJitter", BindingFlags.NonPublic | BindingFlags.Static);

        // 极低间隔 → 应 clamp 到 100ms
        avgField?.SetValue(null, 0.01f); // 10ms
        jitterField?.SetValue(null, 0f);
        Assert.Equal(0.1f, SnapshotApplySystem.AdaptiveInterpolationDelaySeconds, 0.001f);

        // 极高间隔 → 应 clamp 到 300ms
        avgField?.SetValue(null, 0.5f); // 500ms
        jitterField?.SetValue(null, 0f);
        Assert.Equal(0.3f, SnapshotApplySystem.AdaptiveInterpolationDelaySeconds, 0.001f);
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
/// Lerp 平滑追赶插值系统测试（替代原 Dead Reckoning 测试）。
/// </summary>
public class LerpInterpolationTests
{
    private World CreateWorldWithEntity(out Entity entity, float targetX, float targetY, float targetZ)
    {
        var world = World.Create();
        var interp = new InterpolatedTransformComponent
        {
            X = 0f, Y = 0f, Z = 0f,
            TargetX = targetX, TargetY = targetY, TargetZ = targetZ,
            Yaw = 0f, TargetYaw = 0f,
            Alpha = 0f,
            TimeSinceLastSnapshot = 0f,
        };
        entity = world.Create(interp);
        return world;
    }

    [Fact]
    public void Lerp_MovesTowardTarget()
    {
        var world = CreateWorldWithEntity(out var entity, targetX: 1f, targetY: 0f, targetZ: 0f);
        var system = new InterpolationSystem { UseAdaptiveSpeed = false, InterpolationSpeed = 10f };

        system.Update(world, TimeSpan.FromSeconds(1.0 / 60.0));

        var interp = world.Get<InterpolatedTransformComponent>(entity);
        // lerpFactor = (1/60) * 10 = 0.167，X 应从 0 移动到 ~0.167
        Assert.True(interp.X > 0.1f && interp.X < 0.2f,
            $"X should be ~0.167 but was {interp.X:F4}");
        World.Destroy(world);
    }

    [Fact]
    public void Lerp_ConvergesToTarget()
    {
        var world = CreateWorldWithEntity(out var entity, targetX: 5f, targetY: 0f, targetZ: 0f);
        var system = new InterpolationSystem { UseAdaptiveSpeed = false, InterpolationSpeed = 10f };

        // 运行 60 帧（1 秒）
        for (int i = 0; i < 60; i++)
            system.Update(world, TimeSpan.FromSeconds(1.0 / 60.0));

        var interp = world.Get<InterpolatedTransformComponent>(entity);
        // 1 秒后应接近目标（指数衰减，剩余距离 < 0.01%）
        Assert.True(MathF.Abs(interp.X - 5f) < 0.01f,
            $"X should converge to 5.0 but was {interp.X:F4}");
        World.Destroy(world);
    }

    [Fact]
    public void Lerp_TeleportThreshold_JumpsDirectly()
    {
        // 目标距离 > 10m → 直接跳到目标
        var world = CreateWorldWithEntity(out var entity, targetX: 50f, targetY: 0f, targetZ: 0f);
        var system = new InterpolationSystem { UseAdaptiveSpeed = false, InterpolationSpeed = 10f, TeleportThresholdMeters = 10f };

        system.Update(world, TimeSpan.FromSeconds(1.0 / 60.0));

        var interp = world.Get<InterpolatedTransformComponent>(entity);
        Assert.Equal(50f, interp.X, 0.001f);
        World.Destroy(world);
    }

    [Fact]
    public void Lerp_IsPaused_NoMovement()
    {
        var world = CreateWorldWithEntity(out var entity, targetX: 10f, targetY: 10f, targetZ: 10f);
        var system = new InterpolationSystem { IsPaused = true };

        system.Update(world, TimeSpan.FromSeconds(0.1));

        var interp = world.Get<InterpolatedTransformComponent>(entity);
        Assert.Equal(0f, interp.X, 0.001f);
        Assert.Equal(0f, interp.Y, 0.001f);
        Assert.Equal(0f, interp.Z, 0.001f);
        World.Destroy(world);
    }

    [Fact]
    public void Lerp_YawWrapping_ShortestPath()
    {
        var world = World.Create();
        var interp = new InterpolatedTransformComponent
        {
            X = 0f, Y = 0f, Z = 0f,
            TargetX = 0f, TargetY = 0f, TargetZ = 0f,
            Yaw = 3.0f, TargetYaw = -3.0f, // 跨越 ±π 边界
            Alpha = 0f,
        };
        var entity = world.Create(interp);
        var system = new InterpolationSystem { UseAdaptiveSpeed = false, InterpolationSpeed = 10f };

        system.Update(world, TimeSpan.FromSeconds(1.0 / 60.0));

        var result = world.Get<InterpolatedTransformComponent>(entity);
        // 最短路径：从 3.0 向 +π 方向移动（而非向 -3.0 方向移动 6.0 rad）
        Assert.True(result.Yaw > 3.0f,
            $"Yaw should increase from 3.0 (toward π) but was {result.Yaw:F4}");
        World.Destroy(world);
    }
}
