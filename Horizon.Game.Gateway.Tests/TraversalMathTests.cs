using System;
using Horizon.Game.Sim;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 翻越（Traversal）共享数学层回归：曲线端点/弧顶/单调性/确定性、
/// 步进器窗口（忽略输入、结束即落点）与计划校验矩阵。
/// <para>
/// 这些判据被客户端预测与服务端权威同时依赖——曲线或边界任一侧改动都会让两端位置分叉，
/// 所以边界必须写死在这里逐条断言，而不是只测"能跑通"。
/// </para>
/// </summary>
public class TraversalMathTests
{
    private const float Dt = 1f / 60f;

    private static TraversalPlan Plan(
        float sx = 0f, float sy = 0f, float sz = 0f,
        float ex = 120f, float ey = 80f, float ez = 100f,
        float duration = 0.8f, float arc = 30f)
        => new TraversalPlan
        {
            StartX = sx, StartY = sy, StartZ = sz,
            EndX = ex, EndY = ey, EndZ = ez,
            Duration = duration, ArcHeight = arc,
        };

    // ── 曲线采样 ──────────────────────────────────────────────

    [Fact]
    public void Sample_Endpoints_Exact()
    {
        var plan = Plan();
        var start = TraversalMath.Sample(plan, 0f);
        Assert.Equal(plan.StartX, start.X);
        Assert.Equal(plan.StartY, start.Y);
        Assert.Equal(plan.StartZ, start.Z);

        var end = TraversalMath.Sample(plan, plan.Duration);
        Assert.Equal(plan.EndX, end.X, 5);
        Assert.Equal(plan.EndY, end.Y, 5);
        Assert.Equal(plan.EndZ, end.Z, 5);

        // 超时采样必须钳到终点，而不是继续外推。
        var over = TraversalMath.Sample(plan, plan.Duration * 3f);
        Assert.Equal(plan.EndX, over.X, 5);
        Assert.Equal(plan.EndY, over.Y, 5);
        Assert.Equal(plan.EndZ, over.Z, 5);

        // 负时间钳到起点。
        var before = TraversalMath.Sample(plan, -1f);
        Assert.Equal(plan.StartX, before.X);
        Assert.Equal(plan.StartY, before.Y);
        Assert.Equal(plan.StartZ, before.Z);
    }

    [Fact]
    public void Sample_Midpoint_HalfWayPlusArcPeak()
    {
        var plan = Plan();
        var mid = TraversalMath.Sample(plan, plan.Duration * 0.5f);
        Assert.Equal((plan.StartX + plan.EndX) * 0.5f, mid.X, 5);
        Assert.Equal((plan.StartY + plan.EndY) * 0.5f, mid.Y, 5);
        // smoothstep(0.5)=0.5，弧 4t(1-t)=1 ⇒ 中点 z = 线性中点 + 完整弧高。
        Assert.Equal((plan.StartZ + plan.EndZ) * 0.5f + plan.ArcHeight, mid.Z, 5);

        // 弧的贡献必须逐点可分离：同参数无弧计划在中点差 ArcHeight、在 1/4 处差 0.75×ArcHeight。
        // （终点本来更高时，中点的绝对 Z 不一定最大，所以不能拿"中点最高"当判据。）
        var flat = Plan(arc: 0f);
        var midFlat = TraversalMath.Sample(flat, flat.Duration * 0.5f);
        var quarter = TraversalMath.Sample(plan, plan.Duration * 0.25f);
        var quarterFlat = TraversalMath.Sample(flat, flat.Duration * 0.25f);
        Assert.Equal(plan.ArcHeight, mid.Z - midFlat.Z, 5);
        Assert.Equal(plan.ArcHeight * 0.75f, quarter.Z - quarterFlat.Z, 5);
    }

