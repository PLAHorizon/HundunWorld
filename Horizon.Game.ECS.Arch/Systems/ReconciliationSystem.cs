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
    /// </remarks>
    private void ProcessCorrection(World world)
    {
        if (!CorrectionReceiveBuffer.Instance.TryTake(out var correction) || correction == null)
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

                // 从修正后的权威位置重放所有未确认输入。
                // 已确认输入已被 ProcessInputAck 清理，GetFromTick(0) 返回的是服务端尚未确认的输入，
                // 这些输入对应的预测移动在吸附后失效，必须从权威位置重新应用。
                var unconfirmedInputs = InputHistoryBuffer.Instance.GetFromTick(0);
                foreach (var historyInput in unconfirmedInputs)
                {
                    var jumpImpulse = ((historyInput.InputBits & 0x1) != 0) ? 5.5f : 0f;

                    var (nx, ny, nz, nvz) = MovementFormula.Step(
                        pred.X, pred.Y, pred.Z, pred.Vz,
                        historyInput.MoveX, historyInput.MoveY, jumpImpulse,
                        1f / 60f,
                        maxSpeed: 0f);

                    pred.X = nx;
                    pred.Y = ny;
                    pred.Z = nz;
                    pred.Vz = nvz;
                }
            }
        });
    }
}
