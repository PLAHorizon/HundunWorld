// 模块关系说明：
// 本文件是 NarrativePro.Core 的**引擎无关纯规则层**（不引用任何 FlaxEngine 类型），
// 承载"两个阵营容器 → 态度"与"态度 + 友伤设置 → 是否否决本次伤害"这两条判定。
//
// 为什么需要它：这两条规则原本长在
//   - Core/ArsenalStatics.cs 的 GetAttitude/IsSameTeam（入参是 FlaxEngine.Actor，取阵营要过场景/控制器解析），
//   - UnrealFramework/NarrativeGameState.cs 的 GetFactionsAttitudeTowardsFactions/GetFactionAttitudeTowardsFaction/
//     GetOneWayFactionAttitude（入参是 GameplayTag/GameplayTagContainer），
//   - GAS/NarrativeExecCalc.cs 的 UNarrativeDamageExecCalc 友伤早退分支，
// 而 Actor 与 ASC 都是 FlaxEngine.Script 子类：没有原生 FlaxEngine 库时 `new` 直接抛 DllNotFoundException，
// 进程外一条断言都打不到。友伤否决的致命点是"判定顺序"：
//   ① 双方都无阵营必须是 Neutral（否则无阵营的道具/NPC 被全员自动敌对，ArsenalStatics.cs:75-84 已修过一次）；
//   ② 只要有一方登记了阵营且不同队就必须维持 Hostile（否则匪徒对友方阵营也不再敌对，战斗被削弱）；
//   ③ 部分重叠时"任一敌对即敌对"必须**优先于**"存在友好即友好"（UE5 NarrativeGameState.cpp:225-243），
//      写反顺序会让 {A,B} 打 {B,C} 在 A↔C 已宣战的情况下仍判 Friendly，友伤门把这一刀整口抹掉。
//
// 实时通路（ArsenalStatics.GetAttitude/IsSameTeam、NarrativeGameState 的三个态度查询、
// NarrativeDamageExecCalc.Execute 的友伤早退）全部经由这里，harness 驱动的是同一份实现。
//
// UE5 口径核对：
//   NarrativeDamageExecCalc.cpp:52-67 —— 只有 SourceActor != TargetActor 时才走友伤门；
//   禁友伤 + GetAttitude == Friendly 时**整次执行 return**（连带后续的施毒等副作用一起跳过）；
//   NarrativeGameState.cpp:190-243 —— 容器级聚合：任一 hostile 立刻返回 hostile，
//   否则有任一 friendly 记为 friendly，全 neutral 则 neutral；
//   NarrativeGameState.cpp:246-255 —— 单对：先看 source→target，source 侧为 neutral 时回退看 target→source；
//   NarrativeGameState.cpp:257-…（GetOneWayFactionAttitude）—— 无效标签 neutral、同名 friendly，
//   其余查 FactionAllianceMap，查不到 neutral。
// 注：UE5 在容器级还先做 FriendlyAll/HostileAll/HostileOthers 三个覆写标签短路
// （NarrativeGameState.cpp:201-218）。本端口从复刻首轮起就没有把这三个覆写接进态度查询，
// 端口里匪徒的敌对性由 ComputeActorAttitude 的"单方有阵营即 Hostile"策略承担；
// 把它们接进来会当场改写已发货 NPC 的仇恨结果（npc_village_smith.json 的 FriendlyAll
// 会让铁匠对所有目标变成 Friendly，禁友伤下再也打不出伤害），因此本文件保持端口语义不变，
// 只做抽取与断言，不做规则升级。

using System;
using System.Collections.Generic;

namespace NarrativePro.Core
{
    /// <summary>
    /// 阵营态度 + 友伤否决的纯函数层。签名里只有字符串集合与委托，可在无引擎环境下直接调用。
    /// </summary>
    public static class NarrativeFactionAttitude
    {
        /// <summary>
        /// 联盟表一次查表的委托：源阵营名 → 目标阵营名的态度。
        /// 实时侧由 <c>NarrativeGameState.FactionAllianceMap</c> 提供；查不到时返回 Neutral。
        /// </summary>
        public delegate ArsenalStatics.ETeamAttitude FactionAllianceLookup(string sourceFaction, string targetFaction);

        private static readonly List<string> EmptyNames = new List<string>();

        // =========================================================================
        // Rule A —— Actor 侧态度（ArsenalStatics.GetAttitude / IsSameTeam 的语义层）
        // =========================================================================

