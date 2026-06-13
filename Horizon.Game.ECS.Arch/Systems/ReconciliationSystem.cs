using System;
using Arch.Core;
using Horizon.Game.Core.Sim;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Core;
using Horizon.Game.ECS.Arch.Network;
using Horizon.Game.Message.Sync;

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
    /// 处理服务器输入确认：清理已确认输入，从上一次确认位置重播未确认输入。
    /// </summary>
    private void ProcessInputAck(World world)
    {
        if (!InputAckReceiveBuffer.Instance.TryTake(out var ack) || ack == null)
        {
            return;
        }

        var lastProcessedTick = ack.LastProcessedClientTick;

        var query = new QueryDescription()
            .WithAll<PlayerInputComponent, PredictedTransformComponent, NetworkIdentityComponent>();

        world.Query(in query, (Entity entity, ref PlayerInputComponent input, ref PredictedTransformComponent pred, ref NetworkIdentityComponent netId) =>
        {
            if (!netId.IsLocalPlayer)
            {
                return;
            }

            var unconfirmedInputs = InputHistoryBuffer.Instance.GetFromTick(lastProcessedTick);

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

            InputHistoryBuffer.Instance.ClearUpTo(lastProcessedTick);
        });
    }

    /// <summary>
    /// 处理服务器位置修正：当偏差超过阈值时强制吸附到权威位置。
    /// </summary>
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
                pred.X = correction.CorrectedX;
                pred.Y = correction.CorrectedY;
                pred.Z = correction.CorrectedZ;
                pred.Vz = correction.CorrectedVz;
                pred.NeedsReconciliation = true;

                TotalCorrectionsApplied++;
            }
        });
    }
}
