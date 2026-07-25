using System;
using Arch.Core;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Core;
using Horizon.Game.Message.Sync.Components;

namespace Horizon.Game.ECS.Arch.Systems;

/// <summary>
/// 插值系统：在 Render 阶段对非玩家实体进行位置插值，平滑网络抖动。
/// </summary>
/// <remarks>
/// 查询所有携带 <see cref="InterpolatedTransformComponent"/> 的实体，
/// 根据 <see cref="TimeSpan"/> 递推 <see cref="InterpolatedTransformComponent.Alpha"/>，
/// 将当前位置向目标位置线性插值。
/// <para>Task B.6.2：当实体携带 <see cref="MovementStateAuthComponent"/> 时，在到达目标位置后
/// 使用服务器下发的速度向量进行 dead reckoning（航位推算），沿速度方向继续推进位置，
/// 避免在两个快照之间角色“卡顿”，实现速度向量的平滑过渡。</para>
/// <para>[Phase C4] 支持自适应插值延迟（基于快照到达间隔 jitter）和 dead reckoning 速度衰减
/// （超过 200ms 无新快照时速度线性衰减到 0，避免角色无限滑行）。</para>
/// <para>本地玩家实体不携带此组件，不受本系统影响（由 <see cref="LocalSimulationSystem"/> 驱动）。</para>
/// </remarks>
[ArchSystem(SystemGroup.Render, order: 0)]
public sealed class InterpolationSystem : ArchSystemBase
{
    /// <summary>插值速度系数（每秒推进 Alpha 的速率）。当 UseAdaptiveSpeed=true 时每帧从自适应延迟计算。</summary>
    public float InterpolationSpeed { get; set; } = 1f / 0.1f;

    /// <summary>[Phase C4] 是否使用自适应插值速度（从 SnapshotApplySystem.AdaptiveInterpolationDelaySeconds 计算）。</summary>
    public bool UseAdaptiveSpeed { get; set; } = false;

    /// <summary>[Phase C4] 是否启用 dead reckoning 速度衰减（默认开启）。</summary>
    public bool EnableDeadReckoningDecay { get; set; } = true;

    /// <summary>
    /// Dead reckoning 速度阈值（m/s）。水平速度低于此值视为静止，不进行航位推算。
    /// </summary>
    private const float DeadReckonSpeedThreshold = 0.1f;

    /// <summary>
    /// [Phase C4] Dead reckoning 衰减启动时间（秒）。超过此时间无新快照时，速度开始线性衰减。
    /// </summary>
    private const float DecayStartTime = 0.2f; // 200ms

    /// <summary>
    /// [Phase C4] Dead reckoning 衰减完成时间（秒）。超过此时间速度完全为 0。
    /// </summary>
    private const float DecayEndTime = 0.5f; // 500ms

    /// <summary>[Phase C4] 断线期间暂停插值推进（避免无新数据时 dead reckoning 漂移）。</summary>
    public bool IsPaused { get; set; } = false;

    /// <inheritdoc />
    public override void Update(World world, TimeSpan deltaTime)
    {
        // [Phase C5] 断线期间暂停插值推进
        if (IsPaused)
            return;

        var query = new QueryDescription().WithAll<InterpolatedTransformComponent>();
        var dt = (float)deltaTime.TotalSeconds;

        // [Phase C4] 自适应插值速度
        var speed = UseAdaptiveSpeed
            ? 1f / SnapshotApplySystem.AdaptiveInterpolationDelaySeconds
            : InterpolationSpeed;

        world.Query(in query, (Entity entity, ref InterpolatedTransformComponent interp) =>
        {
            // [Phase C4] 累计自上次快照以来的时间
            interp.TimeSinceLastSnapshot += dt;

            interp.Alpha += dt * speed;
            if (interp.Alpha >= 1f)
            {
                interp.Alpha = 1f;
                interp.X = interp.TargetX;
                interp.Y = interp.TargetY;
                interp.Z = interp.TargetZ;

                // Task B.6.2 + [Phase C4]：速度向量平滑过渡 + 衰减
                if (world.TryGet<MovementStateAuthComponent>(entity, out var movement))
                {
                    var velX = movement.VelocityXZ_X;
                    var velY = movement.VelocityXZ_Y;
                    var speedSquared = velX * velX + velY * velY;
                    if (speedSquared > DeadReckonSpeedThreshold * DeadReckonSpeedThreshold)
                    {
                        // [Phase C4] 速度衰减：超过 DecayStartTime 后线性衰减，到 DecayEndTime 完全停止
                        float decayFactor = 1f;
                        if (EnableDeadReckoningDecay && interp.TimeSinceLastSnapshot > DecayStartTime)
                        {
                            decayFactor = 1f - Math.Clamp(
                                (interp.TimeSinceLastSnapshot - DecayStartTime) / (DecayEndTime - DecayStartTime),
                                0f, 1f);
                        }

                        if (decayFactor > 0f)
                        {
                            // VelocityXZ_X/Y 为 ECS Z-up 水平速度（X=左右, Y=前后）。
                            // interp 为 Flax Y-up（X=左右, Y=上下, Z=前后），故 velY 应用到 Z 轴。
                            interp.X += velX * dt * decayFactor;
                            interp.Z += velY * dt * decayFactor;
                        }
                    }
                }
            }
            else
            {
                var t = interp.Alpha;
                interp.X = interp.StartX + (interp.TargetX - interp.StartX) * t;
                interp.Y = interp.StartY + (interp.TargetY - interp.StartY) * t;
                interp.Z = interp.StartZ + (interp.TargetZ - interp.StartZ) * t;

                // Yaw 插值：最短路径插值，避免 359°→0° 时反向旋转 359°
                var yawDelta = interp.TargetYaw - interp.StartYaw;
                // 归一化到 [-180, 180] 范围
                if (yawDelta > 180f) yawDelta -= 360f;
                else if (yawDelta < -180f) yawDelta += 360f;
                interp.Yaw = interp.StartYaw + yawDelta * t;
            }
        });
    }
}
