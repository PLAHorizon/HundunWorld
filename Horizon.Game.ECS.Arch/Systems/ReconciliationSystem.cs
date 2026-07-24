using System;
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
///   <item>从 <see cref="InputAckReceiveBuffer"/> 读取最新 ACK，清理已确认输入并重播未确认输入。</item>
///   <item>从 <see cref="CorrectionReceiveBuffer"/> 读取修正包，当偏差超过阈值时强制修正位置。</item>
/// </list>
/// </remarks>
[ArchSystem(SystemGroup.FixedUpdate, order: 20)]
public sealed class ReconciliationSystem : ArchSystemBase
{
    /// <summary>位置修正阈值（米），超过此值将触发强制吸附。</summary>
    public float CorrectionThreshold { get; set; } = 0.5f;

    /// <summary>累计修正次数（诊断用）。</summary>
    public int TotalCorrectionsApplied { get; private set; }

    /// <summary>[Phase C2] 最近一次预测误差（米），供游戏层转发到 ClientSyncMetrics。</summary>
    public float LastPredictionError { get; private set; }

    /// <summary>[Phase C2] 是否有新的预测误差样本待消费。</summary>
    public bool HasNewPredictionError { get; set; }

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
        ProcessCorrection(world);
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
    /// 处理服务器位置修正：当偏差超过阈值时强制吸附到权威位置，并从权威位置重放未确认输入。
    /// </summary>
    /// <remarks>
    /// 仅吸附不重放会导致角色停留在修正位置（回弹冻结），因为之前的预测结果已作废。
    /// 重放未确认输入（已确认的输入已被 <see cref="ProcessInputAck"/> 清理）后，
    /// 角色才能恢复到基于权威位置的最新预测状态。
    /// <para>
    /// 修复 #13：Drain 所有待处理的修正包，仅保留最新的一个。当修正包到达速度超过
    /// FixedUpdate 消费速度时，单次 TryTake 会丢失较早的修正包。由于 CorrectionReceiveBuffer
    /// 是覆盖式存储（Add 覆盖旧值），Drain 后只处理最新的修正包即可（服务器每次修正都是
    /// 基于全量状态的权威计算，不依赖增量累积）。
    /// </para>
    /// </remarks>
    private void ProcessCorrection(World world)
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
                // 吸附到服务端权威位置
                pred.X = correction.CorrectedX;
                pred.Y = correction.CorrectedY;
                pred.Z = correction.CorrectedZ;
                pred.Vz = correction.CorrectedVz;
                pred.NeedsReconciliation = true;

                TotalCorrectionsApplied++;

                // [Phase C2] 记录预测误差供游戏层采集
                LastPredictionError = drift;
                HasNewPredictionError = true;

                // 从修正后的权威位置重放所有未确认输入。
                // 已确认输入已被 ProcessInputAck 清理，GetFromTick(0) 返回的是服务端尚未确认的输入，
                // 这些输入对应的预测移动在吸附后失效，必须从权威位置重新应用。
                // [MoveSpeed 链路修复] 重放时必须用 historyInput.MaxSpeed（与服务端权威回放一致），
                // 否则重放会用 DefaultMaxSpeed=6 m/s 推进，与客户端原始预测速度不一致，
                // 导致 reconciliation 后位置再次漂移触发 correction。
                // [跳跃逻辑对齐] 重放必须复刻 LocalSimulationSystem 的跳跃逻辑：
                //   - InputBits bit0=1 等价 JumpPressedThisFrame=true（PlayerController 已做边沿触发）
                //   - 轻功（InputBits bit3=1）支持三段跳：jumpCount 1→5.5f / 2→4.5f / 3→3.5f
                //   - 非轻功固定 5.5f，且 jumpCount=1
                //   - 落地时（地面约束触发或 groundedEpsilon 判定）重置 jumpCount
                // 否则重放结果与原始预测不一致，导致下一次 FixedUpdate 后再次触发 correction 循环。
                var unconfirmedInputs = InputHistoryBuffer.Instance.GetFromTick(0);
                var sampler = GroundHeightSampler;
                int jumpCount = 0;
                bool isGrounded = true;
                const float groundedEpsilon = 0.001f;
                const int maxQinggongJumps = 3;
                const int maxNormalJumps = 1;
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

                    var prevZ = pred.Z;
                    var (nx, ny, nz, nvz) = MovementFormula.Step(
                        pred.X, pred.Y, pred.Z, pred.Vz,
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

                    pred.X = nx;
                    pred.Y = ny;
                    pred.Z = nz;
                    pred.Vz = nvz;
                }
            }
        });
    }
}
