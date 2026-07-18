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
/// 避免在两个快照之间角色"卡顿"，实现速度向量的平滑过渡。</para>
/// <para>本地玩家实体不携带此组件，不受本系统影响（由 <see cref="LocalSimulationSystem"/> 驱动）。</para>
/// </remarks>
[ArchSystem(SystemGroup.Render, order: 0)]
public sealed class InterpolationSystem : ArchSystemBase
{
    /// <summary>插值速度系数（每秒推进 Alpha 的速率）。</summary>
    public float InterpolationSpeed { get; set; } = 1f / 0.1f;

    /// <summary>
    /// Dead reckoning 速度阈值（m/s）。水平速度低于此值视为静止，不进行航位推算。
    /// </summary>
    private const float DeadReckonSpeedThreshold = 0.1f;

    /// <inheritdoc />
    public override void Update(World world, TimeSpan deltaTime)
    {
        var query = new QueryDescription().WithAll<InterpolatedTransformComponent>();
        var dt = (float)deltaTime.TotalSeconds;

        world.Query(in query, (Entity entity, ref InterpolatedTransformComponent interp) =>
        {
            interp.Alpha += dt * InterpolationSpeed;
            if (interp.Alpha >= 1f)
            {
                interp.Alpha = 1f;
                interp.X = interp.TargetX;
                interp.Y = interp.TargetY;
                interp.Z = interp.TargetZ;

                // Task B.6.2：速度向量平滑过渡 — 到达目标位置后使用速度进行 dead reckoning。
                // 在等待下一个快照（20Hz，50ms 间隔）期间沿速度方向继续推进位置，
                // 避免移动中的角色在两个快照之间"卡顿"。下一个快照到达时 Start/Target/Alpha 重置，
                // 插值会平滑过渡到新目标，吸收 dead reckoning 的误差。
                if (world.TryGet<MovementStateAuthComponent>(entity, out var movement))
                {
                    var velX = movement.VelocityXZ_X;
                    var velY = movement.VelocityXZ_Y;
                    var speedSquared = velX * velX + velY * velY;
                    if (speedSquared > DeadReckonSpeedThreshold * DeadReckonSpeedThreshold)
                    {
                        // VelocityXZ_X/Y 为 ECS Z-up 水平速度（X=左右, Y=前后）。
                        // interp 为 Flax Y-up（X=左右, Y=上下, Z=前后），故 velY 应用到 Z 轴。
                        interp.X += velX * dt;
                        interp.Z += velY * dt;
                    }
                }
            }
            else
            {
                var t = interp.Alpha;
                interp.X = interp.StartX + (interp.TargetX - interp.StartX) * t;
                interp.Y = interp.StartY + (interp.TargetY - interp.StartY) * t;
                interp.Z = interp.StartZ + (interp.TargetZ - interp.StartZ) * t;
            }
        });
    }
}