    [Fact]
    public void Sample_HorizontalProgressIsMonotonic()
    {
        var plan = Plan();
        float last = -1f;
        foreach (var t in Linspace(0f, plan.Duration, 240))
        {
            var p = TraversalMath.Sample(plan, t);
            var dx = p.X - plan.StartX;
            var dy = p.Y - plan.StartY;
            var d = MathF.Sqrt(dx * dx + dy * dy);
            Assert.True(d >= last - 1e-6f, $"t={t} 水平距离回退：{last} -> {d}");
            last = d;
        }
        Assert.Equal(MathF.Sqrt(
            (plan.EndX - plan.StartX) * (plan.EndX - plan.StartX)
            + (plan.EndY - plan.StartY) * (plan.EndY - plan.StartY)), last, 4);
    }

    [Fact]
    public void Sample_IsDeterministic_Bitwise()
    {
        var plan = Plan();
        for (int i = 1; i < 40; i++)
        {
            var t = plan.Duration * i / 40f;
            var a = TraversalMath.Sample(plan, t);
            var b = TraversalMath.Sample(plan, t);
            Assert.Equal(a.X, b.X);
            Assert.Equal(a.Y, b.Y);
            Assert.Equal(a.Z, b.Z);
        }
    }

    [Fact]
    public void ZeroDuration_SampleReturnsEnd()
    {
        var plan = Plan(duration: 0f);
        var p = TraversalMath.Sample(plan, 0f);
        Assert.Equal(plan.EndX, p.X);
        Assert.Equal(plan.EndY, p.Y);
        Assert.Equal(plan.EndZ, p.Z);
    }

    // ── 步进器窗口 ────────────────────────────────────────────

    [Fact]
    public void Stepper_Traversal_IgnoresMoveAndJumpInput()
    {
        var stepper = new MovementFormulaStepper();
        var plan = Plan();
        var result = stepper.Step(new MovementStepInput
        {
            X = 0f, Y = 0f, Z = 0f, Vz = 5f,
            MoveX = 1f, MoveY = 1f,
            JumpPressed = true, IsQinggongJump = true,
            JumpCount = 2,
            MaxSpeed = 6f,
            TraversalActive = true,
            TraversalPlan = plan,
            TraversalElapsed = 0.1f,
        }, Dt);

        var expected = TraversalMath.Sample(plan, 0.1f + Dt);
        Assert.Equal(expected.X, result.X, 6);
        Assert.Equal(expected.Y, result.Y, 6);
        Assert.Equal(expected.Z, result.Z, 6);
        Assert.Equal(0f, result.Vz);
        Assert.Equal(0, result.JumpCount);
        Assert.True(result.TraversalActive);
        Assert.False(result.TraversalFinished);
    }

    [Fact]
    public void Stepper_Traversal_FinishesExactlyAtEnd()
    {
        var stepper = new MovementFormulaStepper();
        var plan = Plan(duration: 0.5f);
        float elapsed = 0f;
        int steps = 0;
        MovementStepResult result = default;
        var input = new MovementStepInput
        {
            MoveX = 1f, MoveY = 0f, MaxSpeed = 6f,
            TraversalActive = true,
            TraversalPlan = plan,
        };

        while (!result.TraversalFinished && steps < 120)
        {
            result = stepper.Step(new MovementStepInput
            {
                X = result.X, Y = result.Y, Z = result.Z, Vz = result.Vz,
                MoveX = input.MoveX, MoveY = input.MoveY, MaxSpeed = input.MaxSpeed,
                TraversalActive = true,
                TraversalPlan = plan,
                TraversalElapsed = elapsed,
            }, Dt);
            elapsed = result.TraversalElapsed;
            steps++;
        }

        Assert.True(result.TraversalFinished, "翻越在 120 步内没有结束");
        Assert.Equal(plan.Duration, result.TraversalElapsed);
        Assert.Equal(plan.EndX, result.X, 5);
        Assert.Equal(plan.EndY, result.Y, 5);
        Assert.Equal(plan.EndZ, result.Z, 5);
        // 0.5s / (1/60s) ≈ 30 步；给足进位余量但不能明显超过。
        Assert.InRange(steps, 30, 32);

        // 结束后再推进仍停在终点（幂等，不会外推/回弹）。
        var again = stepper.Step(new MovementStepInput
        {
            X = result.X, Y = result.Y, Z = result.Z,
            TraversalActive = true,
            TraversalPlan = plan,
            TraversalElapsed = result.TraversalElapsed,
        }, Dt);
        Assert.True(again.TraversalFinished);
        Assert.Equal(plan.EndX, again.X, 5);
        Assert.Equal(plan.EndY, again.Y, 5);
        Assert.Equal(plan.EndZ, again.Z, 5);
    }