        /// <summary>
        /// 两个阵营集合是否有交集。对应 UE5 <c>FGameplayTagContainer::HasAny</c>
        /// （ArsenalStatics.cpp:188-202 IsSameTeam 的比较口径）。
        /// </summary>
        public static bool SharesAnyFaction(IEnumerable<string> left, IEnumerable<string> right)
        {
            if (left == null || right == null) return false;

            // 以较小集合做外层，比较次数与原容器 HasAny 一致
            var rightSet = ToSet(right);
            if (rightSet.Count == 0) return false;

            foreach (var name in left)
            {
                if (string.IsNullOrEmpty(name)) continue;
                if (rightSet.Contains(name)) return true;
            }
            return false;
        }

        /// <summary>集合是否登记了任何有效阵营（ArsenalStatics.cs:80-81 的 testHasFaction/targetHasFaction）。</summary>
        public static bool HasAnyFaction(IEnumerable<string> factions)
        {
            if (factions == null) return false;
            foreach (var name in factions)
                if (!string.IsNullOrEmpty(name)) return true;
            return false;
        }

        /// <summary>
        /// Actor 侧态度判定（对应 <c>ArsenalStatics.GetAttitude</c> 的现有全部分支）。
        /// <para>顺序即语义，不可重排：自身 → 同队 → 双方无阵营 → 其余敌对。</para>
        /// </summary>
        /// <param name="sourceFactions">发起方阵营名集合（Actor 为空时传空集合）。</param>
        /// <param name="targetFactions">目标阵营名集合。</param>
        /// <param name="bSameActor">双方是否为同一个 Actor（UE5 <c>TestActor == Target</c>）。</param>
        public static ArsenalStatics.ETeamAttitude ComputeActorAttitude(
            IEnumerable<string> sourceFactions, IEnumerable<string> targetFactions, bool bSameActor)
        {
            // 同一个 Actor：直接 Friendly（ArsenalStatics.cs:72）
            if (bSameActor) return ArsenalStatics.ETeamAttitude.Friendly;

            // 有交集即同队 Friendly（ArsenalStatics.cs:73 → IsSameTeam 的 HasAny）
            if (SharesAnyFaction(sourceFactions, targetFactions)) return ArsenalStatics.ETeamAttitude.Friendly;

            // 双方都没登记阵营 → Neutral：无阵营的道具/路人不会被全员自动敌对（ArsenalStatics.cs:75-82）
            bool sourceHasFaction = HasAnyFaction(sourceFactions);
            bool targetHasFaction = HasAnyFaction(targetFactions);
            if (!sourceHasFaction && !targetHasFaction) return ArsenalStatics.ETeamAttitude.Neutral;

            // 只要有一方登记了阵营而双方又不同队，维持 Hostile（ArsenalStatics.cs:76-84）
            return ArsenalStatics.ETeamAttitude.Hostile;
        }

        // =========================================================================
        // Rule B —— 阵营联盟图侧态度（NarrativeGameState 的三个态度查询的语义层）
        // =========================================================================

        /// <summary>
        /// 容器级态度聚合。对应 UE5 <c>ANarrativeGameState::GetFactionsAttitudeTowardsFactions</c>
        /// （NarrativeGameState.cpp:190-243）：任一 pair 敌对立刻敌对；
        /// 否则只要有 pair 友好就友好；全中性则中性。双方都空 ⇒ 中性。
        /// <para>部分重叠（{A,B} vs {B,C}）时 A↔C 的敌对记录会**盖掉** B↔B 的友好，
        /// 这正是"敌对优先"必须排在"友好优先"之前的原因。</para>
        /// </summary>
        public static ArsenalStatics.ETeamAttitude ComputeFactionsAttitude(
            IEnumerable<string> sourceFactions, IEnumerable<string> targetFactions, FactionAllianceLookup alliances)
        {
            if (sourceFactions == null || targetFactions == null) return ArsenalStatics.ETeamAttitude.Neutral;

            var sourceList = ToList(sourceFactions);
            var targetList = ToList(targetFactions);

            bool anyFriendly = false;
            for (int i = 0; i < sourceList.Count; i++)
            {
                for (int j = 0; j < targetList.Count; j++)
                {
                    var attitude = ComputeOneWayFactionAttitude(sourceList[i], targetList[j], alliances);
                    if (attitude == ArsenalStatics.ETeamAttitude.Hostile) return ArsenalStatics.ETeamAttitude.Hostile;
                    if (attitude == ArsenalStatics.ETeamAttitude.Friendly) anyFriendly = true;
                }
            }
            return anyFriendly ? ArsenalStatics.ETeamAttitude.Friendly : ArsenalStatics.ETeamAttitude.Neutral;
        }

