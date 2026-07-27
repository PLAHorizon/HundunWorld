using System;
using Arch.Core;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Core;
using Horizon.Game.Message.Sync.Components;

namespace Horizon.Game.ECS.Arch.Systems;

/// <summary>
/// 插值系统：在 Render 阶段对非玩家实体进行位置 Lerp 平滑追赶，平滑网络抖动。
/// </summary>
/// <remarks>
/// 查询所有携带 <see cref="InterpolatedTransformComponent"/> 的实体，
/// 每帧将当前位置向目标位置进行指数平滑追赶（Lerp）。
/// <para>
/// 修复（远程角色闪移/移动不可见 — Alpha 插值加速运动问题）：
/// 原 Alpha 插值方案在快照到达频率（60Hz）高于插值完成时间（100ms）时，
/// Alpha 不断被重置或目标不断更新，导致帧间移动距离从 17% 到 183% 速度变化，
/// 视觉表现为"慢启动后突然加速"的加速运动。
/// </para>
/// <para>
/// Lerp 平滑追赶方案：
/// 位置 += (目标 - 位置) * lerpFactor，其中 lerpFactor = dt * speed。
/// 角色以指数衰减速度追赶目标，稳态速度与服务端速度一致，稳态滞后 = v / speed。
/// 帧间移动距离变化温和（4倍范围内），视觉上更自然。
/// </para>
/// <para>
/// 传送保护：当目标位置与当前位置距离超过 <see cref="TeleportThresholdMeters"/> 时，
/// 直接跳到目标位置，避免长距离 Lerp 导致角色"飞过去"。
/// </para>
/// <para>本地玩家实体不携带此组件，不受本系统影响（由 <see cref="LocalSimulationSystem"/> 驱动）。</para>
/// </remarks>
[ArchSystem(SystemGroup.Render, order: 0)]
public sealed class InterpolationSystem : ArchSystemBase
{
    /// <summary>Lerp 平滑追赶速度系数（每秒追赶比例）。当 UseAdaptiveSpeed=true 时从自适应延迟计算。</summary>
    /// <remarks>
    /// speed=10 → lerpFactor=0.167/帧 → 稳态滞后=v/speed=0.6m（100ms 延迟 @6m/s）
    /// speed=20 → lerpFactor=0.333/帧 → 稳态滞后=0.3m（50ms 延迟）
    /// </remarks>
    public float InterpolationSpeed { get; set; } = 1f / 0.1f; // 默认 10（100ms 延迟）

    /// <summary>是否使用自适应插值速度（从 SnapshotApplySystem.AdaptiveInterpolationDelaySeconds 计算）。</summary>
    public bool UseAdaptiveSpeed { get; set; } = true;

    /// <summary>
    /// 传送阈值（米）。当目标位置与当前位置距离超过此值时，直接跳到目标位置。
    /// 避免 Lerp 在长距离移动（如传送、复活）时角色"飞过去"的不自然视觉效果。
    /// </summary>
    public float TeleportThresholdMeters { get; set; } = 10f;

    /// <summary>断线期间暂停插值推进（避免无新数据时角色漂移）。</summary>
    public bool IsPaused { get; set; } = false;

    /// <inheritdoc />
    public override void Update(World world, TimeSpan deltaTime)
    {
        if (IsPaused)
            return;

        var query = new QueryDescription().WithAll<InterpolatedTransformComponent>();
        var dt = (float)deltaTime.TotalSeconds;

        // Lerp 平滑追赶速度
        var speed = UseAdaptiveSpeed
            ? 1f / SnapshotApplySystem.AdaptiveInterpolationDelaySeconds
            : InterpolationSpeed;

        // lerpFactor = dt * speed，限制在 [0, 1]
        // 60fps + speed=10 → lerpFactor=0.167（每帧追赶 16.7% 的距离）
        var lerpFactor = Math.Clamp(dt * speed, 0f, 1f);
        var teleportThresholdSq = TeleportThresholdMeters * TeleportThresholdMeters;

        world.Query(in query, (Entity entity, ref InterpolatedTransformComponent interp) =>
        {
            // 累计自上次快照以来的时间（供诊断和外部系统使用）
            interp.TimeSinceLastSnapshot += dt;

            // 计算当前位置与目标位置的距离平方
            var dx = interp.TargetX - interp.X;
            var dy = interp.TargetY - interp.Y;
            var dz = interp.TargetZ - interp.Z;
            var distSq = dx * dx + dy * dy + dz * dz;

            if (distSq > teleportThresholdSq)
            {
                // 传送：直接跳到目标位置，避免长距离 Lerp
                interp.X = interp.TargetX;
                interp.Y = interp.TargetY;
                interp.Z = interp.TargetZ;
                interp.Yaw = interp.TargetYaw;
                interp.Alpha = 1f; // 标记已到达目标
            }
            else
            {
                // Lerp 平滑追赶：位置 += (目标 - 位置) * lerpFactor
                interp.X += dx * lerpFactor;
                interp.Y += dy * lerpFactor;
                interp.Z += dz * lerpFactor;

                // Yaw 最短路径插值，避免 ±π 跨界时反向旋转
                var yawDelta = interp.TargetYaw - interp.Yaw;
                if (yawDelta > MathF.PI) yawDelta -= 2f * MathF.PI;
                else if (yawDelta < -MathF.PI) yawDelta += 2f * MathF.PI;
                interp.Yaw += yawDelta * lerpFactor;

                // Alpha 用于标记追赶进度（供外部诊断，不参与位置计算）
                // distSq 越小 Alpha 越接近 1
                interp.Alpha = 1f - Math.Clamp(distSq / teleportThresholdSq, 0f, 1f);
            }
        });
    }
}
