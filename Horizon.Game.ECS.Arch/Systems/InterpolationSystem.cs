using System;
using Arch.Core;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Core;
using Horizon.Game.ECS.Arch.Diagnostics;
using Horizon.Game.Message.Sync.Components;

namespace Horizon.Game.ECS.Arch.Systems;

/// <summary>
/// 插值系统：在 Render 阶段对远程实体进行位置插值，平滑网络抖动。
/// </summary>
/// <remarks>
/// <para>
/// <b>状态机驱动的插值策略</b>（修复远程角色移动不平滑/闪移/莫名离线）：
/// </para>
/// <para>
/// 不同状态下采用不同插值策略，避免单一 Lerp 处理所有场景导致的混杂问题：
/// <list type="bullet">
/// <item>
/// <term>Initializing</term>
/// <description>刚创建，等待首个 Update delta。保持当前位置，不插值。</description>
/// </item>
/// <item>
/// <term>Active</term>
/// <description>在线移动中。采用前向预测（Extrapolation）+ 线性 Lerp 修正算法：
/// predictedTarget = Target + LastVelocityXZ × min(timeSinceSnapshot, maxExtrapolation)，
/// 位置 += (predictedTarget - 位置) × lerpFactor，消除弹性带效应导致的渲染速度周期性波动。
/// 速度自适应，所有网络质量等级统一启用。</description>
/// </item>
/// <item>
/// <term>Idle</term>
/// <description>在线静止（1~5 秒未收到 delta）。若最后已知速度非零，继续 Dead Reckoning 外推
/// 位置（最长 <see cref="IdleDeadReckoningMaxSeconds"/>），避免网络抖动时远程角色瞬间冻结。
/// 速度为零时保持当前位置（与静止行为一致）。</description>
/// </item>
/// <item>
/// <term>Stale</term>
/// <description>疑似异常（5~90 秒未收到 delta）。保持当前位置，不追赶，等待服务端恢复或超时清理。</description>
/// </item>
/// <item>
/// <term>Offline / TimeoutDespawn</term>
/// <description>已销毁，不进入插值循环。</description>
/// </item>
/// </list>
/// </para>
/// <para>
/// <b>传送处理（3 档策略，减少闪跳对可游玩性的影响）</b>：当目标位置与当前位置距离
/// 超过 <see cref="TeleportThresholdMeters"/> 时，根据距离分档处理：
/// <list type="bullet">
/// <item>
/// <term>≤ <see cref="TeleportThresholdMeters"/>（默认 100m）</term>
/// <description>普通 Lerp 平滑追赶（前向预测 + 线性修正）。</description>
/// </item>
/// <item>
/// <term>(<see cref="TeleportThresholdMeters"/>, <see cref="HardSnapThresholdMeters"/>]（默认 100~500m）</term>
/// <description>加速混合：在 <see cref="TeleportBlendDurationSeconds"/>（默认 200ms）内用 smoothstep
/// 缓动从当前位置过渡到 Target，把"瞬移"变成可见的"快速冲刺"，避免视觉割裂。</description>
/// </item>
/// <item>
/// <term>&gt; <see cref="HardSnapThresholdMeters"/>（默认 500m）</term>
/// <description>硬跳：直接瞬移到 Target。专处理复活/跨地图传送，避免长距离混合像"飞行"。</description>
/// </item>
/// </list>
/// </para>
/// <para>本地玩家实体不携带此组件，不受本系统影响（由 <see cref="LocalSimulationSystem"/> 驱动）。</para>
/// </remarks>
[ArchSystem(SystemGroup.Render, order: 0)]
public sealed class InterpolationSystem : ArchSystemBase
{
    /// <summary>Lerp 平滑追赶速度系数（每秒追赶比例）。当 UseAdaptiveSpeed=true 时从自适应延迟计算。</summary>
    /// <remarks>
    /// speed=10 → lerpFactor=0.167/帧 → 稳态滞后=v/speed=0.6m（100ms 延迟 @6m/s）
    /// speed=20 → lerpFactor=0.333/帧 → 稳态滞后=0.3m（50ms 延迟）
    /// </remarks>
    public float InterpolationSpeed { get; set; } = 1f / 0.1f; // 默认 10（100ms 延迟）

    /// <summary>是否使用自适应插值速度（从 SnapshotApplySystem.AdaptiveInterpolationDelaySeconds 计算）。</summary>
    public bool UseAdaptiveSpeed { get; set; } = true;

    /// <summary>
    /// 传送阈值（米）。当目标位置与当前位置距离超过此值时进入"传送处理"（加速混合或硬跳），
    /// 不再走普通 Lerp 平滑追赶。<br/>
    /// 修复（闪跳可游玩性 — 扩大平滑区）：原值 50m 在远程玩家临时断网恢复后位置累积变化（&gt;50m）时
    /// 触发传送，表现为"闪跳"。提升到 100m，让更多漂移场景（断网 5~10 秒累积位移、AOI chunk 重订阅
    /// 位置对齐、服务端 tick 异常）落入普通 Lerp 平滑追赶区，避免不必要的闪跳。<br/>
    /// 超过此阈值但 &lt; <see cref="HardSnapThresholdMeters"/> 的场景走加速混合（200ms smoothstep 过渡），
    /// 仅当距离 &gt; <see cref="HardSnapThresholdMeters"/> 时才硬跳（专处理复活/跨地图）。<br/>
    /// <b>配置语义</b>：默认值由 <see cref="Configuration.RemoteSyncThresholdOptions"/> 经
    /// <see cref="Configuration.RemoteSyncThresholdValidator"/> 校验后于启动时注入，
    /// 此处默认值作为无配置时的兜底。
    /// </summary>
    public float TeleportThresholdMeters { get; set; } = 100f;

