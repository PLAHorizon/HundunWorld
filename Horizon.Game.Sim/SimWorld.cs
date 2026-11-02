using System;
using System.Collections.Generic;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;

namespace Horizon.Game.Sim;

/// <summary>
/// 自包含的确定性模拟世界（P1-b 心脏的容器层）。
/// <para>
/// 职责：实体注册 → 输入排队（<see cref="SimWorldEntity.PendingInputs"/>）→ 按帧推进
/// （<see cref="Step"/> 批量回放 + <see cref="MovementValidator"/> 校验）→ 产出权威位置/
/// 速度/移动模式与 <see cref="CorrectionPacket"/>。双端共用：<c>ZoneShardGrain</c>（Orleans
/// 权威回放）与未来的 UE5 DS（<c>SimDriver</c>）都以本类为唯一模拟驱动，杜绝"双份状态 +
/// 双向 ring-call"，使灰度切换退化为"同一套模拟换宿主"。
/// </para>
/// <para>
/// 确定性约束：状态只能由 <see cref="SubmitInput"/> + <see cref="Step"/> 驱动；
/// 不得依赖 <c>DateTime.Now</c>、随机源或遍历顺序不定的集合。RTT 估计（EMA）仅用于
/// 动态放宽校验阈值，不影响权威位置计算（与 <c>ZoneShardGrain</c> 行为一致）。
/// </para>
/// </summary>
public sealed class SimWorld
{
    private readonly MovementValidator _validator;

    /// <summary>每帧固定步长（秒），与 <see cref="MovementValidator.Options.TickDtSeconds"/> 一致。</summary>
    private readonly float _stepDt;

    /// <summary>
    /// 本世界所有实体的权威状态（key = 实体网络 ID）。
    /// 使用 <see cref="SortedDictionary{TKey,TValue}"/> 保证遍历按实体 ID 升序、跨运行/进程稳定，
    /// 使 <see cref="Step"/> 产出的 <see cref="StepResult"/> 顺序确定（修复确定性契约违反）。
    /// </summary>
    private readonly SortedDictionary<ulong, SimWorldEntity> _entities = new();

    /// <summary>Step 返回结果复用缓冲（单线程 tick 串行，无并发风险）。</summary>
    private readonly List<StepResult> _stepResults = new();

    public SimWorld(MovementValidator.Options? options = null)
    {
        _validator = new MovementValidator(options ?? new MovementValidator.Options());
        _stepDt = _validator.TickDtSeconds;
    }

    /// <summary>本世界使用的确定性公式版本。</summary>
    public int FormulaVersion => MovementFormula.FormulaVersion;

    /// <summary>内部校验器（选项读取 / 外部诊断用）。</summary>
    public MovementValidator Validator => _validator;

    /// <summary>实体数量。</summary>
    public int EntityCount => _entities.Count;

    /// <summary>所有实体的只读视图。</summary>
    public IReadOnlyCollection<SimWorldEntity> Entities => _entities.Values;

    /// <summary>按 ID 查询实体；不存在返回 null。</summary>
    public SimWorldEntity? GetEntity(ulong entityId)
        => _entities.TryGetValue(entityId, out var e) ? e : null;

    /// <summary>世界内所有实体当前积压的输入总数（负载/诊断用）。</summary>
    public int PendingInputCount
    {
        get
        {
            int count = 0;
            foreach (var e in _entities.Values)
                count += e.PendingInputs.Count;
            return count;
        }
    }

    /// <summary>
    /// 注册或更新实体。已存在时仅更新 <see cref="SimWorldEntity.MaxSpeed"/>（保留位置/输入队列），
    /// 与 <c>ZoneShardGrain.RegisterEntityAsync</c> 语义一致。
    /// </summary>
    public void AddOrUpdateEntity(ulong entityId, float x, float y, float z, float maxSpeed)
    {
        if (_entities.TryGetValue(entityId, out var existing))
        {
            existing.MaxSpeed = maxSpeed;
            return;
        }
        _entities[entityId] = new SimWorldEntity
        {
            EntityId = entityId,
            X = x,
            Y = y,
            Z = z,
            Vz = 0f,
            Yaw = 0f,
            MaxSpeed = maxSpeed,
            IsGrounded = true,
            PrevX = x,
            PrevY = y,
            PrevZ = z,
            PrevVz = 0f,
        };
    }

