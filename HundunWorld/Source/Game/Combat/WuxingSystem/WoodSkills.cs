using System;
using FlaxEngine;
using Game.Character.Attributes;
using Game.Combat.Skills;
using Game.Combat.Effects;
using HundunWorld.Game.Combat.Skills;
using Horizon.Game.Message.Enums;

namespace Game.Combat.WuxingSystem
{
    /// <summary>
    /// 木系技能集合 - 特性：持续、恢复、控制
    /// 木系代表生命力、韧性和生长，技能侧重持续伤害和治疗效果
    /// </summary>

    // ============================
    // 青木藤缠 - 控制技能
    // ============================
    
    /// <summary>
    /// 青木藤缠：召唤藤蔓束缚敌人，造成定身效果
    /// 等级要求：5级
    /// 冷却时间：10秒
    /// 内力消耗：70点
    /// </summary>
    public class QingMuTengChan : SkillBase
    {
        [Tooltip("定身持续时间")]
        public float RootDuration = 2.0f;
        
        [Tooltip("藤蔓减速效果")]
        public float SlowPercent = 50f;
        
        [Tooltip("持续伤害间隔")]
        public float DamageInterval = 0.5f;
        
        [Tooltip("藤蔓特效预制体")]
        public Prefab VineEffectPrefab;
        
        private float damageTimer = 0f;
        private bool isRooting = false;
        private float rootTimer = 0f;
        private Actor activeVineEffectActor;

        public override void OnAwake()
        {
            base.OnAwake();
            
            // 配置技能数据
            Data = new SkillData
            {
                SkillId = 2001,
                SkillName = "青木藤缠",
                Description = "召唤藤蔓束缚敌人，造成持续伤害和定身效果",
                Element = WuxingElement.Wood,
                Type = SkillType.Control,
                DamageMultiplier = 0.5f, // 持续伤害较低
                EnergyCost = 70f,
                Cooldown = 10f,
                Range = 15f,
                CastTime = 0.3f,
                RequiredLevel = 5
            };
        }

        protected override void ExecuteSkill(Actor target)
        {
            if (target == null) return;

            float damage = CalculateDamage(target);
            Debug.Log($"青木藤缠束缚目标，造成 {damage:F1} 点初始伤害");

            // 应用定身效果（眩晕实现定身）
            var stunEffect = SkillEffectFactory.CreateStun(RootDuration);
            stunEffect.Apply(target);

            // 生成藤蔓特效
            var effectManager = SkillEffectManager.Instance;
            if (effectManager != null)
            {
                activeVineEffectActor = effectManager.PlayEffect("QingMuTengChan_Vine", target.Position, default, target);
            }

            // 播放束缚音效
            Debug.Log("[Audio] 播放青木藤缠束缚音效");

            isRooting = true;
            rootTimer = RootDuration;
            damageTimer = 0f;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (isRooting)
            {
                rootTimer -= Time.DeltaTime;
                damageTimer += Time.DeltaTime;

                // 持续伤害
                if (damageTimer >= DamageInterval)
                {
                    damageTimer = 0f;
                    float tickDamage = CalculateDamage(null) * 0.2f;
                    Debug.Log($"藤蔓持续伤害 {tickDamage:F1}");
                    // 应用持续伤害
                    var dotEffect = SkillEffectFactory.CreateDamageOverTime(tickDamage, DamageInterval, DamageInterval);
                    dotEffect.Apply(Actor);
                }

                if (rootTimer <= 0)
                {
                    isRooting = false;
                    Debug.Log("青木藤缠效果结束");
                    // 清除藤蔓特效
                    var effectManager = SkillEffectManager.Instance;
                    if (effectManager != null && activeVineEffectActor != null)
                    {
                        effectManager.StopEffect(activeVineEffectActor);
                        activeVineEffectActor = null;
                    }
                }
            }
        }
    }

    // ============================
    // 春回大地 - 治疗技能
    // ============================
    
    /// <summary>
    /// 春回大地：范围治疗友军，恢复生命值
    /// 等级要求：10级
    /// 冷却时间：30秒
    /// 内力消耗：120点
    /// </summary>
    public class ChunHuiDaDi : SkillBase
    {
        [Tooltip("治疗范围半径")]
        public float HealRadius = 10f;
        
        [Tooltip("治疗百分比（基于最大生命值）")]
        public float HealPercent = 30f;
        
        [Tooltip("持续治疗时间")]
        public float HealDuration = 5f;
        
        [Tooltip("治疗间隔")]
        public float HealInterval = 1f;
        
        [Tooltip("治疗特效预制体")]
        public Prefab HealEffectPrefab;