    /// <summary>
    /// 硬跳阈值（米）。当目标距离超过此值时，直接瞬移到目标位置，不走加速混合。<br/>
    /// 专处理真正的传送（复活、跨地图传送），这些场景距离通常 &gt; 500m，混合会让角色看起来
    /// "飞过去"（500m @ 200ms = 2500m/s，视觉模糊冲刺上限），瞬移反而符合玩家预期。<br/>
    /// 距离在 (<see cref="TeleportThresholdMeters"/>, 此值] 区间走加速混合。<br/>
    /// 默认 500m：典型 MMO 地图尺寸 1~8km，500m 足以区分"网络漂移"与"真传送"。<br/>
    /// <b>配置语义</b>：默认值由 <see cref="Configuration.RemoteSyncThresholdOptions"/> 经
    /// <see cref="Configuration.RemoteSyncThresholdValidator"/> 校验后于启动时注入，
    /// 此处默认值作为无配置时的兜底。
    /// </summary>
    public float HardSnapThresholdMeters { get; set; } = 500f;

    /// <summary>
    /// 加速混合时长（秒）。当目标距离在 (<see cref="TeleportThresholdMeters"/>,
    /// <see cref="HardSnapThresholdMeters"/>] 区间时，用 smoothstep 缓动在此时长内
    /// 从当前位置过渡到 Target，把"瞬移"变成可见的"快速冲刺"。<br/>
    /// 默认 0.2s（200ms）：MMO 通用 150~300ms 区间的中间值，60fps 下约 12 帧，
    /// 足够可见又不至于像"飞行"。过短（&lt;100ms）仍像瞬移，过长（&gt;300ms）有飞行感。<br/>
    /// <b>配置语义</b>：默认值由 <see cref="Configuration.RemoteSyncThresholdOptions"/> 经
    /// <see cref="Configuration.RemoteSyncThresholdValidator"/> 校验后于启动时注入，
    /// 此处默认值作为无配置时的兜底。
    /// </summary>
    public float TeleportBlendDurationSeconds { get; set; } = 0.2f;

    /// <summary>断线期间暂停插值推进（避免无新数据时角色漂移）。</summary>
    public bool IsPaused { get; set; } = false;

    /// <summary>
    /// 规模档位降级标记：为 true 时"暂停插值推进"（spec 5.5.1.3 超档位实体不消失）。
    /// 与 <see cref="IsPaused"/> 并列——IsPaused 冻结全部实体（断线），IsDegraded 仅冻结被降级的最远实体。
    /// 被降级实体保留 <c>Target</c> 状态，恢复标记清除后按既有平滑/传送策略无跳变继续推进。
    /// </summary>
    public bool IsDegraded { get; set; } = false;

    /// <summary>
    /// 诊断事件汇（可选）。由游戏层 DI 注入，null 时不输出诊断日志，保证零开销。
    /// </summary>
    public ISyncDiagnosticsSink? Diagnostics { get; set; }

    /// <summary>
    /// Dead Reckoning 速度衰减系数（每秒衰减比例）。
    /// [已废弃] 前向预测模式下不生效，字段保留以兼容外部配置注入。
    /// 原指数衰减分支已被基于速度的恒定前向预测取代（predictedTarget = Target + LastVelocityXZ × extrapTime）。
    /// 历史背景：原实现当实体追赶上目标位置后（distSq ≈ 0）使用指数衰减外推，
    /// decayFactor=3 → 0.5 秒后速度衰减到 e^(-1.5) ≈ 22%，1 秒后 ≈ 5%。
    /// </summary>
    public float DeadReckoningDecayRate { get; set; } = 3f;

    /// <summary>
    /// 前向预测最大外推时间（秒）。前向预测模式下作为最大外推时间上限
    /// extrapTime = min(TimeSinceLastSnapshot, 此值)，避免长时间无快照时预测位置飘太远。
    /// <para>
    /// 修复（Active→Idle 速度不连续）：原值 0.5s，但 Active→Idle 阈值为 1.0s。
    /// 0.5s~1.0s 之间 Active 状态外推已停止（predictedTarget 静止），实体 Lerp 减速收敛，
    /// 1.0s 进入 Idle 后 Dead Reckoning 突然恢复全速 → 速度突变。
    /// 提升到 1.0s 与 Active→Idle 阈值匹配，Active 全程匀速外推，Idle 无缝接管。
    /// </para>
    /// </summary>
    public float DeadReckoningMaxExtrapolationSeconds { get; set; } = 1.0f;

