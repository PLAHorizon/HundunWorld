using System;
using System.Collections.Generic;
using Arch.Core;
using Horizon.Game.Sim;
using Horizon.Game.Core.World;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Core;
// MovementFormula 统一使用 Horizon.Game.Sim 版本（原 Core.Sim 副本已收敛删除）。
using MovementFormula = Horizon.Game.Sim.MovementFormula;

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

    /// <summary>
    /// 单帧移动推进（阶段 1.3）：三段跳冲量/跳跃计数/公式调用唯一实现，与
    /// <see cref="ReconciliationSystem"/>（修正重放）共用同一 stepper，消除预测 vs 重放分叉。
    /// </summary>
    public IMovementStepper Stepper { get; set; } = new MovementFormulaStepper();

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

    /// <summary>
    /// 本地翻越预测状态（key = Arch entity.Id）。与 <see cref="_jumpCounts"/> 同一生命周期管理：
    /// 进入时由 <see cref="BeginLocalTraversal"/> 写入，曲线走完或被权威拒绝时移除。
    /// </summary>
    private readonly Dictionary<int, TraversalRuntime> _traversals = new();

    private struct TraversalRuntime
    {
        public TraversalPlan Plan;
        public float Elapsed;
        public float StartYaw;
    }

    /// <summary>翻越曲线在本地走完（entityId）。宿主据此结束 GASP 翻越动画。</summary>
    public event Action<int>? LocalTraversalFinished;

    /// <summary>翻越曲线在本地被移除（含权威拒绝/修正），参数 entityId。</summary>
    public event Action<int>? LocalTraversalCancelled;

    /// <summary>本机玩家是否在翻越预测中（诊断/宿主查询）。</summary>
    public bool IsTraversalActive(int entityId) => _traversals.ContainsKey(entityId);

    /// <summary>本机玩家当前翻越已用时长（无翻越返回 0）。</summary>
    public float TraversalElapsed(int entityId)
        => _traversals.TryGetValue(entityId, out var t) ? t.Elapsed : 0f;

    /// <summary>
    /// 为本机玩家登记一次翻越预测：下一 FixedUpdate 起位置/朝向由共享曲线推进，移动输入被忽略。
    /// 计划必须已经过 <see cref="TraversalMath.Validate"/>（客户端预检与服务端同一判据）。
    /// </summary>
    /// <param name="entityId">本地玩家 Arch entity.Id。</param>
    /// <param name="plan">翻越计划（ECS 坐标，米）。</param>
    /// <param name="startYaw">起始朝向（弧度），用于最短弧插值。</param>
    /// <returns>登记成功；同一实体已有进行中的翻越时返回 false（不叠加）。</returns>
    public bool BeginLocalTraversal(int entityId, in TraversalPlan plan, float startYaw)
    {
        if (_traversals.ContainsKey(entityId))
            return false;
        _traversals[entityId] = new TraversalRuntime
        {
            Plan = plan,
            Elapsed = 0f,
            StartYaw = startYaw,
        };
        return true;
    }

    /// <summary>
    /// 取消本地翻越预测（权威拒绝 / 收到修正 / 会话切换）。不改变位置——回正由权威修正包负责。
    /// </summary>
    /// <returns>是否真的移除了进行中的翻越。</returns>
    public bool CancelLocalTraversal(int entityId)
    {
        if (!_traversals.Remove(entityId))
            return false;
        LocalTraversalCancelled?.Invoke(entityId);
        return true;
    }

    /// <summary>
    /// 修复「进入游戏后在天上飞」：初始帧地面锁定计数器。
    /// 系统启动后的前 N 帧，若 GroundHeightSampler 返回 NaN（场景未加载/地形碰撞未就绪），
    /// 则冻结垂直位置（不施加重力），防止角色在场景过渡期间坠入虚空。
    /// 一旦采样成功或超过保护帧数，恢复正常重力模拟。
    /// </summary>
    private int _groundLockFramesRemaining = 120; // 保护 2 秒（60fps × 2）

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

            // 翻越预测：曲线由共享 TraversalMath 推进，移动输入被忽略（与服务端权威同一分支）。
            if (_traversals.TryGetValue(entity.Id, out var traversal))
            {
                StepLocalTraversal(world, entity, ref pred, traversal, isLocal);
                return;
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

            // 阶段 1.3：跳跃状态机（轻功三段/普通单跳/未按清零）+ 公式调用收敛到 IMovementStepper，
            // 与 ReconciliationSystem 重放共用同一实现（预测与重放消除分叉）。
            var step = Stepper.Step(new MovementStepInput
            {
                X = pred.X,
                Y = pred.Y,
                Z = pred.Z,
                Vz = pred.Vz,
                MoveX = input.MoveX,
                MoveY = input.MoveY,
                JumpPressed = isJumpPressed,
                IsQinggongJump = isQinggongJump,
                JumpCount = jumpCount,
                MaxSpeed = input.MaxSpeed,
            }, FixedDtSeconds);

            jumpCount = step.JumpCount;
            _jumpCounts[entity.Id] = jumpCount;

            var nx = step.X;
            var ny = step.Y;
            var nz = step.Z;
            var nvz = step.Vz;

            // 地面碰撞检测：采样 (nx, ny) 处的地面 ECS.Z 高度，
            // 若新位置低于地面则吸附到地面并清零垂直速度，防止角色穿透 Terrain。
            // GroundHeightSampler 由 HundunWorldGame 注入（使用 FlaxEngine.Physics.RayCast）。
            // 未注入时不约束；返回 NaN（此处地面高度未知）时悬停，见下方说明。
            var sampler = GroundHeightSampler;
            if (sampler != null)
            {
                var groundZ = sampler(nx, ny);
                if (!float.IsNaN(groundZ))
                {
                    if (nz < groundZ)
                    {
                        nz = groundZ;
                        nvz = 0f;
                        // 落地后重置跳跃计数，允许下一轮三段跳
                        _jumpCounts[entity.Id] = 0;
                        jumpCount = 0;
                    }
                }
                else
                {
                    // 采样失败：冻结垂直位置，不施加重力。
                    // 这里不能"保护 N 帧后放行"：NaN 的含义是"此处的地面高度未知"，而不是"确认没有地面"。
                    // 原实现在 120 帧（2s）后让重力接管，而本工程的玩法区在碰撞体就绪前一直是 NaN，
                    // 于是角色以 TerminalVelocity（实测 49cm/s）一路下坠，相机跟着穿地后每几帧被
                    // correction 拽回来一次，表现就是"停留几秒→沉入地面→反复抖动"（实测 3 分钟坠到 -91m）。
                    // 未知地面时悬停是更安全的默认：有地面的地方采样必然成功，重力与跳跃照常生效。
                    nz = pred.Z;  // 保持原始高度
                    nvz = 0f;     // 清零垂直速度
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
                UpdateLocalChunkSubscription(world, entity, in pred);
            }

            pred.NeedsReconciliation = false;
        });
    }

    /// <summary>
    /// 翻越预测单帧推进：位置/朝向沿共享曲线走，垂直速度为 0，移动/跳跃输入被忽略。
    /// 走完或被取消时移除状态；宿主通过 <see cref="LocalTraversalFinished"/> 同步结束 GASP 动画。
    /// </summary>
    private void StepLocalTraversal(World world, Entity entity, ref PredictedTransformComponent pred,
        TraversalRuntime traversal, bool isLocal)
    {
        var step = Stepper.Step(new MovementStepInput
        {
            X = pred.X,
            Y = pred.Y,
            Z = pred.Z,
            Vz = pred.Vz,
            JumpCount = 0,
            TraversalActive = true,
            TraversalPlan = traversal.Plan,
            TraversalElapsed = traversal.Elapsed,
        }, FixedDtSeconds);

        pred.X = step.X;
        pred.Y = step.Y;
        pred.Z = step.Z;
        pred.Vz = 0f;

        // 翻越期间不允许带着跳跃计数（空中段落数会跨到落点后）。
        _jumpCounts[entity.Id] = 0;
        _jumpCountsLastSeenTick[entity.Id] = CurrentClientTick;

        traversal.Elapsed = step.TraversalElapsed;
        var finished = step.TraversalFinished;
        if (finished)
            _traversals.Remove(entity.Id);
        else
            _traversals[entity.Id] = traversal;

        if (isLocal)
        {
            pred.ClientTick = CurrentClientTick;
            pred.Yaw = finished
                ? traversal.Plan.EndYaw
                : TraversalMath.SampleYaw(traversal.StartYaw, traversal.Plan, traversal.Elapsed);
            UpdateLocalChunkSubscription(world, entity, in pred);
        }

        pred.NeedsReconciliation = false;

        if (finished)
            LocalTraversalFinished?.Invoke(entity.Id);
    }

    /// <summary>
    /// 本地玩家跨 chunk 订阅维护：首次附加订阅状态组件并通知，之后只在 chunk key 变化时触发。
    /// 普通移动与翻越预测两条路径都必须调用（翻越同样会把人送过 chunk 边界）。
    /// </summary>
    private void UpdateLocalChunkSubscription(World world, Entity entity, in PredictedTransformComponent pred)
    {
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
}
