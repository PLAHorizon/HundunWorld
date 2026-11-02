using System;

namespace Horizon.Game.Sim;

/// <summary>
/// 集中式仿真配置（阶段 1.2.2）：三段跳冲量 / 固定步长 / 反作弊阈值单一来源。
/// <para>
/// 供 <see cref="MovementValidator.Options"/>（权威校验）、<see cref="IMovementStepper"/>（客户端
/// 预测/重放共用跳跃语义）与未来 UE5 DS 共享引用，消除各调用方方法内硬编码冲量/阈值的漂移。
/// 默认值必须与历史硬编码逐位一致（金轨迹指纹 / FormulaVersion 护航网依赖行为不变）。
/// </para>
/// </summary>
public sealed class SimulationOptions
{
    /// <summary>
    /// 轻功三段跳冲量基准（米/秒²·帧）：1→5.5 / 2→4.5 / 3→3.5（jumpCount 越界 → 0，即超过三段无冲量）。
    /// 规范来源：预测（LocalSimulationSystem）与重放（ReconciliationSystem）原方法内 switch 的同一组值。
    /// </summary>
    public static readonly float[] DefaultJumpImpulses = { 5.5f, 4.5f, 3.5f };

    /// <summary>每帧固定步长（秒），默认 1/60。</summary>
    public float TickDtSeconds { get; set; } = 1f / 60f;

    /// <summary>轻功三段跳冲量（jumpCount=1 → [0]、2 → [1]、3 → [2]；越界或无此档 → 0）。</summary>
    public float[] JumpImpulses { get; set; } = (float[])DefaultJumpImpulses.Clone();

    /// <summary>位置偏差阈值（米），超过下发 correction。默认 0.5m。</summary>
    public float PositionEpsilon { get; set; } = SimulationDefaults.PositionEpsilon;

    /// <summary>速度硬性上限（米/秒）。默认 200 m/s（v6 协议后 MaxSpeed 合法字段，容纳坐骑/轻功/技能加速）。</summary>
    public float HardSpeedCap { get; set; } = SimulationDefaults.HardSpeedCap;

    /// <summary>MaxSpeed 兜底值（客户端未填充时）。</summary>
    public float MaxSpeed { get; set; } = MovementFormula.DefaultMaxSpeed;

    /// <summary>普通跳跃最大次数（每次落地重置；默认 2 段含初始跳？实际跳数由 jumpCount 递增 + 冲量表决定）。</summary>
    public int MaxJumpCount { get; set; } = 2;

    /// <summary>轻功最大段数（默认 3 段；超过后冲量归零但仍递增计数，语义与历史一致）。</summary>
    public int MaxQinggongJumpCount { get; set; } = 3;

    /// <summary>最大允许加速度（m/s²），默认 50（约 5G）。</summary>
    public float MaxAcceleration { get; set; } = 50f;

    /// <summary>瞬移距离阈值（米），默认 100m。</summary>
    public float TeleportDistanceThreshold { get; set; } = 100f;

    /// <summary>动态阈值上限（米），默认 12.0m。</summary>
    public float MaxDynamicEpsilon { get; set; } = 12.0f;

    /// <summary>
    /// 翻越计划限值（时长/抬升/水平距离/弧高/起点容差）。客户端生成计划与服务端权威校验必须引用
    /// 同一份，禁止各写一套数字——见 <see cref="TraversalMath.Validate"/>。
    /// </summary>
    public TraversalLimits Traversal { get; set; } = new TraversalLimits();
}