    /// <summary>移除实体（含其输入队列）。</summary>
    public void RemoveEntity(ulong entityId) => _entities.Remove(entityId);

    /// <summary>
    /// 把实体的权威位置直接对齐到指定坐标（重连握手对齐 / 恢复自愈场景）。
    /// 同步重置垂直速度与"客户端报告终点"，使下一 tick 的回放从该点起步、零漂移。
    /// 保留 <see cref="SimWorldEntity.LastProcessedClientTick"/> 不重置（客户端 tick 持续递增，
    /// 使 P-F12 间隙吸附在重连后首包继续生效）。
    /// </summary>
    public void SetPosition(ulong entityId, float x, float y, float z)
    {
        if (!_entities.TryGetValue(entityId, out var e))
        {
            AddOrUpdateEntity(entityId, x, y, z, MovementFormula.DefaultMaxSpeed);
            e = _entities[entityId];
        }
        e.X = x;
        e.Y = y;
        e.Z = z;
        e.Vz = 0f;
        e.PrevX = x;
        e.PrevY = y;
        e.PrevZ = z;
        e.PrevVz = 0f;
        e.ReportedEndX = x;
        e.ReportedEndY = y;
        e.ReportedEndZ = z;
    }

    /// <summary>
    /// 传送：把实体权威位置直接对齐到目标坐标并设置朝向，同时**丢弃传送前的待处理输入**。
    /// <para>
    /// 与 <see cref="SetPosition"/> 的关键区别：传送后若保留 <see cref="SimWorldEntity.PendingInputs"/>，
    /// 那些"传送前"的输入会在新位置继续被回放，把玩家又走回原处（且会污染瞬移/漂移基线）。
    /// 因此快速旅行、场景切换等主动传送必须走本方法。
    /// </para>
    /// </summary>
    /// <param name="entityId">实体 Id</param>
    /// <param name="x">目标 X（ECS：左右）</param>
    /// <param name="y">目标 Y（ECS：前后）</param>
    /// <param name="z">目标 Z（ECS：上下）</param>
    /// <param name="yawRadians">目标朝向（弧度）</param>
    public void TeleportTo(ulong entityId, float x, float y, float z, float yawRadians)
    {
        SetPosition(entityId, x, y, z);

        if (_entities.TryGetValue(entityId, out var e))
        {
            e.Yaw = yawRadians;
            e.PendingInputs.Clear();
        }
    }

