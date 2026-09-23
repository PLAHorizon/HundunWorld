using System;
using System.Threading.Tasks;
using Horizon.Game.Core.Interfaces;
using Horizon.Game.Core.World;
using Horizon.Game.Message.Sync;
using Horizon.Orleans.Interface.Combat;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Horizon.Game.Core.Handlers;

/// <summary>
/// 阶段 3.2 — <see cref="SyncPacketKind.CombatAction"/> 流处理器：战斗动作上行 → CombatSystemGrain
/// 裁决 → DamagePacket 下发。自 <see cref="SyncPacketHandler"/> 拆分而来。
/// </summary>
public sealed class SyncCombatHandler : SyncPacketFlowHandlerBase
{
    public SyncCombatHandler(
        ILogger<MessageHandlerBase> logger,
        IClusterClient clusterClient,
        IShardRouter shardRouter,
        IDSyncDataPlane? dsDataPlane,
        ISessionBindingValidator? sessionBindingValidator,
        SyncSessionState sessionState,
        Func<string> connectionIdProvider)
        : base(logger, clusterClient, shardRouter, dsDataPlane, sessionBindingValidator, sessionState, connectionIdProvider)
    {
    }

    /// <summary>
    /// P1.4：处理客户端上行的战斗动作（攻击/技能/道具）。
    /// 路由到 CombatSystemGrain 裁决，返回 DamagePacket 或 null。
    /// </summary>
    public async Task<SyncPacket?> HandleCombatActionAsync(CombatActionPacket combatAction)
    {
        var attackerId = (long)combatAction.AttackerId;

        // 基本校验
        if (combatAction.AttackerId == 0)
        {
            Logger.LogWarning("战斗动作无效：AttackerId=0");
            return null;
        }

        try
        {
            // 路由到 CombatSystemGrain（以攻击者 ID 为 Grain Key，简化版 Phase 1）
            var combatGrain = ClusterClient.GetGrain<ICombatSystemGrain>(ShardRouter.Resolve(attackerId));

            var request = new CombatActionRequest
            {
                AttackerId = combatAction.AttackerId,
                TargetId = combatAction.TargetId,
                ActionKind = (byte)combatAction.ActionKind,
                SkillId = combatAction.SkillId,
                ClientTick = combatAction.ClientTick,
                AttackerYaw = combatAction.AttackerYaw,
            };

            var verdict = await combatGrain.ProcessActionAsync(request);

            if (!verdict.IsHit)
            {
                return null; // 闪避/无效，不下发包
            }

            // 构造 DamagePacket 返回给客户端
            var damagePacket = new DamagePacket
            {
                AttackerId = combatAction.AttackerId,
                TargetId = combatAction.TargetId,
                DamageAmount = verdict.DamageAmount,
                DamageType = verdict.DamageType,
                IsCritical = verdict.IsCritical,
                RemainingHp = verdict.TargetRemainingHp,
                MaxHp = verdict.TargetMaxHp,
                SkillId = combatAction.SkillId,
                ServerTick = verdict.ServerTick,
            };

            // 如果目标死亡，后续由 ZoneShard 广播 DeathPacket（TODO Phase 2）
            if (verdict.IsTargetDead)
            {
                Logger.LogInformation(
                    "战斗击杀。AttackerId={AttackerId}, TargetId={TargetId}, Damage={Damage}",
                    combatAction.AttackerId, combatAction.TargetId, verdict.DamageAmount);
            }

            return damagePacket;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "处理战斗动作失败。AttackerId={AttackerId}, TargetId={TargetId}",
                combatAction.AttackerId, combatAction.TargetId);
            return null;
        }
    }
}
