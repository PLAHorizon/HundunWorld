using System;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;

namespace Horizon.Game.Core.Sim;

/// <summary>
/// 服务器权威移动校验器（P1-b）。<br/>
/// 输入：客户端上报的 (起点, 一串 InputPacket, 终点) 元组；
/// 输出：按 <see cref="MovementFormula"/> 回放后得到的权威终点 + 是否需要发 <see cref="CorrectionPacket"/>。
/// </summary>
/// <remarks>
/// 本类**不依赖 Orleans**，可在 grain / 单测 / 反外挂扫描器中共享使用。
/// 调用方负责：
/// <list type="bullet">
///   <item>把 <see cref="InputPacket"/> 按 <c>ClientTick</c> 排序后传入（通常已经由 PlayerSessionState 排好）。</item>
///   <item>提供每个 tick 的 dt（秒），通常为 1/60 或 1/30 的定频值。</item>
///   <item>地面检测等环境条件在调用方实现（本层仅做自由运动 + 重力）。</item>
/// </list>
/// </remarks>
public sealed class MovementValidator
{
    /// <summary>默认位置偏差阈值（米）：超过此值下发 correction。</summary>
    public const float DefaultPositionEpsilon = 0.5f;

    /// <summary>
    /// 默认速度硬性上限（米/秒）：客户端 <see cref="InputPacket.MaxSpeed"/> 超此值立即判异常。
    /// v6 协议后 MaxSpeed 是合法字段（携带客户端配置的目标速度），此上限用于反作弊，
    /// 取值需容纳合理游戏速度（坐骑/轻功/技能加速等），默认 200 m/s（720 km/h）。
    /// </summary>
    public const float DefaultHardSpeedCap = 200f;

    /// <summary>配置项。</summary>
    public sealed class Options
    {
        public float PositionEpsilon { get; set; } = DefaultPositionEpsilon;
        public float HardSpeedCap { get; set; } = DefaultHardSpeedCap;
        /// <summary>
        /// 兆底最大速度：当 <see cref="InputPacket.MaxSpeed"/> &lt;= 0 时（旧客户端未填充）使用。
        /// v6 协议前等价于"全局固定速度上限"，v6 后仅作向后兼容兆底。
        /// </summary>
        public float MaxSpeed { get; set; } = MovementFormula.DefaultMaxSpeed;
        public float TickDtSeconds { get; set; } = 1f / 60f;
        public int MaxJumpCount { get; set; } = 2;
        public int MaxQinggongJumpCount { get; set; } = 3;
        
        /// <summary>
        /// P2.6：最大允许加速度（m/s²）。
        /// 相邻两次校验之间速度变化超过此值则判定为瞬移外挂。
        /// 默认 50 m/s²（约 5G，容纳轻功/传送门/击飞等合法加速）。
        /// </summary>
        public float MaxAcceleration { get; set; } = 50f;
        
        /// <summary>
        /// P2.6：瞬移距离阈值（米）。
        /// 相邻两次校验之间位置跳变超过此值则判定为瞬移。
        /// 默认 100m（合法传送门走独立协议，不经过移动校验）。
        /// </summary>
        public float TeleportDistanceThreshold { get; set; } = 100f;
    
        /// <summary>
        /// 动态阈值 RTT 缩放因子（m/ms）。
        /// 高 RTT 时客户端预测误差天然增大，动态放宽 epsilon 避免 Correction 风暴。
        /// 公式：effectiveEpsilon = PositionEpsilon + RttScalingFactor × rttMs。
        /// 默认 0.002f（即 RTT=300ms 时额外放宽 0.6m，总阈值 1.1m）。
        /// 设为 0 禁用动态阈值（退化为固定 PositionEpsilon）。
        /// </summary>
        public float RttScalingFactor { get; set; } = 0.002f;
    
        /// <summary>
        /// 动态阈值上限（米）。无论 RTT 多高，epsilon 不超过此值。
        /// 防止极端网络条件下阈值过大导致反作弊失效。默认 2.0m。
        /// </summary>
        public float MaxDynamicEpsilon { get; set; } = 2.0f;
    }

