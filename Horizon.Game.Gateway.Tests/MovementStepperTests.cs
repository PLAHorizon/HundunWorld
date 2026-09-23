using Horizon.Game.Sim;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 1.2.1 / 1.3 — IMovementStepper（MovementFormulaStepper）行为等价性与
/// SimulationOptions 配置收敛回归。
/// 验证：①三段跳冲量与历史硬编码 switch（5.5/4.5/3.5，越界 0）逐位一致；
/// ②预测（LocalSimulationSystem）与重放（ReconciliationSystem）共用同一 stepper 语义；
/// ③普通跳跃固定首段冲量；④未按轻功清零/非轻功清零；⑤SimulationOptions 自定义冲量生效。
/// </summary>
public class MovementStepperTests
{
    private const float Dt = 1f / 60f;

    /// <summary>旧实现三段跳冲量 switch（历史硬编码，预期基准）。</summary>
    private static float LegacyQinggongImpulse(int jumpCount)
        => jumpCount switch { 1 => 5.5f, 2 => 4.5f, 3 => 3.5f, _ => 0f };

    [Fact]
    public void QinggongTripleJump_ImpulsesMatchLegacySwitch()
    {
        // 轻功三段跳：jumpCount 递增，冲量必须与历史 switch 逐位一致。
        var stepper = new MovementFormulaStepper();
        int jumpCount = 0;
        var step = default(MovementStepInput);

        for (int i = 1; i <= 3; i++)
        {
            step = new MovementStepInput
            {
                X = 0f, Y = 0f, Z = 0f, Vz = 0f,
                MoveX = 0f, MoveY = 0f,
                JumpPressed = true, IsQinggongJump = true,
                JumpCount = jumpCount,
                MaxSpeed = 6f,
            };
            var result = stepper.Step(step, Dt);

            // Vz 增量 = jumpImpulse（重力在 Formula.Step 内扣除 dt×Gravity；用 ΔVz + Gravity*dt 还原冲量）
            var appliedImpulse = result.Vz - step.Vz + MovementFormula.Gravity * Dt;
            Assert.Equal(LegacyQinggongImpulse(i), appliedImpulse, 5);
            Assert.Equal(i, result.JumpCount); // jumpCount 递增 1→2→3
            jumpCount = result.JumpCount;
        }
    }

    [Fact]
    public void QinggongFourthJump_ImpulseZero_LegacySwitchConsistent()
    {
        // 第 4 段轻功：旧 switch _ => 0f，stepper 必须同样给 0 冲量（越界无冲量）。
        var stepper = new MovementFormulaStepper();
        var step = new MovementStepInput
        {
            X = 0f, Y = 0f, Z = 0f, Vz = 0f,
            MoveX = 0f, MoveY = 0f,
            JumpPressed = true, IsQinggongJump = true,
            JumpCount = 3, // 已 3 段，本次为第 4 段
            MaxSpeed = 6f,
        };
        var result = stepper.Step(step, Dt);

        var appliedImpulse = result.Vz - step.Vz + MovementFormula.Gravity * Dt;
        Assert.Equal(0f, appliedImpulse, 5);
        Assert.Equal(4, result.JumpCount);
    }

    [Fact]
    public void NormalJump_AlwaysFirstImpulse_ResetsJumpCountToOne()
    {
        // 普通跳跃（非轻功）：jumpCount 重置为 1，冲量 = 首段 5.5f。
        var stepper = new MovementFormulaStepper();
        var step = new MovementStepInput
        {
            X = 0f, Y = 0f, Z = 0f, Vz = 0f,
            MoveX = 0f, MoveY = 0f,
            JumpPressed = true, IsQinggongJump = false,
            JumpCount = 2, // 无论此前计数，普通跳重置为 1
            MaxSpeed = 6f,
        };
        var result = stepper.Step(step, Dt);

        var appliedImpulse = result.Vz - step.Vz + MovementFormula.Gravity * Dt;
        Assert.Equal(5.5f, appliedImpulse, 5);
        Assert.Equal(1, result.JumpCount);
    }

    [Fact]
    public void NotPressed_KeepsJumpCount_AlignedWithAuthority()
    {
        // 跳跃状态机对齐（修复）：未按下帧保留 jumpCount（无论是否轻功）——与权威 MovementValidator
        // 语义一致（旧实现"非轻功未按下清零"会在轻功三段跳中完全松键一帧再按时把预测重置回首段，
        // 权威按上一段继续 → 漂移 → correction 风暴）。落地清零由调用方地面约束负责。
        var stepper = new MovementFormulaStepper();
        var step = new MovementStepInput
        {
            X = 0f, Y = 0f, Z = 0f, Vz = 0f,
            MoveX = 0f, MoveY = 0f,
            JumpPressed = false, IsQinggongJump = false,
            JumpCount = 2,
            MaxSpeed = 6f,
        };
        var result = stepper.Step(step, Dt);

        Assert.Equal(0f, result.Vz - step.Vz + MovementFormula.Gravity * Dt, 5); // 无冲量
        Assert.Equal(2, result.JumpCount); // 非按下保留计数（与权威一致）
    }