    /// <summary>
    /// 提交一帧客户端输入：入队到 <see cref="SimWorldEntity.PendingInputs"/>，
    /// 更新"客户端报告终点"（下一 tick drift 校验基准）与 RTT 估计（EMA）。
    /// <para>
    /// 未知实体自动重注册（自愈）：以客户端上报终点作为权威位置，MaxSpeed 取输入值。
    /// 首次输入 / Grain 恢复后首输入：位置对齐到客户端报告终点，并把该帧移动分量置零
    /// （位移已体现在终点中，避免回放双倍位移）。这些语义与 <c>ZoneShardGrain.SubmitInputAsync</c> 一致。
    /// </para>
    /// </summary>
    /// <param name="estimatedRttMs">
    /// 由宿主显式提供的客户端→服务端单向延迟估计（毫秒，0 表示不放宽校验阈值）。
    /// SimWorld 自身不再从墙钟推导 RTT，以保证确定性（不读 DateTimeOffset.UtcNow、
    /// 不把 ClientTick 当时间戳）；宿主若需动态放宽阈值，须用真实发送时间戳计算后传入。
    /// </param>
    /// <returns>是否自动重注册了未知实体（调用方据此告警）。</returns>
    public bool SubmitInput(ulong entityId, InputPacket input, float reportedEndX, float reportedEndY, float reportedEndZ, float estimatedRttMs = 0f)
    {
        bool autoRegistered = false;
        if (!_entities.TryGetValue(entityId, out var e))
        {
            _entities[entityId] = new SimWorldEntity
            {
                EntityId = entityId,
                X = reportedEndX,
                Y = reportedEndY,
                Z = reportedEndZ,
                Vz = 0f,
                Yaw = input?.LookYaw ?? 0f,
                MaxSpeed = input is { MaxSpeed: > 0 } ? input.MaxSpeed : 6f,
                IsGrounded = true,
                PrevX = reportedEndX,
                PrevY = reportedEndY,
                PrevZ = reportedEndZ,
                PrevVz = 0f,
            };
            e = _entities[entityId];
            autoRegistered = true;
        }

        // 首次输入 / Grain 恢复后首输入（LastSyncTick>0 且 LastProcessedClientTick==0）：
        // 位置对齐到客户端报告终点，本帧移动分量置零。
        if (e.LastSyncTick == 0 || (e.LastSyncTick > 0 && e.LastProcessedClientTick == 0))
        {
            e.X = reportedEndX;
            e.Y = reportedEndY;
            e.Z = reportedEndZ;
            e.Vz = 0f;
            input = new InputPacket
            {
                ClientTick = input.ClientTick,
                InputBits = input.InputBits,
                LookYaw = input.LookYaw,
                LookPitch = input.LookPitch,
                MoveX = 0f,
                MoveY = 0f,
                CharacterId = input.CharacterId,
                PredictedEndX = input.PredictedEndX,
                PredictedEndY = input.PredictedEndY,
                PredictedEndZ = input.PredictedEndZ,
                MaxSpeed = input.MaxSpeed,
            };
        }

        e.PendingInputs.Add(input);
        e.ReportedEndX = reportedEndX;
        e.ReportedEndY = reportedEndY;
        e.ReportedEndZ = reportedEndZ;

        // §12.2 动态 RTT 阈值：由宿主显式传入（estimatedRttMs），EMA（α=0.3）平滑抖动，
        // 供 MovementValidator 动态放宽校验阈值。修复：不再从 DateTimeOffset.UtcNow 推导、
        // 也不把 ClientTick（帧序号）误当 Unix 时间戳——消除注入墙钟导致的跨主机修正输出发散。
        if (estimatedRttMs > 0f && estimatedRttMs < 5000f)
        {
            const float alpha = 0.3f;
            e.EstimatedRttMs = e.EstimatedRttMs <= 0f
                ? estimatedRttMs
                : e.EstimatedRttMs + alpha * (estimatedRttMs - e.EstimatedRttMs);
        }

        return autoRegistered;
    }

    /// <summary>
    /// 尝试为实体开始一次翻越：校验「起点贴合权威位置 + 计划限值 + 冷却 + 不叠加」，
    /// 通过后由 <see cref="Step"/> 按共享曲线推进权威位置。
    /// 客户端生成计划时必须用同一份 <see cref="TraversalLimits"/> 预检，否则会出现
    /// "本地能翻、服务端一律拒"的体感。
    /// </summary>
    /// <returns><see cref="TraversalRejectReason.None"/> = 已受理；其余为拒绝原因。</returns>
    public TraversalRejectReason TryBeginTraversal(ulong entityId, in TraversalPlan plan, long serverTick)
    {
        if (!_entities.TryGetValue(entityId, out var e))
            return TraversalRejectReason.StartTooFar;

        if (e.TraversalActive)
            return TraversalRejectReason.AlreadyActive;

        var limits = _validator.Settings.Traversal;
        var cooldownTicks = (long)MathF.Ceiling(MathF.Max(0f, limits.CooldownSeconds) / _stepDt);
        if (e.LastTraversalEndTick > 0 && serverTick - e.LastTraversalEndTick < cooldownTicks)
            return TraversalRejectReason.Cooldown;

        var reason = TraversalMath.Validate(plan, e.X, e.Y, e.Z, limits);
        if (reason != TraversalRejectReason.None)
            return reason;

        e.TraversalActive = true;
        e.TraversalElapsed = 0f;
        e.TraversalPlan = plan;
        e.TraversalStartYaw = e.Yaw;
        e.Vz = 0f;
        e.JumpCount = 0;
        e.LastSyncTick = serverTick;
        return TraversalRejectReason.None;
    }

