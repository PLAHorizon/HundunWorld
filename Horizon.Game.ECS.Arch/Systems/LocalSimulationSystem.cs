using System;
using System.Collections.Generic;
using Arch.Core;
using Horizon.Game.Core.Sim;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Core;

namespace Horizon.Game.ECS.Arch.Systems;

/// <summary>
/// 本地预测模拟系统：在 FixedUpdate 阶段读取玩家输入，调用 <see cref="MovementFormula"/> 进行本地位置预测。
/// </summary>
/// <remarks>
/// 本系统仅处理拥有 <see cref="PlayerInputComponent"/> + <see cref="PredictedTransformComponent"/> 的实体。
/// 对于本地玩家，每次执行会递增 <see cref="CurrentClientTick"/> 并维护输入历史。
/// 固定时间步长默认为 1/60 秒（可被外部通过 <see cref="FixedDtSeconds"/> 调整）。
/// </remarks>
[ArchSystem(SystemGroup.FixedUpdate, order: 10)]
public sealed class LocalSimulationSystem : ArchSystemBase
{
    /// <summary>固定时间步长（秒），默认 1/60 秒。</summary>
    public float FixedDtSeconds { get; set; } = 1f / 60f;

    /// <summary>当前客户端 tick 序号（从 0 开始，仅对本地玩家有效）。</summary>
    public long CurrentClientTick { get; private set; }

    /// <summary>本地玩家实体 ID，未设置时为 0。</summary>
    public ulong LocalPlayerEntityId { get; set; }

    private readonly Dictionary<int, int> _jumpCounts = new();

    /// <inheritdoc />
    public override void Update(World world, TimeSpan deltaTime)
    {
        var query = new QueryDescription()
            .WithAll<PlayerInputComponent, PredictedTransformComponent>();

        world.Query(in query, (Entity entity, ref PlayerInputComponent input, ref PredictedTransformComponent pred) =>
        {
            var isLocal = entity.Id == (int)LocalPlayerEntityId;

            if (isLocal)
            {
                CurrentClientTick++;
            }

            var isQinggongJump = (input.InputBits & (1u << 3)) != 0;
            var isJumpPressed = (input.InputBits & 0x1) != 0;

            if (!_jumpCounts.ContainsKey(entity.Id))
                _jumpCounts[entity.Id] = 0;

            var jumpCount = _jumpCounts[entity.Id];

            float jumpImpulse;
            if (isJumpPressed)
            {
                if (isQinggongJump)
                {
                    jumpCount++;
                    jumpImpulse = jumpCount switch
                    {
                        1 => 5.5f,
                        2 => 4.5f,
                        3 => 3.5f,
                        _ => 0f
                    };
                }
                else
                {
                    jumpCount = 1;
                    jumpImpulse = 5.5f;
                }
            }
            else
            {
                jumpImpulse = 0f;
                if (!isQinggongJump)
                    jumpCount = 0;
            }

            _jumpCounts[entity.Id] = jumpCount;

            var (nx, ny, nz, nvz) = MovementFormula.Step(
                pred.X, pred.Y, pred.Z, pred.Vz,
                input.MoveX, input.MoveY, jumpImpulse,
                FixedDtSeconds,
                maxSpeed: 0f);

            pred.X = nx;
            pred.Y = ny;
            pred.Z = nz;
            pred.Vz = nvz;

            if (isLocal)
            {
                pred.ClientTick = CurrentClientTick;
            }

            pred.NeedsReconciliation = false;
        });
    }
}