    /// <summary>
    /// Idle 状态下 Dead Reckoning 的最大持续时间（秒）。
    /// <para>
    /// 修复（网络抖动时远程角色冻结 — "无法看到远程角色的移动"根因）：
    /// 原实现在 Idle 状态（0.5~5 秒未收到 delta）直接冻结位置，但实体可能正在移动。
    /// 网络抖动 &gt; 0.5s 时实体进入 Idle 并瞬间冻结，恢复 Active 时 Lerp 追赶产生视觉突变；
    /// 多客户端网络拥塞时频繁 Active→Idle→Active 切换，远程角色"一顿一顿"。
    /// </para>
    /// <para>
    /// 优化：Idle 状态下继续用最后已知速度做 Dead Reckoning，保持匀速移动。
    /// - 速度为零（真正静止的实体）→ 退化为保持当前位置（与原行为一致）
    /// - 速度非零 → 按 LastVelocityXZ × dt 推进位置，最长外推此时间
    ///   超过后停止漂移（防止长时间无快照时位置飘太远）
    /// - Stale 状态仍保持冻结（5s+ 无更新视为真实异常）
    /// </para>
    /// <para>
    /// 默认 2.0s：覆盖常见网络抖动（1~3s），超过后冻结等待恢复。
    /// 6m/s × 2s = 12m 最大漂移，恢复时由加速混合（200ms smoothstep）平滑过渡，视觉无突变。
    /// </para>
    /// </summary>
    public float IdleDeadReckoningMaxSeconds { get; set; } = 2.0f;

    /// <summary>
    /// 方案6（plan.md §5 方案6 / §4.1）：服务端 tick 频率（Hz），用于把插值延迟（秒）转换为 tick 数。
    /// 默认 60Hz（与服务端 ZoneShardGrain 60Hz tick 对齐）。偏差不影响正确性，仅影响缓冲插值的 renderTick 位置。
    /// </summary>
    public float ServerTickRateHz { get; set; } = 60f;

    /// <summary>
    /// 本帧所有 Active 远程角色平均渲染位置 delta（米），供外部（ECSUpdateDriver）转发到平滑度评分。
    /// 每帧 Update 结束后更新，<see cref="HasNewSmoothnessSample"/> 置 true。
    /// </summary>
    public float LastFrameSmoothnessPositionDeltaMeters { get; private set; }

    /// <summary>本帧帧时间（秒），与 <see cref="LastFrameSmoothnessPositionDeltaMeters"/> 配对供平滑度评分。</summary>
    public float LastFrameSmoothnessFrameTimeSeconds { get; private set; }

    /// <summary>本帧是否有新的平滑度采样（存在 Active 远程角色时为 true）。</summary>
    public bool HasNewSmoothnessSample { get; private set; }

    /// <summary>当前被规模档位降级的远程实体 ID 集合（暂停插值推进但保留 Target，恢复后无闪跳）。</summary>
    private readonly HashSet<ulong> _degradedEntityIds = new();

    /// <summary>设置/更新被降级实体集合（由 SyncScaleController 或 FlaxActorSyncSystem 装配时注入）。</summary>
    /// <param name="entityIds">被降级实体的 NetworkIdentityComponent.EntityId 集合。</param>
    public void SetDegradedEntities(IEnumerable<ulong> entityIds)
    {
        _degradedEntityIds.Clear();
        if (entityIds is null) return;
        foreach (var id in entityIds) _degradedEntityIds.Add(id);
    }

    /// <summary>当前是否存在被降级实体（快速路径判断，避免空集合每帧遍历）。</summary>
    public bool HasDegradedEntities => _degradedEntityIds.Count > 0;

    private float _framePositionDeltaSum;
    private int _sampledEntityCount;

