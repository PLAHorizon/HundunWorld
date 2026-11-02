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
    /// 尸体拾取请求：**只有 DS 权威承载的会话才转**（没有 DS 世界就没有"尸体"这件事）。
    /// <para>转成功后返回 null：结果包由 DS 直接按会话下行（走它自己的下行编码），
    /// 网关不在这里替 DS 合成——同一条结果两条路各发一份的话，客户端会收到两次"获得 X"。</para>
    /// <para>非 DS 会话回一条 <c>UnknownCorpse</c>：让客户端当场得到理由，而不是干等超时。</para>
    /// </summary>
    public async Task<SyncPacket?> HandleCorpseLootTakeAsync(CorpseLootTakeRequestPacket packet)
    {
        if (packet.CharacterId == 0) return null;
        var characterId = (long)packet.CharacterId;
        if (DsDataPlane is { IsEnabled: true })
        {
            var shardId = ShardRouter.Resolve(characterId);
            if (DsDataPlane.IsSessionDsAuthoritative(shardId, characterId)
                && await TryRouteToDsAsync(packet, characterId))
            {
                return null;
            }
        }
        return new CorpseLootTakeResultPacket
        {
            Kind = SyncPacketKind.CorpseLootTakeResult,
            CorpseEntityId = packet.CorpseEntityId,
            SlotIndex = packet.SlotIndex,
            Status = 7,   // UnknownCorpse：这个会话不在 DS 世界里，谈不到尸体
        };
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
            // **DS 权威会话的战斗必须转给 DS**（这一跳在本轮之前是缺的）：
            // 世界、数据面、上行分发都已经受理战斗包了，可网关把战斗帧无条件送进 CombatSystemGrain，
            // 而那只认识 CharacterGrain 的角色——于是住在 DS 世界里的怪物（有 HP、会读条、会掉东西的那一份）
            // 在实机上仍然一格血也掉不了，玩家看到的是"我在砍，数字在跳，怪就是不死"。
            // 判据比输入包那道更严一层：**只有这个会话确实由 DS 承载权威时才转**，
            // 因为战斗多一个受击方维度，没进 DS 的会话的攻击不该被抢过来。
            // 转成功后返回 null：伤害/死亡/掉落的表现由 DS 的下行数据面回推（快照 + 定向事件）；
            // 网关不在这里替 DS 合成 DamagePacket，否则同一跳伤害会有两个权威各发一份。
            if (DsDataPlane is { IsEnabled: true })
            {
                var shardId = ShardRouter.Resolve(attackerId);
                if (DsDataPlane.IsSessionDsAuthoritative(shardId, attackerId)
                    && await TryRouteToDsAsync(combatAction, attackerId))
                {
                    return null;
                }
            }

            // 走到这里才允许触碰 Orleans grain（与 SyncInputHandler 同一条"唯一 grain 解析点"纪律）。
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
