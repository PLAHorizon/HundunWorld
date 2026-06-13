using System;
using Arch.Core;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Core;

namespace Horizon.Game.ECS.Arch.Systems;

/// <summary>
/// 插值系统：在 Render 阶段对非玩家实体进行位置插值，平滑网络抖动。
/// </summary>
/// <remarks>
/// 查询所有携带 <see cref="InterpolatedTransformComponent"/> 的实体，
/// 根据 <see cref="TimeSpan"/> 递推 <see cref="InterpolatedTransformComponent.Alpha"/>，
/// 将当前位置向目标位置线性插值。
/// <para>本地玩家实体不携带此组件，不受本系统影响（由 <see cref="LocalSimulationSystem"/> 驱动）。</para>
/// </remarks>
[ArchSystem(SystemGroup.Render, order: 0)]
public sealed class InterpolationSystem : ArchSystemBase
{
    /// <summary>插值速度系数（每秒推进 Alpha 的速率）。</summary>
    public float InterpolationSpeed { get; set; } = 1f / 0.1f;

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
            }
            else
            {
                var t = interp.Alpha;
                interp.X = interp.X + (interp.TargetX - interp.X) * t;
                interp.Y = interp.Y + (interp.TargetY - interp.Y) * t;
                interp.Z = interp.Z + (interp.TargetZ - interp.Z) * t;
            }
        });
    }
}
