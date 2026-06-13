using System;
using System.Collections.Generic;
using Arch.Core;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Core;
using Horizon.Game.ECS.Arch.Network;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;

namespace Horizon.Game.ECS.Arch.Systems;

/// <summary>
/// 快照应用系统：在 NetworkReceive 阶段消费 <see cref="SnapshotReceiveBuffer"/>，
/// 将服务器下发的快照增量（Spawn / Update / Despawn）写入 Arch 世界。
/// </summary>
/// <remarks>
/// 系统维护一个 EntityId → <see cref="Entity"/> 的字典映射，用于快速查找远程实体。
/// 对于本地玩家实体（通过 <see cref="LocalPlayerOwnerId"/> 匹配），跳过变换更新，
/// 以保证本地预测优先权。
/// </remarks>
[ArchSystem(SystemGroup.NetworkReceive, order: 10)]
public sealed class SnapshotApplySystem : ArchSystemBase
{
    /// <summary>EntityId 到 Arch Entity 的映射表。</summary>
    private readonly Dictionary<ulong, Entity> _entityIdToArchEntity = new();

    /// <summary>本地玩家归属 ID（0 表示未设置）。</summary>
    public ulong LocalPlayerOwnerId { get; set; }

    /// <inheritdoc />
    public override void Update(World world, TimeSpan deltaTime)
    {
        while (SnapshotReceiveBuffer.Instance.TryDequeue(out var snapshot))
        {
            foreach (var delta in snapshot.Deltas)
            {
                switch (delta.Kind)
                {
                    case EntityDeltaKind.Spawn:
                        HandleSpawn(world, delta, snapshot.ServerTick);
                        break;

                    case EntityDeltaKind.Update:
                        HandleUpdate(world, delta, snapshot.ServerTick);
                        break;

                    case EntityDeltaKind.Despawn:
                        HandleDespawn(world, delta);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// 处理实体生成：创建新的 Arch 实体并添加网络身份、权威变换和插值变换组件。
    /// </summary>
    private void HandleSpawn(World world, EntityDelta delta, long serverTick)
    {
        if (delta.Identity == null)
        {
            return;
        }

        var archEntity = world.Create();

        var netId = new NetworkIdentityComponent
        {
            EntityId = delta.EntityId,
            IsLocalPlayer = delta.Identity.Value.OwnerId == LocalPlayerOwnerId && LocalPlayerOwnerId != 0,
        };

        var authTransform = delta.Transform != null
            ? delta.Transform.Value
            : new AuthTransformComponent
            {
                X = 0f, Y = 0f, Z = 0f,
                Pitch = 0f, Yaw = 0f, Roll = 0f,
                ServerTick = serverTick,
            };
        authTransform.ServerTick = serverTick;

        world.Add(archEntity, netId);
        world.Add(archEntity, authTransform);

        if (!netId.IsLocalPlayer)
        {
            var interp = new InterpolatedTransformComponent
            {
                X = authTransform.X,
                Y = authTransform.Y,
                Z = authTransform.Z,
                TargetX = authTransform.X,
                TargetY = authTransform.Y,
                TargetZ = authTransform.Z,
                Alpha = 1f,
                ServerTick = serverTick,
                ReceivedTick = 0,
            };
            world.Add(archEntity, interp);
        }

        _entityIdToArchEntity[delta.EntityId] = archEntity;
    }

    /// <summary>
    /// 处理实体更新：查找对应 Arch 实体，更新权威变换和插值目标。
    /// 本地玩家实体的变换更新被跳过（本地预测优先）。
    /// </summary>
    private void HandleUpdate(World world, EntityDelta delta, long serverTick)
    {
        if (!_entityIdToArchEntity.TryGetValue(delta.EntityId, out var archEntity))
        {
            return;
        }

        if (!world.IsAlive(archEntity))
        {
            _entityIdToArchEntity.Remove(delta.EntityId);
            return;
        }

        if (delta.Transform != null)
        {
            var newTransform = delta.Transform.Value;
            newTransform.ServerTick = serverTick;

            ref var netId = ref world.Get<NetworkIdentityComponent>(archEntity);

            if (netId.IsLocalPlayer)
            {
                world.Set(archEntity, ref newTransform);
                return;
            }

            if (world.Has<InterpolatedTransformComponent>(archEntity))
            {
                ref var oldAuth = ref world.Get<AuthTransformComponent>(archEntity);
                ref var interp = ref world.Get<InterpolatedTransformComponent>(archEntity);

                interp.X = oldAuth.X;
                interp.Y = oldAuth.Y;
                interp.Z = oldAuth.Z;
                interp.TargetX = newTransform.X;
                interp.TargetY = newTransform.Y;
                interp.TargetZ = newTransform.Z;
                interp.Alpha = 0f;
                interp.ServerTick = serverTick;
            }

            world.Set(archEntity, ref newTransform);
        }

        if (delta.State != null)
        {
            var newState = delta.State.Value;
            if (world.Has<EntityStateAuthComponent>(archEntity))
            {
                world.Set(archEntity, ref newState);
            }
            else
            {
                world.Add(archEntity, newState);
            }
        }
    }

    /// <summary>
    /// 处理实体销毁：从 Arch 世界中移除实体并清理映射表。
    /// </summary>
    private void HandleDespawn(World world, EntityDelta delta)
    {
        if (_entityIdToArchEntity.TryGetValue(delta.EntityId, out var archEntity))
        {
            if (world.IsAlive(archEntity))
            {
                world.Destroy(archEntity);
            }
            _entityIdToArchEntity.Remove(delta.EntityId);
        }
    }
}
