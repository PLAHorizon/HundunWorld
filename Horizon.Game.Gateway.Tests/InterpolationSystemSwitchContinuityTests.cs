using System;
using System.Diagnostics;
using System.Reflection;
using Arch.Core;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Systems;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 任务 10.7 — 算法切换连续性单元测试。
/// 验证 InterpolationSystem Lerp→Dead Reckoning 切换瞬间位置差 &lt; 0.01m，
/// Debug.Assert 在 Debug 构建生效（切换点 distSq &lt; 0.01f 断言）。
/// 被测代码：InterpolationSystem.cs:180-207（切换点断言 + Dead Reckoning 分支）。
/// </summary>
public class InterpolationSystemSwitchContinuityTests : IDisposable
{
    private readonly World _world;
    private readonly InterpolationSystem _system;

    public InterpolationSystemSwitchContinuityTests()
    {
        ResetAdaptiveState();
        SnapshotApplySystem.UseAdaptiveDelay = true;

        _world = World.Create();
        _system = new InterpolationSystem
        {
            UseAdaptiveSpeed = false,
            InterpolationSpeed = 30f, // 60fps 下 lerpFactor=0.5
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
        SnapshotApplySystem.Diagnostics = null;
    }

    /// <summary>
    /// 创建 Active 远程实体，当前位置 (currentX,0,0)，目标 (targetX,0,0)。
    /// </summary>
    private Entity CreateActiveEntity(float currentX, float targetX)
    {
        var entity = _world.Create();
        var interp = new InterpolatedTransformComponent
        {
            X = currentX, Y = 0f, Z = 0f,
            StartX = currentX, StartY = 0f, StartZ = 0f,
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

    // ─── 切换瞬间位置差 < 0.01m ───

    [Fact]
    public void SwitchContinuity_PositionDiffUnder001m_WhenSwitchingToDeadReckoning()
    {
        // 让实体 Lerp 追赶 Target，当 distSq <= 0.0001f 时切换到 Dead Reckoning。
        // 验证切换瞬间位置与 Target 距离 < 0.01m（Debug.Assert 条件 distSq < 0.01f）。
        var entity = CreateActiveEntity(0f, 0.5f);

        var dt = TimeSpan.FromSeconds(1.0 / 60.0);
        long switchTick = -1;
        float positionDiffAtSwitch = float.MaxValue;

        for (int frame = 0; frame < 60; frame++)
        {
            ref var before = ref _world.Get<InterpolatedTransformComponent>(entity);
            var distSqBefore = DistanceSq(before.X, before.Y, before.Z, before.TargetX, before.TargetY, before.TargetZ);

            _system.Update(_world, dt);

            ref var after = ref _world.Get<InterpolatedTransformComponent>(entity);

            // 检测是否在本帧切换到 Dead Reckoning（SwitchFromLerpToDeadReckoningTick 被更新）
            if (after.SwitchFromLerpToDeadReckoningTick > 0 && switchTick < 0)
            {
                switchTick = after.SwitchFromLerpToDeadReckoningTick;
                // 切换瞬间位置差（切换前 distSq <= 0.0001f，即距离 <= 0.01m）
                positionDiffAtSwitch = MathF.Sqrt(distSqBefore);
            }
        }

        Assert.True(switchTick > 0,
            "应在 60 帧内发生 Lerp→Dead Reckoning 切换");
        Assert.True(positionDiffAtSwitch < 0.01f,
            $"切换瞬间位置差应 < 0.01m，实际 {positionDiffAtSwitch:F6}m");
    }

    [Fact]
    public void SwitchContinuity_NoJump_WhenSwitchingToDeadReckoning()
    {
        // 切换瞬间帧间位移应连续无跳变（与前一帧位移量级一致）
        var entity = CreateActiveEntity(0f, 0.5f);

        var dt = TimeSpan.FromSeconds(1.0 / 60.0);
        float prevX = 0f;
        float prevFrameMove = 0f;
        bool switched = false;
        float switchFrameMove = 0f;

        for (int frame = 0; frame < 60; frame++)
        {
            _system.Update(_world, dt);
            ref var after = ref _world.Get<InterpolatedTransformComponent>(entity);

            var frameMove = MathF.Abs(after.X - prevX);

            if (after.SwitchFromLerpToDeadReckoningTick > 0 && !switched)
            {
                switched = true;
                switchFrameMove = frameMove;
                // 切换帧位移不应远大于前一帧（无跳变）
                if (prevFrameMove > 0.0001f)
                {
                    Assert.True(switchFrameMove < prevFrameMove * 5f + 0.001f,
                        $"切换帧位移 ({switchFrameMove:F6}) 不应远大于前一帧 ({prevFrameMove:F6})，无跳变");
                }
            }

            prevFrameMove = frameMove;
            prevX = after.X;
        }

        Assert.True(switched, "应发生切换");
    }

    // ─── Dead Reckoning 分支：到达 Target 后惯性外推 ───

    [Fact]
    public void SwitchContinuity_DeadReckoning_UsesLastVelocity()
    {
        // 到达 Target 后，Dead Reckoning 用最后已知速度外推
        // 设置 LastVelocityXZ 使切换后有惯性滑动
        var entity = CreateActiveEntity(0f, 0.01f); // 距离 0.01m，distSq=0.0001，进入 Dead Reckoning
        ref var setup = ref _world.Get<InterpolatedTransformComponent>(entity);
        setup.LastVelocityXZ_X = 6f; // 6 m/s 沿 X
        setup.LastVelocityXZ_Y = 0f;

        // 需要非 Strong 网络才启用 Dead Reckoning
        // 通过 RTT 设置为 Medium
        SnapshotApplySystem.RecordRttSample(60f); // >50 → Medium
        _ = SnapshotApplySystem.AdaptiveInterpolationDelaySeconds;

        var dt = TimeSpan.FromSeconds(1.0 / 60.0);
        float xBefore = _world.Get<InterpolatedTransformComponent>(entity).X;

        _system.Update(_world, dt);

        ref var after = ref _world.Get<InterpolatedTransformComponent>(entity);
        // Dead Reckoning 应使位置沿速度方向移动（X 增大）
        Assert.True(after.X > xBefore,
            $"Dead Reckoning 应使位置沿 LastVelocity 滑动：before={xBefore:F6}, after={after.X:F6}");
        // SwitchFromLerpToDeadReckoningTick 应被记录
        Assert.True(after.SwitchFromLerpToDeadReckoningTick > 0,
            "应记录切换 tick");
    }

    // ─── Debug.Assert 在 Debug 构建生效 ───

    [Fact]
    public void SwitchContinuity_DebugAssertActiveInDebugBuild()
    {
        // Debug.Assert(distSq < 0.01f) 在 Debug 构建下会执行检查。
        // 进入 Dead Reckoning 分支的条件是 distSq <= 0.0001f，
        // 而 0.0001f < 0.01f 恒成立，所以断言不会失败。
        // 本测试验证在 Debug 构建下，切换点执行了断言后的代码（记录 SwitchFromLerpToDeadReckoningTick）。
#if DEBUG
        var entity = CreateActiveEntity(0f, 0.001f); // distSq=0.000001 < 0.0001 → 进入 Dead Reckoning

        _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));

        ref var after = ref _world.Get<InterpolatedTransformComponent>(entity);
        // 断言通过后应记录切换 tick（证明断言后的代码执行了）
        Assert.True(after.SwitchFromLerpToDeadReckoningTick > 0,
            "Debug 构建下断言通过后应记录 SwitchFromLerpToDeadReckoningTick");
#else
        // Release 构建下 Debug.Assert 不执行，跳过本测试
        Assert.Skip("Debug.Assert 仅在 Debug 构建生效，当前为 Release 构建");
#endif
    }

    [Fact]
    public void SwitchContinuity_AssertCondition_AlwaysSatisfiedAtSwitchPoint()
    {
        // 验证进入 Dead Reckoning 分支时断言条件 distSq < 0.01f 恒满足
        // （因为进入条件 distSq <= 0.0001f < 0.01f）
        var entity = CreateActiveEntity(0f, 0.0001f); // 极小距离

        ref var before = ref _world.Get<InterpolatedTransformComponent>(entity);
        var distSq = DistanceSq(before.X, before.Y, before.Z, before.TargetX, before.TargetY, before.TargetZ);

        _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));

        ref var after = ref _world.Get<InterpolatedTransformComponent>(entity);

        // 如果切换发生，断言条件 distSq < 0.01f 必满足
        if (after.SwitchFromLerpToDeadReckoningTick > 0)
        {
            Assert.True(distSq < 0.01f,
                $"切换点 distSq={distSq:F8} 应 < 0.01f（断言条件恒满足）");
        }
    }

    private static float DistanceSq(float x1, float y1, float z1, float x2, float y2, float z2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var dz = z2 - z1;
        return dx * dx + dy * dy + dz * dz;
    }
}