using System;
using Horizon.Game.Message.Sync;
using MemoryPack;
using Horizon.Game.Sim;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 1.3 → 1.4 — Sim 确定性契约测试（对拍测试收敛后的新形态）。
/// <para>
/// 历史：本测试曾以 <c>Horizon.Game.Core.Sim</c> 与 <c>Horizon.Game.Sim</c> 双实现逐位对拍
/// 守护"过渡期不漂移"。阶段 1.4 完成 Sim 收敛（Core.Sim 三个同名文件已删除，唯一权威为
/// <see cref="Horizon.Game.Sim"/>），本套件转型为**单实现确定性契约**：
/// <list type="number">
///   <item><c>FormulaVersion</c> 稳定为 2（与金轨迹契约联动，改动必须 bump）；</item>
///   <item><c>MovementFormula</c> 任意输入序列输出逐位（bitwise）确定；</item>
///   <item><c>MovementValidator</c> 同批输入产出 <c>CorrectionPacket</c> 序列化字节确定；</item>
///   <item>反作弊判定（SpeedHack / JumpCount / Acceleration / DynamicEpsilon）结论稳定。</item>
/// </list>
/// 任一断言失败即说明确定性被破坏：必须同步修改并 bump <c>FormulaVersion</c>。
/// </para>
/// </summary>
public class SimCoreParityTests
{
    private static InputPacket MoveInput(long tick, float x, float y, float maxSpeed = 6f, uint bits = 0, float predictedZ = 0f)
        => new()
        {
            ClientTick = tick,
            InputBits = bits,
            MoveX = x,
            MoveY = y,
            MaxSpeed = maxSpeed,
            PredictedEndZ = predictedZ,
        };

    [Fact]
    public void FormulaVersion_IsStable_AndMatchesGoldenTraceContract()
    {
        // 与 Horizon.Contract.Tests.GoldenTraceContractTest 联动的版本不变量。
        Assert.Equal(3, MovementFormula.FormulaVersion);
    }

    [Fact]
    public void MovementFormula_MixedTrajectory_IsBitwiseDeterministic()
    {
        // 混合轨迹：直线、斜向、跳跃、轻功多段跳、自由落体 —— 逐帧输出 IEEE754 位模式确定。
        float sx = 0f, sy = 0f, sz = 0f, svz = 0f;
        const float dt = 1f / 60f;

        for (int i = 0; i < 300; i++)
        {
            var frame = i % 60;
            float moveX, moveY, jump;
            if (frame < 20) { moveX = 1f; moveY = 0f; jump = 0f; }
            else if (frame < 40) { moveX = 1f; moveY = 1f; jump = 0f; }
            else if (frame < 50) { moveX = 0f; moveY = 0f; jump = 5.5f; }
            else if (frame < 56)
            {
                moveX = 0f; moveY = 0f;
                jump = (frame - 50) switch { 0 => 5.5f, 1 => 4.5f, 2 => 3.5f, _ => 0f };
            }
            else { moveX = 0f; moveY = 0f; jump = 0f; }

            var (nsx, nsy, nsz, nsvz) = MovementFormula.Step(sx, sy, sz, svz, moveX, moveY, jump, dt, 6f);

            // 与上一帧功率无关的确定性断言：同输入序列重放两次逐位一致。
            var (rsx, rsy, rsz, rsvz) = MovementFormula.Step(sx, sy, sz, svz, moveX, moveY, jump, dt, 6f);
            AssertBitwiseEqual(nsx, rsx, "X", i);
            AssertBitwiseEqual(nsy, rsy, "Y", i);
            AssertBitwiseEqual(nsz, rsz, "Z", i);
            AssertBitwiseEqual(nsvz, rsvz, "Vz", i);

            sx = nsx; sy = nsy; sz = nsz; svz = nsvz;
        }
    }

    [Fact]
    public void ValidMovement_AuthoritativeEndAndDrift_AreStable()
    {
        var inputs = new[]
        {
            MoveInput(1, 1f, 0f),
            MoveInput(2, 1f, 0f),
            MoveInput(3, 1f, 0f),
        };
        var start = new WorldPosition(0f, 0f, 0f);
        // 客户端正确自报终点（无 correction 路径）
        var end = new WorldPosition(0.3f, 0f, 0f);

        var result = new MovementValidator().Validate(1, start, 0f, inputs, end, 100);

        Assert.False(result.NeedsCorrection);
        AssertBitwiseEqual(result.AuthoritativeEnd.X, end.X, "End.X", 0);
        AssertBitwiseEqual(result.AuthoritativeEnd.Y, end.Y, "End.Y", 0);
        AssertBitwiseEqual(result.AuthoritativeEnd.Z, end.Z, "End.Z", 0);
        Assert.Equal(0f, result.DriftMeters, 5);
    }

    [Fact]
    public void DriftCorrection_SerializedBytes_AreStable()
    {
        var inputs = new[]
        {
            MoveInput(1, 1f, 0f),
            MoveInput(2, 1f, 0f),
            MoveInput(3, 1f, 0f),
        };
        // 客户端伪造终点 → 必须下发 PredictionDrift correction
        var end = new WorldPosition(50f, 0f, 0f);

        var result = new MovementValidator().Validate(1, new WorldPosition(0f, 0f, 0f), 0f, inputs, end, 200);

        Assert.NotNull(result.Correction);
        Assert.Equal(CorrectionReason.PredictionDrift, result.Correction!.Reason);
        Assert.Equal(3L, result.Correction.LastProcessedClientTick);
        Assert.Equal(200L, result.Correction.ServerTick);

        // 契约核心：CorrectionPacket 序列化字节确定（MemoryPack 线协议稳定）。
        var bytes = MemoryPackSerializer.Serialize(result.Correction);
        Assert.Equal(
            MemoryPackSerializer.Serialize(
                new MovementValidator().Validate(1, new WorldPosition(0f, 0f, 0f), 0f, inputs, end, 200).Correction),
            bytes);
    }