    /// <inheritdoc />
    public override void Update(World world, TimeSpan deltaTime)
    {
        if (IsPaused)
            return;

        var query = new QueryDescription().WithAll<InterpolatedTransformComponent>();
        var dt = (float)deltaTime.TotalSeconds;

        // Lerp 平滑追赶速度
        var speed = UseAdaptiveSpeed
            ? 1f / SnapshotApplySystem.AdaptiveInterpolationDelaySeconds
            : InterpolationSpeed;

        // lerpFactor = dt * speed，限制在 [0, 1]
        // 60fps + speed=10 → lerpFactor=0.167（每帧追赶 16.7% 的距离）
        var lerpFactor = Math.Clamp(dt * speed, 0f, 1f);
        var teleportThresholdSq = TeleportThresholdMeters * TeleportThresholdMeters;
        // 硬跳阈值平方（仅距离 > HardSnapThresholdMeters 时瞬移，专处理复活/跨地图传送）
        var hardSnapThresholdSq = HardSnapThresholdMeters * HardSnapThresholdMeters;

        _framePositionDeltaSum = 0f;
        _sampledEntityCount = 0;

        world.Query(in query, (Entity entity, ref InterpolatedTransformComponent interp) =>
        {
            // 累计自上次快照以来的时间（供诊断和外部系统使用）
            interp.TimeSinceLastSnapshot += dt;

            // [规模档位降级] 被降级实体跳过本帧推进但保留 Target 状态（spec 5.5.1.3）：
            // 冻结于最后权威位置附近（不漂移、不消失），恢复后按既有平滑/传送策略无跳变继续推进。
            if (IsDegraded || (HasDegradedEntities && TryGetDegradedEntityId(world, entity, out var degradedId) && _degradedEntityIds.Contains(degradedId)))
            {
                interp.Alpha = 1f;
                return;
            }

            // 状态机分支：按远程实体当前状态采用不同插值策略
            switch (interp.State)
            {
                case RemoteEntityState.Initializing:
                    // 初始状态：保持当前位置，等待首个 Update delta
                    // Alpha 保持 1f（已到达目标，目标即当前位置）
                    interp.Alpha = 1f;
                    return;

                case RemoteEntityState.Active:
                    // 在线移动：Lerp 平滑追赶
                    break;

                case RemoteEntityState.Idle:
                    // 在线静止（1~5 秒未收到 delta）：Dead Reckoning 保持移动
                    // 修复（网络抖动时远程角色冻结 — "无法看到远程角色的移动"根因）：
                    // 原实现直接 return 冻结位置，但实体可能正在移动（LastVelocityXZ != 0）。
                    // 网络抖动 > 0.5s 时实体进入 Idle 并瞬间冻结，恢复 Active 时 Lerp 追赶产生视觉突变；
                    // 多客户端网络拥塞时频繁 Active→Idle→Active 切换，远程角色"一顿一顿"。
                    //
                    // 优化：Idle 状态下继续用最后已知速度做 Dead Reckoning，保持匀速移动。
                    // - 速度为零（真正静止的实体）→ 退化为保持当前位置（与原行为一致）
                    // - 速度非零 → 按 LastVelocityXZ × dt 推进位置，最长外推 IdleDeadReckoningMaxSeconds
                    //   超过后停止漂移（防止长时间无快照时位置飘太远）
                    // - Stale 状态仍保持冻结（5s+ 无更新视为真实异常）
                    // 注意：不重置 Target，保留最后一个已知目标位置。
                    // 这样恢复 Active 时，如果新 delta 还没到达，Lerp 仍可向旧目标追赶（通常已到达）。
                    if (float.IsFinite(interp.LastVelocityXZ_X) && float.IsFinite(interp.LastVelocityXZ_Y)
                        && (interp.LastVelocityXZ_X != 0f || interp.LastVelocityXZ_Y != 0f)
                        && interp.TimeSinceLastSnapshot <= IdleDeadReckoningMaxSeconds)
                    {
                        // 按最后已知速度推进位置（纯外推，无 Lerp 修正——Idle 期间无新快照可修正）
                        // 注意：LastVelocityXZ_X 对应 ECS X（左右），LastVelocityXZ_Y 对应 ECS Z（前后）。
                        // InterpolatedTransformComponent 的 Y 是上下（Flax 坐标系），不预测。
                        interp.X += interp.LastVelocityXZ_X * dt;
                        interp.Z += interp.LastVelocityXZ_Y * dt;
                        // Yaw 保持不变（无角速度信息），避免旋转漂移
                    }
                    interp.Alpha = 1f;
                    return;

                case RemoteEntityState.Stale:
                    // 疑似异常：保持当前位置，不追赶
                    // 与 Idle 行为一致，但状态不同（供 UI 区分显示）
                    interp.Alpha = 1f;
                    return;

                case RemoteEntityState.Offline:
                case RemoteEntityState.TimeoutDespawn:
                    // 已销毁：理论上不会进入此分支（实体已从世界移除）
                    return;

                default:
                    // 未知状态：保守处理，保持当前位置
                    return;
            }

            // === Active 状态：3 档传送处理（Lerp / 加速混合 / 硬跳） ===
            // 修复（闪跳可游玩性）：原实现仅 2 档（Lerp / 硬跳），距离 > TeleportThreshold 即瞬移，
            // 视觉割裂。改为 3 档：
            //   - dist ≤ TeleportThreshold：普通 Lerp 平滑追赶（前向预测 + 线性修正）
            //   - TeleportThreshold < dist ≤ HardSnapThreshold：加速混合，200ms smoothstep 过渡
            //   - dist > HardSnapThreshold：硬跳（专处理复活/跨地图传送）

            // [异常数据隔离] 目标位置有限值防御（spec 5.3.1 规则 7 的 a、DFX 4.2.4）：
            // 在 distSq 计算之前检查 Target 是否有限值——任一轴非有限值（NaN/Infinity）时
            // distSq 会变为 NaN/Infinity 并污染所有后续分支（含硬跳覆盖、加速混合启动），
            // 因此跳过本帧全部推进处理，保持当前渲染位置与 Alpha 不变，等待合法快照覆盖。
            // 不调用任何诊断方法（避免刷屏），仅非法时跳过。
            if (!float.IsFinite(interp.TargetX) || !float.IsFinite(interp.TargetY) || !float.IsFinite(interp.TargetZ)
                || !float.IsFinite(interp.TargetYaw))
            {
                interp.Alpha = 1f;
                return;
            }

            // 计算当前位置与目标位置的距离平方
            var dx = interp.TargetX - interp.X;
            var dy = interp.TargetY - interp.Y;
            var dz = interp.TargetZ - interp.Z;
            var distSq = dx * dx + dy * dy + dz * dz;

            // 分支 A：混合进行中（TeleportBlendRemainingSeconds > 0）
            // 混合期间 Target 可能被新快照更新（HandleUpdate 写入新 Target），混合自动重定向到新 Target。
            // 若新 Target 距离当前已插值位置超过 HardSnap 阈值，立即硬跳覆盖混合（极端漂移场景兜底）。
            if (interp.TeleportBlendRemainingSeconds > 0f)
            {
                if (distSq > hardSnapThresholdSq)
                {
                    // 混合中 Target 跳到 > HardSnap 阈值：硬跳覆盖混合
                    interp.X = interp.TargetX;
                    interp.Y = interp.TargetY;
                    interp.Z = interp.TargetZ;
                    interp.Yaw = interp.TargetYaw;
                    interp.Alpha = 1f;
                    interp.TeleportBlendRemainingSeconds = 0f;
                    interp.TeleportBlendDurationSeconds = 0f;

                    if (Diagnostics != null && world.Has<NetworkIdentityComponent>(entity))
                    {
                        var netId = world.Get<NetworkIdentityComponent>(entity);
                        Diagnostics.OnTeleportJump(netId.EntityId, MathF.Sqrt(distSq), interp.ServerTick);
                    }
                }
                else
                {
                    // 推进混合：remaining -= dt，alpha = elapsed / duration = 1 - remaining/duration
                    interp.TeleportBlendRemainingSeconds -= dt;
                    if (interp.TeleportBlendRemainingSeconds <= 0f)
                    {
                        // 混合完成：snap 到 Target，清零混合状态
                        interp.X = interp.TargetX;
                        interp.Y = interp.TargetY;
                        interp.Z = interp.TargetZ;
                        interp.Yaw = interp.TargetYaw;
                        interp.Alpha = 1f;
                        interp.TeleportBlendRemainingSeconds = 0f;
                        interp.TeleportBlendDurationSeconds = 0f;
                    }
                    else
                    {
                        // smoothstep 缓动：alpha² × (3 - 2α)，ease-in-out 视觉自然
                        var blendAlpha = Math.Clamp(
                            1f - interp.TeleportBlendRemainingSeconds / interp.TeleportBlendDurationSeconds, 0f, 1f);
                        var smoothedAlpha = blendAlpha * blendAlpha * (3f - 2f * blendAlpha);

                        // 位置 Lerp(Start, Target, smoothedAlpha)
                        interp.X = interp.TeleportBlendStartX + (interp.TargetX - interp.TeleportBlendStartX) * smoothedAlpha;
                        interp.Y = interp.TeleportBlendStartY + (interp.TargetY - interp.TeleportBlendStartY) * smoothedAlpha;
                        interp.Z = interp.TeleportBlendStartZ + (interp.TargetZ - interp.TeleportBlendStartZ) * smoothedAlpha;

                        // Yaw 最短路径插值（避免 ±π 跨界反向旋转）
                        var yawDelta = interp.TargetYaw - interp.TeleportBlendStartYaw;
                        if (yawDelta > MathF.PI) yawDelta -= 2f * MathF.PI;
                        else if (yawDelta < -MathF.PI) yawDelta += 2f * MathF.PI;
                        interp.Yaw = interp.TeleportBlendStartYaw + yawDelta * smoothedAlpha;
                        // 归一化 Yaw 到 [-π, π]
                        if (interp.Yaw > MathF.PI) interp.Yaw -= 2f * MathF.PI;
                        else if (interp.Yaw < -MathF.PI) interp.Yaw += 2f * MathF.PI;

                        interp.Alpha = blendAlpha; // 0→1 标记混合进度（供外部诊断）
                    }
                }
                // 混合分支处理完毕，跳过普通 Lerp
            }
            // 分支 B：硬跳（dist > HardSnapThreshold）—— 真传送（复活/跨地图）
            else if (distSq > hardSnapThresholdSq)
            {
                interp.X = interp.TargetX;
                interp.Y = interp.TargetY;
                interp.Z = interp.TargetZ;
                interp.Yaw = interp.TargetYaw;
                interp.Alpha = 1f; // 标记已到达目标

                // 诊断：传送跳变事件（仅硬跳触发，启动=完成）
                if (Diagnostics != null && world.Has<NetworkIdentityComponent>(entity))
                {
                    var netId = world.Get<NetworkIdentityComponent>(entity);
                    Diagnostics.OnTeleportJump(netId.EntityId, MathF.Sqrt(distSq), interp.ServerTick);
                }
            }
            // 分支 C：加速混合启动（TeleportThreshold < dist ≤ HardSnapThreshold）
            // 把"瞬移"变成 200ms smoothstep 过渡，视觉上是"快速冲刺"而非闪跳
            else if (distSq > teleportThresholdSq)
            {
                // 初始化混合状态：Start = 当前位置/Yaw
                interp.TeleportBlendStartX = interp.X;
                interp.TeleportBlendStartY = interp.Y;
                interp.TeleportBlendStartZ = interp.Z;
                interp.TeleportBlendStartYaw = interp.Yaw;
                interp.TeleportBlendDurationSeconds = TeleportBlendDurationSeconds;
                interp.TeleportBlendRemainingSeconds = TeleportBlendDurationSeconds;

                // 诊断：传送跳变事件（启动时触发一次，后续混合帧不重复）
                if (Diagnostics != null && world.Has<NetworkIdentityComponent>(entity))
                {
                    var netId = world.Get<NetworkIdentityComponent>(entity);
                    Diagnostics.OnTeleportJump(netId.EntityId, MathF.Sqrt(distSq), interp.ServerTick);
                }

                // 立即推进一帧（避免空帧，首帧 alpha = dt/duration）
                interp.TeleportBlendRemainingSeconds -= dt;
                if (interp.TeleportBlendRemainingSeconds <= 0f)
                {
                    // 极端：dt >= duration（极低帧率或 duration 配置过小），直接完成
                    interp.X = interp.TargetX;
                    interp.Y = interp.TargetY;
                    interp.Z = interp.TargetZ;
                    interp.Yaw = interp.TargetYaw;
                    interp.Alpha = 1f;
                    interp.TeleportBlendRemainingSeconds = 0f;
                    interp.TeleportBlendDurationSeconds = 0f;
                }
                else
                {
                    var blendAlpha = Math.Clamp(
                        1f - interp.TeleportBlendRemainingSeconds / interp.TeleportBlendDurationSeconds, 0f, 1f);
                    var smoothedAlpha = blendAlpha * blendAlpha * (3f - 2f * blendAlpha);

                    interp.X = interp.TeleportBlendStartX + (interp.TargetX - interp.TeleportBlendStartX) * smoothedAlpha;
                    interp.Y = interp.TeleportBlendStartY + (interp.TargetY - interp.TeleportBlendStartY) * smoothedAlpha;
                    interp.Z = interp.TeleportBlendStartZ + (interp.TargetZ - interp.TeleportBlendStartZ) * smoothedAlpha;

                    var yawDelta = interp.TargetYaw - interp.TeleportBlendStartYaw;
                    if (yawDelta > MathF.PI) yawDelta -= 2f * MathF.PI;
                    else if (yawDelta < -MathF.PI) yawDelta += 2f * MathF.PI;
                    interp.Yaw = interp.TeleportBlendStartYaw + yawDelta * smoothedAlpha;
                    if (interp.Yaw > MathF.PI) interp.Yaw -= 2f * MathF.PI;
                    else if (interp.Yaw < -MathF.PI) interp.Yaw += 2f * MathF.PI;

                    interp.Alpha = blendAlpha;
                }
            }
            // 分支 D：普通 Lerp + 前向预测（dist ≤ TeleportThreshold）
            else
            {
                // 前向预测（Extrapolation）+ 线性 Lerp 修正算法。
                // 修复（远程角色移动卡顿 — Lerp 追赶模型的"弹性带"效应）：
                // 原实现"位置 += (Target - 位置) × lerpFactor"在快照到达瞬间追赶速度快、
                // 快照间隙指数衰减减速，导致渲染速度周期性"快-慢"波动（CV ≈ 0.6~0.8），
                // 视觉上"一顿一顿"。新实现用 LastVelocityXZ 外推目标位置使角色恒速前进，
                // 新快照到达时 Lerp 平滑修正小偏差，消除弹性带效应（目标 CV ≤ 0.3）。
                //
                // 公式：extrapTime = min(TimeSinceLastSnapshot, maxExtrapolation)
                //       predictedTarget = Target + LastVelocityXZ × extrapTime（仅 X/Z，Y 不预测）
                //       位置 += (predictedTarget - 位置) × lerpFactor（线性，禁止 smoothstep）
                //
                // 退化兼容：LastVelocityXZ == 0 时 predictedTarget = Target，退化为纯 Lerp 追赶 Target。
                // 所有网络质量等级（Strong/Medium/Weak）均统一走此分支，无网络等级判定。

                // [异常数据隔离] 防御性有限值检查（spec 5.3.1 规则 7 的 a、DFX 4.2.4）：
                // Target 已在 Active 分支入口校验过有限值；此处仅需校验速度分量，
                // 若 LastVelocityXZ 含 NaN/Infinity（来自异常快照的 MovementState），跳过本次插值推进，
                // 保持当前渲染位置与 Alpha 不变，避免 NaN 污染 predictedTarget 与渲染位置。
                if (!float.IsFinite(interp.LastVelocityXZ_X) || !float.IsFinite(interp.LastVelocityXZ_Y))
                {
                    // 非法值不推进位置、不触发诊断（避免刷屏），等待下一合法快照覆盖 Target
                    interp.Alpha = 1f;
                    return;
                }

                var extrapTime = Math.Min(interp.TimeSinceLastSnapshot, DeadReckoningMaxExtrapolationSeconds);
                // 注意：LastVelocityXZ_X 对应 ECS X（左右），LastVelocityXZ_Y 对应 ECS Z（前后）。
                // InterpolatedTransformComponent 的 Y 是上下（Flax 坐标系），Z 是前后。
                // 服务端 MovementState.VelocityXZ_X/Y 是水平面速度（X=左右, Y=前后），
                // 映射到 interp 坐标系：X→X, Y→Z。垂直方向（Y）不预测。

                // 方案6（plan.md §5 方案6 / §4.1）：优先尝试基于快照缓冲的时间插值（Mirror 式）。
                // 缓冲不足（< 2 样本）时回退到 Target+Lerp 前向预测（兼容旧逻辑）。
                // 缓冲插值基于历史快照做时间插值，比前向预测更准确（不依赖速度外推），
                // 快照抖动 200ms 期间可在两个旧快照间插值，避免 Target+Lerp 模型的视觉冻结（根因 #6）。
                var bufInterpDelay6 = UseAdaptiveSpeed
                    ? SnapshotApplySystem.AdaptiveInterpolationDelaySeconds
                    : 1f / InterpolationSpeed;
                float predictedTargetX, predictedTargetY, predictedTargetZ, predictedTargetYaw;
                if (TrySnapshotBufferInterpolate(ref interp, bufInterpDelay6, ServerTickRateHz,
                    out var bufX6, out var bufY6, out var bufZ6, out var bufYaw6))
                {
                    // 缓冲插值成功：用时间插值位置作为追赶目标（比前向预测更准确，基于历史快照）
                    predictedTargetX = bufX6;
                    predictedTargetY = bufY6;
                    predictedTargetZ = bufZ6;
                    predictedTargetYaw = bufYaw6;
                }
                else
                {
                    // 缓冲不足：回退到前向预测（Extrapolation）+ Target
                    predictedTargetX = interp.TargetX + interp.LastVelocityXZ_X * extrapTime;
                    predictedTargetY = interp.TargetY;
                    predictedTargetZ = interp.TargetZ + interp.LastVelocityXZ_Y * extrapTime;
                    predictedTargetYaw = interp.TargetYaw;
                }

                // Lerp 追赶 predictedTarget（线性，禁止回退 smoothstep）
                var pdx = predictedTargetX - interp.X;
                var pdy = predictedTargetY - interp.Y;
                var pdz = predictedTargetZ - interp.Z;
                interp.X += pdx * lerpFactor;
                interp.Y += pdy * lerpFactor;
                interp.Z += pdz * lerpFactor;

                // Yaw 最短路径插值，避免 ±π 跨界时反向旋转（不做角速度外推，角速度信息不足）
                var yawDelta = predictedTargetYaw - interp.Yaw;
                if (yawDelta > MathF.PI) yawDelta -= 2f * MathF.PI;
                else if (yawDelta < -MathF.PI) yawDelta += 2f * MathF.PI;
                interp.Yaw += yawDelta * lerpFactor;

                // Alpha 用于标记追赶进度（基于预测距离，供外部诊断，不参与位置计算）
                var predictedDistSq = pdx * pdx + pdy * pdy + pdz * pdz;
                interp.Alpha = 1f - Math.Clamp(predictedDistSq / teleportThresholdSq, 0f, 1f);

                // 前向预测模式标记：有速度时记录进入前向预测模式的服务器 tick，
                // 供诊断消费者追踪前向预测生效起点与切换点位置连续性。
                // 无速度时（退化纯 Lerp 场景）不写入标记。
                if (interp.LastVelocityXZ_X != 0f || interp.LastVelocityXZ_Y != 0f)
                {
                    interp.SwitchFromLerpToDeadReckoningTick = interp.ServerTick;
                }
            }

            // 平滑度采样：计算本帧渲染位置与上一帧位置差，累计到系统层聚合。
            if (interp.PreviousFrameInitialized)
            {
                var pdx = interp.X - interp.PreviousFrameX;
                var pdy = interp.Y - interp.PreviousFrameY;
                var pdz = interp.Z - interp.PreviousFrameZ;
                _framePositionDeltaSum += MathF.Sqrt(pdx * pdx + pdy * pdy + pdz * pdz);
                _sampledEntityCount++;
            }
            interp.PreviousFrameX = interp.X;
            interp.PreviousFrameY = interp.Y;
            interp.PreviousFrameZ = interp.Z;
            interp.PreviousFrameInitialized = true;
        });

        // 计算本帧平均位置 delta，暴露供外部（ECSUpdateDriver）转发到平滑度评分
        if (_sampledEntityCount > 0)
        {
            LastFrameSmoothnessPositionDeltaMeters = _framePositionDeltaSum / _sampledEntityCount;
            LastFrameSmoothnessFrameTimeSeconds = dt;
            HasNewSmoothnessSample = true;
        }
        else
        {
            HasNewSmoothnessSample = false;
        }
    }