    [Fact]
    public void Stepper_TraversalWithZeroDuration_FinishesAtEndImmediately()
    {
        var stepper = new MovementFormulaStepper();
        var plan = Plan(duration: 0f);
        var result = stepper.Step(new MovementStepInput
        {
            TraversalActive = true,
            TraversalPlan = plan,
        }, Dt);
        Assert.True(result.TraversalFinished);
        Assert.Equal(plan.EndX, result.X);
        Assert.Equal(plan.EndY, result.Y);
        Assert.Equal(plan.EndZ, result.Z);
        Assert.Equal(0f, result.Vz);
    }

    /// <summary>
    /// 非翻越帧必须与"手工调用 JumpStateMachine + MovementFormula"逐位一致——
    /// 独立参照（不复用 stepper），防止新增分支顺手改坏常规移动。
    /// </summary>
    [Fact]
    public void Stepper_NonTraversal_MatchesIndependentReference()
    {
        var options = new SimulationOptions();
        var stepper = new MovementFormulaStepper(options);

        var inputs = new[]
        {
            new { X = 1f, Y = 2f, Z = 3f, Vz = 0f, Mx = 0.5f, My = -0.25f, Jump = false, Qing = false, Jc = 0, Speed = 6f },
            new { X = 1f, Y = 2f, Z = 3f, Vz = 0f, Mx = 0f, My = 0f, Jump = true, Qing = false, Jc = 0, Speed = 6f },
            new { X = 1.2f, Y = 1.8f, Z = 3.1f, Vz = 4.2f, Mx = 1f, My = 1f, Jump = true, Qing = true, Jc = 1, Speed = 10f },
            new { X = 1.3f, Y = 1.7f, Z = 3.2f, Vz = 3.1f, Mx = -1f, My = 0f, Jump = false, Qing = true, Jc = 2, Speed = 12f },
        };

        foreach (var i in inputs)
        {
            var result = stepper.Step(new MovementStepInput
            {
                X = i.X, Y = i.Y, Z = i.Z, Vz = i.Vz,
                MoveX = i.Mx, MoveY = i.My,
                JumpPressed = i.Jump, IsQinggongJump = i.Qing,
                JumpCount = i.Jc, MaxSpeed = i.Speed,
            }, Dt);

            var (jumpCount, impulse, _) = JumpStateMachine.Advance(
                i.Jc, i.Jump, i.Qing, options.JumpImpulses, options.MaxQinggongJumpCount);
            var (x, y, z, vz) = MovementFormula.Step(
                i.X, i.Y, i.Z, i.Vz, i.Mx, i.My, impulse, Dt, maxSpeed: i.Speed);

            Assert.Equal(x, result.X);
            Assert.Equal(y, result.Y);
            Assert.Equal(z, result.Z);
            Assert.Equal(vz, result.Vz);
            Assert.Equal(jumpCount, result.JumpCount);
            Assert.False(result.TraversalActive);
            Assert.False(result.TraversalFinished);
            Assert.Equal(0f, result.TraversalElapsed);
        }
    }

    // ── 计划校验矩阵 ──────────────────────────────────────────

    [Fact]
    public void Validate_AcceptsPlausiblePlan()
    {
        var plan = Plan(sx: 1f, sy: 2f, sz: 3f, ex: 51f, ey: 62f, ez: 83f, duration: 0.6f, arc: 20f);
        Assert.Equal(TraversalRejectReason.None, TraversalMath.Validate(plan, 1f, 2f, 3f));
    }

