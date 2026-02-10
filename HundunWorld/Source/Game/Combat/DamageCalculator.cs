using FlaxEngine;
using System;
using System.Collections.Generic;
using HundunWorld.Game.ECS.Components;
using Horizon.Game.Message.Enums;
using Game.Character.Attributes;

namespace HundunWorld.Game.Combat
{
    /// <summary>
    /// 战斗伤害计算器
    /// 负责计算各种类型的伤害，包括物理、法术、真实伤害和五行元素伤害
    /// </summary>
    public class DamageCalculator
    {
        private static DamageCalculator _instance;
        public static DamageCalculator Instance => _instance ??= new DamageCalculator();

        // 随机数生成器
        private readonly Random _random = new Random();

        // 五行相克关系表
        private readonly Dictionary<WuxingElement, WuxingElement> _counterRelations;
        private readonly Dictionary<WuxingElement, WuxingElement> _generateRelations;

        private DamageCalculator()
        {
            // 初始化五行相克关系：金克木、木克土、土克水、水克火、火克金
            _counterRelations = new Dictionary<WuxingElement, WuxingElement>
            {
                { WuxingElement.Metal, WuxingElement.Wood },
                { WuxingElement.Wood, WuxingElement.Earth },
                { WuxingElement.Earth, WuxingElement.Water },
                { WuxingElement.Water, WuxingElement.Fire },
                { WuxingElement.Fire, WuxingElement.Metal }
            };

            // 初始化五行相生关系：金生水、水生木、木生火、火生土、土生金
            _generateRelations = new Dictionary<WuxingElement, WuxingElement>
            {
                { WuxingElement.Metal, WuxingElement.Water },
                { WuxingElement.Water, WuxingElement.Wood },
                { WuxingElement.Wood, WuxingElement.Fire },
                { WuxingElement.Fire, WuxingElement.Earth },
                { WuxingElement.Earth, WuxingElement.Metal }
            };
        }

