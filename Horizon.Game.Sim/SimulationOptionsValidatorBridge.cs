namespace Horizon.Game.Sim;

/// <summary>
/// <see cref="SimulationOptions"/> 到权威校验器配置的桥接。<br/>
/// 配置类型 <see cref="MovementValidator.Options"/> 留在服务端（它属于反作弊校验链），
/// 而 <see cref="SimulationOptions"/> 是客户端与服务端共享的契约，故投影方法放这一侧。
/// </summary>
public static class SimulationOptionsValidatorBridge
{
    /// <summary>
    /// 投影为 <see cref="MovementValidator.Options"/>（权威校验器配置）。
    /// 复制数组：避免校验器与 stepper 共享可变数组造成意外交叉修改。
    /// </summary>
    public static MovementValidator.Options ToValidatorOptions(this SimulationOptions options) => new()
    {
        PositionEpsilon = options.PositionEpsilon,
        HardSpeedCap = options.HardSpeedCap,
        MaxSpeed = options.MaxSpeed,
        TickDtSeconds = options.TickDtSeconds,
        MaxJumpCount = options.MaxJumpCount,
        MaxQinggongJumpCount = options.MaxQinggongJumpCount,
        MaxAcceleration = options.MaxAcceleration,
        TeleportDistanceThreshold = options.TeleportDistanceThreshold,
        MaxDynamicEpsilon = options.MaxDynamicEpsilon,
        JumpImpulses = (float[])options.JumpImpulses.Clone(),
    };
}