    /// <summary>实体是否正在翻越（宿主诊断/测试用）。</summary>
    public bool IsTraversalActive(ulong entityId)
        => _entities.TryGetValue(entityId, out var e) && e.TraversalActive;

    /// <summary>
    /// 推进一帧（<paramref name="serverTick"/> 为当前服务器 tick）：对每个实体批量回放
    /// 积压输入（含 P-F12 间隙吸附 / P-F13 速度归一），产出权威位置/速度/移动模式与
    /// <see cref="CorrectionPacket"/>（预测漂移 / 超速 / 跳跃超限 / 加速度超限）。
    /// </summary>
    public IReadOnlyList<StepResult> Step(long serverTick)
    {
        _stepResults.Clear();
        foreach (var e in _entities.Values)
        {
            _stepResults.Add(StepEntity(e, serverTick));
        }
        return _stepResults;
    }

    /// <summary>
    /// 翻越帧推进：位置/朝向沿共享曲线走，垂直速度为 0；本 tick 到达计划终点即落点并清状态。
    /// 期间积压输入照常消费（推进 <see cref="SimWorldEntity.LastProcessedClientTick"/> 以便回 ack），
    /// 但不参与位置计算，也不做漂移/加速度纠偏——曲线本身就是权威。
    /// </summary>
    private StepResult StepTraversal(SimWorldEntity e, long serverTick, WorldPosition startPos, float startYaw)
    {
        var plan = e.TraversalPlan;
        var elapsed = e.TraversalElapsed + _stepDt;
        var finished = elapsed >= plan.Duration;
        var (x, y, z) = TraversalMath.Sample(plan, elapsed);

        e.X = x;
        e.Y = y;
        e.Z = z;
        e.Vz = 0f;
        e.IsGrounded = true;
        e.JumpCount = 0;
        e.TraversalElapsed = finished ? plan.Duration : elapsed;
        e.Yaw = finished
            ? plan.EndYaw
            : TraversalMath.SampleYaw(e.TraversalStartYaw, plan, e.TraversalElapsed);

        if (finished)
        {
            // 端点显式落位：采样在 t=1 已等于终点，这里再钉一次防浮点边界。
            e.X = plan.EndX;
            e.Y = plan.EndY;
            e.Z = plan.EndZ;
            e.TraversalActive = false;
            e.LastTraversalEndTick = serverTick;
        }

        long lastProcessedClientTick = e.LastProcessedClientTick;
        var consumed = e.PendingInputs.Count;
        if (consumed > 0)
        {
            lastProcessedClientTick = e.PendingInputs[consumed - 1].ClientTick;
            e.LastProcessedClientTick = lastProcessedClientTick;
            e.PendingInputs.Clear();
        }
        // 翻越帧不回放输入：计数必须归零，否则宿主会把"没回放"读成"回放了这么多帧"。
        e.LastStepReplayedInputCount = 0;
        e.LastSyncTick = serverTick;

        var velX = (e.X - startPos.X) / _stepDt;
        var velY = (e.Y - startPos.Y) / _stepDt;
        e.VelocityXZ_X = velX;
        e.VelocityXZ_Y = velY;
        var speedSq = velX * velX + velY * velY;
        e.MovementMode = speedSq > 0.1f ? MovementMode.Run : MovementMode.Walk;

        e.PrevX = e.X;
        e.PrevY = e.Y;
        e.PrevZ = e.Z;
        e.PrevVz = 0f;
        e.PrevVelocityXZ_X = velX;
        e.PrevVelocityXZ_Y = velY;

        return new StepResult(
            e.EntityId,
            startPos,
            startYaw,
            new WorldPosition(e.X, e.Y, e.Z),
            0f,
            velX,
            velY,
            e.MovementMode,
            e.Yaw,
            true,
            consumed > 0,
            lastProcessedClientTick,
            null);
    }