    private readonly Options _options;

    public MovementValidator(Options? options = null)
    {
        _options = options ?? new Options();
        if (_options.TickDtSeconds <= 0f)
            throw new ArgumentOutOfRangeException(nameof(options), "TickDtSeconds 必须为正数。");
    }

    /// <summary>
    /// 地面高度采样委托：给定 ECS 世界坐标 (x, y)（Z-up 坐标系，x=左右, y=前后），
    /// 返回该位置的地面 ECS.Z 高度（米）；返回 <see cref="float.NaN"/> 表示采样失败（无地面）。
    /// <para>
    /// 与 <see cref="Horizon.Game.ECS.Arch.Systems.LocalSimulationSystem.GroundHeightSampler"/> 语义一致，
    /// 确保服务端权威回放与客户端预测应用相同的地面约束，避免服务端位置穿透 Terrain
    /// 后通过 <see cref="CorrectionPacket"/> 把客户端拉到地下。
    /// </para>
    /// <para>
    /// 服务端通常没有 FlaxEngine.Physics，需要由调用方提供基于世界数据（heightmap / chunk geometry）
    /// 的采样实现。若委托为 null（未注入），本类保持原行为（仅自由运动 + 重力，不做地面约束）。
    /// </para>
    /// </summary>
    public Func<float, float, float>? GroundHeightSampler { get; set; }

    /// <summary>
    /// 按输入序列回放：从 <paramref name="start"/> 出发，依次套用每个 <see cref="InputPacket"/>，
    /// 与 <paramref name="clientEnd"/> 比较；若差异 &gt; <see cref="Options.PositionEpsilon"/>，
    /// 返回携带服务器权威终点的 <see cref="CorrectionPacket"/>。
    /// </summary>
    /// <param name="entityId">目标实体的网络 ID；回种到 correction 包中便于客户端路由。</param>
    /// <param name="start">起点坐标（服务器权威）。</param>
    /// <param name="startVz">起点 Z 方向速度。</param>
    /// <param name="inputs">按 <see cref="InputPacket.ClientTick"/> 升序排列的输入序列。</param>
    /// <param name="clientEnd">客户端自报的终点。</param>
    /// <param name="serverTick">当前服务器 tick；写入 correction 与权威 <see cref="AuthTransformComponent"/>。</param>
    public ValidationResult Validate(
        ulong entityId,
        in WorldPosition start,
        float startVz,
        ReadOnlySpan<InputPacket> inputs,
        in WorldPosition clientEnd,
        long serverTick)
    {
        return Validate(entityId, in start, startVz, inputs, in clientEnd, serverTick, rttMs: 0f);
    }

    /// <summary>
    /// 按输入序列回放（动态阈值版本）：当 <paramref name="rttMs"/> &gt; 0 时，
    /// 位置偏差阈值按 <c>epsilon = PositionEpsilon + RttScalingFactor × rttMs</c> 动态放宽，
    /// 避免高延迟玩家因预测误差天然偏大触发 Correction 风暴。
    /// </summary>
    /// <param name="entityId">目标实体的网络 ID。</param>
    /// <param name="start">起点坐标（服务器权威）。</param>
    /// <param name="startVz">起点 Z 方向速度。</param>
    /// <param name="inputs">按 <see cref="InputPacket.ClientTick"/> 升序排列的输入序列。</param>
    /// <param name="clientEnd">客户端自报的终点。</param>
    /// <param name="serverTick">当前服务器 tick。</param>
    /// <param name="rttMs">该玩家当前估计 RTT（毫秒）。0 或负值使用固定阈值。</param>
    public ValidationResult Validate(
        ulong entityId,
        in WorldPosition start,
        float startVz,
        ReadOnlySpan<InputPacket> inputs,
        in WorldPosition clientEnd,
        long serverTick,
        float rttMs)
    {
        // 1) 权威回放
        // v6 协议：每个 InputPacket 携带当帧 MaxSpeed，服务端按客户端指定的速度回放，
        // 保证两端按同一速度推进。MaxSpeed <= 0 时兜底使用 _options.MaxSpeed（向后兼容旧客户端）。
        // 同时记录每帧 MaxSpeed 用于反作弊判定（HardSpeedCap 校验）。
        float x = start.X, y = start.Y, z = start.Z, vz = startVz;
        float maxObservedSpeed = 0f;
        float maxInputMaxSpeed = 0f;
        int jumpCount = 0;
        bool isGrounded = true;
        const float groundedEpsilon = 0.001f;
        bool jumpCountExceeded = false;
        for (int i = 0; i < inputs.Length; i++)
        {
            var input = inputs[i];
            if (input is null) continue;

            // 解析本帧 MaxSpeed：> 0 用客户端值，否则兜底 Options.MaxSpeed
            var frameMaxSpeed = input.MaxSpeed > 0f ? input.MaxSpeed : _options.MaxSpeed;
            if (frameMaxSpeed > maxInputMaxSpeed) maxInputMaxSpeed = frameMaxSpeed;

            var prevZ = z;
            var isQinggongJump = (input.InputBits & (1u << 3)) != 0;
            var isJumpPressed = (input.InputBits & 0x1) != 0;

            float jumpImpulse = 0f;

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
                    if (jumpCount > _options.MaxQinggongJumpCount)
                    {
                        jumpCountExceeded = true;
                        jumpImpulse = 0f;
                    }
                }
                else
                {
                    jumpCount = 1;
                    jumpImpulse = 5.5f;
                }
            }

