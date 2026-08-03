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

    /// <summary>插值起始位置 X。</summary>
    public float StartX;

    /// <summary>插值起始位置 Y。</summary>
    public float StartY;

    /// <summary>插值起始位置 Z。</summary>
    public float StartZ;

    /// <summary>插值系数（0..1），0 表示位于旧位置，1 表示到达目标位置。</summary>
    public float Alpha;

    /// <summary>目标位置对应的服务器 Tick 序号。</summary>
    public long ServerTick;

    /// <summary>接收到此目标位置的本地 Tick 序号。</summary>
    public long ReceivedTick;

    /// <summary>[Phase C4] 自上次快照重置 Alpha 以来经过的时间（秒），用于 dead reckoning 速度衰减。</summary>
    public float TimeSinceLastSnapshot;

    /// <summary>当前插值 Yaw（弧度）。服务端 entity.Yaw 为弧度，InterpolationSystem 用 MathF.PI 做最短路径归一化。</summary>
    public float Yaw;

    /// <summary>插值起始 Yaw（弧度）。</summary>
    public float StartYaw;

    /// <summary>目标 Yaw（弧度），由快照更新写入。</summary>
    public float TargetYaw;

    /// <summary>
    /// 远程实体的网络状态，用于精确控制不同场景下的插值与清理策略。
    /// </summary>
    public RemoteEntityState State;

    /// <summary>
    /// 上一次收到 Update delta 时的水平速度（米/秒），用于 dead reckoning 时按原速度推进位置。
    /// 仅在 State == Active 时有效。
    /// </summary>
    public float LastVelocityXZ_X;

    /// <summary>上一次收到 Update delta 时的水平速度 Y 分量（米/秒）。</summary>
    public float LastVelocityXZ_Y;

    /// <summary>
    /// 最近一次进入前向预测模式（Active 且 LastVelocityXZ 非零）的服务器 tick，
    /// 供诊断消费者追踪前向预测生效起点与切换点位置连续性。
    /// </summary>
    public long SwitchFromLerpToDeadReckoningTick;

    /// <summary>上一帧渲染位置 X（米），用于平滑度评分采样（位置 delta 计算）。</summary>
    public float PreviousFrameX;

    /// <summary>上一帧渲染位置 Y（米）。</summary>
    public float PreviousFrameY;

    /// <summary>上一帧渲染位置 Z（米）。</summary>
    public float PreviousFrameZ;

    /// <summary>PreviousFrameX/Y/Z 是否已初始化（首帧跳过 delta 计算避免初始跳变污染评分）。</summary>
    public bool PreviousFrameInitialized;
}

/// <summary>
/// 远程实体的网络状态枚举。区分"在线移动"、"在线静止"、"疑似异常"、"主动离线"等场景，
/// 避免不同状态被同一套插值逻辑混杂处理导致闪移/闪现/莫名离线。
/// </summary>
public enum RemoteEntityState : byte
{
    /// <summary>
    /// 初始状态：刚创建，尚未收到任何 Update delta。
    /// 进入条件：HandleSpawn 创建实体。
    /// 行为：保持当前位置，等待首个 Update delta 到达。
    /// 转移：收到 Update delta → Active。
    /// </summary>
    Initializing = 0,

    /// <summary>
    /// 在线移动：最近 0.5 秒内收到过 Update delta，远程角色正在移动。
    /// 进入条件：HandleUpdate 收到 Update delta（且位置/旋转有变化）。
    /// 行为：Lerp 平滑追赶目标位置，速度自适应（基于快照到达频率）。
    /// 转移：超过 0.5 秒未收到新 delta → Idle。
    /// </summary>
    Active = 1,

    /// <summary>
    /// 在线静止：0.5 ~ 5 秒未收到 Update delta，远程角色保持静止。
    /// 进入条件：TimeSinceLastSnapshot 超过 0.5 秒但小于 5 秒。
    /// 行为：停止 Lerp 追赶（保持在当前位置），避免静止实体漂移。
    /// 转移：
    ///   - 收到 Update delta → Active
    ///   - 超过 5 秒 → Stale
    /// </summary>
    Idle = 2,

    /// <summary>
    /// 疑似异常：5 ~ 90 秒未收到 Update delta，远程角色可能掉线或网络分区。
    /// 进入条件：TimeSinceLastSnapshot 超过 5 秒但小于 90 秒。
    /// 行为：停止 Lerp 追赶，保持实体（不销毁），供 UI 显示警告图标。
    /// 转移：
    ///   - 收到 Update delta → Active
    ///   - 超过 90 秒 → TimeoutDespawn（兜底销毁）
    /// </summary>
    Stale = 3,

    /// <summary>
    /// 主动离线：收到服务端 Despawn delta，远程角色确认下线。
    /// 进入条件：HandleDespawn 收到 Despawn delta。
    /// 行为：立即销毁实体，触发 EntityDespawned 事件。
    /// </summary>
    Offline = 4,

    /// <summary>
    /// 超时清理：90 秒未收到任何快照，兜底机制判定为离线。
    /// 进入条件：TimeSinceLastSnapshot ≥ 90 秒。
    /// 行为：销毁实体（与主动离线视觉一致，但日志区分原因）。
    /// </summary>
    TimeoutDespawn = 5,
}
