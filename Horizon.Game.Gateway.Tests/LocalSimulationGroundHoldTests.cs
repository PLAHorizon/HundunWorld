using System;
using Arch.Core;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Systems;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 本地预测的地面约束守卫：采样器返回 <see cref="float.NaN"/>（该处地面高度未知）时角色必须悬停。
/// <para>
/// 旧实现是"保护 120 帧（2s）后放重力"，而本工程玩法区在碰撞体就绪前一直是 NaN，
/// 于是角色以 TerminalVelocity 无界下坠（实测一局 3 分钟坠到 -91m，另一局坠到 -590m），
/// 相机跟着穿地、再被服务端 correction 反复拽回——就是用户报的"停留几秒后沉入地面并反复抖动"。
/// </para>
/// </summary>
public class LocalSimulationGroundHoldTests
{
    /// <summary>6 秒固频 tick，远超旧实现 120 帧（2 秒）的保护期，专门用来抓"保护期一过就放行"的回归。</summary>
    private const int Ticks = 360;

    private static readonly TimeSpan Dt = TimeSpan.FromSeconds(1.0 / 60.0);

    private static World CreateWorldWithLocalPlayer(out Entity entity)
    {
        var world = World.Create();
        entity = world.Create(
            new PlayerInputComponent { MoveX = 0f, MoveY = 0f, JumpPressedThisFrame = false },
            new NetworkIdentityComponent { EntityId = 1, IsLocalPlayer = true },
            new PredictedTransformComponent { X = 0f, Y = 0f, Z = 0f, Vz = 0f });
        return world;
    }

    [Fact]
    public void SamplerReturnsNaN_HoldsAltitude_ForTimesBeyondOldProtectionWindow()
    {
        var world = CreateWorldWithLocalPlayer(out var entity);
        try
        {
            var system = new LocalSimulationSystem { GroundHeightSampler = (x, y) => float.NaN };

            for (var i = 0; i < Ticks; i++)
                system.Update(world, Dt);

            // 活性哨兵：查询若没命中实体，系统体一次都不会执行，下面的 Z 断言会"空转通过"。
            Assert.Equal(Ticks, system.CurrentClientTick);

            var pred = world.Get<PredictedTransformComponent>(entity);
            Assert.True(pred.Z == 0f, $"地面未知时应当悬停，实际 Z={pred.Z:F2}");
            Assert.True(pred.Vz == 0f, $"地面未知时垂直速度应当清零，实际 Vz={pred.Vz:F4}");
        }
        finally
        {
            World.Destroy(world);
        }
    }

    [Fact]
    public void SamplerNotInjected_StillIntegratesGravity_LivenessContrast()
    {
        // 正对照：与上面同一套夹具、同样 360 个 tick，不注入采样器时角色必须真的下坠。
        // 若这条挂了，说明"悬停"那条其实是系统没在跑（查询没命中/提前 return），而不是约束生效。
        var world = CreateWorldWithLocalPlayer(out var entity);
        try
        {
            var system = new LocalSimulationSystem();

            for (var i = 0; i < Ticks; i++)
                system.Update(world, Dt);

            var pred = world.Get<PredictedTransformComponent>(entity);
            Assert.True(pred.Z < -1f, $"未注入采样器时应当受重力下坠，实际 Z={pred.Z:F2}");
        }
        finally
        {
            World.Destroy(world);
        }
    }
}