            var (nx, ny, nz, nvz) = MovementFormula.Step(
                x, y, z, vz,
                input.MoveX, input.MoveY, jumpImpulse,
                _options.TickDtSeconds, frameMaxSpeed);

            // 地面碰撞检测：采样 (nx, ny) 处的地面 ECS.Z 高度，
            // 若新位置低于地面则吸附到地面并清零垂直速度，防止权威位置穿透 Terrain。
            // 与 LocalSimulationSystem / ReconciliationSystem 应用相同约束，保证 C/S 一致。
            // GroundHeightSampler 为 null（服务端未注入 heightmap / 物理采样器）时，
            // 退化为"信任客户端的地面约束"：用本帧 InputPacket.PredictedEndZ 作为参考地面 Z。
            // 客户端 LocalSimulationSystem 在每帧 Step 后用 GroundHeightSampler 约束过 Z，
            // PredictedEndZ 即约束后的 Z。如果服务端权威回放 Z 低于 PredictedEndZ（差距 > 0.05m），
            // 说明客户端落地（采样到地形高度），服务端也用相同地面 Z 做约束，避免 C/S 漂移触发 correction 风暴。
            // 反作弊保障：
            //   1) 10m 上限：客户端不能把自己拉到离地 >10m 的位置（dz > 10m 视为异常，不 clamp，drift 会兜底）。
            //   2) PositionEpsilon：客户端伪造 PredictedEndZ 后，若 drift > 0.5m 仍触发 CorrectionPacket。
            var sampler = GroundHeightSampler;
            if (sampler != null)
            {
                var groundZ = sampler(nx, ny);
                if (!float.IsNaN(groundZ) && nz < groundZ)
                {
                    nz = groundZ;
                    nvz = 0f;
                    isGrounded = true;
                    jumpCount = 0;
                    jumpCountExceeded = false;
                }
                else
                {
                    var dz = MathF.Abs(nz - prevZ);
                    isGrounded = dz < groundedEpsilon && nvz <= 0f;
                    if (isGrounded)
                    {
                        jumpCount = 0;
                        jumpCountExceeded = false;
                    }
                }
            }
            else
            {
                var predictedZ = input.PredictedEndZ;
                // 修复（PredictedEndZ 未设置导致实体 Z 被错误拉到 0 — 角色移动后回弹+跳跃Mode错误根因）：
                // InputPacket.PredictedEndZ 默认为 0。当客户端未填写此字段（旧客户端/边界情况/测试环境）
                // 且实体当前 Z 不为 0 时，原 fallback 会把 Z 错误地拉到 0，导致：
                // 1) 位置突变（8→0）触发 Correction 风暴，角色被拉回"起点"
                // 2) 跳跃后 nz=0、zChange=0 → isGrounded=true、nvz=0 → MovementMode 变为 Walk 而非 Jump
                // 3) 连锁影响：旋转 tick 已把 Z 拉到 0，跳跃 tick 从 Z=0 起跳，完全偏离真实位置
                //
                // 判断策略：predictedZ == 0 有两种含义——
                //   a) 实体真的在 Z=0 地面（合法）→ 服务端计算 nz 也应接近 0
                //   b) PredictedEndZ 未设置（默认 0）但实体实际 Z 远离 0 → nz 不接近 0
                // 用 |nz| > 0.5f 区分：服务端 Z 远离 0 时 predictedZ=0 很可能是未设置，不信任。
                // 0.5m 阈值覆盖正常单帧重力偏移（~0.003m）和微小地形起伏，不会误判合法地面。
                const float predictedUnsetThreshold = 0.5f;
                if (predictedZ == 0f && MathF.Abs(nz) > predictedUnsetThreshold)
                {
                    // PredictedEndZ 未设置且实体远离 Z=0：不进行地面约束，保留重力计算结果。
                    // 生产环境客户端 InputSendSystem 会从 PredictedTransformComponent 正确填充 PredictedEndZ。
                    var dz = MathF.Abs(nz - prevZ);
                    isGrounded = dz < groundedEpsilon && nvz <= 0f;
                    if (isGrounded)
                    {
                        jumpCount = 0;
                        jumpCountExceeded = false;
                    }
                }
                else
                {
                    // PredictedEndZ 已设置（非 0）或服务端 Z 接近 0（实体在地面）：信任客户端的地面约束。
                    // 修复（角色移动后回弹根因）：原条件 dzPred >= 0f 仅处理上坡/平地（客户端Z ≥ 服务端Z），
                    // 下坡时客户端Z < 服务端Z → dzPred < 0 → fallback 不触发 → 服务端Z漂移 → Correction风暴。
                    // 改为 MathF.Abs(dzPred) < 10f：下坡时也钳制到客户端Z，消除Z漂移。
                    // 10m 上限保留防作弊保障；drift 校验兜底防止极端伪造。
                    var dzPred = predictedZ - nz;
                    if (MathF.Abs(dzPred) < 10f)
                    {
                        nz = predictedZ;
                        // 修复（多段跳被破坏）：原代码无条件 nvz=0 + isGrounded=true + jumpCount=0，
                        // 跳跃中(Z上升)也会重置jumpCount，破坏轻功多段跳。
                        // 改为基于Z变化判断接地状态：
                        // - Z变化小(|zChange| < 0.05m)→ 在地面（平地/缓坡）→ Vz=0, isGrounded=true
                        // - Z变化大→ 跳跃/下落/陡坡 → 保留Vz, isGrounded=false（不重置jumpCount）
                        var zChange = predictedZ - prevZ;
                        if (MathF.Abs(zChange) < 0.05f)
                        {
                            nvz = 0f;
                            isGrounded = true;
                            jumpCount = 0;
                            jumpCountExceeded = false;
                        }
                        else
                        {
                            // 跳跃/下落/陡坡：保留重力计算的Vz供下一帧使用，
                            // isGrounded保持false避免错误重置jumpCount。
                            // nz已钳制到客户端Z，位置不会漂移。
                            isGrounded = false;
                        }
                    }
                    else
                    {
                        // |dzPred| >= 10m：客户端Z与服务端计算Z差异过大，不信任客户端。
                        // 保留重力计算结果，让drift校验兜底。
                        var dz = MathF.Abs(nz - prevZ);
                        isGrounded = dz < groundedEpsilon && nvz <= 0f;
                        if (isGrounded)
                        {
                            jumpCount = 0;
                            jumpCountExceeded = false;
                        }
                    }
                }
            }

