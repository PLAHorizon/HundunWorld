using System;
using FlaxEngine;
using Game.Character.Attributes;
using Game.Combat.Skills;

namespace Game.Combat.WuxingSystem
{
    /// <summary>
    /// 火系技能集合 - 特性：爆发、燃烧、范围伤害
    /// 火系代表炽热、毁灭和爆发，技能侧重高伤害和燃烧效果
    /// </summary>

    // ============================
    // 烈焰掌 - 基础攻击技能
    // ============================
    
    /// <summary>
    /// 烈焰掌：单体火焰攻击，附带燃烧效果
    /// 等级要求：5级
    /// 冷却时间：7秒
    /// 内力消耗：75点
    /// </summary>
    public class LieYanZhang : SkillBase
    {
        [Tooltip("燃烧持续时间")]
        public float BurnDuration = 5f;
        
        [Tooltip("燃烧伤害间隔")]
        public float BurnInterval = 1f;
        
        [Tooltip("燃烧伤害倍率（每跳）")]
        public float BurnDamageMultiplier = 0.15f;
        
        [Tooltip("火焰特效预制体")]
        public Prefab FlameEffectPrefab;

        private bool isBurning = false;
        private float burnTimer = 0f;
        private float burnTickTimer = 0f;

        public override void OnAwake()
        {
            base.OnAwake();
            
            Data = new SkillData
            {
                SkillId = 4001,
                SkillName = "烈焰掌",
                Description = "掌心凝聚烈焰攻击敌人，造成燃烧效果",
                Element = WuxingElement.Fire,
                Type = SkillType.ActiveAttack,
                DamageMultiplier = 1.3f,
                EnergyCost = 75f,
                Cooldown = 7f,
                Range = 8f,
                CastTime = 0.3f,
                RequiredLevel = 5
            };
        }

        protected override void ExecuteSkill(Actor target)
        {
            if (target == null) return;

            float damage = CalculateDamage(target);
            Debug.Log($"烈焰掌命中目标，造成 {damage:F1} 点伤害");

            // TODO: 应用燃烧效果
            // TODO: 生成火焰特效
            // TODO: 播放火焰音效

            isBurning = true;
            burnTimer = BurnDuration;
            burnTickTimer = 0f;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (isBurning)
            {
                burnTimer -= Time.DeltaTime;
                burnTickTimer += Time.DeltaTime;

                if (burnTickTimer >= BurnInterval)
                {
                    burnTickTimer = 0f;
                    float burnDamage = CalculateDamage(null) * BurnDamageMultiplier;
                    Debug.Log($"燃烧效果造成 {burnDamage:F1} 点伤害");
                    // TODO: 应用燃烧伤害
                }

                if (burnTimer <= 0)
                {
                    isBurning = false;
                    Debug.Log("燃烧效果结束");
                    // TODO: 清除燃烧特效
                }
            }
        }
    }

    // ============================
    // 火球术 - 远程攻击技能
    // ============================
    
    /// <summary>
    /// 火球术：投掷火球造成范围伤害
    /// 等级要求：10级
    /// 冷却时间：10秒
    /// 内力消耗：110点
    /// </summary>
    public class HuoQiuShu : SkillBase
    {
        [Tooltip("火球飞行速度")]
        public float ProjectileSpeed = 15f;
        
        [Tooltip("爆炸范围半径")]
        public float ExplosionRadius = 4f;
        
        [Tooltip("爆炸伤害衰减")]
        public bool DamageDecay = true;
        
        [Tooltip("火球特效预制体")]
        public Prefab FireballPrefab;
        
        [Tooltip("爆炸特效预制体")]
        public Prefab ExplosionEffectPrefab;

        private bool isProjectileActive = false;
        private Vector3 projectilePosition;
        private Vector3 targetPosition;
        private float travelDistance = 0f;
        private float maxDistance = 0f;

