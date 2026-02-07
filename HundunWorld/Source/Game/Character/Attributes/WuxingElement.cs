using System;

namespace Game.Character.Attributes
{
    /// <summary>
    /// 五行元素枚举
    /// 定义游戏中的五行属性：金、木、水、火、土
    /// </summary>
    public enum WuxingElement
    {
        /// <summary>无属性</summary>
        None = 0,
        
        /// <summary>金 - 锋锐、坚硬、破甲</summary>
        Metal = 1,
        
        /// <summary>木 - 生长、韧性、持续恢复</summary>
        Wood = 2,
        
        /// <summary>水 - 流动、柔和、减速</summary>
        Water = 3,
        
        /// <summary>火 - 炽热、爆裂、范围伤害</summary>
        Fire = 4,
        
        /// <summary>土 - 厚重、防御、反伤</summary>
        Earth = 5
    }

    /// <summary>
    /// 五行相生相克规则
    /// </summary>
    public static class WuxingRules
    {
        /// <summary>
        /// 相克关系：金克木、木克土、土克水、水克火、火克金
        /// </summary>
        public static WuxingElement GetCounterElement(WuxingElement element)
        {
            return element switch
            {
                WuxingElement.Metal => WuxingElement.Wood,   // 金克木
                WuxingElement.Wood => WuxingElement.Earth,   // 木克土
                WuxingElement.Earth => WuxingElement.Water,  // 土克水
                WuxingElement.Water => WuxingElement.Fire,   // 水克火
                WuxingElement.Fire => WuxingElement.Metal,   // 火克金
                _ => WuxingElement.None
            };
        }

        /// <summary>
        /// 相生关系：金生水、水生木、木生火、火生土、土生金
        /// </summary>
        public static WuxingElement GetGenerateElement(WuxingElement element)
        {
            return element switch
            {
                WuxingElement.Metal => WuxingElement.Water,  // 金生水
                WuxingElement.Water => WuxingElement.Wood,   // 水生木
                WuxingElement.Wood => WuxingElement.Fire,    // 木生火
                WuxingElement.Fire => WuxingElement.Earth,   // 火生土
                WuxingElement.Earth => WuxingElement.Metal,  // 土生金
                _ => WuxingElement.None
            };
        }

        /// <summary>
        /// 计算五行克制系数
        /// </summary>
        /// <param name="attackElement">攻击属性</param>
        /// <param name="defenseElement">防御属性</param>
        /// <returns>伤害系数：克制+50%，被克-30%，相同1.0</returns>
        public static float CalculateCounterMultiplier(WuxingElement attackElement, WuxingElement defenseElement)
        {
            if (attackElement == WuxingElement.None || defenseElement == WuxingElement.None)
                return 1.0f;

            if (GetCounterElement(attackElement) == defenseElement)
                return 1.5f; // 克制：伤害+50%

            if (GetCounterElement(defenseElement) == attackElement)
                return 0.7f; // 被克：伤害-30%

            return 1.0f; // 相同或无关系：正常伤害
        }

        /// <summary>
        /// 计算五行相生加成
        /// </summary>
        /// <param name="firstElement">前一个技能属性</param>
        /// <param name="secondElement">后一个技能属性</param>
        /// <returns>相生加成：+30%，否则1.0</returns>
        public static float CalculateGenerateBonus(WuxingElement firstElement, WuxingElement secondElement)
        {
            if (GetGenerateElement(firstElement) == secondElement)
                return 1.3f; // 相生：伤害+30%

            return 1.0f; // 无相生关系
        }
    }
}