            var dx = nx - x; var dy = ny - y;
            var speed = MathF.Sqrt(dx * dx + dy * dy) / _options.TickDtSeconds;
            if (speed > maxObservedSpeed) maxObservedSpeed = speed;

            x = nx; y = ny; z = nz; vz = nvz;
        }

        var authoritativeEnd = new WorldPosition(x, y, z);

        // 2) 客户端硬性速度上限（反作弊）：
        //    主判定：input.MaxSpeed 上限校验（v6 协议字段），超 HardSpeedCap 判 SpeedHackSuspected。
        //    辅助判定：客户端自报"首末位移速度"超 maxInputMaxSpeed * 1.5（容差 50%）也判异常，
        //    防止客户端伪造 PredictedEnd 绕过位置校验。
        var hardCapViolated = maxInputMaxSpeed > _options.HardSpeedCap;
        if (!hardCapViolated && maxInputMaxSpeed > 0f)
        {
            var totalDt = _options.TickDtSeconds * MathF.Max(1, inputs.Length);
            var clientDistance = MovementFormula.Distance3D(
                start.X, start.Y, start.Z,
                clientEnd.X, clientEnd.Y, clientEnd.Z);
            var clientSpeed = clientDistance / totalDt;
            hardCapViolated = clientSpeed > maxInputMaxSpeed * 1.5f;
        }

        // 3) 位置偏差（动态阈值）
        // 高 RTT 时客户端预测误差天然增大（RTT=300ms → 预测移动 ~1.8m），
        // 方向变化可能导致 drift > 固定 0.5m 触发 Correction 风暴。
        // 动态阈值：effectiveEpsilon = base + k × rttMs，上限 MaxDynamicEpsilon。
        var effectiveEpsilon = _options.PositionEpsilon;
        if (rttMs > 0f && _options.RttScalingFactor > 0f)
        {
            effectiveEpsilon = MathF.Min(
                _options.PositionEpsilon + _options.RttScalingFactor * rttMs,
                _options.MaxDynamicEpsilon);
        }

        var drift = MovementFormula.Distance3D(
            authoritativeEnd.X, authoritativeEnd.Y, authoritativeEnd.Z,
            clientEnd.X, clientEnd.Y, clientEnd.Z);
        var needCorrection = drift > effectiveEpsilon;

        CorrectionPacket? correction = null;
        if (needCorrection || hardCapViolated || jumpCountExceeded)
        {
            // 优先级：位置漂移 > 速度超限 > 跳跃次数超限。
            // 当 drift > PositionEpsilon 时，核心问题是位置不一致，应发 PredictionDrift correction；
            // 仅当漂移未超阈值但客户端自报速度超限时，才归类为 SpeedHackSuspected（早期预警）。
            CorrectionReason reason;
            if (needCorrection)
                reason = CorrectionReason.PredictionDrift;
            else if (hardCapViolated)
                reason = CorrectionReason.SpeedHackSuspected;
            else
                reason = CorrectionReason.JumpCountExceeded;

            // 取本批输入序列中最大的 ClientTick 作为 LastProcessedClientTick。
            // inputs 按 ClientTick 升序排列，故最后一个即为本次服务端处理到的最新客户端 tick。
            // 客户端 ReconciliationSystem 据此清理 InputHistoryBuffer 并仅重放未确认输入，
            // 避免重放已确认输入导致角色飞出 → drift 巨大 → Correction 风暴的死循环。
            var lastProcessedClientTick = inputs.Length > 0
                ? inputs[inputs.Length - 1].ClientTick
                : 0L;

            correction = new CorrectionPacket
            {
                EntityId = entityId,
                ServerTick = serverTick,
                CorrectedX = authoritativeEnd.X,
                CorrectedY = authoritativeEnd.Y,
                CorrectedZ = authoritativeEnd.Z,
                CorrectedVz = vz,
                DriftMeters = drift,
                Reason = reason,
                LastProcessedClientTick = lastProcessedClientTick,
            };
        }

        return new ValidationResult(authoritativeEnd, vz, drift, maxObservedSpeed, correction, effectiveEpsilon, isGrounded);
    }

    /// <summary>校验结果。</summary>
    public readonly struct ValidationResult
    {
        public WorldPosition AuthoritativeEnd { get; }
        public float AuthoritativeVz { get; }
        public float DriftMeters { get; }
        public float MaxObservedHorizontalSpeed { get; }
        public CorrectionPacket? Correction { get; }
        /// <summary>本次校验实际使用的位置偏差阈值（含动态 RTT 放宽）。</summary>
        public float EffectiveEpsilon { get; }
        /// <summary>本次校验结束时实体是否在地面（供 ZoneShardGrain 判断 MovementMode）。</summary>
        public bool IsGrounded { get; }

        public ValidationResult(
            WorldPosition end, float vz, float drift, float maxSpeed,
            CorrectionPacket? correction, float effectiveEpsilon,
            bool isGrounded)
        {
            AuthoritativeEnd = end;
            AuthoritativeVz = vz;
            DriftMeters = drift;
            MaxObservedHorizontalSpeed = maxSpeed;
            Correction = correction;
            EffectiveEpsilon = effectiveEpsilon;
            IsGrounded = isGrounded;
        }

        /// <summary>是否需要下发 correction（存在 <see cref="Correction"/> 即为真）。</summary>
        public bool NeedsCorrection => Correction != null;
    }

    // --- P2.6：加速度校验（防瞬移） ---

    /// <summary>
    /// P2.6：校验相邻两次权威位置之间的加速度是否合法。
    /// 用于检测瞬移外挂：客户端在两次校验之间 teleport 到远处。
    /// </summary>
    /// <param name="prevPosition">上次校验的权威位置。</param>
    /// <param name="currentPosition">本次校验的权威位置。</param>
    /// <param name="prevSpeed">上次校验时的水平速度（m/s）。</param>
    /// <param name="currentSpeed">本次校验时的水平速度（m/s）。</param>
    /// <param name="deltaSeconds">两次校验之间的时间差（秒）。</param>
    /// <returns>校验结果。</returns>
    public AccelerationCheckResult CheckAcceleration(
        in WorldPosition prevPosition,
        in WorldPosition currentPosition,
        float prevSpeed,
        float currentSpeed,
        float deltaSeconds)
    {
        if (deltaSeconds <= 0f)
            return new AccelerationCheckResult(true, 0f, 0f, AccelerationViolation.None);

        // 距离跳变检测（瞬移）
        var distance = MovementFormula.Distance3D(
            prevPosition.X, prevPosition.Y, prevPosition.Z,
            currentPosition.X, currentPosition.Y, currentPosition.Z);

        if (distance > _options.TeleportDistanceThreshold)
        {
            return new AccelerationCheckResult(false, distance, 0f, AccelerationViolation.TeleportDetected);
        }

        // 加速度检测
        var speedDelta = MathF.Abs(currentSpeed - prevSpeed);
        var acceleration = speedDelta / deltaSeconds;

        if (acceleration > _options.MaxAcceleration)
        {
            return new AccelerationCheckResult(false, distance, acceleration, AccelerationViolation.AccelerationExceeded);
        }

        return new AccelerationCheckResult(true, distance, acceleration, AccelerationViolation.None);
    }

    /// <summary>P2.6：加速度校验结果。</summary>
    public readonly struct AccelerationCheckResult
    {
        public bool IsValid { get; }
        public float DistanceTraveled { get; }
        public float Acceleration { get; }
        public AccelerationViolation Violation { get; }

        public AccelerationCheckResult(bool isValid, float distance, float acceleration, AccelerationViolation violation)
        {
            IsValid = isValid;
            DistanceTraveled = distance;
            Acceleration = acceleration;
            Violation = violation;
        }
    }

    /// <summary>P2.6：加速度违规类型。</summary>
    public enum AccelerationViolation : byte
    {
        /// <summary>无违规。</summary>
        None = 0,
        /// <summary>瞬移检测（距离跳变超过阈值）。</summary>
        TeleportDetected = 1,
        /// <summary>加速度超限。</summary>
        AccelerationExceeded = 2,
    }
}

/// <summary>简单三元组，避免引 System.Numerics（保证确定性）。</summary>
public readonly struct WorldPosition
{
    public float X { get; }
    public float Y { get; }
    public float Z { get; }
    public WorldPosition(float x, float y, float z) { X = x; Y = y; Z = z; }
    public override string ToString() => $"({X:F3},{Y:F3},{Z:F3})";
}