    [Fact]
    public void NotPressed_Qinggong_KeepsJumpCount()
    {
        // 未按下但标记轻功（滞空轻功状态）：jumpCount 保留（不清零），无冲量。
        var stepper = new MovementFormulaStepper();
        var step = new MovementStepInput
        {
            X = 0f, Y = 0f, Z = 0f, Vz = 0f,
            MoveX = 0f, MoveY = 0f,
            JumpPressed = false, IsQinggongJump = true,
            JumpCount = 2,
            MaxSpeed = 6f,
        };
        var result = stepper.Step(step, Dt);

        Assert.Equal(0f, result.Vz - step.Vz + MovementFormula.Gravity * Dt, 5);
        Assert.Equal(2, result.JumpCount); // 轻功滞空不清零
    }

    [Fact]
    public void Qinggong_ReleaseOneFrameThenPress_ContinuesFromSecondJump()
    {
        // 对齐契约的核心场景：轻功三段跳中间完全松键一帧再按，预测必须延续第二段（非重新起跳）。
        // 旧实现清零 → 预测从首段 5.5 起跳，权威按第二段 4.5 → 漂移；修复后两路径一致。
        var stepper = new MovementFormulaStepper();
        int jumpCount = 0;

        // 第 1 段按下
        var r1 = stepper.Step(new MovementStepInput
        {
            X = 0f, Y = 0f, Z = 0f, Vz = 0f,
            JumpPressed = true, IsQinggongJump = true, JumpCount = jumpCount, MaxSpeed = 6f,
        }, Dt);
        jumpCount = r1.JumpCount;
        Assert.Equal(1, jumpCount);

        // 空中完全松键一帧（无跳跃、无轻功标记）：位置/速度延续，计数保留
        var r2 = stepper.Step(new MovementStepInput
        {
            X = r1.X, Y = r1.Y, Z = r1.Z, Vz = r1.Vz,
            JumpPressed = false, IsQinggongJump = false, JumpCount = jumpCount, MaxSpeed = 6f,
        }, Dt);
        jumpCount = r2.JumpCount;
        Assert.Equal(1, jumpCount); // 保留计数（修复：不再清零）

        // 再按：第二段（4.5），而非重新首段（5.5）
        var r3 = stepper.Step(new MovementStepInput
        {
            X = r2.X, Y = r2.Y, Z = r2.Z, Vz = r2.Vz,
            JumpPressed = true, IsQinggongJump = true, JumpCount = jumpCount, MaxSpeed = 6f,
        }, Dt);
        var appliedImpulse = r3.Vz - r2.Vz + MovementFormula.Gravity * Dt;
        Assert.Equal(2, r3.JumpCount);
        Assert.Equal(4.5f, appliedImpulse, 5);
    }

    [Fact]
    public void HorizontalStep_MatchesDirectFormulaCall()
    {
        // 无跳跃时，stepper 的水平步进必须与直接调用 MovementFormula.Step 逐位一致
        // （预测/重放共用 stepper 不改变位移结果）。
        var stepper = new MovementFormulaStepper();
        var step = new MovementStepInput
        {
            X = 10f, Y = 20f, Z = 5f, Vz = 1f,
            MoveX = 0.5f, MoveY = -0.3f,
            JumpPressed = false, IsQinggongJump = false,
            JumpCount = 0,
            MaxSpeed = 6f,
        };
        var result = stepper.Step(step, Dt);

        var (nx, ny, nz, nvz) = MovementFormula.Step(
            step.X, step.Y, step.Z, step.Vz,
            step.MoveX, step.MoveY, 0f,
            Dt, maxSpeed: step.MaxSpeed);

        Assert.Equal(nx, result.X, 5);
        Assert.Equal(ny, result.Y, 5);
        Assert.Equal(nz, result.Z, 5);
        Assert.Equal(nvz, result.Vz, 5);
        Assert.Equal(0, result.JumpCount);
    }

    [Fact]
    public void CustomJumpImpulses_FromSimulationOptions_Applied()
    {
        // SimulationOptions.JumpImpulses 自定义后，stepper 必须使用配置值而非默认。
        var options = new SimulationOptions { JumpImpulses = new[] { 7f, 6f, 5f } };
        var stepper = new MovementFormulaStepper(options);

        var step = new MovementStepInput
        {
            X = 0f, Y = 0f, Z = 0f, Vz = 0f,
            MoveX = 0f, MoveY = 0f,
            JumpPressed = true, IsQinggongJump = true,
            JumpCount = 0,
            MaxSpeed = 6f,
        };
        var result = stepper.Step(step, Dt);

        var appliedImpulse = result.Vz - step.Vz + MovementFormula.Gravity * Dt;
        Assert.Equal(7f, appliedImpulse, 5);
    }