        /// <summary>
        /// 计算最终伤害
        /// </summary>
        /// <param name="attackerStats">攻击者属性</param>
        /// <param name="defenderStats">防御者属性</param>
        /// <param name="skill">使用的技能</param>
        /// <param name="previousSkill">上一个技能（用于五行相生计算）</param>
        /// <returns>伤害计算结果</returns>
        public DamageCalculationResult CalculateDamage(
            CharacterStats attackerStats,
            CharacterStats defenderStats,
            SkillInfo skill,
            SkillInfo previousSkill = null)
        {
            var result = new DamageCalculationResult();
            
            try
            {
                // 1. 计算基础伤害
                float baseDamage = CalculateBaseDamage(attackerStats, skill);
                result.BaseDamage = baseDamage;

                // 2. 应用伤害类型修正
                float damageWithType = ApplyDamageTypeModifiers(baseDamage, attackerStats, defenderStats, skill);
                result.DamageAfterTypeModifiers = damageWithType;

                // 3. 应用五行相克相生
                float damageWithWuxing = ApplyWuxingModifiers(damageWithType, attackerStats, defenderStats, skill, previousSkill);
                result.DamageAfterWuxing = damageWithWuxing;

                // 4. 应用暴击计算
                var criticalResult = CalculateCriticalHit(damageWithWuxing, attackerStats, defenderStats);
                result.IsCritical = criticalResult.IsCritical;
                result.CriticalMultiplier = criticalResult.Multiplier;
                float damageWithCritical = criticalResult.Damage;
                result.DamageAfterCritical = damageWithCritical;

                // 5. 应用防御减免
                float finalDamage = ApplyDefenseReduction(damageWithCritical, attackerStats, defenderStats, skill);
                result.FinalDamage = Math.Max(0, finalDamage); // 确保不会出现负伤害

                // 6. 应用随机波动
                result.FinalDamage = ApplyRandomVariance(result.FinalDamage);

                // 7. 记录计算详情
                result.CalculationDetails = GenerateCalculationLog(attackerStats, defenderStats, skill, result);

                Debug.Log($"[DamageCalculator] 伤害计算完成: {result.BaseDamage:F1} -> {result.FinalDamage:F1}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DamageCalculator] 伤害计算出错: {ex.Message}");
                result.FinalDamage = 0;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 计算基础伤害
        /// </summary>
        private float CalculateBaseDamage(CharacterStats attackerStats, SkillInfo skill)
        {
            float baseDamage = 0;

            switch (skill.DamageType)
            {
                case DamageType.Physical:
                    baseDamage = attackerStats.Attack * skill.DamageMultiplier;
                    break;
                case DamageType.Magic:
                    baseDamage = attackerStats.MagicAttack * skill.DamageMultiplier;
                    break;
                case DamageType.Fire:
                    // 五行技能使用对应属性攻击力
                    baseDamage = GetElementalAttack(attackerStats, skill.Element) * skill.DamageMultiplier;
                    break;
                case DamageType.True:
                    // 真实伤害通常基于固定值或百分比
                    baseDamage = attackerStats.Level * 10 * skill.DamageMultiplier;
                    break;
            }

            return Math.Max(baseDamage, 1); // 最小伤害为1
        }

        /// <summary>
        /// 应用伤害类型修正
        /// </summary>
        private float ApplyDamageTypeModifiers(float damage, CharacterStats attacker, CharacterStats defender, SkillInfo skill)
        {
            float modifiedDamage = damage;

            // 应用攻击者增益
            switch (skill.DamageType)
            {
                case DamageType.Physical:
                    modifiedDamage *= (1 + attacker.PhysicalDamageBonus);
                    break;
                case DamageType.Magic:
                    modifiedDamage *= (1 + attacker.MagicDamageBonus);
                    break;
                case DamageType.Fire:
                    modifiedDamage *= (1 + attacker.ElementalDamageBonus);
                    break;
            }

            // 应用防御者减益
            switch (skill.DamageType)
            {
                case DamageType.Physical:
                    modifiedDamage *= (1 - Math.Min(defender.PhysicalResistance, 0.75f)); // 最多减免75%
                    break;
                case DamageType.Magic:
                    modifiedDamage *= (1 - Math.Min(defender.MagicResistance, 0.75f));
                    break;
                case DamageType.Fire:
                    modifiedDamage *= (1 - Math.Min(defender.ElementalResistance, 0.75f));
                    break;
            }

            return modifiedDamage;
        }

        /// <summary>
        /// 应用五行相克相生修正
        /// </summary>
        private float ApplyWuxingModifiers(float damage, CharacterStats attacker, CharacterStats defender, SkillInfo skill, SkillInfo previousSkill)
        {
            if (skill.Element == WuxingElement.None)
                return damage;

            float modifiedDamage = damage;

            // 五行相克：攻击克制的元素增加50%伤害
            if (_counterRelations.TryGetValue(skill.Element, out var counteredElement) && 
                defender.Element == counteredElement)
            {
                modifiedDamage *= 1.5f;
            }
            // 被克制：减少30%伤害
            else if (_counterRelations.TryGetValue(defender.Element, out var counterElement) && 
                     counterElement == skill.Element)
            {
                modifiedDamage *= 0.7f;
            }

            // 五行相生：如果连续使用相生技能，增加30%伤害
            if (previousSkill != null && previousSkill.Element != WuxingElement.None)
            {
                if (_generateRelations.TryGetValue(previousSkill.Element, out var generatedElement) && 
                    generatedElement == skill.Element)
                {
                    modifiedDamage *= 1.3f;
                }
            }

            return modifiedDamage;
        }

        /// <summary>
        /// 计算暴击
        /// </summary>
        private CriticalHitResult CalculateCriticalHit(float damage, CharacterStats attacker, CharacterStats defender)
        {
            var result = new CriticalHitResult
            {
                IsCritical = false,
                Multiplier = 1.0f,
                Damage = damage
            };

            // 暴击判定
            float critChance = attacker.CriticalRate - defender.CriticalResistance;
            critChance = Math.Max(0, Math.Min(critChance, 1)); // 限制在0-1之间

            if (_random.NextSingle() < critChance)
            {
                result.IsCritical = true;
                result.Multiplier = 1.5f + attacker.CriticalDamage; // 基础150% + 额外暴击伤害
                result.Damage = damage * result.Multiplier;
            }

            return result;
        }

        /// <summary>
        /// 应用防御减免
        /// </summary>
        private float ApplyDefenseReduction(float damage, CharacterStats attacker, CharacterStats defender, SkillInfo skill)
        {
            float reducedDamage = damage;

            switch (skill.DamageType)
            {
                case DamageType.Physical:
                    // 物理伤害减免公式：damage * (100 / (100 + defense))
                    reducedDamage = damage * (100.0f / (100.0f + defender.Defense));
                    break;
                case DamageType.Magic:
                    // 法术伤害减免公式
                    reducedDamage = damage * (100.0f / (100.0f + defender.MagicDefense));
                    break;
                case DamageType.Fire:
                    // 元素伤害减免
                    reducedDamage = damage * (100.0f / (100.0f + defender.ElementalDefense));
                    break;
                case DamageType.True:
                    // 真实伤害不受防御影响
                    break;
            }

            return reducedDamage;
        }

        /// <summary>
        /// 应用随机波动（±10%）
        /// </summary>
        private float ApplyRandomVariance(float damage)
        {
            float variance = 0.1f; // ±10%
            float randomFactor = 1.0f + (_random.NextSingle() - 0.5f) * 2 * variance;
            return damage * randomFactor;
        }

        /// <summary>
        /// 获取对应元素的攻击力
        /// </summary>
        private float GetElementalAttack(CharacterStats stats, WuxingElement element)
        {
            return element switch
            {
                WuxingElement.Metal => stats.MetalAttack,
                WuxingElement.Wood => stats.WoodAttack,
                WuxingElement.Water => stats.WaterAttack,
                WuxingElement.Fire => stats.FireAttack,
                WuxingElement.Earth => stats.EarthAttack,
                _ => stats.Attack
            };
        }

        /// <summary>
        /// 生成计算日志
        /// </summary>
        private string GenerateCalculationLog(CharacterStats attacker, CharacterStats defender, SkillInfo skill, DamageCalculationResult result)
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine($"=== 伤害计算详情 ===");
            log.AppendLine($"攻击者: {attacker.Name} (等级{attacker.Level})");
            log.AppendLine($"防御者: {defender.Name} (等级{defender.Level})");
            log.AppendLine($"技能: {skill.Name} ({skill.Element}属性)");
            log.AppendLine($"基础伤害: {result.BaseDamage:F1}");
            log.AppendLine($"类型修正后: {result.DamageAfterTypeModifiers:F1}");
            log.AppendLine($"五行修正后: {result.DamageAfterWuxing:F1}");
            log.AppendLine($"暴击: {(result.IsCritical ? $"是 (x{result.CriticalMultiplier:F1})" : "否")} → {result.DamageAfterCritical:F1}");
            log.AppendLine($"最终伤害: {result.FinalDamage:F1}");
            return log.ToString();
        }

        /// <summary>
        /// 获取被克制的元素
        /// </summary>
        public WuxingElement GetCounterElement(WuxingElement element)
        {
            return _counterRelations.TryGetValue(element, out var countered) ? countered : WuxingElement.None;
        }

        /// <summary>
        /// 获取相生的元素
        /// </summary>
        public WuxingElement GetGenerateElement(WuxingElement element)
        {
            return _generateRelations.TryGetValue(element, out var generated) ? generated : WuxingElement.None;
        }
    }