    private StepResult StepEntity(SimWorldEntity e, long serverTick)
    {
        // 变化检测基准（广播用）：本 tick 开始时的权威位置与朝向。
        var startPos = new WorldPosition(e.X, e.Y, e.Z);
        var startYaw = e.Yaw;

        // P-F12：回放起点（间隙吸附后可能与 startPos 不同），速度计算同样以此为准。
        var replayStartPos = startPos;
        var replayedFrameCount = 0;
        // 无输入 tick 复位回放计数：字段契约"0 = 无输入"，避免保留上一 tick 的计数误导诊断/对拍。
        // （有输入时下方按 replayInputs.Length 覆盖；唯一消费方 _processedInputs 以 step.HadInput 为守卫。）
        e.LastStepReplayedInputCount = 0;
        float velX = 0f, velY = 0f;
        CorrectionPacket? correction = null;
        long lastProcessedClientTick = e.LastProcessedClientTick;

        // 翻越窗口：位置/朝向由共享曲线决定，忽略一切移动输入（客户端报告终点也不参与校验）。
        if (e.TraversalActive)
        {
            return StepTraversal(e, serverTick, startPos, startYaw);
        }

        if (e.PendingInputs.Count > 0)
        {
            // 朝向取本 tick 积压的最后一个输入（批量回放期间的朝向刷新语义）。
            e.Yaw = e.PendingInputs[e.PendingInputs.Count - 1].LookYaw;

            e.EnsureInputBuffer(e.PendingInputs.Count);
            e.PendingInputs.CopyTo(e.PendingInputBuffer!, 0);
            ReadOnlySpan<InputPacket> inputs = new(e.PendingInputBuffer!, 0, e.PendingInputs.Count);

            // P-F12 输入间隙回放起点修复：间隙后吸附到首个缺失间隙输入的 PredictedEnd
            // （其位移已含在终点内），回放跳过首个输入只重放其余 → 权威终点 = 客户端最后上报终点，零过冲。
            var replayInputs = inputs;
            if (e.LastProcessedClientTick > 0 && inputs.Length > 0
                && inputs[0].ClientTick > e.LastProcessedClientTick + 1)
            {
                e.X = inputs[0].PredictedEndX;
                e.Y = inputs[0].PredictedEndY;
                e.Z = inputs[0].PredictedEndZ;
                e.Vz = 0f;
                replayInputs = inputs.Slice(1);
            }

            replayedFrameCount = replayInputs.Length;
            replayStartPos = new WorldPosition(e.X, e.Y, e.Z);
            var clientEnd = new WorldPosition(e.ReportedEndX, e.ReportedEndY, e.ReportedEndZ);

            // 1.3.4：跨 tick 跳跃状态持久化——把实体遗留计数传入权威回放（轻功连按跨批延续段数），
            // 回写结果计数供下一批使用；接地时校验器内部已清零（见 MovementValidator 地面约束分支）。
            var result = _validator.Validate(
                e.EntityId,
                replayStartPos,
                e.Vz,
                replayInputs,
                clientEnd,
                serverTick,
                e.EstimatedRttMs,
                e.JumpCount);

            e.X = result.AuthoritativeEnd.X;
            e.Y = result.AuthoritativeEnd.Y;
            e.Z = result.AuthoritativeEnd.Z;
            e.Vz = result.AuthoritativeVz;

            e.PendingInputs.Clear();
            e.LastSyncTick = serverTick;
            e.LastProcessedClientTick = inputs[inputs.Length - 1].ClientTick;
            e.LastStepReplayedInputCount = replayInputs.Length;
            lastProcessedClientTick = e.LastProcessedClientTick;

            // 下坡时 isGrounded 误判修复：Vz≈0 或校验器显式标记接地 → 接地。
            var isGrounded = MathF.Abs(result.AuthoritativeVz) < 0.01f || result.IsGrounded;
            e.IsGrounded = isGrounded;
            // 回写权威跳跃计数（接地时 0，由校验器落地分支保证；显式再钳一次防边界语义漂移）。
            e.JumpCount = isGrounded ? 0 : result.JumpCount;

            correction = result.Correction;
        }

        // P-F13 速度归一：一个 tick 可能回放多个积压输入（客户端打包上行/网络批处理），
        // 除以真实回放帧数×dt，匀速移动速度恒定，避免加速度误判。
        var replaySeconds = Math.Max(1, replayedFrameCount) * _stepDt;
        velX = (e.X - replayStartPos.X) / replaySeconds;
        velY = (e.Y - replayStartPos.Y) / replaySeconds;
        e.VelocityXZ_X = velX;
        e.VelocityXZ_Y = velY;

        // 根据速度推导移动模式：区分 Jump（上升）与 Fall（下降），供客户端动画状态机。
        var speedSq = velX * velX + velY * velY;
        MovementMode movementMode;
        if (!e.IsGrounded)
        {
            movementMode = e.Vz > 0f ? MovementMode.Jump : MovementMode.Fall;
        }
        else if (speedSq > 0.1f)
        {
            movementMode = MovementMode.Run;
        }
        else
        {
            movementMode = MovementMode.Walk;
        }
        e.MovementMode = movementMode;

        // P2.6 加速度/瞬移检测：仅对"本 tick 有输入"的实体做检测
        // （replayedFrameCount>0 表示本 tick 消费了积压输入；PendingInputs 已 Clear）。
        if (replayedFrameCount > 0)
        {
            var prevPos = new WorldPosition(e.PrevX, e.PrevY, e.PrevZ);
            var curPos = new WorldPosition(e.X, e.Y, e.Z);
            var prevSpeed = MathF.Sqrt(e.PrevVelocityXZ_X * e.PrevVelocityXZ_X
                + e.PrevVelocityXZ_Y * e.PrevVelocityXZ_Y);
            var curSpeed = MathF.Sqrt(velX * velX + velY * velY);

            // P-F11/P-F14 起停瞬变豁免（详见 ZoneShardGrain 注释）：瞬时加减速模型下
            // 起步/停止的 0↔MaxSpeed 阶跃合法；持续非零速度间的异常加速仍被捕获。
            var accelDtSeconds = _stepDt;
            var skipAccelerationCheck = e.LastInputValidatedTick <= 0
                || prevSpeed < 0.01f
                || curSpeed < 0.01f;
            if (!skipAccelerationCheck)
            {
                var ticksSinceLastValidation = serverTick - e.LastInputValidatedTick;
                accelDtSeconds = Math.Max(1, ticksSinceLastValidation) * _stepDt;
            }
            var accelResult = _validator.CheckAcceleration(
                prevPos, curPos, prevSpeed,
                skipAccelerationCheck ? prevSpeed : curSpeed,
                accelDtSeconds);
            e.LastInputValidatedTick = serverTick;
            if (!accelResult.IsValid)
            {
                correction = new CorrectionPacket
                {
                    EntityId = e.EntityId,
                    ServerTick = serverTick,
                    CorrectedX = e.X,
                    CorrectedY = e.Y,
                    CorrectedZ = e.Z,
                    CorrectedVz = e.Vz,
                    DriftMeters = accelResult.DistanceTraveled,
                    Reason = accelResult.Violation == MovementValidator.AccelerationViolation.TeleportDetected
                        ? CorrectionReason.TeleportDetected
                        : CorrectionReason.AccelerationExceeded,
                    LastProcessedClientTick = e.LastProcessedClientTick,
                };
            }
        }

        // 更新上一帧追踪（加速度/变化检测基准）。
        e.PrevX = e.X;
        e.PrevY = e.Y;
        e.PrevZ = e.Z;
        e.PrevVz = e.Vz;
        e.PrevVelocityXZ_X = velX;
        e.PrevVelocityXZ_Y = velY;

        return new StepResult(
            e.EntityId,
            startPos,
            startYaw,
            new WorldPosition(e.X, e.Y, e.Z),
            e.Vz,
            velX,
            velY,
            movementMode,
            e.Yaw,
            e.IsGrounded,
            replayedFrameCount > 0,
            lastProcessedClientTick,
            correction);
    }

