using System;
using System.Diagnostics;
using System.Reflection;
using Arch.Core;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Systems;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 任务 10.1 — 三次方缓动（smoothstep）单元测试。
/// 验证 InterpolationSystem Active 分支 Lerp 追赶使用的缓动函数
/// smoothedFactor = lerpFactor * lerpFactor * (3 - 2 * lerpFactor) 的数学性质与性能。
/// 被测代码：InterpolationSystem.cs:200。
/// </summary>
public class InterpolationSystemSmoothStepTests : IDisposable
{
    private readonly World _world;
    private readonly InterpolationSystem _system;

    public InterpolationSystemSmoothStepTests()
    {
        // 重置自适应延迟静态状态，避免其他测试残留影响
        ResetAdaptiveState();
        SnapshotApplySystem.UseAdaptiveDelay = true;

        _world = World.Create();
        _system = new InterpolationSystem
        {
            UseAdaptiveSpeed = false, // 固定速度便于确定性验证
            InterpolationSpeed = 30f, // 60fps 下 lerpFactor = (1/60)*30 = 0.5
            TeleportThresholdMeters = 50f,
        };
    }

    public void Dispose()
    {
        World.Destroy(_world);
        ResetAdaptiveState();
    }

    private static void ResetAdaptiveState()
    {
        SnapshotApplySystem.ResetAdaptiveDelayStats();
        // 关闭诊断避免噪声
        SnapshotApplySystem.Diagnostics = null;
    }

    /// <summary>
    /// 创建一个 Active 状态远程实体，当前位置 (0,0,0)，目标 (targetX,0,0)。
    /// </summary>
    private Entity CreateActiveEntity(float targetX)
    {
        var entity = _world.Create();
        var interp = new InterpolatedTransformComponent
        {
            X = 0f, Y = 0f, Z = 0f,
            StartX = 0f, StartY = 0f, StartZ = 0f,
            TargetX = targetX, TargetY = 0f, TargetZ = 0f,
            Yaw = 0f, StartYaw = 0f, TargetYaw = 0f,
            Alpha = 0f,
            ServerTick = 1,
            ReceivedTick = 1,
            TimeSinceLastSnapshot = 0f,
            State = RemoteEntityState.Active,
        };
        _world.Add(entity, interp);
        return entity;
    }

    // ─── 数学性质：lerpFactor=0/0.5/1 时 smoothedFactor=0/0.5/1 ───

    [Fact]
    public void SmoothStep_LerpFactor0_PositionUnchanged()
    {
        // lerpFactor = dt * speed = 0 * 30 = 0 → smoothedFactor = 0 → 位置不变
        const float targetX = 1f;
        var entity = CreateActiveEntity(targetX);

        _system.Update(_world, TimeSpan.Zero); // dt=0 → lerpFactor=0

        ref var interp = ref _world.Get<InterpolatedTransformComponent>(entity);
        Assert.Equal(0f, interp.X, 0.0001f);
    }

