namespace Horizon.Game.ECS.Arch.Components;

/// <summary>
/// 玩家输入状态组件：存储当前帧的玩家输入数据。
/// </summary>
public struct PlayerInputComponent
{
    /// <summary>水平移动输入 X（-1..1），对应 strafing。</summary>
    public float MoveX;

    /// <summary>水平移动输入 Y（-1..1），对应前进/后退。</summary>
    public float MoveY;

    /// <summary>视角偏航（Yaw 弧度）。</summary>
    public float LookYaw;

    /// <summary>视角俯仰（Pitch 弧度）。</summary>
    public float LookPitch;

    /// <summary>跳跃按键是否按下。</summary>
    public bool JumpPressed;

    /// <summary>
    /// 本帧是否为跳跃按下边沿（前一帧 false → 当前帧 true）。
    /// 由 PlayerController 在 WriteInputToEcs 中维护，仅 true 的那一帧触发跳跃 jumpCount++。
    /// 修复"持续按住空格 → 轻功三段跳在 50ms 内被消耗完"问题。
    /// 服务端通过 InputBits bit0 推断跳跃，客户端需在 JumpPressedThisFrame=true 的帧才设 bit0=1，
    /// 下一帧即使持续按住也置 0，相当于客户端先行做边沿触发，服务端无需改动。
    /// </summary>
    public bool JumpPressedThisFrame;

    /// <summary>输入位掩码：移动方向、跳跃、技能 1..N。</summary>
    public uint InputBits;

    /// <summary>
    /// 本帧目标最大水平移动速度（米/秒）。由 PlayerController.WriteInputToEcs 根据 MoveSpeed
    /// 及当前状态（Run/Sprint/Crouch 倍数）计算后写入。LocalSimulationSystem 据此调用
    /// MovementFormula.Step，InputSendSystem 打包到 InputPacket.MaxSpeed 上送服务端。
    /// 取值 &lt;= 0 时由 MovementFormula 内部兜底为 DefaultMaxSpeed（向后兼容）。
    /// </summary>
    public float MaxSpeed;
}
