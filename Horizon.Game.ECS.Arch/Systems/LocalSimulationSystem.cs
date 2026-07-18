using System;
using System.Collections.Generic;
using Arch.Core;
using Horizon.Game.Core.Sim;
using Horizon.Game.Core.World;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Core;
using Horizon.Game.Message.Sim;
// 消除 MovementFormula 歧义：Horizon.Game.Core.Sim 与 Horizon.Game.Message.Sim 均存在同名类型，
// 添加 Horizon.Game.Core 引用后产生冲突。保持原有行为，统一使用 Message 版本。
using MovementFormula = Horizon.Game.Message.Sim.MovementFormula;

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

    /// <summary>
    /// 本地玩家跨越 chunk 边界时触发，参数为新位置的世界坐标 (X, Y, Z)（米）。
    /// <para>
    /// 由客户端网络运行时订阅，计算新视野范围内的 chunk 集合并上行
    /// <see cref="Horizon.Game.Message.Sync.SubscriptionUpdatePacket"/>。
    /// 事件在 FixedUpdate 阶段（主线程）触发，订阅方可在事件回调中直接读写 ECS 组件。
    /// </para>
    /// </summary>
    public event Action<float, float, float>? PlayerChunkChanged;

    private readonly Dictionary<int, int> _jumpCounts = new();

    // Task 5：_jumpCounts 懒清理相关字段。
    // _jumpCountsLastSeenTick 记录每个 entity.Id 上次访问时的 CurrentClientTick；
    // 每 60 帧扫描移除超过 600 tick 未见的条目，避免已销毁实体的条目持续累积导致内存泄漏。
    private readonly Dictionary<int, long> _jumpCountsLastSeenTick = new();
    private int _cleanupCounter;
    private readonly List<int> _staleJumpEntityIds = new();

    /// <inheritdoc />
    public override void Update(World world, TimeSpan deltaTime)
    {
        // Task 5：懒清理 _jumpCounts — 每 60 帧扫描，移除超过 600 tick 未访问的条目。
        if (_cleanupCounter++ % 60 == 0)
        {
            var currentTick = CurrentClientTick;
            _staleJumpEntityIds.Clear();
            foreach (var kvp in _jumpCountsLastSeenTick)
            {
                if (currentTick - kvp.Value > 600)
                    _staleJumpEntityIds.Add(kvp.Key);
            }
            foreach (var id in _staleJumpEntityIds)
            {
                _jumpCountsLastSeenTick.Remove(id);
                _jumpCounts.Remove(id);
            }
        }

        var query = new QueryDescription()
            .WithAll<PlayerInputComponent, NetworkIdentityComponent, PredictedTransformComponent>();

        world.Query(in query, (Entity entity, ref PlayerInputComponent input, ref NetworkIdentityComponent netId, ref PredictedTransformComponent pred) =>
        {
            // 本地玩家判定：通过 NetworkIdentityComponent.IsLocalPlayer 标志（与 InputSendSystem 一致）。
            // 原先使用 entity.Id == (int)LocalPlayerEntityId，但 LocalPlayerEntityId 从未被外部设置，
            // 导致 CurrentClientTick 永远不递增，上行输入链路断裂。
            var isLocal = netId.IsLocalPlayer;

            if (isLocal)
            {
                CurrentClientTick++;
            }

            var isQinggongJump = (input.InputBits & (1u << 3)) != 0;
            var isJumpPressed = (input.InputBits & 0x1) != 0;

            if (!_jumpCounts.ContainsKey(entity.Id))
                _jumpCounts[entity.Id] = 0;

            var jumpCount = _jumpCounts[entity.Id];
            _jumpCountsLastSeenTick[entity.Id] = CurrentClientTick;

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

                // 多玩家 AOI：检测本地玩家是否跨越 chunk 边界。
                // 首次：附加 PlayerSubscriptionStateComponent 并触发事件，让订阅方发起初始订阅。
                // 后续：CurrentChunkKey 与当前位置所在 chunk 不同时更新组件并触发事件，
                //       由 NetworkRuntime 计算 Added/Removed 集合并上行 SubscriptionUpdatePacket。
                var currentChunkKey = WorldCoord.ToChunkMortonKey(pred.X, pred.Y, pred.Z);
                if (!world.Has<PlayerSubscriptionStateComponent>(entity))
                {
                    var state = new PlayerSubscriptionStateComponent
                    {
                        CurrentChunkKey = currentChunkKey,
                        SubscribedChunks = null,
                        Initialized = true,
                    };
                    world.Add(entity, state);
                    PlayerChunkChanged?.Invoke(pred.X, pred.Y, pred.Z);
                }
                else
                {
                    ref var state = ref world.Get<PlayerSubscriptionStateComponent>(entity);
                    if (state.CurrentChunkKey != currentChunkKey)
                    {
                        state.CurrentChunkKey = currentChunkKey;
                        world.Set(entity, state);
                        PlayerChunkChanged?.Invoke(pred.X, pred.Y, pred.Z);
                    }
                }
            }

            pred.NeedsReconciliation = false;
        });
    }
}
