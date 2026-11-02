using System;
using FlaxEngine;
using Horizon.Game.Message;
using Horizon.Game.Message.Network;

namespace HundunWorld.Game.Combat
{
    /// <summary>
    /// 战斗上报出口：把"我打了谁 / 我放了什么技能"交给服务端权威结算。
    /// <para>
    /// 伤害数字一律由服务端返回的 <see cref="DamageMessage"/> 决定，这里<b>不报伤害</b>
    /// （<c>Damage</c> 固定为 0）。客户端报伤害等于把结算权交回给可被改包的一方：
    /// 服务端的通用攻击、五行攻击力、克制、防御与破防就全都被一个本地数字顶掉了。
    /// </para>
    /// <para>
    /// 只对网络实体上报。怪物现在是有服务端 id 的（DS 快照里的 <c>RemotePlayerActor.EntityId</c>，
    /// 由刷怪点槽位编码），所以每次动作都同时交给两个受击方：Orleans 的角色战斗链（只认 CharacterGrain）
    /// 与 DS 权威世界（只认它自己世界里的怪物）——见 <see cref="ReportToDsWorld"/>。
    /// 场景物件仍只有本地表现，等它们纳入同步后走同一条通道。
    /// </para>
    /// </summary>
    public static class CombatReporter
    {
        /// <summary>本地角色的服务端 characterId（服务端寻址用它，不是 Flax Actor id）。</summary>
        private static ulong LocalCharacterId => HundunWorldGame.Instance?.PlayerId ?? 0UL;

        /// <summary>
        /// 从目标 Actor 上取服务端实体 id；不是网络实体（怪物/物件/尚未同步的角色）返回 0。
        /// </summary>
        public static ulong ResolveNetworkTarget(Actor target)
        {
            if (target == null) return 0UL;
            var remote = target.GetScript<RemotePlayerActor>();
            return remote != null ? remote.EntityId : 0UL;
        }

        /// <summary>普攻上报。<paramref name="elementType"/> 为 0 时由服务端按攻方命行结算。</summary>
        public static void ReportAttack(Actor target, int elementType = 0, int skillId = 0)
        {
            var targetId = ResolveNetworkTarget(target);
            if (targetId == 0UL) return;

            // 怪物只在 **DS 权威世界**里有血量与掉落；发给 Orleans 的 AttackMessage 只认
            // CharacterGrain，对怪物 id 一律回"实体未注册 / 剩余血量 0"——实测客户端日志里
            // 每刀一条"受到伤害 7499，剩余血量 0"，而怪物血条一动没动：那是一条没有受击方的结算。
            ReportAttackById(targetId, elementType, skillId, YawTowards(target?.Position),
                sendToOrleans: !IsMonsterTarget(target));
        }

        /// <summary>普攻上报（已持有服务端实体 id 的调用点用）。</summary>
        public static void ReportAttackById(ulong targetId, int elementType = 0, int skillId = 0,
            float? targetYaw = null, bool sendToOrleans = true)
        {
            var attackerId = LocalCharacterId;
            if (attackerId == 0UL || targetId == 0UL)
            {
                // 没进世界（或目标是本地怪物）属正常路径，不该刷错误。
                return;
            }

            if (sendToOrleans)
            {
                _ = SendAsync(new AttackMessage
                {
                    AttackerId = attackerId,
                    TargetId = targetId,
                    SkillId = skillId,
                    ElementType = elementType,
                    Damage = 0,           // 权威伤害由服务端算，客户端不报数
                }, $"攻击 {targetId}");
            }

            ReportToDsWorld(attackerId, targetId, skillId,
                Horizon.Game.Message.Sync.CombatActionKind.NormalAttack, targetYaw);
        }

        /// <summary>目标是不是世界怪物（只看标记组件，不猜 id 编码）。</summary>
        private static bool IsMonsterTarget(Actor target)
        {
            var tag = target?.GetScript<HundunWorld.Game.Selection.SelectableEntityTag>();
            return tag != null && tag.Kind == HundunWorld.Game.Selection.SelectableKind.Monster;
        }

        /// <summary>
        /// 技能施放上报。能量消耗按客户端已知值上报，服务端会用自己的内力池裁决并拒绝超支。
        /// </summary>
        public static void ReportSkillCast(Actor target, int skillId, float energyCost = 0f)
        {
            var targetId = ResolveNetworkTarget(target);
            if (targetId == 0UL) return;

            ReportSkillCastById(targetId, skillId, energyCost, Vector3.Zero, YawTowards(target?.Position),
                sendToOrleans: !IsMonsterTarget(target));
        }