        private bool isHealing = false;
        private float healTimer = 0f;
        private float tickTimer = 0f;
        private Actor activeHealEffectActor;

        public override void OnAwake()
        {
            base.OnAwake();
            
            Data = new SkillData
            {
                SkillId = 2002,
                SkillName = "春回大地",
                Description = "范围治疗友军，恢复生命值并持续恢复",
                Element = WuxingElement.Wood,
                Type = SkillType.Support,
                DamageMultiplier = 0f, // 纯治疗技能
                EnergyCost = 120f,
                Cooldown = 30f,
                Range = 0f, // 以自身为中心
                CastTime = 1.0f,
                RequiredLevel = 10
            };
        }

        protected override void ExecuteSkill(Actor target)
        {
            Debug.Log($"春回大地施放，范围 {HealRadius}米");

            // 计算治疗量
            if (characterAttributes != null)
            {
                float healAmount = characterAttributes.MaxHealth * (HealPercent / 100f);
                characterAttributes.Heal(healAmount);
                Debug.Log($"立即恢复 {healAmount:F1} 点生命值");
            }

            // 检测范围内友军 - 使用HealOverTime应用持续治疗
            var hotEffect = SkillEffectFactory.CreateHealOverTime(
                characterAttributes != null ? characterAttributes.MaxHealth * (HealPercent / 100f) * 0.1f : 0f,
                HealDuration, HealInterval);
            hotEffect.Apply(Actor);

            // 生成治疗特效（绿色光环）
            var effectManager = SkillEffectManager.Instance;
            if (effectManager != null)
            {
                activeHealEffectActor = effectManager.PlayAreaEffect("ChunHuiDaDi_Heal", Actor.Position, HealRadius);
            }

            // 播放治疗音效
            Debug.Log("[Audio] 播放春回大地治疗音效");

            isHealing = true;
            healTimer = HealDuration;
            tickTimer = 0f;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (isHealing)
            {
                healTimer -= Time.DeltaTime;
                tickTimer += Time.DeltaTime;

                if (tickTimer >= HealInterval)
                {
                    tickTimer = 0f;
                    if (characterAttributes != null)
                    {
                        float tickHeal = characterAttributes.MaxHealth * (HealPercent / 100f) * 0.1f;
                        characterAttributes.Heal(tickHeal);
                        Debug.Log($"持续治疗 {tickHeal:F1}");
                    }
                }

                if (healTimer <= 0)
                {
                    isHealing = false;
                    Debug.Log("春回大地效果结束");
                }
            }
        }
    }

    // ============================
    // 万木森罗 - 召唤技能
    // ============================
    
    /// <summary>
    /// 万木森罗：召唤树木持续攻击敌人
    /// 等级要求：15级
    /// 冷却时间：25秒
    /// 内力消耗：180点
    /// </summary>
    public class WanMuSenLuo : SkillBase
    {
        [Tooltip("召唤树木数量")]
        public int TreeCount = 5;
        
        [Tooltip("树木持续时间")]
        public float TreeDuration = 10f;
        
        [Tooltip("树木攻击间隔")]
        public float AttackInterval = 1.5f;
        
        [Tooltip("树木伤害倍率")]
        public float TreeDamageMultiplier = 0.6f;
        
        [Tooltip("召唤范围半径")]
        public float SummonRadius = 8f;

        private bool isActive = false;
        private float activeTimer = 0f;
        private float attackTimer = 0f;
        private Actor activeTreeEffectActor;

        public override void OnAwake()
        {
            base.OnAwake();
            
            Data = new SkillData
            {
                SkillId = 2003,
                SkillName = "万木森罗",
                Description = "召唤树木持续攻击范围内的敌人",
                Element = WuxingElement.Wood,
                Type = SkillType.ActiveAttack, // 修正：使用SkillBase中定义的SkillType枚举
                DamageMultiplier = 0.8f,
                EnergyCost = 180f,
                Cooldown = 25f,
                Range = 20f,
                CastTime = 1.5f,
                RequiredLevel = 15
            };
        }

