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

    /// <summary>输入位掩码：移动方向、跳跃、技能 1..N。</summary>
    public uint InputBits;
}
