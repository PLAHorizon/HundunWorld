namespace Horizon.Orleans.Grains
{
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

            // 被克：伤害减少20%
            if ((attackerElement == 1 && defenderElement == 4) || // 金被火克
                (attackerElement == 4 && defenderElement == 2) || // 火被木克（实际：木克火不在五行中，此处为代码原始逻辑）
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
    }
}