        /// <summary>技能施放上报（已持有服务端实体 id 的调用点用）。</summary>
        public static void ReportSkillCastById(ulong targetId, int skillId, float energyCost = 0f,
            Vector3 impactPosition = default, float? targetYaw = null, bool sendToOrleans = true)
        {
            var casterId = LocalCharacterId;
            if (casterId == 0UL || targetId == 0UL) return;

            if (sendToOrleans)
            {
                _ = SendAsync(new SkillCastMessage
                {
                    CasterId = casterId,
                    SkillId = skillId,
                    TargetEntityId = targetId,
                    TargetIds = new System.Collections.Generic.List<ulong> { targetId },
                    TargetPosition = new Position { X = impactPosition.X, Y = impactPosition.Y, Z = impactPosition.Z },
                    EnergyCost = energyCost,
                }, $"施放 {skillId} → {targetId}");
            }

            ReportToDsWorld(casterId, targetId, skillId,
                Horizon.Game.Message.Sync.CombatActionKind.SkillCast, targetYaw);
        }

        /// <summary>
        /// 把同一次动作也交给 **DS 权威世界**（同步通道，与移动输入同一出口、同一授权规则）。
        /// <para>为什么必须有这一路：住在 <c>DsWorldSession</c> 里的那份怪物状态才是玩家看见的那份
        /// （血条、施法条、索敌、死亡、掉落与经验全从它的快照来）。而本方法以上只发 Orleans 的
        /// <c>AttackMessage</c>/<c>SkillCastMessage</c>，那条链路按 <c>CharacterGrain</c> 寻址，
        /// 根本不认识怪物实体 id（刷怪点编码出来的 id，不是角色 id）。
        /// 结果是"客户端在砍、服务器在算、玩家什么也没打掉"。</para>
        /// <para>两条通道各管各的受击方，互不重复计数：DS 只受理 <c>_monstersById</c> 里的目标，
        /// Orleans 侧只受理有 CharacterGrain 的角色。发错边的那一半会被当场判 Invalid。</para>
        /// </summary>
        private static void ReportToDsWorld(ulong attackerId, ulong targetId, int skillId,
            Horizon.Game.Message.Sync.CombatActionKind kind, float? targetYaw = null)
        {
            var guardSender = HundunWorldGame.Instance?.GuardSyncSender;
            if (guardSender == null) return;   // 没走 DS 同步通道（离线/本地演练）时这条就是空操作

            var packet = new Horizon.Game.Message.Sync.CombatActionPacket
            {
                AttackerId = attackerId,
                TargetId = targetId,
                SkillId = kind == Horizon.Game.Message.Sync.CombatActionKind.SkillCast ? skillId : 0,
                // 权威节拍由服务端的 tick 决定，这个字段 DS 不参与结算；
                // 与 InputPacket 不同，战斗动作不需要客户端预测回滚。
                ClientTick = 0,
                // 朝向必须是**真实朝向**：DS 的正面判定（CombatCore 的 120° 扇区）拿这一格点乘
                // "攻方→目标"方向。这里曾经写死 0（= 服务端平面里的 +X），于是"怪不在正东侧"
                // 的攻击全被判 OutOfArc 静默作废——实测 60 刀打在同一只怪身上一滴血不掉。
                AttackerYaw = targetYaw ?? YawOfLocalPlayer(),
                ActionKind = kind,
            };
            _ = guardSender.SendLocalAsync(packet, attackerId);
        }

        /// <summary>
        /// 本地玩家站在哪、要不要转身面向目标：服务端朝向约定是 <c>atan2(dy, dx)</c>，
        /// 其中服务端平面 <c>X = Flax X、Y = Flax Z</c>（与移动上行的预测坐标同一套轴）。
        /// <para>为什么由客户端算：WoW 的客户端在起手普攻时就把角色自动转向目标，服务端只做扇区校验
        /// （<c>Unit::CanAttack</c>）。这里补的是同一件事——玩家不用自己先站好角度，
        /// 但服务端那条扇区门仍然有效（它照样能拒掉朝向错的包）。</para>
        /// </summary>
        private static float? YawTowards(Vector3? targetPosition)
        {
            var self = HundunWorldGame.Instance?.LocalPlayerActor;
            if (self == null || targetPosition == null) return null;

            var from = self.Position;
            var to = targetPosition.Value;
            var dx = to.X - from.X;
            var dy = to.Z - from.Z;
            // 与目标重合时方向无意义（服务端也会跳过扇区判定），保持 0 即可。
            if (Math.Abs(dx) < 1e-4f && Math.Abs(dy) < 1e-4f) return 0f;
            return (float)Math.Atan2(dy, dx);
        }

