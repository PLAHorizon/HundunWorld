namespace Horizon.Game.Sim;

/// <summary>
/// 单帧移动推进委派接口（阶段 1.3）：封装「水平步进 + 垂直重力 + 跳跃状态机（轻功三段/普通单跳/
/// 未按清零）」的**唯一**实现，供客户端预测（LocalSimulationSystem）与修正重放（ReconciliationSystem）
/// 共用——消除预测 vs 重放两处二度拷贝的三段跳冲量 switch 与跳跃计数逻辑。
/// </summary>
/// <remarks>
/// 确定性约束：实现必须只依赖 <see cref="MovementFormula"/>（纯静态确定性数学层）与配置，
/// 不得引入随机源/时钟/遍历顺序不定集合。返回 <see cref="MovementStepResult"/> 携带更新后的跳跃计数，
/// 调用方负责按实体存储（预测为字典、重放为局部变量）。
/// </remarks>
public interface IMovementStepper
{
    /// <summary>
    /// 推进一帧：从 (x, y, z, vz) 出发，按 (moveX, moveY, jump 状态机) + <paramref name="dt"/> 计算新位置/速度。
    /// </summary>
    /// <param name="input">当前状态 + 输入。</param>
    /// <param name="dt">时间步长（秒）。</param>
    /// <returns>新位置/速度 + 更新后的跳跃计数（调用方据此持久化）。</returns>
    MovementStepResult Step(in MovementStepInput input, float dt);
}

/// <summary>单帧移动输入：当前位置/速度 + 本帧输入 + 当前跳跃计数。</summary>
public readonly struct MovementStepInput
{
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    public float Vz { get; init; }

    /// <summary>水平输入 X 分量，[-1, 1]。</summary>
    public float MoveX { get; init; }

    /// <summary>水平输入 Y 分量，[-1, 1]。</summary>
    public float MoveY { get; init; }

    /// <summary>本帧跳跃按下（边沿触发，与 InputPacket.JumpPressedThisFrame 语义一致）。</summary>
    public bool JumpPressed { get; init; }

    /// <summary>本帧是否为轻功跳跃（InputBits bit3）。</summary>
    public bool IsQinggongJump { get; init; }

    /// <summary>当前跳跃计数（上一帧遗留；0 = 未起跳/落地已重置）。</summary>
    public int JumpCount { get; init; }

    /// <summary>本帧最大水平速度；非正值使用默认。</summary>
    public float MaxSpeed { get; init; }
}

/// <summary>单帧移动结果：新位置/速度 + 更新后的跳跃计数。</summary>
public readonly struct MovementStepResult
{
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    public float Vz { get; init; }

    /// <summary>推进后的跳跃计数（调用方应持久化到实体状态）。</summary>
    public int JumpCount { get; init; }
}

/// <summary>
/// <see cref="IMovementStepper"/> 的默认实现：以 <see cref="MovementFormula"/> 为底层数学，
/// 跳跃状态机语义与 LocalSimulationSystem / ReconciliationSystem 历史实现逐位一致。
/// </summary>
public sealed class MovementFormulaStepper : IMovementStepper
{
    private readonly SimulationOptions _options;

    public MovementFormulaStepper(SimulationOptions? options = null)
    {
        _options = options ?? new SimulationOptions();
    }

    /// <inheritdoc />
    public MovementStepResult Step(in MovementStepInput input, float dt)
    {
        // 跳跃状态机对齐：与权威 MovementValidator 共用 JumpStateMachine——按下轻功递增/普通重置为 1、
        // 未按下保留计数（空中滞空延续；落地清零由调用方地面约束负责）。
        // 修复历史分叉：旧实现"未按下+非轻功 → jumpCount 清零"与权威"保留"不一致——轻功三段跳中
        // 完全松键一帧再按时，预测从首段 5.5 重新起跳而权威按上一段继续 → 漂移 → correction 风暴。
        var (jumpCount, jumpImpulse, _) = JumpStateMachine.Advance(
            input.JumpCount,
            input.JumpPressed,
            input.IsQinggongJump,
            _options.JumpImpulses,
            _options.MaxQinggongJumpCount);

        var (nx, ny, nz, nvz) = MovementFormula.Step(
            input.X, input.Y, input.Z, input.Vz,
            input.MoveX, input.MoveY, jumpImpulse,
            dt, maxSpeed: input.MaxSpeed);

        return new MovementStepResult
        {
            X = nx,
            Y = ny,
            Z = nz,
            Vz = nvz,
            JumpCount = jumpCount,
        };
    }
}
