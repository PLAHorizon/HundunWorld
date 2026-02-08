using System;
using FlaxEngine;
using Game.Character.Attributes;
using Game.Combat.Skills;
using Game.Combat.Effects;
using HundunWorld.Game.Combat.Skills;

namespace Game.Combat.WuxingSystem
{
    /// <summary>
    /// 水系技能集合 - 特性：控制、治疗、流动
    /// 水系代表柔和、流动和适应，技能侧重控制和治疗
    /// </summary>

    // ============================
    // 寒冰掌 - 基础攻击技能
    // ============================
    
    /// <summary>
    /// 寒冰掌：单体攻击并减速目标
    /// 等级要求：5级
    /// 冷却时间：6秒
    /// 内力消耗：60点
    /// </summary>
    public class HanBingZhang : SkillBase
    {
        [Tooltip("减速持续时间")]
        public float SlowDuration = 3f;
        
        [Tooltip("减速百分比")]
        public float SlowPercent = 40f;
        
        [Tooltip("冰冻特效预制体")]
        public Prefab FrostEffectPrefab;

        private bool isSlowing = false;
        private float slowTimer = 0f;
        private Actor activeFrostEffectActor;
        private SkillEffect activeSlowEffect;

        public override void OnAwake()
        {
            base.OnAwake();
            
            Data = new SkillData
            {
                SkillId = 3001,
                SkillName = "寒冰掌",
                Description = "掌心凝聚寒冰之力，攻击敌人并减速",
                Element = WuxingElement.Water,
                Type = SkillType.ActiveAttack,
                DamageMultiplier = 1.2f,
                EnergyCost = 60f,
                Cooldown = 6f,
                Range = 8f,
                CastTime = 0.3f,
                RequiredLevel = 5
            };
        }

        protected override void ExecuteSkill(Actor target)
        {
            if (target == null) return;

            float damage = CalculateDamage(target);
            Debug.Log($"寒冰掌命中目标，造成 {damage:F1} 点伤害");

            // 应用减速效果
            activeSlowEffect = SkillEffectFactory.CreateSlow(SlowPercent / 100f, SlowDuration);
            activeSlowEffect.Apply(target);

            // 生成寒冰特效
            var effectManager = SkillEffectManager.Instance;
            if (effectManager != null)
            {
                activeFrostEffectActor = effectManager.PlayHitEffect("HanBingZhang_Frost", target.Position);
            }

            // 播放冰冻音效
            Debug.Log("[Audio] 播放寒冰掌冰冻音效");

            isSlowing = true;
            slowTimer = SlowDuration;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (isSlowing)
            {
                slowTimer -= Time.DeltaTime;
                if (slowTimer <= 0)
                {
                    isSlowing = false;
                    Debug.Log("寒冰减速效果结束");
                    // 移除减速效果
                    if (activeSlowEffect != null)
                    {
                        activeSlowEffect.Remove();
                        activeSlowEffect = null;
                    }
                    var effectManager = SkillEffectManager.Instance;
                    if (effectManager != null && activeFrostEffectActor != null)
                    {
                        effectManager.StopEffect(activeFrostEffectActor);
                        activeFrostEffectActor = null;
                    }
                }
            }
        }
    }

    // ============================
    // 水愈术 - 治疗技能
    // ============================
    
    /// <summary>
    /// 水愈术：快速恢复生命值
    /// 等级要求：10级
    /// 冷却时间：15秒
    /// 内力消耗：100点
    /// </summary>
    public class ShuiYuShu : SkillBase
    {
        [Tooltip("治疗百分比")]
        public float HealPercent = 40f;
        
        [Tooltip("额外治疗量（固定值）")]
        public float BonusHeal = 200f;
        
        [Tooltip("治疗特效预制体")]
        public Prefab HealWaterEffectPrefab;

        public override void OnAwake()
        {
            base.OnAwake();
            
            Data = new SkillData
            {
                SkillId = 3002,
                SkillName = "水愈术",
                Description = "引导水之灵力，快速恢复生命值",
                Element = WuxingElement.Water,
                Type = SkillType.Support,
                DamageMultiplier = 0f,
                EnergyCost = 100f,
                Cooldown = 15f,
                Range = 15f,
                CastTime = 0.8f,
                RequiredLevel = 10
            };
        }

        protected override void ExecuteSkill(Actor target)
        {
            // 如果没有目标，治疗自己
            Actor healTarget = target ?? Actor;

            if (characterAttributes != null)
            {
                float healAmount = characterAttributes.MaxHealth * (HealPercent / 100f) + BonusHeal;
                
                // 五行亲和度加成
                int waterAffinity = characterAttributes.WaterAffinity;
                float affinityBonus = 1.0f + (waterAffinity / 10) * 0.005f;
                healAmount *= affinityBonus;

                characterAttributes.Heal(healAmount);
                Debug.Log($"水愈术恢复 {healAmount:F1} 点生命值");

                // 生成水流治疗特效
                var effectManager = SkillEffectManager.Instance;
                if (effectManager != null)
                {
                    effectManager.PlayEffect("ShuiYuShu_Heal", healTarget.Position, default, healTarget);
                }

                // 播放治疗音效
                Debug.Log("[Audio] 播放水愈术治疗音效");
            }
        }
    }