    /// <summary>单实体单帧推进结果（供宿主广播 / 修正分发消费）。</summary>
    public readonly struct StepResult
    {
        /// <summary>实体网络 ID。</summary>
        public ulong EntityId { get; }

        /// <summary>本 tick 开始时的权威位置（广播变化检测基准）。</summary>
        public WorldPosition StartPosition { get; }

        /// <summary>本 tick 开始时的朝向（广播变化检测基准）。</summary>
        public float StartYaw { get; }

        /// <summary>本帧权威位置（Z-up）。</summary>
        public WorldPosition AuthoritativeEnd { get; }

        /// <summary>本帧权威 Z 方向速度。</summary>
        public float AuthoritativeVz { get; }

        /// <summary>本帧水平速度 X（按真实回放帧数归一）。</summary>
        public float VelocityX { get; }

        /// <summary>本帧水平速度 Y（按真实回放帧数归一）。</summary>
        public float VelocityY { get; }

        /// <summary>本帧推导移动模式（Walk/Run/Jump/Fall）。</summary>
        public MovementMode MovementMode { get; }

        /// <summary>本帧朝向（积压输入最后一个 LookYaw）。</summary>
        public float Yaw { get; }

        /// <summary>本帧是否接地。</summary>
        public bool IsGrounded { get; }

        /// <summary>本 tick 是否处理了输入。</summary>
        public bool HadInput { get; }

        /// <summary>本帧处理到的最大客户端 tick（InputAck 用）。</summary>
        public long LastProcessedClientTick { get; }

        /// <summary>需要下发的修正包；null 表示无需修正。</summary>
        public CorrectionPacket? Correction { get; }

        public StepResult(
            ulong entityId,
            WorldPosition startPosition,
            float startYaw,
            WorldPosition authoritativeEnd,
            float authoritativeVz,
            float velocityX,
            float velocityY,
            MovementMode movementMode,
            float yaw,
            bool isGrounded,
            bool hadInput,
            long lastProcessedClientTick,
            CorrectionPacket? correction)
        {
            EntityId = entityId;
            StartPosition = startPosition;
            StartYaw = startYaw;
            AuthoritativeEnd = authoritativeEnd;
            AuthoritativeVz = authoritativeVz;
            VelocityX = velocityX;
            VelocityY = velocityY;
            MovementMode = movementMode;
            Yaw = yaw;
            IsGrounded = isGrounded;
            HadInput = hadInput;
            LastProcessedClientTick = lastProcessedClientTick;
            Correction = correction;
        }
    }
}

