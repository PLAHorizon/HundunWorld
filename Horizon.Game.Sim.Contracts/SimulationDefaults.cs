namespace Horizon.Game.Sim;

/// <summary>
/// 确定性仿真阈值单一来源：客户端预测与服务端权威校验必须取同一组数值，
/// 否则 <c>SimulationOptions.ToValidatorOptions()</c> 投影出的配置会与客户端 stepper 行为分叉。
/// </summary>
public static class SimulationDefaults
{
    /// <summary>位置偏差阈值（米），超过下发 correction。</summary>
    public const float PositionEpsilon = 0.5f;

    /// <summary>速度硬性上限（米/秒）：v6 协议起 MaxSpeed 为合法字段，此值仅作上限。</summary>
    public const float HardSpeedCap = 200f;
}
