using System;
using System.Collections.Generic;
using Arch.Core;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Core;
using Horizon.Game.ECS.Arch.Network;
using Horizon.Game.Message.Sync;

namespace Horizon.Game.ECS.Arch.Systems;

/// <summary>
/// 输入发送系统：在 NetworkSend 阶段将本地玩家的输入打包成 <see cref="InputPacket"/> 并放入发送队列。
/// </summary>
/// <remarks>
/// 仅查询拥有 <see cref="PlayerInputComponent"/> + <see cref="NetworkIdentityComponent"/>（IsLocalPlayer=true）
/// + <see cref="PredictedTransformComponent"/> 的实体。
/// 打包后的 InputPacket 通过 <see cref="InputSendQueue"/> 缓存，网络层通过 <see cref="GetPendingInputs"/> 批量取出。
/// </remarks>
[ArchSystem(SystemGroup.NetworkSend, order: 0)]
public sealed class InputSendSystem : ArchSystemBase
{
    /// <summary>
    /// 获取所有待发送的输入包列表（网络层应调用此方法消费并清空队列）。
    /// </summary>
    public static List<InputPacket> GetPendingInputs()
    {
        var list = new List<InputPacket>();
        while (InputSendQueue.Instance.TryDequeue(out var packet))
        {
            list.Add(packet);
        }
        return list;
    }

    /// <inheritdoc />
    public override void Update(World world, TimeSpan deltaTime)
    {
        var query = new QueryDescription()
            .WithAll<PlayerInputComponent, NetworkIdentityComponent, PredictedTransformComponent>();

        world.Query(in query, (Entity entity, ref PlayerInputComponent input, ref NetworkIdentityComponent netId, ref PredictedTransformComponent pred) =>
        {
            if (!netId.IsLocalPlayer)
            {
                return;
            }

            var packet = new InputPacket
            {
                ClientTick = pred.ClientTick,
                InputBits = input.InputBits,
                LookYaw = input.LookYaw,
                LookPitch = input.LookPitch,
                MoveX = input.MoveX,
                MoveY = input.MoveY,
            };

            InputSendQueue.Instance.Enqueue(packet);
        });
    }
}
