namespace Horizon.Game.ECS.Arch.Configuration;

/// <summary>
/// 远程角色同步阈值与多角色性能配置（受控配置通道下发，非法值由 <see cref="RemoteSyncThresholdValidator"/> 兜底）。
/// </summary>
/// <remarks>
/// 集中承载平滑区/硬跳/混合时长/多角色性能参数，替代 <c>InterpolationSystem</c> 与
/// <c>FlaxActorSyncSystem</c> 中编译期硬编码的阈值（DFX 4.4.2 阈值可配置）。
/// 默认值与需求规格完全一致：平滑区 100m / 硬跳 500m / 混合时长 0.2s / Near 30m / Mid 80m /
/// 性能降档 10 个 / 数量硬上限 20 个。
/// </remarks>
public sealed class RemoteSyncThresholdOptions
{
    /// <summary>
    /// 平滑区阈值（米），默认 200，合法区间 (0, <see cref="HardSnapThresholdMeters"/>]。
    /// 修复（远程角色"前冲/回拉"——基于实测日志）：B 端实测 TeleportJump 距离反复为 101~114m，
    /// 恰好压过原 100m 阈值 → 每次都触发 200ms "加速混合冲刺"（视觉=前冲），位置跳回 → 回拉。
    /// 提到 200m 后，100m 级跳变走普通 Lerp 平滑追赶（无冲刺感）；真传送（复活/跨地图，&gt;500m）
    /// 仍由 <see cref="HardSnapThresholdMeters"/> 硬跳兜底。
    /// </summary>
    public float SmoothThresholdMeters { get; set; } = 200f;

    /// <summary>硬跳阈值（米），默认 500，合法区间 (<see cref="SmoothThresholdMeters"/>, +∞)。</summary>
    public float HardSnapThresholdMeters { get; set; } = 500f;

    /// <summary>加速混合时长（秒），默认 0.2，合法区间 (0, +∞)，建议 [0.1, 0.3]。</summary>
    public float BlendDurationSeconds { get; set; } = 0.2f;

    /// <summary>分级距离：Near 上限（米），默认 30，合法区间 (0, +∞)。</summary>
    public float NearDistanceMeters { get; set; } = 30f;

    /// <summary>分级距离：Mid 上限（米），默认 80，合法区间 (<see cref="NearDistanceMeters"/>, +∞)。</summary>
    public float MidDistanceMeters { get; set; } = 80f;

    /// <summary>远程角色性能降档阈值（个），超此数量整体降档，默认 10，合法区间 (0, +∞)。</summary>
    public int PerformanceDegradeEntityCount { get; set; } = 10;

    /// <summary>远程角色数量硬上限（个），超限暂停最远角色插值推进，默认 20，合法区间 [<see cref="PerformanceDegradeEntityCount"/>, +∞)。</summary>
    public int MaxRemoteEntityCount { get; set; } = 20;

    /// <summary>
    /// 客户端规模档位阈值（实体数），默认 { 20, 100, 1000, 5000 }（spec 5.7.1 档位 20/100/1000/5000）。
    /// 必须严格递增且全部 &gt; 0；非法时回退默认。
    /// </summary>
    public int[] TierThresholds { get; set; } = { 20, 100, 1000, 5000 };

    /// <summary>超规模实体数上限（个），默认 5000；同屏实体数超过该值触发 <c>OverLimit</c> 档位最远优先降级。</summary>
    public int UltraScaleEntityCap { get; set; } = 5000;

    // ── 阶段 5.3.1 — SnapshotApplySystem（快照应用/自适应插值延迟/RTT 估计）──

    /// <summary>单帧最多消费的快照包数量，超出部分留待下一帧处理。默认 32。</summary>
    public int MaxSnapshotsPerFrame { get; set; } = 32;

    /// <summary>远程实体超时清理阈值（秒），超过此时间未收到快照更新的远程实体被客户端主动清理。默认 90。</summary>
    public float StaleEntityTimeoutSeconds { get; set; } = 90f;

    /// <summary>自适应插值延迟下限（秒）。默认 0.1。</summary>
    public float AdaptiveDelayMinSeconds { get; set; } = 0.1f;

    /// <summary>自适应插值延迟上限（秒），须 ≥ <see cref="AdaptiveDelayMinSeconds"/>（否则 Math.Clamp 抛异常）。默认 0.2。</summary>
    public float AdaptiveDelayMaxSeconds { get; set; } = 0.2f;

    /// <summary>固定插值延迟（秒），自适应关闭时使用。默认 0.1。</summary>
    public float FixedInterpolationDelaySeconds { get; set; } = 0.1f;

    /// <summary>平滑延迟最大变化速率（秒/秒），时间门控限幅防 delayTicks 逐帧跳变。默认 0.3。</summary>
    public float MaxDelayChangePerSecond { get; set; } = 0.3f;

    /// <summary>RTT EWMA 平滑因子（0, 1）。默认 0.125（与 TCP RTT 估计一致）。</summary>
    public float RttEwmaAlpha { get; set; } = 0.125f;

    /// <summary>RTT 抖动 EWMA 平滑因子（0, 1）。默认 0.25。</summary>
    public float RttJitterBeta { get; set; } = 0.25f;

    /// <summary>快照到达间隔 EWMA 平滑因子（0, 1）。默认 0.2。</summary>
    public float SnapshotArrivalEwmaAlpha { get; set; } = 0.2f;

    /// <summary>快照到达抖动 EWMA 平滑因子（0, 1）。默认 0.25。</summary>
    public float SnapshotArrivalJitterBeta { get; set; } = 0.25f;

    /// <summary>停止判定位移阈值（米）：新 Target 与旧位置位移小于此值判定实体已停止（清零外推速度）。默认 0.05。</summary>
    public float StopDisplacementThreshold { get; set; } = 0.05f;

    /// <summary>速度停止阈值（米/秒）：MovementState 心跳水平速度低于此值视为停止。默认 0.5。</summary>
    public float StopVelocityThreshold { get; set; } = 0.5f;

    /// <summary>孤儿实体映射清理扫描间隔（帧）。默认 300（约 5 秒 @60fps）。</summary>
    public int OrphanCleanupInterval { get; set; } = 300;

    /// <summary>远程实体超时清理扫描间隔（帧）。默认 60（约 1 秒 @60fps）。</summary>
    public int StaleEntityCleanupInterval { get; set; } = 60;

    // ── 阶段 5.3.2 — InterpolationSystem（前向预测/传送分档）──

    /// <summary>前向预测最大外推时间（秒）：extrapTime = min(TimeSinceLastSnapshot, 此值)，避免长时间无快照时预测位置飘太远。默认 0.25。</summary>
    public float DeadReckoningMaxExtrapolationSeconds { get; set; } = 0.25f;
}