    [Fact]
    public void Validate_RejectsBadDuration()
    {
        var tooShort = Plan(duration: 0.24f);
        Assert.Equal(TraversalRejectReason.InvalidDuration, TraversalMath.Validate(tooShort, 0f, 0f, 0f));

        var tooLong = Plan(duration: 2.01f);
        Assert.Equal(TraversalRejectReason.InvalidDuration, TraversalMath.Validate(tooLong, 0f, 0f, 0f));

        var zero = Plan(duration: 0f);
        Assert.Equal(TraversalRejectReason.InvalidDuration, TraversalMath.Validate(zero, 0f, 0f, 0f));

        var negative = Plan(duration: -1f);
        Assert.Equal(TraversalRejectReason.InvalidDuration, TraversalMath.Validate(negative, 0f, 0f, 0f));
    }

    [Fact]
    public void Validate_RejectsHeightOutOfRange()
    {
        Assert.Equal(TraversalRejectReason.HeightOutOfRange,
            TraversalMath.Validate(Plan(ez: 19f), 0f, 0f, 0f));
        Assert.Equal(TraversalRejectReason.HeightOutOfRange,
            TraversalMath.Validate(Plan(ez: 251f), 0f, 0f, 0f));
        // 往下跳不是翻越（终点必须高于起点）。
        Assert.Equal(TraversalRejectReason.HeightOutOfRange,
            TraversalMath.Validate(Plan(ez: -100f), 0f, 0f, 0f));
    }

    [Fact]
    public void Validate_RejectsHorizontalOutOfRange()
    {
        Assert.Equal(TraversalRejectReason.HorizontalOutOfRange,
            TraversalMath.Validate(Plan(ex: 0f, ey: 0f), 0f, 0f, 0f));
        Assert.Equal(TraversalRejectReason.HorizontalOutOfRange,
            TraversalMath.Validate(Plan(ex: 251f, ey: 0f), 0f, 0f, 0f));
    }

    [Fact]
    public void Validate_RejectsArcOutOfRange()
    {
        Assert.Equal(TraversalRejectReason.ArcOutOfRange,
            TraversalMath.Validate(Plan(arc: -0.01f), 0f, 0f, 0f));
        Assert.Equal(TraversalRejectReason.ArcOutOfRange,
            TraversalMath.Validate(Plan(arc: 251f), 0f, 0f, 0f));
    }

    [Fact]
    public void Validate_RejectsStartTooFar()
    {
        var plan = Plan(sx: 0f, sy: 0f, sz: 0f);
        Assert.Equal(TraversalRejectReason.None, TraversalMath.Validate(plan, 74f, 10f, 0f));
        Assert.Equal(TraversalRejectReason.StartTooFar, TraversalMath.Validate(plan, 76f, 10f, 0f));
        // 起点容差是三维距离：垂直偏移同样算。
        Assert.Equal(TraversalRejectReason.StartTooFar, TraversalMath.Validate(plan, 0f, 0f, 80f));
    }

    [Fact]
    public void Validate_RejectsNaNAndInfinity()
    {
        Assert.Equal(TraversalRejectReason.InvalidNumbers,
            TraversalMath.Validate(Plan(ex: float.NaN), 0f, 0f, 0f));
        Assert.Equal(TraversalRejectReason.InvalidNumbers,
            TraversalMath.Validate(Plan(sz: float.PositiveInfinity), 0f, 0f, 0f));
        Assert.Equal(TraversalRejectReason.InvalidNumbers,
            TraversalMath.Validate(Plan(duration: float.NaN), 0f, 0f, 0f));
    }

