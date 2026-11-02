using Horizon.Orleans.Interface;
using Horizon.Game.Message.Network;

using Horizon.Model.GameModel;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 五行属性加成结构
    /// </summary>
    public class WuxingAttributeBonus
    {
        public float CritRateBonus { get; set; }
        public float PhysicalDamageBonus { get; set; }
        public float HealthRegenRate { get; set; }
        public float DodgeRateBonus { get; set; }
        public float DefenseBonus { get; set; }
        public float ShieldAmount { get; set; }
        public float BurnDamagePerTick { get; set; }
        public float FreezeChance { get; set; }
    }

    /// <summary>
    /// 战斗计算器 - 提供纯计算逻辑，不依赖Orleans基础设施
    /// </summary>
    public static class CombatCalculator
    {
        /// <summary>
        /// 计算五行相克乘数
        /// 1=金, 2=木, 3=水, 4=火, 5=土
        /// 相克关系：金克木、木克土、土克水、水克火、火克金
        /// </summary>
        public static float GetWuxingMultiplier(int attackerElement, int defenderElement)
        {
            if (attackerElement == 0 || defenderElement == 0)
                return 1.0f;

            // 相克：伤害增加25%
            if ((attackerElement == 1 && defenderElement == 2) || // 金克木
                (attackerElement == 2 && defenderElement == 5) || // 木克土
                (attackerElement == 5 && defenderElement == 3) || // 土克水
                (attackerElement == 3 && defenderElement == 4) || // 水克火
                (attackerElement == 4 && defenderElement == 1))   // 火克金
            {
                return 1.25f;
            }

            // 被克：伤害减少20%（保持原始实现逻辑的向后兼容）
            if ((attackerElement == 1 && defenderElement == 4) || // 金被火克
                (attackerElement == 4 && defenderElement == 2) || // 火攻木（原始逻辑，标准五行应为火被水克）
                (attackerElement == 2 && defenderElement == 3) || // 木被水克
                (attackerElement == 3 && defenderElement == 5) || // 水被土克
                (attackerElement == 5 && defenderElement == 1))   // 土被金克
            {
                return 0.8f;
            }

            return 1.0f;
        }

        /// <summary>
        /// 计算防御减免系数
        /// 公式: defense / (defense + 100)
        /// </summary>
        public static float CalculateDefenseReduction(float defense)
        {
            if (defense <= 0) return 0f;
            return defense / (defense + 100f);
        }

        /// <summary>
        /// 计算五行伤害（包含防御减免）
        /// </summary>
        public static float CalculateWuxingDamage(float baseDamage, int attackerElement, int defenderElement, float defenderDefense)
        {
            float multiplier = GetWuxingMultiplier(attackerElement, defenderElement);
            float finalDamage = baseDamage * multiplier;
            float defenseReduction = CalculateDefenseReduction(defenderDefense);
            finalDamage *= (1 - defenseReduction);
            return finalDamage;
        }

        /// <summary>
        /// 计算基础伤害
        /// </summary>
        public static float CalculateBaseDamage(float attackPower, float rawDamage)
        {
            return attackPower * 0.5f + rawDamage;
        }

        /// <summary>
        /// 应用暴击伤害
        /// </summary>
        public static float ApplyCriticalDamage(float damage, bool isCritical, float critMultiplier = 1.5f)
        {
            return isCritical ? damage * critMultiplier : damage;
        }

        /// <summary>
        /// 确保伤害后生命值不低于0
        /// </summary>
        public static float ClampHealth(float currentHealth, float damage)
        {
            return Math.Max(0, currentHealth - damage);
        }

        /// <summary>
        /// 计算复活后的生命值
        /// </summary>
        /// <param name="maxHealth">最大生命值</param>
        /// <param name="resurrectType">复活类型：1=完全复活，其他=半血复活</param>
        public static float CalculateResurrectHealth(float maxHealth, int resurrectType)
        {
            float restoreRatio = resurrectType == 1 ? 1.0f : 0.5f;
            return maxHealth * restoreRatio;
        }

        /// <summary>
        /// 判断是否闪避
        /// </summary>
        /// <param name="dodgeRate">闪避率 (0-1)</param>
        /// <returns>是否闪避成功</returns>
        public static bool RollDodge(float dodgeRate)
        {
            if (dodgeRate <= 0f) return false;
            if (dodgeRate >= 1f) return true;
            return Random.Shared.NextDouble() < dodgeRate;
        }

        /// <summary>
        /// 计算格挡减伤
        /// 格挡成功时减免固定比例伤害
        /// </summary>
        /// <param name="damage">原始伤害</param>
        /// <param name="blockRate">格挡率 (0-1)</param>
        /// <param name="blockReduction">格挡减伤比例，默认50%</param>
        /// <returns>格挡后的伤害和是否格挡成功</returns>
        public static (float damage, bool isBlocked) ApplyBlock(float damage, float blockRate, float blockReduction = 0.5f)
        {
            if (blockRate <= 0f) return (damage, false);
            bool blocked = blockRate >= 1f || Random.Shared.NextDouble() < blockRate;
            if (blocked)
            {
                return (damage * (1f - blockReduction), true);
            }
            return (damage, false);
        }

        /// <summary>
        /// 检查技能冷却是否已结束
        /// </summary>
        /// <param name="lastCastTime">上次施放时间</param>
        /// <param name="cooldownMs">冷却时间（毫秒）</param>
        /// <returns>是否可以施放</returns>
        public static bool IsSkillReady(DateTime lastCastTime, long cooldownMs)
        {
            if (cooldownMs <= 0) return true;
            return (DateTime.Now - lastCastTime).TotalMilliseconds >= cooldownMs;
        }

        /// <summary>
        /// 获取技能剩余冷却时间（秒）
        /// </summary>
        /// <param name="lastCastTime">上次施放时间</param>
        /// <param name="cooldownMs">冷却时间（毫秒）</param>
        /// <returns>剩余冷却时间（秒），0表示已就绪</returns>
        public static float GetRemainingCooldown(DateTime lastCastTime, long cooldownMs)
        {
            if (cooldownMs <= 0) return 0f;
            var elapsed = (DateTime.Now - lastCastTime).TotalMilliseconds;
            var remaining = cooldownMs - elapsed;
            return remaining > 0 ? (float)(remaining / 1000.0) : 0f;
        }

        /// <summary>
        /// 获取五行属性加成
        /// 1=金, 2=木, 3=水, 4=火, 5=土
        /// </summary>
        /// <param name="element">五行元素类型</param>
        /// <param name="basePower">基础力量</param>
        /// <returns>五行属性加成</returns>
        public static WuxingAttributeBonus GetWuxingAttributeBonus(int element, float basePower)
        {
            var bonus = new WuxingAttributeBonus();

            switch (element)
            {
                case 1: // 金
                    bonus.CritRateBonus = basePower * 0.05f;
                    bonus.PhysicalDamageBonus = basePower * 0.15f;
                    break;
                case 2: // 木
                    bonus.HealthRegenRate = basePower * 0.02f;
                    break;
                case 3: // 水
                    bonus.DodgeRateBonus = basePower * 0.04f;
                    bonus.FreezeChance = basePower * 0.03f;
                    break;
                case 4: // 火
                    bonus.BurnDamagePerTick = basePower * 0.10f;
                    break;
                case 5: // 土
                    bonus.DefenseBonus = basePower * 0.12f;
                    bonus.ShieldAmount = basePower * 0.08f;
                    break;
            }

            return bonus;
        }

        /// <summary>一次循环节推进的结果（纯函数，便于回归用例逐值核对）。</summary>
        public readonly record struct WuxingCycleStep(
            int Stack, float Multiplier, bool Completed, int CompletionCount, int LastElement, DateTime ExpireAt);

        /// <summary>
        /// 五行循环节推进一步（方案 §5.1 的全部规则，无任何 Grain/存储依赖）：
        /// 窗口超时重新起链；<b>起链那一式就占周天的第一行</b>，之后打出上一行的"子行"为顺生 → 层数 +1；
        /// 同属与逆生都不掉层，只把链移到当前行并刷新窗口；满 <paramref name="maxStack"/> 层即周天圆满
        /// （清零、计数 +1、那一击再乘 <paramref name="completionMultiplier"/>）；无属性式（0）完全不碰链。
        /// </summary>
        public static WuxingCycleStep StepWuxingCycle(
            int stack, int lastElement, DateTime expireAt, int completionCount,
            int element, DateTime now,
            int windowSeconds = 3, int maxStack = 5,
            float bonusPerStack = 0.03f, float completionMultiplier = 1.25f)
        {
            if (element <= 0)
                return new WuxingCycleStep(stack, 1f + bonusPerStack * stack, false, completionCount, lastElement, expireAt);

            var current = expireAt != default && now > expireAt ? 0 : stack;
            var generated = lastElement > 0 && GenerateOf(lastElement) == element;
            // 第一式必须自己占一层：节数数的是"本轮周天里已走过几行"，起手那行就是其中一行。
            // 少这一层，按相生序打完五行只到 4 层，§5.1 的设计意图（周天=五行各一式、D4 强制兼修）永远凑不成。
            var isChainAnchor = current == 0;
            if (generated || isChainAnchor) current++;

            var completed = false;
            if (current >= maxStack)
            {
                current = 0;
                completionCount++;
                completed = true;
            }

            float multiplier = 1f + bonusPerStack * current;
            if (completed) multiplier *= completionMultiplier;

            return new WuxingCycleStep(current, multiplier, completed, completionCount, element, now.AddSeconds(windowSeconds));
        }

        /// <summary>
        /// 五行阶梯的基座系数与倍率。判定只看 <c>skill_grade</c>：有阶位才说明这一行的
        /// damage_multiplier 是设计过的阶梯值，否则该列仍是默认的 1.0，乘上去等于把老技能砍到 0.25。
        /// </summary>
        public static float SkillLadderFactor(SkillTemplateEntity? template, out float multiplier)
        {
            multiplier = 1f;
            if (template == null || template.SkillGrade <= 0) return 0.6f;

            multiplier = template.DamageMultiplier > 0f ? (float)template.DamageMultiplier : 1f;
            return 0.25f;
        }

        /// <summary>
        /// 五行相克（有向）：本行克到的那一行。1金 2木 3水 4火 5土 →
        /// 金克木、木克土、土克水、水克火、火克金（与客户端 WuxingRules.GetCounterElement 同表）。
        /// </summary>
        public static int CounteredElement(int element) => element switch
        {
            1 => 2,   // 金克木
            2 => 5,   // 木克土
            5 => 3,   // 土克水
            3 => 4,   // 水克火
            4 => 1,   // 火克金
            _ => 0,
        };

        /// <summary>
        /// 守方这一击的护体行（§5.3）：玩家指定的镇岳行优先，未指定（0）才退化为防御向量最高行。
        /// 做成纯函数是因为"指定行必须压过自动行"这件事只有这一处口径，客户端与服务端都得照它来。
        /// </summary>
        public static int ResolveGuardRow(int guardElement, int dominantDefenseRow) =>
            guardElement > 0 ? guardElement : dominantDefenseRow;

        /// <summary>攻方这一行是否克得住守方那一行（破势判定用）。</summary>
        public static bool Counters(int attackElement, int defendElement) =>
            attackElement > 0 && defendElement > 0 && CounteredElement(attackElement) == defendElement;

        /// <summary>
        /// 五行相生关系对集合（无序）
        /// 金生水(1,3)、水生木(3,2)、木生火(2,4)、火生土(4,5)、土生金(5,1)
        /// </summary>
        private static readonly HashSet<(int, int)> WuxingSynergyPairs = new()
        {
            (1, 3), (3, 1), // 金生水
            (3, 2), (2, 3), // 水生木
            (2, 4), (4, 2), // 木生火
            (4, 5), (5, 4), // 火生土
            (5, 1), (1, 5), // 土生金
        };

        /// <summary>
        /// 五行相生（有向）：本行"生"出的下一行。1金 2木 3水 4火 5土 →
        /// 金生水、水生木、木生火、火生土、土生金。
        /// <para>
        /// 与客户端 <c>WuxingRules.GetGenerateElement</c> 同表。<see cref="WuxingSynergyPairs"/>
        /// 是无序对集合，判不出方向，所以循环节这种"必须按顺序续链"的判定不能复用它的表。
        /// </para>
        /// </summary>
        public static int GenerateOf(int element) => element switch
        {
            1 => 3,   // 金生水
            3 => 2,   // 水生木
            2 => 4,   // 木生火
            4 => 5,   // 火生土
            5 => 1,   // 土生金
            _ => 0,
        };

        /// <summary>
        /// 获取五行相生协同乘数
        /// 相生关系：金生水、水生木、木生火、火生土、土生金
        /// </summary>
        /// <param name="element1">第一个元素</param>
        /// <param name="element2">第二个元素</param>
        /// <returns>协同乘数</returns>
        public static float GetWuxingSynergyMultiplier(int element1, int element2)
        {
            if (element1 == element2 && element1 > 0)
                return 1.10f;

            if (WuxingSynergyPairs.Contains((element1, element2)))
                return 1.15f;

            return 1.0f;
        }

        /// <summary>
        /// 计算能量恢复
        /// </summary>
        /// <param name="maxEnergy">最大能量</param>
        /// <param name="currentEnergy">当前能量</param>
        /// <param name="regenRate">恢复速率，默认2%</param>
        /// <returns>恢复后的能量值</returns>
        public static float CalculateEnergyRecovery(float maxEnergy, float currentEnergy, float regenRate = 0.02f)
        {
            float recovery = maxEnergy * regenRate;
            return Math.Min(currentEnergy + recovery, maxEnergy);
        }

        /// <summary>
        /// 检查全局冷却是否就绪
        /// </summary>
        /// <param name="lastActionTime">上次动作时间</param>
        /// <param name="gcdMs">全局冷却时间（毫秒），默认1000</param>
        /// <returns>是否已就绪</returns>
        public static bool IsGlobalCooldownReady(DateTime lastActionTime, long gcdMs = 1000)
        {
            if (gcdMs <= 0) return true;
            return (DateTime.Now - lastActionTime).TotalMilliseconds >= gcdMs;
        }

        /// <summary>
        /// 聚合伤害统计
        /// </summary>
        public static DamageAggregateStats AggregateDamageStats(List<CombatLogEntry> combatLog, ulong playerId)
        {
            var stats = new DamageAggregateStats { PlayerId = playerId };
            if (combatLog == null || combatLog.Count == 0)
                return stats;

            DateTime? firstAttackTime = null;
            DateTime? lastAttackTime = null;

            foreach (var entry in combatLog)
            {
                if (entry.AttackerId == playerId)
                {
                    if (entry.LogType == CombatLogType.Attack || entry.LogType == CombatLogType.SkillCast)
                    {
                        stats.TotalAttacks++;

                        if (!firstAttackTime.HasValue)
                            firstAttackTime = entry.Timestamp;
                        lastAttackTime = entry.Timestamp;

                        if (!entry.IsDodged && entry.IsBlocked)
                        {
                            stats.TotalHits++;
                            stats.BlockedAttacks++;
                            stats.TotalDamageDealt += entry.DamageDealt;
                            if (entry.DamageDealt > stats.MaxSingleDamage)
                                stats.MaxSingleDamage = entry.DamageDealt;
                        }
                        else if (!entry.IsDodged)
                        {
                            stats.TotalHits++;
                            stats.TotalDamageDealt += entry.DamageDealt;
                            if (entry.IsCritical)
                                stats.CriticalHits++;
                            if (entry.DamageDealt > stats.MaxSingleDamage)
                                stats.MaxSingleDamage = entry.DamageDealt;
                        }
                    }
                    else if (entry.LogType == CombatLogType.Death)
                    {
                        stats.KillCount++;
                    }
                }

                if (entry.DefenderId == playerId)
                {
                    if (entry.LogType == CombatLogType.Attack || entry.LogType == CombatLogType.SkillCast)
                    {
                        if (entry.IsDodged)
                        {
                            stats.DodgedAttacks++;
                        }
                        else
                        {
                            stats.TotalDamageReceived += entry.DamageDealt;
                        }
                    }
                    else if (entry.LogType == CombatLogType.Death)
                    {
                        stats.DeathCount++;
                    }
                }
            }

            if (stats.TotalHits > 0)
                stats.AverageDamagePerHit = stats.TotalDamageDealt / stats.TotalHits;

            if (firstAttackTime.HasValue && lastAttackTime.HasValue)
            {
                var duration = (float)(lastAttackTime.Value - firstAttackTime.Value).TotalSeconds;
                stats.DPS = duration > 0 ? stats.TotalDamageDealt / duration : stats.TotalDamageDealt;
            }

            return stats;
        }

        /// <summary>
        /// 构建战斗回放数据
        /// </summary>
        public static CombatReplayData BuildReplayData(List<CombatLogEntry> combatLog)
        {
            var replay = new CombatReplayData();
            if (combatLog == null || combatLog.Count == 0)
                return replay;

            replay.ReplayId = Guid.NewGuid().ToString("N");
            replay.StartTime = combatLog[0].Timestamp;
            replay.EndTime = combatLog[^1].Timestamp;
            replay.TotalDuration = (float)(replay.EndTime - replay.StartTime).TotalSeconds;

            var participants = new HashSet<ulong>();

            for (int i = 0; i < combatLog.Count; i++)
            {
                var entry = combatLog[i];
                participants.Add(entry.AttackerId);
                participants.Add(entry.DefenderId);

                replay.Frames.Add(new CombatReplayFrame
                {
                    FrameIndex = i,
                    Timestamp = entry.Timestamp,
                    ActionType = entry.LogType,
                    ActorId = entry.AttackerId,
                    TargetId = entry.DefenderId,
                    SkillId = entry.SkillId,
                    DamageDealt = entry.DamageDealt,
                    ElementType = entry.ElementType,
                    IsCritical = entry.IsCritical,
                    IsDodged = entry.IsDodged,
                    IsBlocked = entry.IsBlocked
                });
            }

            replay.Participants = participants.ToList();
            return replay;
        }

        /// <summary>
        /// 计算组队五行匹配加成
        /// 规则:
        /// - 相生对越多,加成越高
        /// - 五行齐全额外20%加成
        /// - 基础计算: 每对相生关系 +5% 加成
        /// </summary>
        public static float CalculateTeamWuxingSynergy(List<int> teamElements)
        {
            if (teamElements == null || teamElements.Count == 0)
                return 0f;

            var uniqueElements = new HashSet<int>(teamElements.Where(e => e >= 1 && e <= 5));
            if (uniqueElements.Count == 0)
                return 0f;

            // 相生关系 (ordered pairs)
            var synergyPairs = new (int, int)[]
            {
                (1, 3), // 金生水
                (3, 2), // 水生木
                (2, 4), // 木生火
                (4, 5), // 火生土
                (5, 1), // 土生金
            };

            int synergyCount = 0;
            foreach (var (a, b) in synergyPairs)
            {
                if (uniqueElements.Contains(a) && uniqueElements.Contains(b))
                    synergyCount++;
            }

            float bonus = synergyCount * 0.05f;

            // 五行齐全额外20%加成
            if (uniqueElements.Count == 5)
                bonus += 0.20f;

            return bonus;
        }

        /// <summary>
        /// 五行共鸣技能触发
        /// 当组队成员五行元素满足特定条件时，触发共鸣技能
        /// 规则:
        /// - 至少2个不同五行元素
        /// - 相生对(金生水(1→3)、水生木(3→2)、木生火(2→4)、火生土(4→5)、土生金(5→1))越多，共鸣等级越高
        /// - 等级1(基础共鸣): 2+种元素, 1-2对相生 → 10%伤害/5%防御
        /// - 等级2(高级共鸣): 3+种元素, 3-4对相生 → 20%伤害/10%防御
        /// - 等级3(混沌共鸣): 5种元素齐全(5对相生) → 35%伤害/20%防御
        /// </summary>
        public static WuxingResonanceResult CalculateWuxingResonance(List<int>? teamElements)
        {
            var result = new WuxingResonanceResult
            {
                ResonanceLevel = 0,
                Description = "无共鸣",
                DamageBonus = 0f,
                DefenseBonus = 0f
            };

            if (teamElements == null || teamElements.Count == 0)
                return result;

            var uniqueElements = new HashSet<int>(teamElements.Where(e => e >= 1 && e <= 5));
            if (uniqueElements.Count < 2)
                return result;

            result.ResonanceElements = uniqueElements.ToList();

            // Count synergy pairs
            var synergyPairs = new (int, int)[]
            {
                (1, 3), // 金生水
                (3, 2), // 水生木
                (2, 4), // 木生火
                (4, 5), // 火生土
                (5, 1), // 土生金
            };

            int synergyCount = 0;
            foreach (var (a, b) in synergyPairs)
            {
                if (uniqueElements.Contains(a) && uniqueElements.Contains(b))
                    synergyCount++;
            }

            if (uniqueElements.Count == 5)
            {
                // Level 3: 混沌共鸣 - all 5 elements present
                result.ResonanceLevel = 3;
                result.Description = "混沌共鸣";
                result.DamageBonus = 0.35f;
                result.DefenseBonus = 0.20f;
            }
            else if (uniqueElements.Count >= 3 && synergyCount >= 3)
            {
                // Level 2: 高级共鸣
                result.ResonanceLevel = 2;
                result.Description = "高级共鸣";
                result.DamageBonus = 0.20f;
                result.DefenseBonus = 0.10f;
            }
            else
            {
                // Level 1: 基础共鸣 - at least 2 unique elements
                result.ResonanceLevel = 1;
                result.Description = "基础共鸣";
                result.DamageBonus = 0.10f;
                result.DefenseBonus = 0.05f;
            }

            return result;
        }
    }

    /// <summary>
    /// 五行共鸣结果
    /// </summary>
    public class WuxingResonanceResult
    {
        /// <summary>共鸣等级 (0=无, 1=基础, 2=高级, 3=混沌)</summary>
        public int ResonanceLevel { get; set; }
        /// <summary>共鸣效果描述</summary>
        public string Description { get; set; } = "";
        /// <summary>额外伤害加成百分比 (0.0-1.0)</summary>
        public float DamageBonus { get; set; }
        /// <summary>额外防御加成百分比 (0.0-1.0)</summary>
        public float DefenseBonus { get; set; }
        /// <summary>触发共鸣的元素列表</summary>
        public List<int> ResonanceElements { get; set; } = new();
    }

    /// <summary>
    /// 伤害统计聚合
    /// </summary>
    public class DamageAggregateStats
    {
        public ulong PlayerId { get; set; }
        public float TotalDamageDealt { get; set; }
        public float TotalDamageReceived { get; set; }
        public int TotalAttacks { get; set; }
        public int TotalHits { get; set; }
        public int CriticalHits { get; set; }
        public int DodgedAttacks { get; set; }
        public int BlockedAttacks { get; set; }
        public int KillCount { get; set; }
        public int DeathCount { get; set; }
        public float MaxSingleDamage { get; set; }
        public float AverageDamagePerHit { get; set; }
        public float DPS { get; set; }
    }

    /// <summary>
    /// 战斗回放帧
    /// </summary>
    public class CombatReplayFrame
    {
        public int FrameIndex { get; set; }
        public DateTime Timestamp { get; set; }
        public CombatLogType ActionType { get; set; }
        public ulong ActorId { get; set; }
        public ulong TargetId { get; set; }
        public int SkillId { get; set; }
        public float DamageDealt { get; set; }
        public int ElementType { get; set; }
        public bool IsCritical { get; set; }
        public bool IsDodged { get; set; }
        public bool IsBlocked { get; set; }
    }

    /// <summary>
    /// 战斗回放数据
    /// </summary>
    public class CombatReplayData
    {
        public string ReplayId { get; set; } = "";
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public List<ulong> Participants { get; set; } = new();
        public List<CombatReplayFrame> Frames { get; set; } = new();
        public float TotalDuration { get; set; }
    }
}
