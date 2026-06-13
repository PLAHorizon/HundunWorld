using System.Collections.Generic;
using Arch.Core;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Core;
using Horizon.Game.ECS.Arch.Network;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;

namespace Horizon.Game.ECS.Arch.Systems;

/// <summary>
/// 事件应用系统：在 NetworkReceive 阶段消费 <see cref="EventReceiveBuffer"/>，
/// 将服务器下发的同步事件（技能释放、伤害、死亡等）应用到 Arch 世界。
/// </summary>
/// <remarks>
/// 当前实现以诊断日志和组件更新为主，VFX/动画触发将通过后续 UE5 绑定完成。
/// 系统通过查询 <see cref="NetworkIdentityComponent"/> 来匹配目标实体。
/// </remarks>
[ArchSystem(SystemGroup.NetworkReceive, order: 20)]
public sealed class EventApplySystem : ArchSystemBase
{
    /// <summary>累计处理的 SkillCast 事件数量（诊断用）。</summary>
    public int TotalSkillCastEvents { get; private set; }

    /// <summary>累计处理的 Damage 事件数量（诊断用）。</summary>
    public int TotalDamageEvents { get; private set; }

    /// <summary>累计处理的 Death 事件数量（诊断用）。</summary>
    public int TotalDeathEvents { get; private set; }

    /// <inheritdoc />
    public override void Update(World world, TimeSpan deltaTime)
    {
        while (EventReceiveBuffer.Instance.TryDequeue(out var eventPacket))
        {
            foreach (var syncEvent in eventPacket.Events)
            {
                switch (syncEvent.Kind)
                {
                    case SyncEventKind.SkillCast:
                        HandleSkillCast(syncEvent);
                        break;

                    case SyncEventKind.Damage:
                        HandleDamage(world, syncEvent);
                        break;

                    case SyncEventKind.Death:
                        HandleDeath(world, syncEvent);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// 处理技能释放事件：记录日志，预留 VFX 触发入口。
    /// </summary>
    private void HandleSkillCast(SyncEvent syncEvent)
    {
        TotalSkillCastEvents++;
        System.Diagnostics.Debug.WriteLine(
            $"[EventApply] SkillCast: Source={syncEvent.SourceEntityId}, Target={syncEvent.TargetEntityId}, " +
            $"SkillId={syncEvent.IntValue}, Duration={syncEvent.FloatValue}");
    }

    /// <summary>
    /// 处理伤害事件：更新目标实体的 <see cref="EntityStateAuthComponent"/> 健康值。
    /// </summary>
    private void HandleDamage(World world, SyncEvent syncEvent)
    {
        TotalDamageEvents++;

        var query = new QueryDescription().WithAll<NetworkIdentityComponent>();
        world.Query(in query, (Entity entity, ref NetworkIdentityComponent netId) =>
        {
            if (netId.EntityId != syncEvent.TargetEntityId)
            {
                return;
            }

            System.Diagnostics.Debug.WriteLine(
                $"[EventApply] Damage: Source={syncEvent.SourceEntityId}, Target={syncEvent.TargetEntityId}, " +
                $"Damage={syncEvent.IntValue}, CritRate={syncEvent.FloatValue}");

            if (world.Has<EntityStateAuthComponent>(entity))
            {
                ref var state = ref world.Get<EntityStateAuthComponent>(entity);
                state.Health -= syncEvent.IntValue;
                if (state.Health < 0)
                {
                    state.Health = 0;
                }
                world.Set(entity, ref state);
            }
        });
    }

    /// <summary>
    /// 处理死亡事件：标记目标实体为死亡状态。
    /// </summary>
    private void HandleDeath(World world, SyncEvent syncEvent)
    {
        TotalDeathEvents++;

        var query = new QueryDescription().WithAll<NetworkIdentityComponent>();
        world.Query(in query, (Entity entity, ref NetworkIdentityComponent netId) =>
        {
            if (netId.EntityId != syncEvent.TargetEntityId)
            {
                return;
            }

            System.Diagnostics.Debug.WriteLine(
                $"[EventApply] Death: Source={syncEvent.SourceEntityId}, Target={syncEvent.TargetEntityId}");

            if (world.Has<EntityStateAuthComponent>(entity))
            {
                ref var state = ref world.Get<EntityStateAuthComponent>(entity);
                state.IsDead = true;
                state.Health = 0;
                world.Set(entity, ref state);
            }
        });
    }
}