    [Fact]
    public void ValidatorOptions_FromSimulationOptions_CarriesJumpImpulses()
    {
        // SimulationOptions.ToValidatorOptions() 必须把 JumpImpulses 复制给 MovementValidator.Options
        // （1.2.1 收敛：权威校验器与 stepper 共享同一冲量配置）。
        var options = new SimulationOptions { JumpImpulses = new[] { 9f, 8f, 7f } };
        var validatorOptions = options.ToValidatorOptions();

        Assert.Equal(9f, validatorOptions.JumpImpulses[0], 5);
        Assert.Equal(8f, validatorOptions.JumpImpulses[1], 5);
        Assert.Equal(7f, validatorOptions.JumpImpulses[2], 5);
        Assert.NotSame(options.JumpImpulses, validatorOptions.JumpImpulses); // 复制防共享修改

        // 默认 validator Options 与默认 SimulationOptions 冲量一致（历史值 5.5/4.5/3.5 回归）。
        var defaults = new MovementValidator.Options();
        Assert.Equal(5.5f, defaults.JumpImpulses[0], 5);
        Assert.Equal(4.5f, defaults.JumpImpulses[1], 5);
        Assert.Equal(3.5f, defaults.JumpImpulses[2], 5);
    }

    [Fact]
    public void ReplayAndPrediction_ShareSameStepper_ProduceIdenticalTrajectory()
    {
        // 阶段 1.3 核心语义：预测（LocalSimulationSystem 路径）与重放（ReconciliationSystem 路径）
        // 共用同一 MovementFormulaStepper 实例，输入相同时逐位一致——分叉消除。
        var shared = new MovementFormulaStepper();
        var inputs = new[]
        {
            new MovementStepInput
            {
                X = 0f, Y = 0f, Z = 0f, Vz = 0f,
                MoveX = 0.6f, MoveY = 0.1f,
                JumpPressed = true, IsQinggongJump = true, JumpCount = 0,
                MaxSpeed = 6f,
            },
            new MovementStepInput
            {
                X = 0f, Y = 0f, Z = 0f, Vz = 0f,
                MoveX = 0.6f, MoveY = 0.1f,
                JumpPressed = true, IsQinggongJump = true, JumpCount = 1,
                MaxSpeed = 6f,
            },
            new MovementStepInput
            {
                X = 0f, Y = 0f, Z = 0f, Vz = 0f,
                MoveX = 0.6f, MoveY = 0.1f,
                JumpPressed = false, IsQinggongJump = false, JumpCount = 2,
                MaxSpeed = 6f,
            },
        };

        // 预测路径（LocalSimulationSystem 风格：jumpCount 从字典延续）
        var predX = 0f; var predY = 0f; var predZ = 0f; var predVz = 0f;
        var predJump = 0;
        var prediction = new List<(float, float, float)>();
        foreach (var raw in inputs)
        {
            var step = shared.Step(new MovementStepInput
            {
                X = predX, Y = predY, Z = predZ, Vz = predVz,
                MoveX = raw.MoveX, MoveY = raw.MoveY,
                JumpPressed = raw.JumpPressed, IsQinggongJump = raw.IsQinggongJump,
                JumpCount = predJump, MaxSpeed = raw.MaxSpeed,
            }, Dt);
            predX = step.X; predY = step.Y; predZ = step.Z; predVz = step.Vz;
            predJump = step.JumpCount;
            prediction.Add((predX, predY, predZ));
        }

        // 重放路径（ReconciliationSystem 风格：从权威位置开始，jumpCount 局部变量）
        var repX = 0f; var repY = 0f; var repZ = 0f; var repVz = 0f;
        var repJump = 0;
        for (int i = 0; i < inputs.Length; i++)
        {
            var step = shared.Step(new MovementStepInput
            {
                X = repX, Y = repY, Z = repZ, Vz = repVz,
                MoveX = inputs[i].MoveX, MoveY = inputs[i].MoveY,
                JumpPressed = inputs[i].JumpPressed, IsQinggongJump = inputs[i].IsQinggongJump,
                JumpCount = repJump, MaxSpeed = inputs[i].MaxSpeed,
            }, Dt);
            repX = step.X; repY = step.Y; repZ = step.Z; repVz = step.Vz;
            repJump = step.JumpCount;

            Assert.Equal(prediction[i].Item1, repX, 5);
            Assert.Equal(prediction[i].Item2, repY, 5);
            Assert.Equal(prediction[i].Item3, repZ, 5);
        }
    }
}
