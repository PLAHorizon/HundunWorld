namespace Horizon.Game.ECS.Arch.Components;

/// <summary>
/// 非玩家实体插值变换组件：用于平滑服务器权威位置更新，消除网络抖动。
/// </summary>
/// <remarks>
/// 每个 Tick 开始时，<see cref="Systems.SnapshotApplySystem"/> 将服务器下发的权威位置写入
/// <see cref="TargetX"/>/<see cref="TargetY"/>/<see cref="TargetZ"/>，并将 <see cref="Alpha"/> 重置为 0。
/// <see cref="Systems.InterpolationSystem"/> 在 Render 阶段根据 <see cref="Alpha"/> 在当前位置与目标位置间插值，
/// 逐步推进 <see cref="Alpha"/> 至 1.0。
/// </remarks>
public struct InterpolatedTransformComponent
{
    /// <summary>当前插值位置 X（米）。</summary>
    public float X;

    /// <summary>当前插值位置 Y（米）。</summary>
    public float Y;

    /// <summary>当前插值位置 Z（米）。</summary>
    public float Z;

    /// <summary>目标位置 X（米），由快照更新写入。</summary>
    public float TargetX;

    /// <summary>目标位置 Y（米），由快照更新写入。</summary>
    public float TargetY;

    /// <summary>目标位置 Z（米），由快照更新写入。</summary>
    public float TargetZ;

    /// <summary>插值系数（0..1），0 表示位于旧位置，1 表示到达目标位置。</summary>
    public float Alpha;

    /// <summary>目标位置对应的服务器 Tick 序号。</summary>
    public long ServerTick;

    /// <summary>接收到此目标位置的本地 Tick 序号。</summary>
    public long ReceivedTick;
}