        public override void OnAwake()
        {
            base.OnAwake();
            
            Data = new SkillData
            {
                SkillId = 4002,
                SkillName = "火球术",
                Description = "投掷火球攻击目标位置，造成范围爆炸伤害",
                Element = WuxingElement.Fire,
                Type = SkillType.ActiveAttack,
                DamageMultiplier = 1.5f,
                EnergyCost = 110f,
                Cooldown = 10f,
                Range = 25f,
                CastTime = 0.8f,
                RequiredLevel = 10
            };
        }

        protected override void ExecuteSkill(Actor target)
        {
            projectilePosition = Actor.Position + new Vector3(0, 1.5f, 0);
            targetPosition = target != null ? target.Position : Actor.Position + Actor.Direction * 20f;
            
            maxDistance = Vector3.Distance(projectilePosition, targetPosition);
            travelDistance = 0f;

            Debug.Log($"火球术发射！目标位置: {targetPosition}");

            // TODO: 生成火球特效
            // TODO: 播放发射音效

            isProjectileActive = true;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (isProjectileActive)
            {
                float moveDistance = ProjectileSpeed * Time.DeltaTime;
                travelDistance += moveDistance;

                Vector3 direction = (targetPosition - projectilePosition).Normalized;
                projectilePosition += direction * moveDistance;

                // TODO: 更新火球特效位置
                // TODO: 检测碰撞

                if (travelDistance >= maxDistance)
                {
                    // 到达目标，触发爆炸
                    TriggerExplosion();
                    isProjectileActive = false;
                }
            }
        }

        private void TriggerExplosion()
        {
            float damage = CalculateDamage(null);
            Debug.Log($"火球爆炸！范围 {ExplosionRadius}米，造成 {damage:F1} 点伤害");

            // TODO: 检测范围内所有敌人
            // TODO: 应用伤害（中心满伤害，边缘衰减）
            // TODO: 生成爆炸特效
            // TODO: 播放爆炸音效
            // TODO: 相机震动

            // 伤害衰减计算示例
            // float distanceRatio = distance / ExplosionRadius;
            // float finalDamage = damage * (1.0f - distanceRatio * 0.5f);
        }
    }

    // ============================
    // 炎龙出海 - 直线攻击技能
    // ============================
    
    /// <summary>
    /// 炎龙出海：释放火龙直线攻击
    /// 等级要求：15级
    /// 冷却时间：18秒
    /// 内力消耗：160点
    /// </summary>
    public class YanLongChuHai : SkillBase
    {
        [Tooltip("火龙长度")]
        public float DragonLength = 20f;
        
        [Tooltip("火龙宽度")]
        public float DragonWidth = 3f;
        
        [Tooltip("火龙飞行速度")]
        public float DragonSpeed = 18f;
        
        [Tooltip("穿透敌人数量")]
        public int PierceCount = 5;
        
        [Tooltip("火龙特效预制体")]
        public Prefab FireDragonEffectPrefab;

        private bool isDragonActive = false;
        private float dragonDistance = 0f;
        private int enemiesHit = 0;

        public override void OnAwake()
        {
            base.OnAwake();
            
            Data = new SkillData
            {
                SkillId = 4003,
                SkillName = "炎龙出海",
                Description = "释放炽热火龙直线冲击，穿透敌人造成伤害",
                Element = WuxingElement.Fire,
                Type = SkillType.ActiveAttack,
                DamageMultiplier = 2.0f,
                EnergyCost = 160f,
                Cooldown = 18f,
                Range = 0f,
                CastTime = 1.0f,
                RequiredLevel = 15
            };
        }

