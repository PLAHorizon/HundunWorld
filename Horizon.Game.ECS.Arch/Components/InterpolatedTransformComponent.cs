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

    // ─── 传送混合状态（3 档传送处理：Lerp / 加速混合 / 硬跳） ───
    // 当 Target 与当前位置距离超过 TeleportThresholdMeters 但未超过 HardSnapThresholdMeters 时，
    // InterpolationSystem 启动"加速混合"——在 TeleportBlendDurationSeconds 内用 smoothstep 缓动
    // 从当前位置过渡到 Target，把"瞬移"变成可见的"快速冲刺"，减少闪跳对游戏可游玩性的影响。
    // 注意：不复用 StartX/Y/Z/StartYaw——这些字段被 FlaxActorSyncSystem 的 IsWalking 回退分支读取
    // （Target - Start 判断是否移动），复用会污染动画状态判断。

    /// <summary>
    /// 传送混合剩余时长（秒）。<c>&gt;0</c> 表示混合进行中，<c>==0</c> 表示未在混合。<br/>
    /// 由 InterpolationSystem 在目标距离超过 TeleportThresholdMeters 时初始化为
    /// <see cref="TeleportBlendDurationSeconds"/>，每帧递减 dt，减到 0 时混合完成并清零。
    /// </summary>
    public float TeleportBlendRemainingSeconds;

    /// <summary>
    /// 传送混合总时长（秒），与 <see cref="TeleportBlendRemainingSeconds"/> 配对计算
    /// <c>alpha = 1 - remaining / duration</c>。混合完成时与 Remaining 一并清零。
    /// </summary>
    public float TeleportBlendDurationSeconds;

    /// <summary>传送混合起始位置 X（混合触发瞬间 interp.X 的快照）。</summary>
    public float TeleportBlendStartX;

    /// <summary>传送混合起始位置 Y。</summary>
    public float TeleportBlendStartY;

    /// <summary>传送混合起始位置 Z。</summary>
    public float TeleportBlendStartZ;

    /// <summary>传送混合起始 Yaw（弧度，含 ±π 最短路径归一化处理）。</summary>
    public float TeleportBlendStartYaw;

    // ─── 方案6（plan.md §5 方案6 / §4.1 Mirror Snapshot Interpolation）：有界快照缓冲 ───
    // Mirror 式基于 server timestamp 的时间插值：维护有界环形缓冲，渲染时在两个相邻快照间 Lerp。
    // 解决根因 #6：Target+Lerp 追赶模型在快照抖动时 Target 不更新 → Lerp 收敛到旧 Target → 视觉冻结。
    // 有了缓冲，快照抖动 200ms 期间可在两个旧快照间插值，保持视觉移动。
    // 缓冲不足（< 2 样本）时回退到 Target+Lerp 追赶（兼容旧逻辑）。

    /// <summary>方案6：快照缓冲容量（环形缓冲上限，借鉴 Mirror bufferSize=64，此处取 16 平衡内存与延迟窗口）。</summary>
    public const int SnapshotBufferSize = 16;

    /// <summary>
    /// 方案6：有界快照环形缓冲。null 表示未初始化（首次 HandleUpdate 时按需创建）。
    /// 按 ServerTick 单调递增写入（覆盖最旧），插值时线性扫描找 renderTick 两侧样本。
    /// </summary>
    public SnapshotSample[]? SnapshotBuffer;

    /// <summary>方案6：环形缓冲下一个写入位置（0..SnapshotBufferSize-1）。</summary>
    public int SnapshotBufferHead;

    /// <summary>方案6：当前有效样本数（0..SnapshotBufferSize，满后不再增长，覆盖最旧）。</summary>
    public int SnapshotBufferCount;
}

/// <summary>
/// 方案6（plan.md §5 方案6 / §4.1）：快照样本，用于 Mirror 式有界快照缓冲时间插值。
/// 每次 HandleUpdate 收到 Transform delta 时写入一帧，InterpolationSystem 基于缓冲做时间插值。
/// </summary>
public struct SnapshotSample
{
    /// <summary>采样时的服务器 tick（作为时间坐标，假设服务端 tick 间隔均匀）。</summary>
    public long ServerTick;

    /// <summary>位置 X（米）。</summary>
    public float X;

    /// <summary>位置 Y（米）。</summary>
    public float Y;

    /// <summary>位置 Z（米）。</summary>
    public float Z;

    /// <summary>Yaw（弧度）。</summary>
    public float Yaw;

    /// <summary>水平速度 X 分量（米/秒，对应 ECS X 轴，供外推/诊断用）。</summary>
    public float VelocityX;

    /// <summary>水平速度 Z 分量（米/秒，对应 ECS Z 轴即前后方向，供外推/诊断用）。</summary>
    public float VelocityZ;
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
