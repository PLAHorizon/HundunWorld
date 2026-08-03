using System;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// NPC 同步零向量归一化守门逻辑回归测试（对应 BUG 3：NaN 传播）。
/// <para>
/// BUG 3 现象：NpcSyncManager.SyncPatrolNpc/SyncFollowerNpc 直接对 (target - position).Normalized
/// 求值，当 NPC 已抵达目标点时差向量为零，零向量归一化产生 NaN，污染 Velocity → Position 变 NaN
/// 并向 AOI/同步管线扩散。
/// </para>
/// <para>
/// 修复后用 <c>LengthSquared &gt; 1e-8f</c> 守门，零向量时速度置零。本测试用纯 C# 复制守门逻辑
/// （与 NpcSyncManager 修复代码保持一致），因 NpcSyncManager 依赖 FlaxEngine Script/Vector3
/// 无法在标准测试项目实例化。守门逻辑本身与 Vector3 实现无关，仅依赖长度平方比较。
/// </para>
/// </summary>
public class NpcSyncNaNGuardTests
{
    // 守门阈值，与 NpcSyncManager 修复代码一致
    private const double EpsilonSq = 1e-8;

    /// <summary>
    /// 复制 NpcSyncManager.SyncPatrolNpc 的速度计算守门逻辑（double 模拟 Vector3）。
    /// 与修复代码 <c>toTarget.LengthSquared > 1e-8f ? toTarget.Normalized : Vector3.Zero</c> 等价。
    /// </summary>
    private static (double Vx, double Vy, double Vz) ComputeVelocity(
        double targetX, double targetY, double targetZ,
        double posX, double posY, double posZ,
        double speed)
    {
        double dx = targetX - posX;
        double dy = targetY - posY;
        double dz = targetZ - posZ;
        double lenSq = dx * dx + dy * dy + dz * dz;
        if (lenSq > EpsilonSq)
        {
            double invLen = 1.0 / Math.Sqrt(lenSq);
            return (dx * invLen * speed, dy * invLen * speed, dz * invLen * speed);
        }
        return (0.0, 0.0, 0.0);
    }

    /// <summary>
    /// NPC 已抵达巡逻路径点（target == position）时，速度应为零而非 NaN。
    /// 这是 BUG 3 的核心回归场景：原实现零向量归一化产生 NaN。
    /// </summary>
    [Fact]
    public void ZeroVector_VelocityIsZero_NotNaN()
    {
        var v = ComputeVelocity(10, 20, 30, 10, 20, 30, 2.0);

        Assert.Equal(0.0, v.Vx);
        Assert.Equal(0.0, v.Vy);
        Assert.Equal(0.0, v.Vz);
        Assert.False(double.IsNaN(v.Vx), "Vx 不应为 NaN");
        Assert.False(double.IsNaN(v.Vy), "Vy 不应为 NaN");
        Assert.False(double.IsNaN(v.Vz), "Vz 不应为 NaN");
        Assert.False(double.IsInfinity(v.Vx), "Vx 不应为 Infinity");
    }

    /// <summary>
    /// NPC 未抵达目标点时，速度应为归一化方向 × 速度。
    /// 验证守门未误伤正常移动场景。
    /// </summary>
    [Fact]
    public void NonZeroVector_VelocityIsNormalized()
    {
        var v = ComputeVelocity(10, 0, 0, 0, 0, 0, 3.0);

        Assert.Equal(3.0, v.Vx, 5);
        Assert.Equal(0.0, v.Vy, 5);
        Assert.Equal(0.0, v.Vz, 5);
    }

    /// <summary>
    /// 差向量极小（长度平方 &lt; 1e-8）时，守门触发，速度置零。
    /// 验证阈值边界：NPC 在目标点极小邻域内不应产生 NaN 或极大速度。
    /// </summary>
    [Fact]
    public void NearZeroVector_VelocityIsZero_NotNaN()
    {
        // 1e-5 的长度平方 = 1e-10 < 1e-8，守门触发
        var v = ComputeVelocity(1e-5, 0, 0, 0, 0, 0, 2.0);

        Assert.Equal(0.0, v.Vx);
        Assert.False(double.IsNaN(v.Vx));
    }

    /// <summary>
    /// 差向量刚好超过阈值时，正常归一化（不触发守门）。
    /// 验证阈值边界另一侧：1e-4 的长度平方 = 1e-8，不大于阈值，仍守门；
    /// 1e-3 的长度平方 = 1e-6 > 1e-8，正常归一化。
    /// </summary>
    [Fact]
    public void JustAboveThreshold_VelocityIsNormalized()
    {
        // 1e-3 长度平方 = 1e-6 > 1e-8，正常归一化
        var v = ComputeVelocity(1e-3, 0, 0, 0, 0, 0, 5.0);

        Assert.Equal(5.0, v.Vx, 5);
        Assert.False(double.IsNaN(v.Vx));
    }

    /// <summary>
    /// 跟随 NPC（SyncFollowerNpc）场景：玩家静止时跟随点与 NPC 位置重合，
    /// 速度应为零而非 NaN。复现 BUG 3 在跟随 NPC 的表现。
    /// </summary>
    [Fact]
    public void FollowerNpc_AtFollowPoint_VelocityZero_NotNaN()
    {
        // 玩家位置 (5,0,5)，跟随点 = 玩家位置 - forward*3
        // 假设 NPC 已在跟随点，差向量为零
        double followX = 5, followY = 0, followZ = 2; // 跟随点
        var v = ComputeVelocity(followX, followY, followZ, followX, followY, followZ, 3.0);

        Assert.Equal(0.0, v.Vx);
        Assert.Equal(0.0, v.Vy);
        Assert.Equal(0.0, v.Vz);
        Assert.False(double.IsNaN(v.Vx));
    }

    /// <summary>
    /// 连续多帧零向量场景：NPC 停留在目标点，速度持续为零，不累积 NaN。
    /// 验证 BUG 3 修复后不会因多帧零向量导致状态污染。
    /// </summary>
    [Fact]
    public void ZeroVector_MultipleFrames_NoNaNAccumulation()
    {
        double posX = 10, posY = 20, posZ = 30;
        for (int frame = 0; frame < 100; frame++)
        {
            var v = ComputeVelocity(10, 20, 30, posX, posY, posZ, 2.0);
            Assert.False(double.IsNaN(v.Vx), $"帧 {frame}: Vx 为 NaN");
            Assert.False(double.IsNaN(v.Vy), $"帧 {frame}: Vy 为 NaN");
            Assert.False(double.IsNaN(v.Vz), $"帧 {frame}: Vz 为 NaN");
            // 速度为零，位置不变
            Assert.Equal(0.0, v.Vx);
            posX += v.Vx * 0.016; // dt=16ms
            posY += v.Vy * 0.016;
            posZ += v.Vz * 0.016;
        }
        Assert.Equal(10, posX);
        Assert.Equal(20, posY);
        Assert.Equal(30, posZ);
    }
}