/// <summary>
/// 模拟世界中的单个实体：权威位置/速度/朝向 + 输入队列 + 校验追踪状态。
/// 字段全部可变（确定性推进由 <see cref="SimWorld.Step"/> 独占驱动）。
/// </summary>
public sealed class SimWorldEntity
{
    /// <summary>实体网络 ID（与 <see cref="CorrectionPacket.EntityId"/> 对应）。</summary>
    public ulong EntityId;

    /// <summary>权威位置 X（Z-up 世界坐标）。</summary>
    public float X;
    /// <summary>权威位置 Y。</summary>
    public float Y;
    /// <summary>权威位置 Z（上下）。</summary>
    public float Z;
    /// <summary>Z 方向速度。</summary>
    public float Vz;
    /// <summary>水平朝向（Yaw，弧度）。</summary>
    public float Yaw;
    /// <summary>最大水平速度（米/秒）。</summary>
    public float MaxSpeed;
    /// <summary>本帧是否接地。</summary>
    public bool IsGrounded;
    /// <summary>跳跃计数（接地时归零，供校验器多段跳语义）。</summary>
    public int JumpCount;

    /// <summary>待处理输入队列（按 ClientTick 升序）。</summary>
    public readonly List<InputPacket> PendingInputs = new();

    /// <summary>上一次 Step 实际回放的输入帧数（0 = 无输入；指标/诊断用）。</summary>
    public int LastStepReplayedInputCount;