    /// <summary>
    /// 角色属性信息
    /// </summary>
    public class CharacterStats
    {
        public string Name { get; set; }
        public int Level { get; set; }
        
        // 基础属性
        public float Attack { get; set; }          // 物理攻击力
        public float MagicAttack { get; set; }     // 法术攻击力
        public float Defense { get; set; }         // 物理防御力
        public float MagicDefense { get; set; }    // 法术防御力
        
        // 五行攻击力
        public float MetalAttack { get; set; }
        public float WoodAttack { get; set; }
        public float WaterAttack { get; set; }
        public float FireAttack { get; set; }
        public float EarthAttack { get; set; }
        
        // 五行防御力
        public float MetalDefense { get; set; }
        public float WoodDefense { get; set; }
        public float WaterDefense { get; set; }
        public float FireDefense { get; set; }
        public float EarthDefense { get; set; }
        
        // 伤害加成
        public float PhysicalDamageBonus { get; set; }
        public float MagicDamageBonus { get; set; }
        public float ElementalDamageBonus { get; set; }
        
        // 伤害减免
        public float PhysicalResistance { get; set; }
        public float MagicResistance { get; set; }
        public float ElementalResistance { get; set; }
        public float ElementalDefense { get; set; }
        
        // 暴击相关
        public float CriticalRate { get; set; }
        public float CriticalDamage { get; set; }
        public float CriticalResistance { get; set; }
        
        // 五行属性
        public WuxingElement Element { get; set; }
    }

    /// <summary>
    /// 技能信息
    /// </summary>
    public class SkillInfo
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public SkillType Type { get; set; }
        public Horizon.Game.Message.Enums.DamageType DamageType { get; set; }
        public WuxingElement Element { get; set; }
        public float DamageMultiplier { get; set; }
        public float BaseDamage { get; set; }
        public float EnergyCost { get; set; }
        public float Cooldown { get; set; }
        public List<int> Effects { get; set; } = new List<int>();
        public List<int> SelfEffects { get; set; } = new List<int>();
        public float Range { get; set; }
    }

    /// <summary>
    /// 伤害计算结果
    /// </summary>
    public class DamageCalculationResult
    {
        public float BaseDamage { get; set; }
        public float DamageAfterTypeModifiers { get; set; }
        public float DamageAfterWuxing { get; set; }
        public bool IsCritical { get; set; }
        public float CriticalMultiplier { get; set; }
        public float DamageAfterCritical { get; set; }
        public float FinalDamage { get; set; }
        public string CalculationDetails { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// 暴击计算结果
    /// </summary>
    public class CriticalHitResult
    {
        public bool IsCritical { get; set; }
        public float Multiplier { get; set; }
        public float Damage { get; set; }
    }
}