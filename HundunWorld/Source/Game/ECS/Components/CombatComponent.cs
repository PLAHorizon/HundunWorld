using FlaxEngine;
using Game.Character.Attributes;
using Horizon.Game.Message.Network;

namespace HundunWorld.Game.ECS.Components
{
    /// <summary>
    /// 战斗组件，存储实体的战斗状态
    /// </summary>
    public struct CombatComponent
    {
        /// <summary>
        /// 是否处于战斗状态
        /// </summary>
        public bool IsInCombat;

        /// <summary>
        /// 当前目标实体ID
        /// </summary>
        public ulong TargetEntityId;

        /// <summary>
        /// 上次攻击时间
        /// </summary>
        public float LastAttackTime;

        /// <summary>
        /// 攻击间隔
        /// </summary>
        public float AttackInterval;

        /// <summary>
        /// 连击计数
        /// </summary>
        public int ComboCount;

        /// <summary>
        /// 连击重置时间
        /// </summary>
        public float ComboResetTime;

        public CombatComponent(float attackInterval = 1.0f, float comboResetTime = 2.0f)
        {
            IsInCombat = false;
            TargetEntityId = 0;
            LastAttackTime = 0;
            AttackInterval = attackInterval;
            ComboCount = 0;
            ComboResetTime = comboResetTime;
        }
    }

    /// <summary>
    /// 伤害组件，用于标记实体受到的伤害
    /// </summary>
    public struct DamageComponent
    {
        /// <summary>
        /// 伤害值
        /// </summary>
        public float Amount;

        /// <summary>
        /// 伤害类型
        /// </summary>
        public Horizon.Game.Message.Enums.DamageType Type;

        /// <summary>
        /// 伤害来源实体ID
        /// </summary>
        public ulong SourceEntityId;

        /// <summary>
        /// 伤害位置
        /// </summary>
        public Vector3 HitPosition;

        /// <summary>
        /// 是否暴击
        /// </summary>
        public bool IsCritical;

        public DamageComponent(float amount, Horizon.Game.Message.Enums.DamageType type, ulong sourceId, Vector3 hitPos, bool critical = false)
        {
            Amount = amount;
            Type = type;
            SourceEntityId = sourceId;
            HitPosition = hitPos;
            IsCritical = critical;
        }
    }

    /// <summary>
    /// 五行属性组件
    /// </summary>
    public struct WuxingComponent
    {
        /// <summary>
        /// 当前元素属性
        /// </summary>
        public WuxingElement Element;

        /// <summary>
        /// 金属性亲和度
        /// </summary>
        public int MetalAffinity;

        /// <summary>
        /// 木属性亲和度
        /// </summary>
        public int WoodAffinity;

        /// <summary>
        /// 水属性亲和度
        /// </summary>
        public int WaterAffinity;

        /// <summary>
        /// 火属性亲和度
        /// </summary>
        public int FireAffinity;

        /// <summary>
        /// 土属性亲和度
        /// </summary>
        public int EarthAffinity;

        public WuxingComponent(WuxingElement element, int metalAff = 50, int woodAff = 50, int waterAff = 50, int fireAff = 50, int earthAff = 50)
        {
            Element = element;
            MetalAffinity = metalAff;
            WoodAffinity = woodAff;
            WaterAffinity = waterAff;
            FireAffinity = fireAff;
            EarthAffinity = earthAff;
        }

        /// <summary>
        /// 获取指定元素的亲和度
        /// </summary>
        public int GetAffinity(WuxingElement element)
        {
            return element switch
            {
                WuxingElement.Metal => MetalAffinity,
                WuxingElement.Wood => WoodAffinity,
                WuxingElement.Water => WaterAffinity,
                WuxingElement.Fire => FireAffinity,
                WuxingElement.Earth => EarthAffinity,
                _ => 50
            };
        }
    }



    /// <summary>
    /// 战斗效果组件（Buff/Debuff）
    /// </summary>
    public struct EffectComponent
    {
        /// <summary>
        /// 效果ID
        /// </summary>
        public int EffectId;

        /// <summary>
        /// 效果类型
        /// </summary>
        public EffectType Type;

        /// <summary>
        /// 剩余持续时间
        /// </summary>
        public float Duration;

        /// <summary>
        /// 效果强度
        /// </summary>
        public float Intensity;

        /// <summary>
        /// 叠加层数
        /// </summary>
        public int Stacks;

        public EffectComponent(int effectId, EffectType type, float duration, float intensity, int stacks = 1)
        {
            EffectId = effectId;
            Type = type;
            Duration = duration;
            Intensity = intensity;
            Stacks = stacks;
        }
    }
}
