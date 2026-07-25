using System;
using System.Collections.Generic;
using Arch.Core;
using Horizon.Game.Core.Sim;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Core;
using Horizon.Game.ECS.Arch.Network;
using Horizon.Game.Message.Sim;
using Horizon.Game.Message.Sync;
// 消除 MovementFormula 歧义：Horizon.Game.Core.Sim 与 Horizon.Game.Message.Sim 均存在同名类型，
// 添加 Horizon.Game.Core 引用后产生冲突。保持原有行为，统一使用 Message 版本。
using MovementFormula = Horizon.Game.Message.Sim.MovementFormula;

namespace Horizon.Game.ECS.Arch.Systems;

/// <summary>
/// 客户端回滚修正系统：处理服务器 InputAck 和 Correction，确保客户端预测位置与权威一致。
/// </summary>
/// <remarks>
/// 在 FixedUpdate 阶段执行（order: 20），位于 <see cref="LocalSimulationSystem"/>（order: 10）之后。
/// <list type="number">
///   <item>从 <see cref="InputAckReceiveBuffer"/> 读取最新 ACK，清理已确认输入（不重放）。</item>
///   <item>从 <see cref="CorrectionReceiveBuffer"/> 读取修正包，当偏差超过阈值时强制修正位置，
///   并依据 <see cref="CorrectionPacket.LastProcessedClientTick"/> 清理已确认输入、重放未确认输入。</item>
/// </list>
/// <para>
/// 修复"吸附+重放导致角色无法移动"：原实现服务端从不发送 InputAckPacket，
/// InputHistoryBuffer 永不清理；CorrectionPacket 也不携带 LastProcessedClientTick，
/// 导致 ProcessCorrection 重放 GetFromTick(0) 返回所有历史输入（含已确认），
/// 角色从权威位置飞出极远 → drift 巨大 → 再次 Correction → 死循环。
/// 现将 LastProcessedClientTick 字段加入 CorrectionPacket，ProcessCorrection 据此清理与重放。
/// </para>
/// </remarks>
[ArchSystem(SystemGroup.FixedUpdate, order: 20)]
public sealed class ReconciliationSystem : ArchSystemBase
{
    /// <summary>位置修正阈值（米），超过此值将触发强制吸附。</summary>
    public float CorrectionThreshold { get; set; } = 0.5f;

    /// <summary>
    /// [已废弃] 硬性吸附阈值（米）。当前实现统一使用平滑修正，此字段保留仅供向后兼容配置。
    /// </summary>
    [System.Obsolete("统一使用平滑修正，不再区分硬性吸附阈值。保留此属性以避免破坏外部配置。")]
    public float HardSnapThreshold { get; set; } = 3.0f;

    /// <summary>
    /// 平滑修正插值速度（每秒）。Correction 后 pred 向"从权威位置重放未确认输入的结果"插值，
    /// 而非瞬移，避免视觉突变。
    /// 默认 15/s（约 4 帧 @60fps 追平）。
    /// </summary>
    public float SmoothCorrectionSpeed { get; set; } = 15f;

    /// <summary>累计修正次数（诊断用）。</summary>
    public int TotalCorrectionsApplied { get; private set; }

    /// <summary>[Phase C2] 最近一次预测误差（米），供游戏层转发到 ClientSyncMetrics。</summary>
    public float LastPredictionError { get; private set; }

    /// <summary>[Phase C2] 是否有新的预测误差样本待消费。</summary>
    public bool HasNewPredictionError { get; set; }

    // ─── 修正风暴抑制 ───

    /// <summary>
    /// 修正风暴检测窗口内允许的最大修正次数。
    /// 超过此次数进入冷却期，跳过后续修正避免角色反复抽搐。
    /// </summary>
    private const int StormThreshold = 5;

    /// <summary>修正风暴检测窗口（秒）。</summary>
    private const float StormWindowSeconds = 2.0f;

    /// <summary>修正风暴冷却时间（秒）。进入风暴模式后跳过修正的时长。</summary>
    private const float StormCooldownSeconds = 1.0f;