    private static bool TryGetDegradedEntityId(World world, Entity entity, out ulong entityId)
    {
        entityId = 0;
        if (world.Has<NetworkIdentityComponent>(entity))
        {
            entityId = world.Get<NetworkIdentityComponent>(entity).EntityId;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 方案6（plan.md §5 方案6 / §4.1 Mirror Snapshot Interpolation）：基于有界快照缓冲的时间插值。
    /// <para>
    /// 在 <see cref="InterpolatedTransformComponent.SnapshotBuffer"/> 中查找 renderTick 两侧的样本 s1/s2，
    /// 线性插值得到渲染位置。renderTick = latestServerTick - interpolationDelayTicks，
    /// interpolationDelayTicks = interpolationDelaySeconds × <see cref="ServerTickRateHz"/>。
    /// </para>
    /// <para>
    /// 缓冲不足（&lt; 2 样本）或 renderTick 超出缓冲覆盖范围时返回 false，调用方回退到 Target+Lerp 追赶。
    /// </para>
    /// </summary>
    /// <param name="interp">插值组件（含快照缓冲）。</param>
    /// <param name="interpolationDelaySeconds">插值延迟（秒），通常取 AdaptiveInterpolationDelaySeconds。</param>
    /// <param name="serverTickRateHz">服务端 tick 频率（Hz），用于秒→tick 转换。</param>
    /// <param name="outX">插值结果 X。</param>
    /// <param name="outY">插值结果 Y。</param>
    /// <param name="outZ">插值结果 Z。</param>
    /// <param name="outYaw">插值结果 Yaw（弧度，含最短路径归一化）。</param>
    /// <returns>true 表示插值成功；false 表示缓冲不足，调用方应回退。</returns>
    private static bool TrySnapshotBufferInterpolate(
        ref InterpolatedTransformComponent interp,
        float interpolationDelaySeconds,
        float serverTickRateHz,
        out float outX, out float outY, out float outZ, out float outYaw)
    {
        outX = outY = outZ = outYaw = 0f;

        var buffer = interp.SnapshotBuffer;
        if (buffer == null || interp.SnapshotBufferCount < 2)
            return false;

        var size = InterpolatedTransformComponent.SnapshotBufferSize;
        var count = interp.SnapshotBufferCount;

        // 最新写入的样本在 (head - 1 + size) % size 位置
        var latestIdx = (interp.SnapshotBufferHead - 1 + size) % size;
        var latestTick = buffer[latestIdx].ServerTick;

        // 计算渲染 tick：renderTick = latestServerTick - interpolationDelayTicks
        // interpolationDelayTicks = interpolationDelaySeconds × serverTickRateHz（至少 1 tick 避免退化）
        var delayTicks = (long)MathF.Ceiling(interpolationDelaySeconds * serverTickRateHz);
        if (delayTicks < 1) delayTicks = 1;
        var renderTick = latestTick - delayTicks;

        // 线性扫描缓冲（从最旧到最新），找到 renderTick 两侧的样本：
        //   s1 = 最大的 tick <= renderTick
        //   s2 = s1 的下一个样本（tick >= renderTick）
        // 缓冲按到达顺序写入（TCP 保序，基本单调递增），线性扫描找首个 tick > renderTick 的样本，
        // 其前一个即为 s1。
        SnapshotSample s1 = default, s2 = default;
        var found = false;

        // 确定扫描起点：缓冲未满时从 0 开始（0..count-1 有效）；满时从 head 开始（head 是最旧）
        var startIdx = count < size ? 0 : interp.SnapshotBufferHead;

        int prevSampleIdx = -1;
        for (int i = 0; i < count; i++)
        {
            var sampleIdx = (startIdx + i) % size;
            var sample = buffer[sampleIdx];

            if (sample.ServerTick > renderTick)
            {
                // 当前样本 tick > renderTick，前一个（若存在）即为 s1
                if (prevSampleIdx >= 0)
                {
                    s1 = buffer[prevSampleIdx];
                    s2 = sample;
                    found = true;
                }
                break;
            }
            prevSampleIdx = sampleIdx;
        }

        if (!found)
            return false;

        // 线性插值：t = (renderTick - s1.tick) / (s2.tick - s1.tick)
        var tickDelta = s2.ServerTick - s1.ServerTick;
        if (tickDelta <= 0)
        {
            // s1/s2 同 tick（异常），直接取 s1
            outX = s1.X; outY = s1.Y; outZ = s1.Z; outYaw = s1.Yaw;
            return true;
        }
        var t = Math.Clamp((float)(renderTick - s1.ServerTick) / tickDelta, 0f, 1f);

        outX = s1.X + (s2.X - s1.X) * t;
        outY = s1.Y + (s2.Y - s1.Y) * t;
        outZ = s1.Z + (s2.Z - s1.Z) * t;

        // Yaw 最短路径插值（避免 ±π 跨界反向旋转）
        var yawDelta = s2.Yaw - s1.Yaw;
        if (yawDelta > MathF.PI) yawDelta -= 2f * MathF.PI;
        else if (yawDelta < -MathF.PI) yawDelta += 2f * MathF.PI;
        outYaw = s1.Yaw + yawDelta * t;
        // 归一化到 [-π, π]
        if (outYaw > MathF.PI) outYaw -= 2f * MathF.PI;
        else if (outYaw < -MathF.PI) outYaw += 2f * MathF.PI;

        return true;
    }
}
