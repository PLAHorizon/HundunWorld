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
            // 修复（预存 bug）：原 helper 未设置 State，默认 Initializing 导致系统提前 return，
            // Lerp_MovesTowardTarget / Lerp_ConvergesToTarget / Lerp_TeleportThreshold_* 均失效。
            State = RemoteEntityState.Active,
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
    public void Lerp_TeleportThreshold_StartsBlend()
    {
        // 目标距离 50m > TeleportThreshold(10m) 但 < HardSnapThreshold(默认 500m) → 启动加速混合
        // 新契约（3 档传送处理）：原"硬跳"行为被加速混合取代，1 帧后应处于混合中（X 在 0~50 之间）
        var world = CreateWorldWithEntity(out var entity, targetX: 50f, targetY: 0f, targetZ: 0f);
        var system = new InterpolationSystem
        {
            UseAdaptiveSpeed = false,
            InterpolationSpeed = 10f,
            TeleportThresholdMeters = 10f,
            // HardSnapThresholdMeters / TeleportBlendDurationSeconds 用默认值（500m / 0.2s）
        };

        system.Update(world, TimeSpan.FromSeconds(1.0 / 60.0));

        var interp = world.Get<InterpolatedTransformComponent>(entity);
        // 混合应已启动：RemainingSeconds > 0
        Assert.True(interp.TeleportBlendRemainingSeconds > 0f,
            $"混合应进行中，RemainingSeconds 应 > 0，实际 {interp.TeleportBlendRemainingSeconds:F4}");
        // 混合总时长应等于系统配置的 0.2s
        Assert.Equal(0.2f, interp.TeleportBlendDurationSeconds, 0.001f);
        // 1 帧（1/60s ≈ 0.0167s）后 alpha ≈ 0.083，smoothstep(0.083) ≈ 0.020，
        // X ≈ 50 × 0.020 ≈ 1.0，应 > 0 且 << 50（未瞬移到目标）
        Assert.True(interp.X > 0f && interp.X < 50f,
            $"混合首帧 X 应在 (0, 50) 区间（已开始移动但未瞬移），实际 {interp.X:F4}");
        Assert.True(interp.X < 5f,
            $"混合首帧 X 应 << 50（smoothstep ease-in 慢启动），实际 {interp.X:F4}");
        World.Destroy(world);
    }

    [Fact]
    public void Lerp_HardSnapThreshold_JumpsDirectly()
    {
        // 目标距离 50m > HardSnapThreshold(10m) → 硬跳瞬移到目标
        // 硬跳契约保留：真传送（复活/跨地图）仍瞬移，不走加速混合
        var world = CreateWorldWithEntity(out var entity, targetX: 50f, targetY: 0f, targetZ: 0f);
        var system = new InterpolationSystem
        {
            UseAdaptiveSpeed = false,
            InterpolationSpeed = 10f,
            TeleportThresholdMeters = 5f,      // 平滑区阈值（< HardSnap，确保 50m 不走普通 Lerp）
            HardSnapThresholdMeters = 10f,    // 硬跳阈值：50m > 10m → 硬跳
        };

        system.Update(world, TimeSpan.FromSeconds(1.0 / 60.0));

        var interp = world.Get<InterpolatedTransformComponent>(entity);
        Assert.Equal(50f, interp.X, 0.001f);
        // 硬跳不应启动混合
        Assert.Equal(0f, interp.TeleportBlendRemainingSeconds, 0.001f);
        Assert.Equal(0f, interp.TeleportBlendDurationSeconds, 0.001f);
        Assert.Equal(1f, interp.Alpha); // 标记已到达目标
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
            // 修复（预存 bug）：原测试未设置 State，默认 Initializing 导致系统提前 return，Yaw 不变。
            State = RemoteEntityState.Active,
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

    // ─── 加速混合（Teleport Blend）行为测试 ───
    // 验证 3 档传送处理中"加速混合"档的正确性：
    //   距离在 (TeleportThreshold, HardSnapThreshold] 区间时，用 smoothstep 缓动在
    //   TeleportBlendDurationSeconds 内从当前位置过渡到 Target。

    [Fact]
    public void TeleportBlend_CompletesWithinDuration()
    {
        // 混合应在 TeleportBlendDurationSeconds(0.2s) 后完成，位置到达 Target，状态清零
        var world = CreateWorldWithEntity(out var entity, targetX: 50f, targetY: 0f, targetZ: 0f);
        var system = new InterpolationSystem
        {
            UseAdaptiveSpeed = false,
            InterpolationSpeed = 10f,
            TeleportThresholdMeters = 10f,   // 50m > 10m → 启动混合
            TeleportBlendDurationSeconds = 0.2f,
        };
        var dt = TimeSpan.FromSeconds(1.0 / 60.0);

        // 运行 0.25 秒（15 帧，超过混合时长 0.2s = 12 帧）
        for (int i = 0; i < 15; i++)
            system.Update(world, dt);

        var interp = world.Get<InterpolatedTransformComponent>(entity);
        Assert.Equal(50f, interp.X, 0.001f); // 到达 Target
        Assert.Equal(0f, interp.TeleportBlendRemainingSeconds, 0.001f); // 混合已清零
        Assert.Equal(0f, interp.TeleportBlendDurationSeconds, 0.001f);
        Assert.Equal(1f, interp.Alpha); // 标记已到达
        World.Destroy(world);
    }

    [Fact]
    public void TeleportBlend_SmoothStepEasing_NoOvershoot_Monotonic()
    {
        // 验证 smoothstep 缓动：位置单调递增（不回退）、不超调（不超过 Target=50）、
        // 中间帧（alpha=0.5 时）位置 ≈ 25（smoothstep(0.5)=0.5）
        var world = CreateWorldWithEntity(out var entity, targetX: 50f, targetY: 0f, targetZ: 0f);
        var system = new InterpolationSystem
        {
            UseAdaptiveSpeed = false,
            InterpolationSpeed = 10f,
            TeleportThresholdMeters = 10f,
            TeleportBlendDurationSeconds = 0.2f,
        };
        var dt = TimeSpan.FromSeconds(1.0 / 60.0);

        float prevX = 0f;
        for (int i = 0; i < 15; i++)
        {
            system.Update(world, dt);
            var interp = world.Get<InterpolatedTransformComponent>(entity);
            // 单调递增（允许浮点误差 0.0001）
            Assert.True(interp.X >= prevX - 0.0001f,
                $"帧 {i}: X={interp.X:F4} 不应小于前一帧 {prevX:F4}（smoothstep 单调递增）");
            // 不超调（不超过 Target=50）
            Assert.True(interp.X <= 50f + 0.001f,
                $"帧 {i}: X={interp.X:F4} 不应超过 Target=50（不超调）");
            prevX = interp.X;
        }

        // 中间帧（第 6 帧 = 0.1s = duration/2，alpha=0.5，smoothstep(0.5)=0.5）
        // 重新创建一个干净的世界验证中间帧位置
        World.Destroy(world);
        world = CreateWorldWithEntity(out entity, targetX: 50f, targetY: 0f, targetZ: 0f);
        system = new InterpolationSystem
        {
            UseAdaptiveSpeed = false,
            InterpolationSpeed = 10f,
            TeleportThresholdMeters = 10f,
            TeleportBlendDurationSeconds = 0.2f,
        };
        for (int i = 0; i < 6; i++)
            system.Update(world, dt);
        var midInterp = world.Get<InterpolatedTransformComponent>(entity);
        // alpha ≈ 6/12 = 0.5，smoothstep(0.5) = 0.5，X ≈ 25
        Assert.True(MathF.Abs(midInterp.X - 25f) < 3f,
            $"第 6 帧（alpha≈0.5）X 应 ≈ 25（smoothstep(0.5)=0.5），实际 {midInterp.X:F4}");
        World.Destroy(world);
    }

    [Fact]
    public void TeleportBlend_RedirectsToNewSnapshot_WithoutResettingElapsed()
    {
        // 混合进行中新快照到达改 Target，混合平滑重定向（不重置 elapsed）
        var world = CreateWorldWithEntity(out var entity, targetX: 50f, targetY: 0f, targetZ: 0f);
        var system = new InterpolationSystem
        {
            UseAdaptiveSpeed = false,
            InterpolationSpeed = 10f,
            TeleportThresholdMeters = 10f,
            TeleportBlendDurationSeconds = 0.2f,
        };
        var dt = TimeSpan.FromSeconds(1.0 / 60.0);

        // 跑 6 帧（0.1s，alpha=0.5，混合进行中）
        for (int i = 0; i < 6; i++)
            system.Update(world, dt);
        var beforeRedirect = world.Get<InterpolatedTransformComponent>(entity);
        Assert.True(beforeRedirect.TeleportBlendRemainingSeconds > 0f, "应处于混合中");
        var elapsedBefore = beforeRedirect.TeleportBlendDurationSeconds - beforeRedirect.TeleportBlendRemainingSeconds;

        // 模拟新快照到达：TargetX 从 50 改到 100（仍在 HardSnap 阈值 500m 内）
        ref var interp = ref world.Get<InterpolatedTransformComponent>(entity);
        interp.TargetX = 100f;
        world.Set(entity, interp);

        // 再跑 1 帧：混合应继续推进（不重置 elapsed），位置朝新 Target 移动
        system.Update(world, dt);
        var afterRedirect = world.Get<InterpolatedTransformComponent>(entity);
        var elapsedAfter = afterRedirect.TeleportBlendDurationSeconds - afterRedirect.TeleportBlendRemainingSeconds;
        // elapsed 应推进约 1 帧（dt），不应重置为 0
        Assert.True(elapsedAfter > elapsedBefore + 0.001f,
            $"混合应继续推进不重置 elapsed：before={elapsedBefore:F4}, after={elapsedAfter:F4}");
        // 位置应朝新 Target(100) 移动（X > 之前的位置）
        Assert.True(afterRedirect.X > beforeRedirect.X,
            $"重定向后位置应朝新 Target 移动：before={beforeRedirect.X:F4}, after={afterRedirect.X:F4}");
        // 混合仍在进行
        Assert.True(afterRedirect.TeleportBlendRemainingSeconds > 0f,
            "重定向后混合应继续进行");
        World.Destroy(world);
    }

    [Fact]
    public void TeleportBlend_HardSnapOverridesBlend_WhenTargetJumpsTooFar()
    {
        // 混合进行中 Target 跳到 > HardSnapThreshold，立即硬跳覆盖混合
        var world = CreateWorldWithEntity(out var entity, targetX: 50f, targetY: 0f, targetZ: 0f);
        var system = new InterpolationSystem
        {
            UseAdaptiveSpeed = false,
            InterpolationSpeed = 10f,
            TeleportThresholdMeters = 10f,
            HardSnapThresholdMeters = 500f,    // 默认值
            TeleportBlendDurationSeconds = 0.2f,
        };
        var dt = TimeSpan.FromSeconds(1.0 / 60.0);

        // 跑 6 帧，混合进行中（位置约 25m 处）
        for (int i = 0; i < 6; i++)
            system.Update(world, dt);
        var duringBlend = world.Get<InterpolatedTransformComponent>(entity);
        Assert.True(duringBlend.TeleportBlendRemainingSeconds > 0f, "应处于混合中");

        // 模拟极端场景：新快照 Target 跳到 1000m（> HardSnap 500m）
        ref var interp = ref world.Get<InterpolatedTransformComponent>(entity);
        interp.TargetX = 1000f;
        world.Set(entity, interp);

        // 再跑 1 帧：距离 (~25 → 1000 = 975m) > HardSnap(500m)，应硬跳覆盖混合
        system.Update(world, dt);
        var afterHardSnap = world.Get<InterpolatedTransformComponent>(entity);
        Assert.Equal(1000f, afterHardSnap.X, 0.001f); // 硬跳到 Target
        Assert.Equal(0f, afterHardSnap.TeleportBlendRemainingSeconds, 0.001f); // 混合被清除
        Assert.Equal(0f, afterHardSnap.TeleportBlendDurationSeconds, 0.001f);
        Assert.Equal(1f, afterHardSnap.Alpha); // 标记已到达
        World.Destroy(world);
    }
}