    /// <summary>最近修正时间戳环形缓冲（用于风暴检测）。</summary>
    private readonly float[] _recentCorrectionTimes = new float[StormThreshold];
    private int _recentCorrectionIndex;
    private int _recentCorrectionCount;
    private float _stormCooldownUntil; // 基于 Environment.TickCount64 的秒数
    private long _lastTickTimestamp = Environment.TickCount64;

    /// <summary>累计因风暴抑制而跳过的修正次数（诊断用）。</summary>
    public int StormSuppressedCount { get; private set; }

    /// <summary>复用缓冲区：避免 GetFromTick 热路径上的 List 分配。</summary>
    private readonly List<InputPacket> _replayBuffer = new(64);

    /// <summary>
    /// 重置内部状态（断线/重连场景使用）。
    /// 清空修正风暴检测历史、冷却计时器和统计计数，
    /// 避免重连后旧会话的风暴历史影响新会话的修正逻辑。
    /// </summary>
    public void ResetState()
    {
        _recentCorrectionIndex = 0;
        _recentCorrectionCount = 0;
        _stormCooldownUntil = 0f;
        _lastTickTimestamp = Environment.TickCount64;
        StormSuppressedCount = 0;
        TotalCorrectionsApplied = 0;
        LastPredictionError = 0f;
        HasNewPredictionError = false;
        _replayBuffer.Clear();
    }

    /// <summary>
    /// 地面高度采样委托：与 <see cref="LocalSimulationSystem.GroundHeightSampler"/> 语义一致。
    /// <para>
    /// 回滚重播时同样需要应用地面约束：否则从服务端权威位置重放未确认输入时，
    /// <see cref="MovementFormula.Step"/> 计算出的 Z 会穿透 Terrain，导致下一次 correction
    /// 触发循环。委托为 null 时跳过约束（保持原行为，仅在未注入时使用）。
    /// </para>
    /// </summary>
    public Func<float, float, float>? GroundHeightSampler { get; set; }

    /// <inheritdoc />
    public override void Update(World world, TimeSpan deltaTime)
    {
        ProcessInputAck(world);
        ProcessCorrection(world, (float)deltaTime.TotalSeconds);
    }

    /// <summary>
    /// 处理服务器输入确认：仅清理已确认输入，不重放未确认输入。
    /// </summary>
    /// <remarks>
    /// 未确认输入已由 <see cref="LocalSimulationSystem"/>（order: 10）在本 tick 的 FixedUpdate 中应用过，
    /// 此处若从当前预测位置再次重放会导致移动被双重应用（位置累加两次）。
    /// 重放职责转移到 <see cref="ProcessCorrection"/>：仅在服务端位置修正后才需要从权威位置重放未确认输入。
    /// </remarks>
    private void ProcessInputAck(World world)
    {
        if (!InputAckReceiveBuffer.Instance.TryTake(out var ack) || ack == null)
        {
            return;
        }

        var lastProcessedTick = ack.LastProcessedClientTick;

        // 仅清理已确认的输入历史。world 参数保留以维持调用签名一致性。
        InputHistoryBuffer.Instance.ClearUpTo(lastProcessedTick);
    }