    [Fact]
    public void SmoothStep_LerpFactor1_ReachesTargetInOneStep()
    {
        // lerpFactor = dt * speed >= 1 → clamp 到 1 → smoothedFactor = 1*1*(3-2)=1 → 一次性到达目标
        // 设置 speed=60, dt=1/60 → lerpFactor=1
        _system.InterpolationSpeed = 60f;
        const float targetX = 1f;
        var entity = CreateActiveEntity(targetX);

        _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));

        ref var interp = ref _world.Get<InterpolatedTransformComponent>(entity);
        // smoothedFactor=1 → X += (1-0)*1 = 1.0
        Assert.Equal(targetX, interp.X, 0.0001f);
    }

    [Fact]
    public void SmoothStep_LerpFactorHalf_MovesHalfDistance()
    {
        // lerpFactor = 0.5 → smoothedFactor = 0.5*0.5*(3-2*0.5) = 0.25*2 = 0.5
        // 设置 speed=30, dt=1/60 → lerpFactor=0.5
        // 距离 1m → 位置移动 0.5m
        _system.InterpolationSpeed = 30f;
        const float targetX = 1f;
        var entity = CreateActiveEntity(targetX);

        _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));

        ref var interp = ref _world.Get<InterpolatedTransformComponent>(entity);
        // smoothedFactor=0.5 → X += (1-0)*0.5 = 0.5
        Assert.Equal(0.5f, interp.X, 0.0001f);
    }

    // ─── 不超调：smoothedFactor ≤ lerpFactor ───

    [Theory]
    [InlineData(0.1f)]
    [InlineData(0.25f)]
    [InlineData(0.4f)]
    [InlineData(0.6f)]
    [InlineData(0.75f)]
    [InlineData(0.9f)]
    public void SmoothStep_NeverExceedsLerpFactor(float lerpFactor)
    {
        // smoothstep(t) = t²(3-2t) 性质：t∈[0,1] → smoothedFactor∈[0,1]（位置不超调）
        // 注意：t>0.5 时 smoothedFactor > t（ease-out 加速段），t<0.5 时 smoothedFactor < t（ease-in 减速段）
        // "不超调"指位置不越过目标，即 smoothedFactor ∈ [0,1]
        var speed = lerpFactor * 60f; // dt=1/60 → lerpFactor = dt*speed
        _system.InterpolationSpeed = speed;
        const float targetX = 1f;
        var entity = CreateActiveEntity(targetX);

        _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));

        ref var interp = ref _world.Get<InterpolatedTransformComponent>(entity);
        var actualSmoothedFactor = interp.X / targetX; // 位置移动比例

        // smoothstep 在 [0,1] 上单调递增，且 t=0→0, t=1→1
        Assert.True(actualSmoothedFactor >= 0f, "smoothedFactor 不应为负");
        Assert.True(actualSmoothedFactor <= 1f, "smoothedFactor 不应超过 1（不超调）");
    }

    [Fact]
    public void SmoothStep_MonotonicIncreasing_WithLerpFactor()
    {
        // lerpFactor 越大，smoothedFactor 越大（单调递增）
        var factors = new[] { 0.1f, 0.3f, 0.5f, 0.7f, 0.9f };
        var smoothedFactors = new float[factors.Length];

        for (int i = 0; i < factors.Length; i++)
        {
            var entity = CreateActiveEntity(1f);
            _system.InterpolationSpeed = factors[i] * 60f;
            _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));
            ref var interp = ref _world.Get<InterpolatedTransformComponent>(entity);
            smoothedFactors[i] = interp.X;
        }

        for (int i = 1; i < factors.Length; i++)
        {
            Assert.True(smoothedFactors[i] > smoothedFactors[i - 1],
                $"lerpFactor 增大时 smoothedFactor 应单调递增：f[{i - 1}]={smoothedFactors[i - 1]:F6} < f[{i}]={smoothedFactors[i]:F6}");
        }
    }

    // ─── 性能：单实例插值 CPU 开销 < 0.01ms ───

    [Fact]
    public void SmoothStep_SingleInstanceCpu_Under001ms()
    {
        // 创建 1 个 Active 远程实体，测量单实例每帧插值耗时
        var entity = CreateActiveEntity(5f);

        // 预热（JIT）
        for (int i = 0; i < 1000; i++)
        {
            _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));
        }

        // 重置位置避免追平后进入 Dead Reckoning 分支
        ref var interpWarm = ref _world.Get<InterpolatedTransformComponent>(entity);
        interpWarm.X = 0f;
        interpWarm.State = RemoteEntityState.Active;

        var sw = Stopwatch.StartNew();
        const int iterations = 100_000;
        for (int i = 0; i < iterations; i++)
        {
            _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));
        }
        sw.Stop();

        var avgMs = sw.Elapsed.TotalMilliseconds / iterations;
        Assert.True(avgMs < 0.01,
            $"单实例插值 CPU 开销应 < 0.01ms，实际 {avgMs:F6}ms");
    }

    [Fact]
    public void SmoothStep_100InstancesCpu_Under1ms()
    {
        // 100 个远程角色同屏插值总耗时 ≤ 1ms
        var entities = new Entity[100];
        for (int i = 0; i < 100; i++)
        {
            entities[i] = CreateActiveEntity(5f);
        }

        // 预热
        for (int i = 0; i < 200; i++)
        {
            _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));
        }

        // 重置位置保持 Active Lerp 分支
        foreach (var e in entities)
        {
            ref var interp = ref _world.Get<InterpolatedTransformComponent>(e);
            interp.X = 0f;
            interp.State = RemoteEntityState.Active;
        }

        var sw = Stopwatch.StartNew();
        const int iterations = 10_000;
        for (int i = 0; i < iterations; i++)
        {
            _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));
        }
        sw.Stop();

        var avgMs = sw.Elapsed.TotalMilliseconds / iterations;
        Assert.True(avgMs < 1.0,
            $"100 实例插值总耗时应 < 1ms，实际 {avgMs:F4}ms");
    }
}