using System;

namespace Game.Character.Attributes
{
    /// <summary>
    /// 角色属性面板数据
    /// 包含基础属性与五行属性，用于 UI 展示与持久化
    /// </summary>
    [Serializable]
    public struct CharacterAttributes
    {
        #region 基础属性

        /// <summary>力量</summary>
        public int Strength;

        /// <summary>敏捷</summary>
        public int Agility;

        /// <summary>智力</summary>
        public int Intelligence;

        /// <summary>体质</summary>
        public int Constitution;

        /// <summary>攻击力</summary>
        public float Attack;

        /// <summary>防御力</summary>
        public float Defense;

        /// <summary>生命值</summary>
        public float HP;

        /// <summary>法力值</summary>
        public float MP;

        #endregion

        #region 五行属性

        /// <summary>金属性</summary>
        public int Metal;

        /// <summary>木属性</summary>
        public int Wood;

        /// <summary>水属性</summary>
        public int Water;

        /// <summary>火属性</summary>
        public int Fire;

        /// <summary>土属性</summary>
        public int Earth;

        #endregion

        /// <summary>
        /// 获取指定五行属性的数值
        /// </summary>
        /// <param name="element">五行元素</param>
        /// <returns>对应五行数值，未识别时返回 0</returns>
        public int GetWuxingValue(WuxingElement element)
        {
            return element switch
            {
                WuxingElement.Metal => Metal,
                WuxingElement.Wood => Wood,
                WuxingElement.Water => Water,
                WuxingElement.Fire => Fire,
                WuxingElement.Earth => Earth,
                _ => 0
            };
        }

        /// <summary>
        /// 从角色属性组件转换为面板数据
        /// </summary>
        /// <param name="component">角色属性组件</param>
        /// <returns>角色属性面板数据</returns>
        public static CharacterAttributes FromComponent(CharacterAttributesComponent component)
        {
            if (component == null)
                return GetDefault();

            return new CharacterAttributes
            {
                Strength = component.Level,
                Agility = component.Level,
                Intelligence = component.Level,
                Constitution = component.Level,
                Attack = component.PhysicalAttack + component.MagicAttack,
                Defense = component.PhysicalDefense + component.MagicDefense,
                HP = component.MaxHealth,
                MP = component.MaxEnergy,
                Metal = component.MetalAffinity,
                Wood = component.WoodAffinity,
                Water = component.WaterAffinity,
                Fire = component.FireAffinity,
                Earth = component.EarthAffinity
            };
        }

        /// <summary>
        /// 获取默认角色属性（用于 UI 预览或数据缺失时）
        /// </summary>
        /// <returns>默认角色属性</returns>
        public static CharacterAttributes GetDefault()
        {
            return new CharacterAttributes
            {
                Strength = 10,
                Agility = 10,
                Intelligence = 10,
                Constitution = 10,
                Attack = 100f,
                Defense = 50f,
                HP = 1000f,
                MP = 1000f,
                Metal = 100,
                Wood = 100,
                Water = 100,
                Fire = 100,
                Earth = 100
            };
        }
    }
}