    // ============================
    // 冰封千里 - 大范围控制技能
    // ============================
    
    /// <summary>
    /// 冰封千里：大范围冰冻控制
    /// 等级要求：15级
    /// 冷却时间：30秒
    /// 内力消耗：200点
    /// </summary>
    public class BingFengQianLi : SkillBase
    {
        [Tooltip("冰封范围半径")]
        public float FreezeRadius = 12f;
        
        [Tooltip("冰冻持续时间")]
        public float FreezeDuration = 3f;
        
        [Tooltip("冰冻后减速时间")]
        public float SlowDuration = 5f;
        
        [Tooltip("解冻后减速百分比")]
        public float SlowPercent = 60f;
        
        [Tooltip("冰封特效预制体")]
        public Prefab IceFieldEffectPrefab;

        private bool isFreezing = false;
        private float freezeTimer = 0f;
        private bool isSlowing = false;
        private float slowTimer = 0f;
        private Actor activeIceEffectActor;
        private SkillEffect activeFreezeEffect;
        private SkillEffect activeSlowEffect;

        public override void OnAwake()
        {
            base.OnAwake();
            
            Data = new SkillData
            {
                SkillId = 3003,
                SkillName = "冰封千里",
                Description = "大范围冰冻敌人，解冻后持续减速",
                Element = WuxingElement.Water,
                Type = SkillType.Control,
                DamageMultiplier = 0.8f,
                EnergyCost = 200f,
                Cooldown = 30f,
                Range = 20f,
                CastTime = 2.0f,
                RequiredLevel = 15
            };
        }

        protected override void ExecuteSkill(Actor target)
        {
            float damage = CalculateDamage(target);
            Debug.Log($"冰封千里！范围 {FreezeRadius}米，造成 {damage:F1} 点伤害");

            // 应用冰冻效果（定身 = 眩晕）
            activeFreezeEffect = SkillEffectFactory.CreateStun(FreezeDuration);
            if (target != null)
            {
                activeFreezeEffect.Apply(target);
            }

            // 生成冰封领域特效（蓝色冰晶扩散）
            var effectManager = SkillEffectManager.Instance;
            if (effectManager != null)
            {
                activeIceEffectActor = effectManager.PlayAreaEffect("BingFengQianLi_Ice", Actor.Position, FreezeRadius);
            }

            // 播放冰封音效
            Debug.Log("[Audio] 播放冰封千里冰封音效");

            isFreezing = true;
            freezeTimer = FreezeDuration;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (isFreezing)
            {
                freezeTimer -= Time.DeltaTime;
                if (freezeTimer <= 0)
                {
                    isFreezing = false;
                    isSlowing = true;
                    slowTimer = SlowDuration;
                    Debug.Log("冰冻解除，进入减速阶段");
                    // 移除冰冻效果
                    if (activeFreezeEffect != null)
                    {
                        activeFreezeEffect.Remove();
                        activeFreezeEffect = null;
                    }

                    // 应用减速效果
                    activeSlowEffect = SkillEffectFactory.CreateSlow(SlowPercent / 100f, SlowDuration);
                    activeSlowEffect.Apply(Actor);
                }
            }

            if (isSlowing)
            {
                slowTimer -= Time.DeltaTime;
                if (slowTimer <= 0)
                {
                    isSlowing = false;
                    Debug.Log("减速效果结束");
                    // 移除减速效果
                    if (activeSlowEffect != null)
                    {
                        activeSlowEffect.Remove();
                        activeSlowEffect = null;
                    }
                    var effectManager = SkillEffectManager.Instance;
                    if (effectManager != null && activeIceEffectActor != null)
                    {
                        effectManager.StopEffect(activeIceEffectActor);
                        activeIceEffectActor = null;
                    }
                }
            }
        }
    }

    // ============================
    // 滔天巨浪 - 范围攻击技能
    // ============================
    
    /// <summary>
    /// 滔天巨浪：召唤水浪冲击敌人
    /// 等级要求：20级
    /// 冷却时间：25秒
    /// 内力消耗：180点
    /// </summary>
    public class TaoTianJuLang : SkillBase
    {
        [Tooltip("波浪宽度")]
        public float WaveWidth = 15f;
        
        [Tooltip("波浪长度")]
        public float WaveLength = 20f;
        
        [Tooltip("击退距离")]
        public float KnockbackDistance = 5f;
        
        [Tooltip("击退持续时间")]
        public float KnockbackDuration = 1f;
        
        [Tooltip("波浪移动速度")]
        public float WaveSpeed = 10f;
        
        [Tooltip("巨浪特效预制体")]
        public Prefab WaveEffectPrefab;

        private bool isWaveActive = false;
        private float waveDistance = 0f;
        private Actor activeWaveEffectActor;