    [Fact]
    public void Validate_AcceptsBoundaryValuesInclusive()
    {
        // 每条限值的上下边界都必须被接受（闭区间），否则客户端按限值生成会被自己拒绝。
        Assert.Equal(TraversalRejectReason.None,
            TraversalMath.Validate(Plan(duration: 0.25f), 0f, 0f, 0f));
        Assert.Equal(TraversalRejectReason.None,
            TraversalMath.Validate(Plan(duration: 2.0f), 0f, 0f, 0f));
        Assert.Equal(TraversalRejectReason.None,
            TraversalMath.Validate(Plan(ez: 20f), 0f, 0f, 0f));
        Assert.Equal(TraversalRejectReason.None,
            TraversalMath.Validate(Plan(ez: 250f), 0f, 0f, 0f));
        Assert.Equal(TraversalRejectReason.None,
            TraversalMath.Validate(Plan(ex: 250f, ey: 0f), 0f, 0f, 0f));
        Assert.Equal(TraversalRejectReason.None,
            TraversalMath.Validate(Plan(arc: 0f), 0f, 0f, 0f));
        Assert.Equal(TraversalRejectReason.None,
            TraversalMath.Validate(Plan(arc: 250f), 0f, 0f, 0f));

        var plan = Plan(sx: 75f);
        Assert.Equal(TraversalRejectReason.None, TraversalMath.Validate(plan, 0f, 0f, 0f));
    }

    [Fact]
    public void Validate_UsesSuppliedLimits()
    {
        var tight = new TraversalLimits
        {
            MinDuration = 0.5f,
            MaxDuration = 0.9f,
            MinHeight = 50f,
            MaxHeight = 100f,
            MaxHorizontalDistance = 100f,
            MaxArcHeight = 10f,
            StartTolerance = 5f,
        };

        var plan = Plan(sx: 0f, sy: 0f, sz: 0f, ex: 50f, ey: 0f, ez: 60f, duration: 0.6f, arc: 5f);
        Assert.Equal(TraversalRejectReason.None, TraversalMath.Validate(plan, 0f, 0f, 0f, tight));

        Assert.Equal(TraversalRejectReason.InvalidDuration,
            TraversalMath.Validate(Plan(duration: 0.4f), 0f, 0f, 0f, tight));
        Assert.Equal(TraversalRejectReason.HeightOutOfRange,
            TraversalMath.Validate(Plan(ez: 120f), 0f, 0f, 0f, tight));
        Assert.Equal(TraversalRejectReason.HorizontalOutOfRange,
            TraversalMath.Validate(Plan(ex: 120f, ey: 0f), 0f, 0f, 0f, tight));
        Assert.Equal(TraversalRejectReason.ArcOutOfRange,
            TraversalMath.Validate(Plan(arc: 20f, ex: 50f, ey: 0f), 0f, 0f, 0f, tight));
        Assert.Equal(TraversalRejectReason.StartTooFar,
            TraversalMath.Validate(plan, 6f, 0f, 0f, tight));
    }

    [Fact]
    public void SimulationOptions_ExposeSameLimitsByDefault()
    {
        // 默认限值只有一份来源：Options 里的实例必须是同样的取值，
        // 否则"客户端用 Options、服务端用 new TraversalLimits()"会静默分叉。
        var options = new SimulationOptions();
        var defaults = new TraversalLimits();
        Assert.Equal(defaults.MinDuration, options.Traversal.MinDuration);
        Assert.Equal(defaults.MaxDuration, options.Traversal.MaxDuration);
        Assert.Equal(defaults.MinHeight, options.Traversal.MinHeight);
        Assert.Equal(defaults.MaxHeight, options.Traversal.MaxHeight);
        Assert.Equal(defaults.MaxHorizontalDistance, options.Traversal.MaxHorizontalDistance);
        Assert.Equal(defaults.MaxArcHeight, options.Traversal.MaxArcHeight);
        Assert.Equal(defaults.StartTolerance, options.Traversal.StartTolerance);
    }

    private static float[] Linspace(float from, float to, int count)
    {
        var values = new float[count];
        for (int i = 0; i < count; i++)
            values[i] = from + (to - from) * i / (count - 1);
        return values;
    }
}
