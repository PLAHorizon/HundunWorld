using Arch.Core;
using Arch.Core.Utils;

namespace HundunWorld.Game.ECS.Components
{
    /// <summary>
    /// 生命值组件，用于存储实体的生命值信息
    /// </summary>
    public struct HealthComponent 
    {
        /// <summary>
        /// 当前生命值
        /// </summary>
        public float CurrentHealth;

        /// <summary>
        /// 最大生命值
        /// </summary>
        public float MaxHealth;
        
        /// <summary>
        /// 攻击力
        /// </summary>
        public float Attack;
        
        /// <summary>
        /// 能量值（内力/灵力/元力）
        /// </summary>
        public float Energy;
        
        /// <summary>
        /// 防御力
        /// </summary>
        public float Defense;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="maxHealth">最大生命值</param>
        public HealthComponent(float maxHealth)
        {
            MaxHealth = maxHealth;
            CurrentHealth = maxHealth;
            Attack = 100f; // 默认攻击力
            Energy = 1000f; // 默认能量值
            Defense = 50f; // 默认防御力
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="currentHealth">当前生命值</param>
        /// <param name="maxHealth">最大生命值</param>
        public HealthComponent(float currentHealth, float maxHealth)
        {
            CurrentHealth = currentHealth;
            MaxHealth = maxHealth;
            Attack = 100f; // 默认攻击力
            Energy = 1000f; // 默认能量值
            Defense = 50f; // 默认防御力
        }

        /// <summary>
        /// 检查是否存活
        /// </summary>
        public bool IsAlive => CurrentHealth > 0;

        /// <summary>
        /// 获取生命值百分比
        /// </summary>
        public float HealthPercentage => MaxHealth > 0 ? CurrentHealth / MaxHealth : 0;

        public override string ToString()
        {
            return $"Health({CurrentHealth}/{MaxHealth})";
        }
    }
}