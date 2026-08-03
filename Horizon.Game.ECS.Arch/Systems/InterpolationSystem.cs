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
/// <description>在线静止（0.5~5 秒未收到 delta）。保持当前位置，停止追赶，避免漂移。</description>
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
/// <b>传送保护</b>：当目标位置与当前位置距离超过 <see cref="TeleportThresholdMeters"/> 时，
/// 直接跳到目标位置，避免长距离 Lerp 导致角色"飞过去"。
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
    /// 传送阈值（米）。当目标位置与当前位置距离超过此值时，直接跳到目标位置。
    /// 避免 Lerp 在长距离移动（如传送、复活）时角色"飞过去"的不自然视觉效果。
    /// 修复（闪移）：原值 10m 在远程玩家临时断网恢复后位置累积变化（>10m）时触发传送，
    /// 表现为"闪移"。增大到 50m，让大多数网络抖动场景走 Lerp 平滑追赶而非直接跳。
    /// 真正的传送（复活、跨地图）通常距离 > 50m，仍会触发直接跳。
    /// </summary>
    public float TeleportThresholdMeters { get; set; } = 50f;

    /// <summary>断线期间暂停插值推进（避免无新数据时角色漂移）。</summary>
    public bool IsPaused { get; set; } = false;

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
    /// </summary>
    public float DeadReckoningMaxExtrapolationSeconds { get; set; } = 0.5f;

    /// <summary>
    /// 本帧所有 Active 远程角色平均渲染位置 delta（米），供外部（ECSUpdateDriver）转发到平滑度评分。
    /// 每帧 Update 结束后更新，<see cref="HasNewSmoothnessSample"/> 置 true。
    /// </summary>
    public float LastFrameSmoothnessPositionDeltaMeters { get; private set; }

    /// <summary>本帧帧时间（秒），与 <see cref="LastFrameSmoothnessPositionDeltaMeters"/> 配对供平滑度评分。</summary>
    public float LastFrameSmoothnessFrameTimeSeconds { get; private set; }

    /// <summary>本帧是否有新的平滑度采样（存在 Active 远程角色时为 true）。</summary>
    public bool HasNewSmoothnessSample { get; private set; }

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

        _framePositionDeltaSum = 0f;
        _sampledEntityCount = 0;

        world.Query(in query, (Entity entity, ref InterpolatedTransformComponent interp) =>
        {
            // 累计自上次快照以来的时间（供诊断和外部系统使用）
            interp.TimeSinceLastSnapshot += dt;

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
                    // 在线静止：保持当前位置，停止追赶
                    // 注意：不重置 Target，保留最后一个已知目标位置。
                    // 这样恢复 Active 时，如果新 delta 还没到达，Lerp 仍可向旧目标追赶（通常已到达）。
                    // Alpha 标记为已到达
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

            // === Active 状态：Lerp 平滑追赶 + Dead Reckoning 惯性外推 ===

            // 计算当前位置与目标位置的距离平方
            var dx = interp.TargetX - interp.X;
            var dy = interp.TargetY - interp.Y;
            var dz = interp.TargetZ - interp.Z;
            var distSq = dx * dx + dy * dy + dz * dz;

            if (distSq > teleportThresholdSq)
            {
                // 传送：直接跳到目标位置，避免长距离 Lerp
                interp.X = interp.TargetX;
                interp.Y = interp.TargetY;
                interp.Z = interp.TargetZ;
                interp.Yaw = interp.TargetYaw;
                interp.Alpha = 1f; // 标记已到达目标

                // 诊断：传送跳变事件（距离超过传送阈值，直接跳到 Target）
                if (Diagnostics != null && world.Has<NetworkIdentityComponent>(entity))
                {
                    var netId = world.Get<NetworkIdentityComponent>(entity);
                    Diagnostics.OnTeleportJump(netId.EntityId, MathF.Sqrt(distSq), interp.ServerTick);
                }
            }
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
                var extrapTime = Math.Min(interp.TimeSinceLastSnapshot, DeadReckoningMaxExtrapolationSeconds);
                // 注意：LastVelocityXZ_X 对应 ECS X（左右），LastVelocityXZ_Y 对应 ECS Z（前后）。
                // InterpolatedTransformComponent 的 Y 是上下（Flax 坐标系），Z 是前后。
                // 服务端 MovementState.VelocityXZ_X/Y 是水平面速度（X=左右, Y=前后），
                // 映射到 interp 坐标系：X→X, Y→Z。垂直方向（Y）不预测。
                var predictedTargetX = interp.TargetX + interp.LastVelocityXZ_X * extrapTime;
                var predictedTargetY = interp.TargetY;
                var predictedTargetZ = interp.TargetZ + interp.LastVelocityXZ_Y * extrapTime;

                // Lerp 追赶 predictedTarget（线性，禁止回退 smoothstep）
                var pdx = predictedTargetX - interp.X;
                var pdy = predictedTargetY - interp.Y;
                var pdz = predictedTargetZ - interp.Z;
                interp.X += pdx * lerpFactor;
                interp.Y += pdy * lerpFactor;
                interp.Z += pdz * lerpFactor;

                // Yaw 最短路径插值，避免 ±π 跨界时反向旋转（不做角速度外推，角速度信息不足）
                var yawDelta = interp.TargetYaw - interp.Yaw;
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
}