    /// <summary>
    /// 处理服务器位置修正：当偏差超过阈值时修正位置，并从权威位置重放未确认输入。
    /// 包含修正风暴抑制和平滑修正机制。
    /// </summary>
    private void ProcessCorrection(World world, float dt)
    {
        // Drain 所有待处理的修正包，仅保留最新的一个：
        // 修复 #13：修正包可能比 FixedUpdate 周期密集，CorrectionReceiveBuffer 是覆盖式存储，
        // 过早到达的修正包会被新包覆盖。Drain 到最新一包可避免丢失所有修正。
        // 使用全限定名避免 CorrectionPacket 在 Horizon.Game.ECS.Arch.Network 与
        // Horizon.Game.Core.Sim 两个命名空间下的歧义。
        Horizon.Game.ECS.Arch.Network.CorrectionPacket? correction = null;
        while (CorrectionReceiveBuffer.Instance.TryTake(out var peek) && peek != null)
        {
            correction = peek;
        }
        if (correction == null)
        {
            return;
        }

        // ─── 修正风暴抑制 ───
        // 检测短时间内修正次数是否超过阈值，超过则进入冷却期跳过修正。
        // 避免高 RTT 玩家因连续 Correction 导致角色反复抽搐。
        var nowMs = Environment.TickCount64;
        var nowSec = nowMs / 1000f;
        if (nowSec < _stormCooldownUntil)
        {
            // 冷却期内：跳过修正，仅记录预测误差供诊断
            StormSuppressedCount++;
            LastPredictionError = MovementFormula.Distance3D(
                0, 0, 0, // 占位，实际在下方 query 中计算
                correction.CorrectedX, correction.CorrectedY, correction.CorrectedZ);
            return;
        }

        var query = new QueryDescription()
            .WithAll<PredictedTransformComponent, NetworkIdentityComponent>();

        world.Query(in query, (Entity entity, ref PredictedTransformComponent pred, ref NetworkIdentityComponent netId) =>
        {
            if (netId.EntityId != correction.EntityId)
            {
                return;
            }

            var drift = MovementFormula.Distance3D(
                pred.X, pred.Y, pred.Z,
                correction.CorrectedX, correction.CorrectedY, correction.CorrectedZ);

            if (drift > CorrectionThreshold)
            {
                // ─── 修正风暴检测 ───
                _recentCorrectionTimes[_recentCorrectionIndex] = nowSec;
                _recentCorrectionIndex = (_recentCorrectionIndex + 1) % StormThreshold;
                if (_recentCorrectionCount < StormThreshold)
                    _recentCorrectionCount++;

                if (_recentCorrectionCount >= StormThreshold)
                {
                    // 检测窗口内最早与最新的修正时间差
                    var oldest = _recentCorrectionTimes[_recentCorrectionIndex % StormThreshold];
                    if (nowSec - oldest < StormWindowSeconds)
                    {
                        // 风暴检测触发：进入冷却期
                        _stormCooldownUntil = nowSec + StormCooldownSeconds;
                        _recentCorrectionCount = 0;
                        StormSuppressedCount++;
                        return; // 跳过本次修正
                    }
                }

                // ─── 平滑修正（重放从权威位置，视觉平滑到重放结果）───
                // 修复"吸附+重放导致角色无法移动"：
                // 原实现区分"平滑修正"（drift < HardSnapThreshold 时插值）与"硬性吸附"（瞬移），
                // 但平滑修正把 pred 插值到中间位置后从中间位置重放 → 重放结果错误 → 持续 Correction。
                // 正确做法：重放必须从权威位置开始（保证预测一致性），视觉上平滑过渡到重放结果。
                // 实现：用临时变量从 correction 位置重放，得到正确的预测位置 targetX/Y/Z，
                // 然后 pred 平滑插值到 target（而非瞬移），兼顾预测正确性与视觉平滑。
                pred.NeedsReconciliation = true;

                TotalCorrectionsApplied++;

                // [Phase C2] 记录预测误差供游戏层采集
                LastPredictionError = drift;
                HasNewPredictionError = true;

                // 修复"吸附+重放导致角色无法移动"：
                // 服务端从不发送 InputAckPacket，导致 InputHistoryBuffer 永不清理。
                // 若直接 GetFromTick(0) 会返回所有历史输入（含已确认），
                // 重放后角色从权威位置飞出极远 → 下一帧 drift 巨大 → 再次 Correction → 死循环。
                // 修复策略：CorrectionPacket 现携带 LastProcessedClientTick（服务端处理到的最大客户端 tick），
                // 据此先清理已确认输入，再仅重放真正未确认的输入。
                var lastProcessedTick = correction.LastProcessedClientTick;
                InputHistoryBuffer.Instance.ClearUpTo(lastProcessedTick);

                // 从权威位置重放所有未确认输入（ClientTick > lastProcessedTick），得到正确的预测位置。
                // 重放使用临时变量 replayX/Y/Z，不直接修改 pred，以便最后平滑插值。
                // [MoveSpeed 链路修复] 重放时必须用 historyInput.MaxSpeed（与服务端权威回放一致），
                // 否则重放会用 DefaultMaxSpeed=6 m/s 推进，与客户端原始预测速度不一致，
                // 导致 reconciliation 后位置再次漂移触发 correction。
                // [跳跃逻辑对齐] 重放必须复刻 LocalSimulationSystem 的跳跃逻辑：
                //   - InputBits bit0=1 等价 JumpPressedThisFrame=true（PlayerController 已做边沿触发）
                //   - 轻功（InputBits bit3=1）支持三段跳：jumpCount 1→5.5f / 2→4.5f / 3→3.5f
                //   - 非轻功固定 5.5f，且 jumpCount=1
                //   - 落地时（地面约束触发或 groundedEpsilon 判定）重置 jumpCount
                // 否则重放结果与原始预测不一致，导致下一次 FixedUpdate 后再次触发 correction 循环。
                var replayX = correction.CorrectedX;
                var replayY = correction.CorrectedY;
                var replayZ = correction.CorrectedZ;
                var replayVz = correction.CorrectedVz;

                var unconfirmedInputs = InputHistoryBuffer.Instance.GetFromTick(lastProcessedTick, _replayBuffer) > 0
                    ? _replayBuffer
                    : null;

                if (unconfirmedInputs != null)
                {
                    var sampler = GroundHeightSampler;
                    int jumpCount = 0;
                    bool isGrounded = true;
                    const float groundedEpsilon = 0.001f;
                    const int maxQinggongJumps = 3;
                    foreach (var historyInput in unconfirmedInputs)
                    {
                        var isQinggongJump = (historyInput.InputBits & (1u << 3)) != 0;
                        var isJumpPressed = (historyInput.InputBits & 0x1) != 0;

                        float jumpImpulse;
                        if (isJumpPressed)
                        {
                            if (isQinggongJump)
                            {
                                jumpCount++;
                                jumpImpulse = jumpCount switch
                                {
                                    1 => 5.5f,
                                    2 => 4.5f,
                                    3 => 3.5f,
                                    _ => 0f
                                };
                                if (jumpCount > maxQinggongJumps)
                                    jumpImpulse = 0f;
                            }
                            else
                            {
                                jumpCount = 1;
                                jumpImpulse = 5.5f;
                            }
                        }
                        else
                        {
                            jumpImpulse = 0f;
                            if (!isQinggongJump)
                                jumpCount = 0;
                        }

                        var prevZ = replayZ;
                        var (nx, ny, nz, nvz) = MovementFormula.Step(
                            replayX, replayY, replayZ, replayVz,
                            historyInput.MoveX, historyInput.MoveY, jumpImpulse,
                            1f / 60f,
                            maxSpeed: historyInput.MaxSpeed);

                        // 地面碰撞检测：与 LocalSimulationSystem 一致，重放时也应用地面约束。
                        // 防止从服务端权威位置重放的预测位置穿透 Terrain。
                        if (sampler != null)
                        {
                            var groundZ = sampler(nx, ny);
                            if (!float.IsNaN(groundZ) && nz < groundZ)
                            {
                                nz = groundZ;
                                nvz = 0f;
                                isGrounded = true;
                                jumpCount = 0;
                            }
                            else
                            {
                                var dz = MathF.Abs(nz - prevZ);
                                isGrounded = dz < groundedEpsilon && nvz <= 0f;
                                if (isGrounded)
                                    jumpCount = 0;
                            }
                        }
                        else
                        {
                            var dz = MathF.Abs(nz - prevZ);
                            isGrounded = dz < groundedEpsilon && nvz <= 0f;
                            if (isGrounded)
                                jumpCount = 0;
                        }

                        replayX = nx;
                        replayY = ny;
                        replayZ = nz;
                        replayVz = nvz;
                    }
                }

                // 平滑插值到重放结果（而非瞬移）。
                // 重放结果是从权威位置应用未确认输入的正确预测位置，下一帧预测应从此处继续。
                // 视觉上平滑过渡避免突变；逻辑上 pred 最终会收敛到 replayX/Y/Z。
                // 注意：插值后 pred 与 replay 仍有差距，下一帧预测会从插值位置继续，
                // 但因为 InputAck 已清理已确认输入，未确认输入很少，drift 会快速收敛。
                var tSmooth = MathF.Min(1f, SmoothCorrectionSpeed * dt);
                pred.X += (replayX - pred.X) * tSmooth;
                pred.Y += (replayY - pred.Y) * tSmooth;
                pred.Z += (replayZ - pred.Z) * tSmooth;
                pred.Vz = replayVz;
            }
        });
    }
}