        protected override void ExecuteSkill(Actor target)
        {
            float damage = CalculateDamage(target);
            Debug.Log($"炎龙出海！火龙长度 {DragonLength}米，宽度 {DragonWidth}米");

            // TODO: 生成火龙特效
            // TODO: 播放龙吟音效

            isDragonActive = true;
            dragonDistance = 0f;
            enemiesHit = 0;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (isDragonActive)
            {
                dragonDistance += DragonSpeed * Time.DeltaTime;

                // TODO: 检测火龙路径上的敌人
                // TODO: 对命中的敌人造成伤害
                // TODO: 更新火龙特效位置

                if (dragonDistance >= DragonLength || enemiesHit >= PierceCount)
                {
                    isDragonActive = false;
                    Debug.Log($"炎龙出海结束，命中 {enemiesHit} 个敌人");
                    // TODO: 移除火龙特效
                }
            }
        }
    }

    // ============================
    // 焚天灭地 - 终结技
    // ============================
    
    /// <summary>
    /// 焚天灭地：超大范围火焰爆炸
    /// 等级要求：20级
    /// 冷却时间：40秒
    /// 内力消耗：300点
    /// </summary>
    public class FenTianMieDi : SkillBase
    {
        [Tooltip("爆炸范围半径")]
        public float ExplosionRadius = 15f;
        
        [Tooltip("预警时间")]
        public float WarningTime = 2f;
        
        [Tooltip("燃烧地面持续时间")]
        public float GroundFireDuration = 8f;
        
        [Tooltip("地面燃烧伤害间隔")]
        public float GroundFireInterval = 1f;
        
        [Tooltip("地面燃烧伤害倍率")]
        public float GroundFireDamageMultiplier = 0.3f;
        
        [Tooltip("爆炸特效预制体")]
        public Prefab MassiveExplosionEffectPrefab;

        private bool isWarning = false;
        private bool isGroundFire = false;
        private float warningTimer = 0f;
        private float groundFireTimer = 0f;
        private float groundFireTickTimer = 0f;
        private Vector3 explosionCenter;

        public override void OnAwake()
        {
            base.OnAwake();
            
            Data = new SkillData
            {
                SkillId = 4004,
                SkillName = "焚天灭地",
                Description = "召唤天火降临，造成毁灭性的范围爆炸伤害",
                Element = WuxingElement.Fire,
                Type = SkillType.Ultimate,
                DamageMultiplier = 3.5f,
                EnergyCost = 300f,
                Cooldown = 40f,
                Range = 30f,
                CastTime = 2.5f,
                RequiredLevel = 20
            };
        }

        protected override void ExecuteSkill(Actor target)
        {
            explosionCenter = target != null ? target.Position : Actor.Position;
            
            Debug.Log($"焚天灭地！预警 {WarningTime} 秒后爆炸");

            // TODO: 在爆炸位置显示预警特效（红色圆圈）
            // TODO: 播放预警音效
            // TODO: 相机聚焦到爆炸中心

            isWarning = true;
            warningTimer = WarningTime;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (isWarning)
            {
                warningTimer -= Time.DeltaTime;

                if (warningTimer <= 0)
                {
                    isWarning = false;
                    TriggerExplosion();
                }
            }

            if (isGroundFire)
            {
                groundFireTimer -= Time.DeltaTime;
                groundFireTickTimer += Time.DeltaTime;

                if (groundFireTickTimer >= GroundFireInterval)
                {
                    groundFireTickTimer = 0f;
                    float groundDamage = CalculateDamage(null) * GroundFireDamageMultiplier;
                    Debug.Log($"地面燃烧造成 {groundDamage:F1} 点伤害");
                    // TODO: 对范围内敌人造成持续伤害
                }

                if (groundFireTimer <= 0)
                {
                    isGroundFire = false;
                    Debug.Log("焚天灭地效果结束");
                    // TODO: 清除地面燃烧特效
                }
            }
        }

