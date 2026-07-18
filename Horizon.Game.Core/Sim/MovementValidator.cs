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

    /// <summary>默认速度硬性上限（米/秒）：客户端速度超此值立即判异常。</summary>
    public const float DefaultHardSpeedCap = 20f;

    /// <summary>配置项。</summary>
    public sealed class Options
    {
        public float PositionEpsilon { get; set; } = DefaultPositionEpsilon;
        public float HardSpeedCap { get; set; } = DefaultHardSpeedCap;
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
        float x = start.X, y = start.Y, z = start.Z, vz = startVz;
        float maxObservedSpeed = 0f;
        int jumpCount = 0;
        bool isGrounded = true;
        const float groundedEpsilon = 0.001f;
        bool jumpCountExceeded = false;
        for (int i = 0; i < inputs.Length; i++)
        {
            var input = inputs[i];
            if (input is null) continue;

            var prevZ = z;
            var isQinggongJump = (input.InputBits & (1u << 3)) != 0;
            var isJumpPressed = (input.InputBits & 0x1) != 0;

            float jumpImpulse = 0f;

            if (isJumpPressed)
            {
                if (isGrounded)
                    jumpCount = 0;

                jumpCount++;

                jumpImpulse = jumpCount switch
                {
                    1 => 5.5f,
                    2 => 4.5f,
                    3 => 3.5f,
                    _ => 0f
                };

                var maxJumps = isQinggongJump ? _options.MaxQinggongJumpCount : 1;
                if (jumpCount > maxJumps)
                {
                    jumpCountExceeded = true;
                    jumpImpulse = 0f;
                }
            }

            var (nx, ny, nz, nvz) = MovementFormula.Step(
                x, y, z, vz,
                input.MoveX, input.MoveY, jumpImpulse,
                _options.TickDtSeconds, _options.MaxSpeed);

            var dz = MathF.Abs(nz - prevZ);
            isGrounded = dz < groundedEpsilon && nvz <= 0f;
            if (isGrounded)
                jumpCount = 0;

            var dx = nx - x; var dy = ny - y;
            var speed = MathF.Sqrt(dx * dx + dy * dy) / _options.TickDtSeconds;
            if (speed > maxObservedSpeed) maxObservedSpeed = speed;

            x = nx; y = ny; z = nz; vz = nvz;
        }

        var authoritativeEnd = new WorldPosition(x, y, z);

        // 2) 客户端硬性速度上限：把客户端自报的"首末位移 / 总时间"算下来，超限直接标异常。
        var totalDt = _options.TickDtSeconds * MathF.Max(1, inputs.Length);
        var clientDistance = MovementFormula.Distance3D(
            start.X, start.Y, start.Z,
            clientEnd.X, clientEnd.Y, clientEnd.Z);
        var clientSpeed = clientDistance / totalDt;
        var hardCapViolated = clientSpeed > _options.HardSpeedCap;

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