        protected override void ExecuteSkill(Actor target)
        {
            Debug.Log($"万木森罗！召唤 {TreeCount} 棵树木");

            // 在目标周围生成树木特效
            var effectManager = SkillEffectManager.Instance;
            if (effectManager != null)
            {
                activeTreeEffectActor = effectManager.PlayAreaEffect("WanMuSenLuo_Trees", Actor.Position, SummonRadius);
            }

            // 播放召唤动画
            Debug.Log("[Anim] 播放万木森罗召唤动画");

            isActive = true;
            activeTimer = TreeDuration;
            attackTimer = 0f;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (isActive)
            {
                activeTimer -= Time.DeltaTime;
                attackTimer += Time.DeltaTime;

                if (attackTimer >= AttackInterval)
                {
                    attackTimer = 0f;
                    float damage = CalculateDamage(null) * TreeDamageMultiplier;
                    Debug.Log($"树木发起攻击，造成 {damage:F1} 点伤害");
                    // 对范围内敌人造成伤害
                    var dotEffect = SkillEffectFactory.CreateDamageOverTime(damage, AttackInterval, AttackInterval);
                    dotEffect.Apply(Actor);

                    // 播放树木攻击动画
                    var em = SkillEffectManager.Instance;
                    if (em != null)
                    {
                        em.PlayEffect("WanMuSenLuo_Attack", Actor.Position);
                    }
                }

                if (activeTimer <= 0)
                {
                    isActive = false;
                    Debug.Log("万木森罗效果结束");
                    // 移除树木特效
                    var em2 = SkillEffectManager.Instance;
                    if (em2 != null && activeTreeEffectActor != null)
                    {
                        em2.StopEffect(activeTreeEffectActor);
                        activeTreeEffectActor = null;
                    }
                }
            }
        }
    }

    // ============================
    // 生生不息 - 被动技能
    // ============================
    
    /// <summary>
    /// 生生不息：持续恢复生命和内力
    /// 等级要求：20级
    /// 冷却时间：60秒
    /// 内力消耗：250点
    /// </summary>
    public class ShengShengBuXi : SkillBase
    {
        [Tooltip("持续时间")]
        public float Duration = 15f;
        
        [Tooltip("生命恢复速率（每秒）")]
        public float HealthRegenRate = 3f;
        
        [Tooltip("能量恢复速率（每秒）")]
        public float EnergyRegenRate = 5f;
        
        [Tooltip("移动速度加成")]
        public float MoveSpeedBonus = 20f;

        private bool isActive = false;
        private float activeTimer = 0f;
        private Actor activeRegenEffectActor;
        private SkillEffect activeSpeedBuff;

        public override void OnAwake()
        {
            base.OnAwake();
            
            Data = new SkillData
            {
                SkillId = 2004,
                SkillName = "生生不息",
                Description = "激活生命之力，持续恢复生命和内力，并提升移动速度",
                Element = WuxingElement.Wood,
                Type = SkillType.Support,
                DamageMultiplier = 0f,
                EnergyCost = 250f,
                Cooldown = 60f,
                Range = 0f,
                CastTime = 0.5f,
                RequiredLevel = 20
            };
        }

        protected override void ExecuteSkill(Actor target)
        {
            Debug.Log($"生生不息激活！持续 {Duration} 秒");

            // 应用移动速度增益
            activeSpeedBuff = SkillEffectFactory.CreateAttributeBuff(
                AttributeBuffEffect.AttributeType.Speed, MoveSpeedBonus, true, Duration);
            activeSpeedBuff.Apply(Actor);

            // 播放生命之力特效（绿色光环）
            var effectManager = SkillEffectManager.Instance;
            if (effectManager != null)
            {
                activeRegenEffectActor = effectManager.PlayCastEffect("ShengShengBuXi_Aura", Actor);
            }

            // 播放激活音效
            Debug.Log("[Audio] 播放生生不息激活音效");

            isActive = true;
            activeTimer = Duration;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (isActive)
            {
                activeTimer -= Time.DeltaTime;

                // 持续恢复
                if (characterAttributes != null)
                {
                    float healthRegen = HealthRegenRate * characterAttributes.MaxHealth / 100f * Time.DeltaTime;
                    characterAttributes.Heal(healthRegen);

                    float energyRegen = EnergyRegenRate * Time.DeltaTime;
                    characterAttributes.CurrentEnergy = Math.Min(
                        characterAttributes.MaxEnergy,
                        characterAttributes.CurrentEnergy + energyRegen
                    );
                }

                if (activeTimer <= 0)
                {
                    isActive = false;
                    Debug.Log("生生不息效果结束");
                    // 移除移动速度增益
                    if (activeSpeedBuff != null)
                    {
                        activeSpeedBuff.Remove();
                        activeSpeedBuff = null;
                    }

                    // 停止特效
                    var effectManager = SkillEffectManager.Instance;
                    if (effectManager != null && activeRegenEffectActor != null)
                    {
                        effectManager.StopEffect(activeRegenEffectActor);
                        activeRegenEffectActor = null;
                    }
                }
            }
        }

        /// <summary>
        /// 检查生生不息是否激活
        /// </summary>
        public bool IsActive()
        {
            return isActive;
        }
    }
}