        private void TriggerExplosion()
        {
            float damage = CalculateDamage(null);
            Debug.Log($"天火降临！范围 {ExplosionRadius}米，造成 {damage:F1} 点伤害");

            // TODO: 检测范围内所有敌人
            // TODO: 应用巨额伤害
            // TODO: 生成超大规模爆炸特效
            // TODO: 播放爆炸音效
            // TODO: 强烈相机震动
            // TODO: 屏幕闪白效果

            isGroundFire = true;
            groundFireTimer = GroundFireDuration;
            groundFireTickTimer = 0f;

            // TODO: 生成地面燃烧特效
        }
    }

    // ============================
    // 凤凰涅槃 - 复活技能（额外）
    // ============================
    
    /// <summary>
    /// 凤凰涅槃：死亡后满状态复活
    /// 等级要求：25级
    /// 冷却时间：300秒（5分钟）
    /// 内力消耗：2000点
    /// </summary>
    public class FengHuangNiePan : SkillBase
    {
        [Tooltip("复活时恢复生命百分比")]
        public float ReviveHealthPercent = 100f;
        
        [Tooltip("复活时恢复内力百分比")]
        public float ReviveEnergyPercent = 100f;
        
        [Tooltip("复活后无敌时间")]
        public float InvincibleDuration = 3f;
        
        [Tooltip("复活范围伤害")]
        public float ReviveDamageRadius = 8f;
        
        [Tooltip("复活爆发伤害倍率")]
        public float ReviveDamageMultiplier = 2.0f;
        
        [Tooltip("凤凰特效预制体")]
        public Prefab PhoenixEffectPrefab;

        private bool isReviving = false;
        private bool isInvincible = false;
        private float invincibleTimer = 0f;

        public override void OnAwake()
        {
            base.OnAwake();
            
            Data = new SkillData
            {
                SkillId = 4005,
                SkillName = "凤凰涅槃",
                Description = "死亡后化身火凤凰复活，满状态复活并对周围敌人造成伤害",
                Element = WuxingElement.Fire,
                Type = SkillType.Support,
                DamageMultiplier = 2.0f,
                EnergyCost = 2000f,
                Cooldown = 300f,
                Range = 0f,
                CastTime = 0f, // 死亡后自动触发
                RequiredLevel = 25
            };
        }

        /// <summary>
        /// 当角色死亡时自动触发
        /// </summary>
        public void TriggerRevive()
        {
            if (currentCooldown > 0)
            {
                Debug.Log("凤凰涅槃冷却中，无法复活");
                return;
            }

            Debug.Log("凤凰涅槃触发！即将复活");

            // TODO: 播放凤凰涅槃动画
            // TODO: 生成火凤凰特效

            isReviving = true;
        }

        protected override void ExecuteSkill(Actor target)
        {
            if (characterAttributes == null) return;

            // 恢复生命和内力
            characterAttributes.CurrentHealth = characterAttributes.MaxHealth * (ReviveHealthPercent / 100f);
            characterAttributes.CurrentEnergy = characterAttributes.MaxEnergy * (ReviveEnergyPercent / 100f);

            Debug.Log($"复活成功！生命 {characterAttributes.CurrentHealth:F1}，内力 {characterAttributes.CurrentEnergy:F1}");

            // 范围伤害
            float damage = CalculateDamage(null) * ReviveDamageMultiplier;
            Debug.Log($"复活爆发！范围 {ReviveDamageRadius}米，造成 {damage:F1} 点伤害");

            // TODO: 对周围敌人造成火焰伤害
            // TODO: 应用击退效果
            // TODO: 生成火焰爆发特效

            isInvincible = true;
            invincibleTimer = InvincibleDuration;
            isReviving = false;

            // 进入冷却
            currentCooldown = Data.Cooldown;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (isInvincible)
            {
                invincibleTimer -= Time.DeltaTime;
                if (invincibleTimer <= 0)
                {
                    isInvincible = false;
                    Debug.Log("无敌状态结束");
                    // TODO: 移除无敌特效
                }
            }
        }

        /// <summary>
        /// 检查是否处于无敌状态
        /// </summary>
        public bool IsInvincible()
        {
            return isInvincible;
        }
    }
}