        /// <summary>
        /// 单对阵营态度。对应 UE5 <c>GetFactionAttitudeTowardsFaction</c>（NarrativeGameState.cpp:246-255）：
        /// 先取 source→target，source 侧为 Neutral 时才回退看 target→source（不是"任一敌对即敌对"）。
        /// </summary>
        public static ArsenalStatics.ETeamAttitude ComputePairAttitude(
            string sourceFaction, string targetFaction, FactionAllianceLookup alliances)
        {
            var oneWay = ComputeOneWayFactionAttitude(sourceFaction, targetFaction, alliances);
            if (oneWay == ArsenalStatics.ETeamAttitude.Neutral)
            {
                return ComputeOneWayFactionAttitude(targetFaction, sourceFaction, alliances);
            }
            return oneWay;
        }

        /// <summary>
        /// 单向阵营态度。对应 UE5 <c>GetOneWayFactionAttitude</c>（NarrativeGameState.cpp:257-297）中端口已实现的分支：
        /// 任一名无效 ⇒ Neutral；同名 ⇒ Friendly；否则查联盟表，查不到 ⇒ Neutral。
        /// </summary>
        public static ArsenalStatics.ETeamAttitude ComputeOneWayFactionAttitude(
            string sourceFaction, string targetFaction, FactionAllianceLookup alliances)
        {
            if (string.IsNullOrEmpty(sourceFaction) || string.IsNullOrEmpty(targetFaction))
                return ArsenalStatics.ETeamAttitude.Neutral;

            if (string.Equals(sourceFaction, targetFaction, StringComparison.Ordinal))
                return ArsenalStatics.ETeamAttitude.Friendly;

            if (alliances != null)
            {
                return alliances(sourceFaction, targetFaction);
            }
            return ArsenalStatics.ETeamAttitude.Neutral;
        }

        // =========================================================================
        // Rule C —— 友伤否决门（UE5 NarrativeDamageExecCalc.cpp:52-67 的语义层）
        // =========================================================================

        /// <summary>
        /// 该态度是否构成友伤否决。UE5 口径：只有 <c>!bAllowFriendlyFire &amp;&amp; attitude == Friendly</c> 才否决；
        /// Neutral/Hostile 一律放行（否则中立路人打不了）。
        /// </summary>
        public static bool ShouldVetoFriendlyFire(ArsenalStatics.ETeamAttitude attitude, bool bAllowFriendlyFire)
        {
            return attitude == ArsenalStatics.ETeamAttitude.Friendly && !bAllowFriendlyFire;
        }

        /// <summary>
        /// 伤害入口的完整否决判定：同一 Actor（自伤/弹药自爆）**不进**友伤门（UE5 cpp:52 的
        /// <c>SourceActor != TargetActor</c>），其余按 <see cref="ComputeActorAttitude"/> +
        /// <see cref="ShouldVetoFriendlyFire"/>。
        /// </summary>
        public static bool ShouldVetoDamage(
            IEnumerable<string> sourceFactions, IEnumerable<string> targetFactions,
            bool bSameActor, bool bAllowFriendlyFire)
        {
            if (bSameActor) return false;
            var attitude = ComputeActorAttitude(sourceFactions, targetFactions, false);
            return ShouldVetoFriendlyFire(attitude, bAllowFriendlyFire);
        }

        /// <summary>连同态度一起回传否决结果，供伤害执行计算写日志/返回值使用。</summary>
        public static bool ShouldVetoDamage(
            IEnumerable<string> sourceFactions, IEnumerable<string> targetFactions,
            bool bSameActor, bool bAllowFriendlyFire, out ArsenalStatics.ETeamAttitude attitude)
        {
            attitude = bSameActor
                ? ArsenalStatics.ETeamAttitude.Friendly
                : ComputeActorAttitude(sourceFactions, targetFactions, false);

            // 自伤不进友伤门（UE5 cpp:52）
            if (bSameActor) return false;
            return ShouldVetoFriendlyFire(attitude, bAllowFriendlyFire);
        }

        // =========================================================================
        // 工具
        // =========================================================================

        private static HashSet<string> ToSet(IEnumerable<string> names)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (names != null)
            {
                foreach (var name in names)
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    set.Add(name);
                }
            }
            return set;
        }

        private static List<string> ToList(IEnumerable<string> names)
        {
            if (names == null) return EmptyNames;
            var list = new List<string>();
            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                list.Add(name);
            }
            return list;
        }
    }
}