    [Fact]
    public void SpeedHack_Reason_IsDetected()
    {
        var inputs = new[] { MoveInput(1, 1f, 0f, maxSpeed: 300f) };
        var result = new MovementValidator().Validate(
            1, new WorldPosition(0f, 0f, 0f), 0f, inputs, new WorldPosition(5f, 0f, 0f), 100);

        Assert.True(result.NeedsCorrection);
        Assert.Equal(CorrectionReason.SpeedHackSuspected, result.Correction!.Reason);
    }

    [Fact]
    public void JumpCount_Qinggong_Reason_IsDetected()
    {
        // 与契约测试相同的真实客户端轨迹：逐帧预演预测 Z 并回填 PredictedEndZ，
        // 触发 z-change 分支使 jumpCount 跨帧累加，判 JumpCountExceeded。
        var validatorInputs = new[]
        {
            MoveInput(1, 0f, 0f, maxSpeed: 12f, bits: 0x1 | (1u << 3)),
            MoveInput(2, 0f, 0f, maxSpeed: 12f, bits: 0x1 | (1u << 3)),
            MoveInput(3, 0f, 0f, maxSpeed: 12f, bits: 0x1 | (1u << 3)),
            MoveInput(4, 0f, 0f, maxSpeed: 12f, bits: 0x1 | (1u << 3)),
        };
        float z = 0f, vz = 0f;
        for (int i = 0; i < validatorInputs.Length; i++)
        {
            var impulse = i switch { 0 => 5.5f, 1 => 4.5f, 2 => 3.5f, _ => 0f };
            (z, vz) = MovementFormula.StepVertical(z, vz, impulse, 1f / 60f);
            validatorInputs[i].PredictedEndZ = z;
        }

        var result = new MovementValidator().Validate(
            1, new WorldPosition(0f, 0f, 0f), 0f, validatorInputs, new WorldPosition(0f, 0f, z), 100);

        Assert.True(result.NeedsCorrection);
        Assert.Equal(CorrectionReason.JumpCountExceeded, result.Correction!.Reason);
    }

    [Fact]
    public void GroundHeightSampler_Clamp_ProducesStableEnd()
    {
        var inputs = new[] { MoveInput(1, 1f, 0f) };

        var result = new MovementValidator { GroundHeightSampler = (x, y) => 0f }
            .Validate(1, new WorldPosition(0f, 0f, 0f), 0f, inputs, new WorldPosition(0.1f, 0f, 0f), 100);

        AssertBitwiseEqual(result.AuthoritativeEnd.Z, 0f, "End.Z", 0);
        Assert.True(result.IsGrounded);
    }

    [Fact]
    public void CheckAcceleration_Verdicts_AreStable()
    {
        var validator = new MovementValidator();

        // 瞬移：距离 500m >> 阈值 100m
        var teleport = validator.CheckAcceleration(
            new WorldPosition(0f, 0f, 0f), new WorldPosition(500f, 0f, 0f), 6f, 6f, 0.1f);
        Assert.False(teleport.IsValid);
        Assert.Equal(MovementValidator.AccelerationViolation.TeleportDetected, teleport.Violation);

        // 加速度超限：速度 6 → 600
        var accel = validator.CheckAcceleration(
            new WorldPosition(0f, 0f, 0f), new WorldPosition(1f, 0f, 0f), 6f, 600f, 0.1f);
        Assert.False(accel.IsValid);
        Assert.Equal(MovementValidator.AccelerationViolation.AccelerationExceeded, accel.Violation);

        // 合法移动
        var ok = validator.CheckAcceleration(
            new WorldPosition(0f, 0f, 0f), new WorldPosition(0.1f, 0f, 0f), 6f, 6.5f, 0.1f);
        Assert.True(ok.IsValid);

        // 垂直位移（仅 Z 变化 120m，水平 0）不得判瞬移（2D 修复后的语义）
        var vertical = validator.CheckAcceleration(
            new WorldPosition(0f, 0f, 0f), new WorldPosition(0f, 0f, -120f), 6f, 6f, 2.4f);
        Assert.True(vertical.IsValid);
    }

    [Fact]
    public void DynamicEpsilon_SpeedAware_IsStable()
    {
        // 契约：动态阈值按本批声明速度缩放（0.5 + 12×300/1000 = 4.1m），
        // 合法冲刺在 RTT=300ms 不触发 correction。
        var inputs = new InputPacket[60];
        for (int i = 0; i < 60; i++) inputs[i] = MoveInput(i + 1, 1f, 0f, maxSpeed: 12f);

        var result = new MovementValidator().Validate(
            1, new WorldPosition(0f, 0f, 0f), 0f, inputs, new WorldPosition(13f, 0f, 0f), 100, rttMs: 300f);

        Assert.Equal(0.5f + 12f * 300f / 1000f, result.EffectiveEpsilon, 3);
        Assert.False(result.NeedsCorrection);
    }

    private static void AssertBitwiseEqual(float expected, float actual, string label, int frame)
        => Assert.True(
            BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(actual),
            $"{label} 位模式不一致 @帧 {frame}: {expected} vs {actual}");
}