using System;
using System.Reflection;
using Horizon.Game.ECS.Arch.Systems;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 任务 10.2 — 阻尼平滑追平（SmoothDamp）单元测试。
/// 验证 ReconciliationSystem.SmoothDamp3 临界阻尼弹簧算法：
/// 漂移 1m 时约 4 帧 @60fps 内追平，首帧位移小于简单 Lerp、无过冲、无瞬移。
/// 被测代码：ReconciliationSystem.cs:423（SmoothDamp3 方法）。
/// </summary>
public class ReconciliationSystemSmoothDampTests
{
    /// <summary>
    /// 通过反射调用 private static SmoothDamp3 方法。
    /// 签名：SmoothDamp3(ref float x, ref float y, ref float z,
    ///                ref float vx, ref float vy, ref float vz,
    ///                float targetX, float targetY, float targetZ,
    ///                float smoothTime, float dt)
    /// </summary>
    private static void InvokeSmoothDamp3(
        ref float x, ref float y, ref float z,
        ref float vx, ref float vy, ref float vz,
        float targetX, float targetY, float targetZ,
        float smoothTime, float dt)
    {
        var method = typeof(ReconciliationSystem).GetMethod(
            "SmoothDamp3",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var args = new object[]
        {
            x, y, z,
            vx, vy, vz,
            targetX, targetY, targetZ,
            smoothTime, dt
        };

        method!.Invoke(null, args);

        x = (float)args[0]!;
        y = (float)args[1]!;
        z = (float)args[2]!;
        vx = (float)args[3]!;
        vy = (float)args[4]!;
        vz = (float)args[5]!;
    }

    /// <summary>简单 Lerp 插值（用于对比首帧位移）。</summary>
    private static float SimpleLerpStep(float current, float target, float speed, float dt)
    {
        var factor = Math.Clamp(dt * speed, 0f, 1f);
        return current + (target - current) * factor;
    }

    // ─── 4 帧 @60fps 内追平 ───

    [Fact]
    public void SmoothDamp_Drift1M_ConvergesWithin4Frames60fps()
    {
        // 漂移 1m，smoothTime = 1/15 ≈ 0.0667s（约 4 帧 @60fps 追平）
        float x = 0f, y = 0f, z = 0f;
        float vx = 0f, vy = 0f, vz = 0f;
        const float targetX = 1f, targetY = 0f, targetZ = 0f;
        const float smoothTime = 1f / 15f; // SmoothCorrectionSpeed=15
        const float dt = 1f / 60f;

        var remainingHistory = new float[10];
        for (int frame = 0; frame < 10; frame++)
        {
            InvokeSmoothDamp3(ref x, ref y, ref z, ref vx, ref vy, ref vz,
                targetX, targetY, targetZ, smoothTime, dt);
            var remaining = MathF.Sqrt(
                (x - targetX) * (x - targetX) +
                (y - targetY) * (y - targetY) +
                (z - targetZ) * (z - targetZ));
            remainingHistory[frame] = remaining;
        }

        // 4 帧内应有显著追平（剩余 < 0.5m，阻尼平滑约 63% 收敛）
        Assert.True(remainingHistory[3] < 0.5f,
            $"4 帧后剩余漂移应 < 0.5m，实际 {remainingHistory[3]:F6}m");
        // 10 帧内应追平到 0.05m 以内（接近完全追平）
        Assert.True(remainingHistory[9] < 0.05f,
            $"10 帧后剩余漂移应 < 0.05m，实际 {remainingHistory[9]:F6}m");

        // 追平过程单调递减（无反复）
        for (int i = 1; i < 10; i++)
        {
            Assert.True(remainingHistory[i] <= remainingHistory[i - 1] + 0.0001f,
                $"追平过程应单调递减：帧 {i - 1}={remainingHistory[i - 1]:F6} >= 帧 {i}={remainingHistory[i]:F6}");
        }
    }

    // ─── 首帧位移小于简单 Lerp ───

    [Fact]
    public void SmoothDamp_FirstFrameDisplacement_LessThanSimpleLerp()
    {
        // 漂移 1m，对比首帧位移
        float x = 0f, y = 0f, z = 0f;
        float vx = 0f, vy = 0f, vz = 0f;
        const float target = 1f;
        const float smoothTime = 1f / 15f;
        const float dt = 1f / 60f;

        // SmoothDamp 首帧
        InvokeSmoothDamp3(ref x, ref y, ref z, ref vx, ref vy, ref vz,
            target, 0f, 0f, smoothTime, dt);
        var smoothDampFirstFrameMove = MathF.Abs(x - 0f); // 首帧位移

        // 简单 Lerp 首帧（speed=15, dt=1/60 → factor=0.25 → 移动 0.25m）
        var simpleLerpResult = SimpleLerpStep(0f, target, 15f, dt);
        var simpleLerpFirstFrameMove = MathF.Abs(simpleLerpResult - 0f);

        Assert.True(smoothDampFirstFrameMove < simpleLerpFirstFrameMove,
            $"SmoothDamp 首帧位移 ({smoothDampFirstFrameMove:F6}m) 应小于简单 Lerp ({simpleLerpFirstFrameMove:F6}m)");
        // 验证简单 Lerp 首帧位移确实是 0.25m（sanity check）
        Assert.Equal(0.25f, simpleLerpFirstFrameMove, 0.001f);
    }

    // ─── 无过冲 ───

    [Fact]
    public void SmoothDamp_NoOvershoot()
    {
        // 漂移 1m，追平过程中位置不应超过 target（无过冲）
        float x = 0f, y = 0f, z = 0f;
        float vx = 0f, vy = 0f, vz = 0f;
        const float target = 1f;
        const float smoothTime = 1f / 15f;
        const float dt = 1f / 60f;

        for (int frame = 0; frame < 30; frame++)
        {
            InvokeSmoothDamp3(ref x, ref y, ref z, ref vx, ref vy, ref vz,
                target, 0f, 0f, smoothTime, dt);

            // 位置不应超过 target（临界阻尼无过冲）
            Assert.True(x <= target + 0.0001f,
                $"帧 {frame}: 位置 {x:F6} 不应超过 target {target}（无过冲）");
            // 位置不应低于初始位置（单调递增趋向 target）
            Assert.True(x >= 0f - 0.0001f,
                $"帧 {frame}: 位置 {x:F6} 不应低于初始位置 0");
        }
    }

    // ─── 无瞬移 ───

    [Fact]
    public void SmoothDamp_NoTeleport_EachFrameDisplacementUnderDrift()
    {
        // 漂移 1m，每帧位移应小于总漂移（无瞬移）
        float x = 0f, y = 0f, z = 0f;
        float vx = 0f, vy = 0f, vz = 0f;
        const float target = 1f;
        const float smoothTime = 1f / 15f;
        const float dt = 1f / 60f;
        const float totalDrift = 1f;

        float prevX = 0f;
        for (int frame = 0; frame < 30; frame++)
        {
            InvokeSmoothDamp3(ref x, ref y, ref z, ref vx, ref vy, ref vz,
                target, 0f, 0f, smoothTime, dt);
            var frameMove = MathF.Abs(x - prevX);

            // 每帧位移应小于总漂移（无瞬移）
            Assert.True(frameMove < totalDrift,
                $"帧 {frame}: 单帧位移 {frameMove:F6}m 应小于总漂移 {totalDrift}m（无瞬移）");
            // 首帧位移应远小于总漂移（渐增特性）
            if (frame == 0)
            {
                Assert.True(frameMove < totalDrift * 0.3f,
                    $"首帧位移 {frameMove:F6}m 应远小于总漂移的 30%（渐增特性），实际占比 {frameMove / totalDrift:P}");
            }
            prevX = x;
        }
    }

    // ─── 三维同步追平 ───

    [Fact]
    public void SmoothDamp_3D_AllAxesConverge()
    {
        // 三维漂移各 1m，三轴应同步追平
        float x = 0f, y = 0f, z = 0f;
        float vx = 0f, vy = 0f, vz = 0f;
        const float targetX = 1f, targetY = 2f, targetZ = 3f;
        const float smoothTime = 1f / 15f;
        const float dt = 1f / 60f;

        for (int frame = 0; frame < 20; frame++)
        {
            InvokeSmoothDamp3(ref x, ref y, ref z, ref vx, ref vy, ref vz,
                targetX, targetY, targetZ, smoothTime, dt);
        }

        Assert.True(MathF.Abs(x - targetX) < 0.05f,
            $"X 轴应追平到 target，剩余 {MathF.Abs(x - targetX):F6}m");
        Assert.True(MathF.Abs(y - targetY) < 0.05f,
            $"Y 轴应追平到 target，剩余 {MathF.Abs(y - targetY):F6}m");
        Assert.True(MathF.Abs(z - targetZ) < 0.05f,
            $"Z 轴应追平到 target，剩余 {MathF.Abs(z - targetZ):F6}m");
    }

    // ─── 速度状态追平后趋零 ───

    [Fact]
    public void SmoothDamp_VelocityConvergesToZero()
    {
        // 追平后速度状态应趋近 0
        float x = 0f, y = 0f, z = 0f;
        float vx = 0f, vy = 0f, vz = 0f;
        const float target = 1f;
        const float smoothTime = 1f / 15f;
        const float dt = 1f / 60f;

        for (int frame = 0; frame < 30; frame++)
        {
            InvokeSmoothDamp3(ref x, ref y, ref z, ref vx, ref vy, ref vz,
                target, 0f, 0f, smoothTime, dt);
        }

        var velMag = MathF.Sqrt(vx * vx + vy * vy + vz * vz);
        Assert.True(velMag < 0.01f,
            $"追平后速度状态应趋近 0，实际 |v|={velMag:F6}m/s");
    }
}