        public override void OnAwake()
        {
            base.OnAwake();
            
            Data = new SkillData
            {
                SkillId = 3004,
                SkillName = "滔天巨浪",
                Description = "召唤巨大水浪向前冲击，击退范围内所有敌人",
                Element = WuxingElement.Water,
                Type = SkillType.ActiveAttack,
                DamageMultiplier = 1.8f,
                EnergyCost = 180f,
                Cooldown = 25f,
                Range = 0f, // 以自身为起点
                CastTime = 1.2f,
                RequiredLevel = 20
            };
        }

        protected override void ExecuteSkill(Actor target)
        {
            float damage = CalculateDamage(target);
            Debug.Log($"滔天巨浪！水浪前进，宽度 {WaveWidth}米，长度 {WaveLength}米");

            // 生成水浪特效
            var effectManager = SkillEffectManager.Instance;
            if (effectManager != null)
            {
                activeWaveEffectActor = effectManager.PlayProjectileEffect(
                    "TaoTianJuLang_Wave", Actor.Position, Actor.Direction);
            }

            // 播放水浪音效
            Debug.Log("[Audio] 播放滔天巨浪水浪音效");

            isWaveActive = true;
            waveDistance = 0f;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (isWaveActive)
            {
                waveDistance += WaveSpeed * Time.DeltaTime;

                // 检测波浪路径上的敌人并更新特效位置
                if (activeWaveEffectActor != null)
                {
                    activeWaveEffectActor.Position = Actor.Position + Actor.Direction * waveDistance;
                }

                if (waveDistance >= WaveLength)
                {
                    isWaveActive = false;
                    Debug.Log("滔天巨浪结束");
                    // 移除水浪特效
                    var effectManager = SkillEffectManager.Instance;
                    if (effectManager != null && activeWaveEffectActor != null)
                    {
                        effectManager.StopEffect(activeWaveEffectActor);
                        activeWaveEffectActor = null;
                    }
                }
            }
        }

        /// <summary>
        /// 计算击退终点位置
        /// </summary>
        private Vector3 CalculateKnockbackPosition(Actor targetActor)
        {
            // 计算从施法者到目标的方向
            Vector3 direction = (targetActor.Position - Actor.Position).Normalized;
            return targetActor.Position + direction * KnockbackDistance;
        }
    }

    // ============================
    // 水月镜花 - 幻术技能（额外）
    // ============================
    
    /// <summary>
    /// 水月镜花：创造水之幻影，迷惑敌人
    /// 等级要求：18级
    /// 冷却时间：35秒
    /// 内力消耗：150点
    /// </summary>
    public class ShuiYueJingHua : SkillBase
    {
        [Tooltip("幻影数量")]
        public int IllusionCount = 3;
        
        [Tooltip("幻影持续时间")]
        public float IllusionDuration = 10f;
        
        [Tooltip("幻影伤害倍率")]
        public float IllusionDamageMultiplier = 0.3f;
        
        [Tooltip("闪避率加成")]
        public float DodgeBonus = 30f;

        private bool isActive = false;
        private float activeTimer = 0f;
        private Actor activeIllusionEffectActor;
        private SkillEffect activeDodgeBuff;

        public override void OnAwake()
        {
            base.OnAwake();
            
            Data = new SkillData
            {
                SkillId = 3005,
                SkillName = "水月镜花",
                Description = "创造水之幻影分身，提升闪避率并协同攻击",
                Element = WuxingElement.Water,
                Type = SkillType.Support,
                DamageMultiplier = 0.5f,
                EnergyCost = 150f,
                Cooldown = 35f,
                Range = 0f,
                CastTime = 1.0f,
                RequiredLevel = 18
            };
        }

        protected override void ExecuteSkill(Actor target)
        {
            Debug.Log($"水月镜花！创造 {IllusionCount} 个幻影分身");

            // 应用闪避率加成
            activeDodgeBuff = SkillEffectFactory.CreateAttributeBuff(
                AttributeBuffEffect.AttributeType.Speed, DodgeBonus, true, IllusionDuration);
            activeDodgeBuff.Apply(Actor);

            // 生成水镜特效
            var effectManager = SkillEffectManager.Instance;
            if (effectManager != null)
            {
                activeIllusionEffectActor = effectManager.PlayCastEffect("ShuiYueJingHua_Illusion", Actor);
            }

            isActive = true;
            activeTimer = IllusionDuration;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (isActive)
            {
                activeTimer -= Time.DeltaTime;

                // 幻影跟随本体移动并自动攻击
                if (activeIllusionEffectActor != null)
                {
                    activeIllusionEffectActor.Position = Actor.Position;
                }

                if (activeTimer <= 0)
                {
                    isActive = false;
                    Debug.Log("水月镜花效果结束");
                    // 移除幻影
                    var effectManager = SkillEffectManager.Instance;
                    if (effectManager != null && activeIllusionEffectActor != null)
                    {
                        effectManager.StopEffect(activeIllusionEffectActor);
                        activeIllusionEffectActor = null;
                    }

                    // 移除闪避加成
                    if (activeDodgeBuff != null)
                    {
                        activeDodgeBuff.Remove();
                        activeDodgeBuff = null;
                    }
                }
            }
        }

        /// <summary>
        /// 获取当前闪避率加成
        /// </summary>
        public float GetDodgeBonus()
        {
            return isActive ? DodgeBonus : 0f;
        }
    }
}
