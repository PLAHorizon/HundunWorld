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
        /// 兜底最大速度：当 <see cref="InputPacket.MaxSpeed"/> &lt;= 0 时（旧客户端未填充）使用。
        /// v6 协议前等价于"全局固定速度上限"，v6 后仅作向后兼容兜底。
        /// </summary>
        public float MaxSpeed { get; set; } = MovementFormula.DefaultMaxSpeed;
        public float TickDtSeconds { get; set; } = 1f / 60f;
        public int MaxJumpCount { get; set; } = 2;
        public int MaxQinggongJumpCount { get; set; } = 3;
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
                var dzPred = predictedZ - nz;
                if (dzPred > 0.05f && dzPred < 10f)
                {
                    nz = predictedZ;
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

        // 3) 位置偏差
        var drift = MovementFormula.Distance3D(
            authoritativeEnd.X, authoritativeEnd.Y, authoritativeEnd.Z,
            clientEnd.X, clientEnd.Y, clientEnd.Z);
        var needCorrection = drift > _options.PositionEpsilon;

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
            };
        }

        return new ValidationResult(authoritativeEnd, vz, drift, maxObservedSpeed, correction);
    }

    /// <summary>校验结果。</summary>
    public readonly struct ValidationResult
    {
        public WorldPosition AuthoritativeEnd { get; }
        public float AuthoritativeVz { get; }
        public float DriftMeters { get; }
        public float MaxObservedHorizontalSpeed { get; }
        public CorrectionPacket? Correction { get; }

        public ValidationResult(
            WorldPosition end, float vz, float drift, float maxSpeed,
            CorrectionPacket? correction)
        {
            AuthoritativeEnd = end;
            AuthoritativeVz = vz;
            DriftMeters = drift;
            MaxObservedHorizontalSpeed = maxSpeed;
            Correction = correction;
        }

        /// <summary>是否需要下发 correction（存在 <see cref="Correction"/> 即为真）。</summary>
        public bool NeedsCorrection => Correction != null;
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