    /// <summary>Task 10.3：PendingInputs 复用缓冲，避免每 tick ToArray 分配。</summary>
    public InputPacket[]? PendingInputBuffer;

    /// <summary>最后一次被权威同步的服务器 tick（0 表示尚未同步）。</summary>
    public long LastSyncTick;

    /// <summary>最后处理的客户端 tick（输入间隙检测基准）。</summary>
    public long LastProcessedClientTick;

    /// <summary>上次执行加速度校验的服务器 tick。</summary>
    public long LastInputValidatedTick;

    /// <summary>客户端报告的终点 X（drift 校验基准）。</summary>
    public float ReportedEndX;
    /// <summary>客户端报告的终点 Y。</summary>
    public float ReportedEndY;
    /// <summary>客户端报告的终点 Z。</summary>
    public float ReportedEndZ;

    /// <summary>上一帧权威位置 X（加速度/瞬移检测）。</summary>
    public float PrevX;
    /// <summary>上一帧权威位置 Y。</summary>
    public float PrevY;
    /// <summary>上一帧权威位置 Z。</summary>
    public float PrevZ;
    /// <summary>上一帧 Z 方向速度。</summary>
    public float PrevVz;
    /// <summary>上一帧水平速度 X。</summary>
    public float PrevVelocityXZ_X;
    /// <summary>上一帧水平速度 Y。</summary>
    public float PrevVelocityXZ_Y;

    /// <summary>本帧推导的水平速度 X（广播用）。</summary>
    public float VelocityXZ_X;
    /// <summary>本帧推导的水平速度 Y（广播用）。</summary>
    public float VelocityXZ_Y;

    /// <summary>本帧推导移动模式（广播用）。</summary>
    public MovementMode MovementMode;

    /// <summary>估算的客户端→服务端单向延迟（毫秒），EMA 平滑，供动态阈值放宽。</summary>
    public float EstimatedRttMs;

    /// <summary>
    /// 是否处于翻越脚本位移中。为 true 时 <see cref="SimWorld.Step"/> 忽略移动/跳跃输入，
    /// 由 <see cref="TraversalMath.Sample"/> 推进位置。
    /// </summary>
    public bool TraversalActive;

    /// <summary>当前翻越已用时长（秒）。</summary>
    public float TraversalElapsed;

    /// <summary>当前翻越计划（<see cref="TraversalActive"/> 时有意义）。</summary>
    public TraversalPlan TraversalPlan;

    /// <summary>翻越开始时的朝向（弧度），用于按最短弧插值到计划终点朝向。</summary>
    public float TraversalStartYaw;

    /// <summary>上一次翻越结束的服务器 tick（0 = 从未翻越；冷却判定基准）。</summary>
    public long LastTraversalEndTick;

    /// <summary>当前权威位置（只读视图）。</summary>
    public WorldPosition Position => new(X, Y, Z);

    /// <summary>确保输入缓冲容量足够（懒初始化 + 扩容）。</summary>
    internal void EnsureInputBuffer(int count)
    {
        if (PendingInputBuffer is null || PendingInputBuffer.Length < count)
        {
            PendingInputBuffer = new InputPacket[count];
        }
    }
}