        /// <summary>拿不到目标坐标时的退路：用玩家自身的真实前向换算成服务端平面朝向。</summary>
        private static float YawOfLocalPlayer()
        {
            var self = HundunWorldGame.Instance?.LocalPlayerActor;
            if (self == null) return 0f;

            var forward = self.Transform.Forward;
            if (Math.Abs(forward.X) < 1e-4f && Math.Abs(forward.Z) < 1e-4f) return 0f;
            return (float)Math.Atan2(forward.Z, forward.X);
        }

        /// <summary>
        /// 尸体拾取：把"取第 slotIndex 个槽"发给 DS（G 键走这里）。 CORPSE-LOOT-PATCH
        /// <para>槽号 -1 = "取第一个还没被取走的"：槽序是只能往尾部追加的持久化契约，
        /// 客户端不该自己跟踪它（权威侧会在回执里带回实际槽号）。</para>
        /// </summary>
        public static void ReportCorpseLoot(ulong localCharacterId, ulong corpseEntityId, int slotIndex)
        {
            var guardSender = HundunWorldGame.Instance?.GuardSyncSender;
            if (guardSender == null || localCharacterId == 0UL || corpseEntityId == 0UL) return;
            var packet = new Horizon.Game.Message.Sync.CorpseLootTakeRequestPacket
            {
                Kind = Horizon.Game.Message.Sync.SyncPacketKind.CorpseLootTakeRequest,
                CharacterId = localCharacterId,
                CorpseEntityId = corpseEntityId,
                SlotIndex = slotIndex,
            };
            _ = guardSender.SendLocalAsync(packet, localCharacterId);
        }

        /// <summary>
        /// 打断（脚踢那一类）上报：目标是"头顶挂着一条金色（可打断）读条"的怪。
        /// <para>服务端会拒三种情形并把原因各自计数（没在读条 / 那条不可打断 / 够不着），
        /// 客户端确认"踢断了"看的是随后那条 <c>MonsterCastStop(Interrupted)</c> 事件——
        /// 不要自己把条子抹掉，那会让"没踢动"和"踢动了"长得一模一样。</para>
        /// </summary>
        public static void ReportInterruptById(ulong targetId)
        {
            var attackerId = LocalCharacterId;
            if (attackerId == 0UL || targetId == 0UL) return;

            ReportToDsWorld(attackerId, targetId, 0, Horizon.Game.Message.Sync.CombatActionKind.Interrupt);
        }

        /// <summary>
        /// 嘲讽上报：目标是 DS 世界里的一只怪（<c>spawnPointId&lt;&lt;32 | slot+1</c> 那个编码）。
        /// <para>客户端**什么都不改**：换目标这件事由服务端在下一拍 AI 里落进快照，
        /// 自己提前把怪"拽过来"会让"拉住了"和"没拉住"长得一模一样——
        /// 而服务端是拒三种情形的（够不着 / 正在回位免疫窗 / 已经是一具尸体），各自计数。</para>
        /// <para>还没接的入口：客户端效果枚举 <c>SkillEffectType.Taunt</c> 是个"声明了却没人实现"的值
        /// （没有对应的效果类、没有分派分支、技能表里也没有一行用它），所以这条上报只能由 UI 按键
        /// 或调试入口调用（与打断同一状态）。</para>
        /// </summary>
        public static void ReportTauntById(ulong targetId)
        {
            var attackerId = LocalCharacterId;
            if (attackerId == 0UL || targetId == 0UL) return;

            ReportToDsWorld(attackerId, targetId, 0, Horizon.Game.Message.Sync.CombatActionKind.Taunt);
        }

        private static async System.Threading.Tasks.Task SendAsync<T>(T message, string what) where T : MessageUnion, INetworkMessage
        {
            try
            {
                var network = HundunWorldGame.Instance?.NetworkManager;
                if (network == null) return;

                bool sent = await network.SendAsync(message);
                if (!sent)
                    Debug.LogWarning($"[CombatReporter] {what} 上报失败：连接不可用或未就绪。");
            }
            catch (Exception ex)
            {
                // 上报失败不能打断技能表现：本地动画与特效照常，伤害数字改由下一次结算或同步补上。
                Debug.LogError($"[CombatReporter] {what} 上报异常: {ex.Message}");
            }
        }
    }
}
