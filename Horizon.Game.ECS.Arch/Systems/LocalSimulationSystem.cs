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

    /// <summary>
    /// 地面高度采样委托：给定 ECS 世界坐标 (x, y)（ECS 为 Z-up，x=左右, y=前后），
    /// 返回该位置对应的地面 ECS.Z 高度（米）。
    /// <para>
    /// ECS 层为纯逻辑，不能依赖 FlaxEngine.Physics；由 <c>HundunWorldGame</c> 在启动时
    /// 注入一个使用 <c>Physics.RayCast</c> 采样 Terrain/碰撞体的回调。
    /// 若委托为 null（未注入），则不做地面约束，角色将受重力持续下落穿透 Terrain。
    /// 委托返回 <c>float.NaN</c> 表示采样失败（无地面），本系统会跳过约束。
    /// </para>
    /// </summary>
    public Func<float, float, float>? GroundHeightSampler { get; set; }

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
            // 修复：用边沿触发 JumpPressedThisFrame 替代 InputBits bit0 推断。
            // 原实现 (input.InputBits & 0x1) != 0 在持续按住空格时每帧为 true，
            // 轻功分支 jumpCount++ 会在 3 帧（50ms）内消耗完三段跳。
            // 改用 JumpPressedThisFrame（仅按下边沿为 true）后，持续按住只触发一次 jumpCount++。
            // 非轻功分支原 jumpCount = 1 重置保证只跳一次，边沿触发后行为一致。
            var isJumpPressed = input.JumpPressedThisFrame;

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
                maxSpeed: input.MaxSpeed);

            // 地面碰撞检测：采样 (nx, ny) 处的地面 ECS.Z 高度，
            // 若新位置低于地面则吸附到地面并清零垂直速度，防止角色穿透 Terrain。
            // GroundHeightSampler 由 HundunWorldGame 注入（使用 FlaxEngine.Physics.RayCast）。
            // 未注入或返回 NaN 时跳过约束（保持原行为，仅用于测试/离线场景）。
            var sampler = GroundHeightSampler;
            if (sampler != null)
            {
                var groundZ = sampler(nx, ny);
                if (!float.IsNaN(groundZ) && nz < groundZ)
                {
                    nz = groundZ;
                    nvz = 0f;
                    // 落地后重置跳跃计数，允许下一轮三段跳
                    _jumpCounts[entity.Id] = 0;
                    jumpCount = 0;
                }
            }

            pred.X = nx;
            pred.Y = ny;
            pred.Z = nz;
            pred.Vz = nvz;

            if (isLocal)
            {
                pred.ClientTick = CurrentClientTick;

                // 修复：将 input.LookYaw 写入 pred.Yaw，使 LocalPlayerActorSyncSystem 能读到朝向。
                // 原实现完全不更新 pred.Yaw，导致 LocalPlayerActorSyncSystem 读到的 Yaw 永远是初始值 0。
                // 服务端 ZoneShardGrain.TickAsync 也从 lastInput.LookYaw 提取 entity.Yaw，
                // 客户端写入 pred.Yaw 后 LocalPlayerActorSyncSystem 据此设置 Actor.Orientation。
                pred.Yaw = input.LookYaw;

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
