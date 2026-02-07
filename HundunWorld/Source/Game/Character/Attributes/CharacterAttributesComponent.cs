using FlaxEngine;
using System;

namespace Game.Character.Attributes
{
    /// <summary>
    /// 角色成长阶段
    /// </summary>
    public enum CharacterStage
    {
        /// <summary>武侠阶段（1-50级）</summary>
        Wuxia = 1,
        
        /// <summary>仙侠阶段（51-150级）</summary>
        Xianxia = 2,
        
        /// <summary>玄幻阶段（151-300级）</summary>
        Xuanhuan = 3
    }

    /// <summary>
    /// 角色属性组件
    /// 管理角色的基础属性、五行亲和度、能量系统等
    /// </summary>
    public class CharacterAttributesComponent : Script
    {
        #region 基础属性

        [Header("基础属性")]
        [Tooltip("角色等级")]
        public int Level = 1;

        [Tooltip("当前生命值")]
        public float CurrentHealth = 1000f;

        [Tooltip("最大生命值")]
        public float MaxHealth = 1000f;

        [Tooltip("物理攻击力")]
        public float PhysicalAttack = 100f;

        [Tooltip("法术攻击力")]
        public float MagicAttack = 100f;

        [Tooltip("物理防御")]
        public float PhysicalDefense = 50f;

        [Tooltip("法术防御")]
        public float MagicDefense = 50f;

        #endregion

        #region 能量系统

        [Header("能量系统")]
        [Tooltip("当前能量值（内力/灵力/元力）")]
        public float CurrentEnergy = 1000f;

        [Tooltip("最大能量值")]
        public float MaxEnergy = 1000f;

        [Tooltip("能量恢复速率（每秒）")]
        public float EnergyRecoveryRate = 10f;

        [Tooltip("体力值")]
        public float CurrentStamina = 100f;

        [Tooltip("最大体力值")]
        public float MaxStamina = 100f;

        [Tooltip("体力恢复速率（每秒）")]
        public float StaminaRecoveryRate = 5f;

        #endregion

        #region 五行属性

        [Header("五行亲和度")]
        [Tooltip("金属性亲和度（0-10000）")]
        public int MetalAffinity = 0;

        [Tooltip("木属性亲和度（0-10000）")]
        public int WoodAffinity = 0;

        [Tooltip("水属性亲和度（0-10000）")]
        public int WaterAffinity = 0;

        [Tooltip("火属性亲和度（0-10000）")]
        public int FireAffinity = 0;

        [Tooltip("土属性亲和度（0-10000）")]
        public int EarthAffinity = 0;

        #endregion

        #region 成长阶段

        [Header("成长阶段")]
        [Tooltip("当前成长阶段")]
        public CharacterStage CurrentStage = CharacterStage.Wuxia;

        #endregion

        /// <summary>
        /// 获取指定五行属性的亲和度
        /// </summary>
        public int GetWuxingAffinity(WuxingElement element)
        {
            return element switch
            {
                WuxingElement.Metal => MetalAffinity,
                WuxingElement.Wood => WoodAffinity,
                WuxingElement.Water => WaterAffinity,
                WuxingElement.Fire => FireAffinity,
                WuxingElement.Earth => EarthAffinity,
                _ => 0
            };
        }

        /// <summary>
        /// 增加五行亲和度
        /// </summary>
        public void AddWuxingAffinity(WuxingElement element, int amount)
        {
            switch (element)
            {
                case WuxingElement.Metal:
                    MetalAffinity = Mathf.Clamp(MetalAffinity + amount, 0, 10000);
                    break;
                case WuxingElement.Wood:
                    WoodAffinity = Mathf.Clamp(WoodAffinity + amount, 0, 10000);
                    break;
                case WuxingElement.Water:
                    WaterAffinity = Mathf.Clamp(WaterAffinity + amount, 0, 10000);
                    break;
                case WuxingElement.Fire:
                    FireAffinity = Mathf.Clamp(FireAffinity + amount, 0, 10000);
                    break;
                case WuxingElement.Earth:
                    EarthAffinity = Mathf.Clamp(EarthAffinity + amount, 0, 10000);
                    break;
            }
        }

        /// <summary>
        /// 消耗能量
        /// </summary>
        public bool ConsumeEnergy(float amount)
        {
            if (CurrentEnergy >= amount)
            {
                CurrentEnergy -= amount;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 消耗体力
        /// </summary>
        public bool ConsumeStamina(float amount)
        {
            if (CurrentStamina >= amount)
            {
                CurrentStamina -= amount;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 造成伤害
        /// </summary>
        public void TakeDamage(float damage)
        {
            CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
            
            if (CurrentHealth <= 0)
            {
                OnDeath();
            }
        }

        /// <summary>
        /// 治疗
        /// </summary>
        public void Heal(float amount)
        {
            CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + amount);
        }

        private void OnDeath()
        {
            Debug.Log("角色死亡");
        }

        public override void OnUpdate()
        {
            float deltaTime = Time.DeltaTime;

            // 能量恢复
            if (CurrentEnergy < MaxEnergy)
            {
                CurrentEnergy = Mathf.Min(MaxEnergy, CurrentEnergy + EnergyRecoveryRate * deltaTime);
            }

            // 体力恢复
            if (CurrentStamina < MaxStamina)
            {
                CurrentStamina = Mathf.Min(MaxStamina, CurrentStamina + StaminaRecoveryRate * deltaTime);
            }
        }
    }
}
