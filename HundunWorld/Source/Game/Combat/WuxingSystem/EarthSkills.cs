using System;
using FlaxEngine;
using Game.Character.Attributes;
using Game.Combat.Skills;

namespace Game.Combat.WuxingSystem
{
    /// <summary>
    /// 土系技能集合 - 特性：防御、控制、厚重
    /// 土系代表稳固、承载和防护，技能侧重防御和地面控制
    /// </summary>

    // ============================
    // 岩甲术 - 防御增益技能
    // ============================
    
    /// <summary>
    /// 岩甲术：提升自身防御力
    /// 等级要求：5级
    /// 冷却时间：20秒
    /// 内力消耗：80点
    /// </summary>
    public class YanJiaShu : SkillBase
    {
        [Tooltip("持续时间")]
        public float Duration = 10f;
        
        [Tooltip("防御力加成百分比")]
        public float DefenseBonus = 50f;
        
        [Tooltip("减速百分比（副作用）")]
        public float SlowPercent = 20f;
        
        [Tooltip("岩石护甲特效预制体")]
        public Prefab RockArmorEffectPrefab;

        private bool isActive = false;
        private float activeTimer = 0f;

        public override void OnAwake()
        {
            base.OnAwake();
            
            Data = new SkillData
            {
                SkillId = 5001,
                SkillName = "岩甲术",
                Description = "凝聚岩石护甲，大幅提升防御力，但降低移动速度",
                Element = WuxingElement.Earth,
                Type = SkillType.Support,
                DamageMultiplier = 0f,
                EnergyCost = 80f,
                Cooldown = 20f,
                Range = 0f,
                CastTime = 0.5f,
                RequiredLevel = 5
            };
        }

        protected override void ExecuteSkill(Actor target)
        {
            Debug.Log($"岩甲术激活！防御+{DefenseBonus}%，持续 {Duration} 秒");

            // TODO: 应用防御力加成
            // TODO: 应用移动速度减速
            // TODO: 生成岩石护甲特效
            // TODO: 播放岩石凝聚音效

            isActive = true;
            activeTimer = Duration;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (isActive)
            {
                activeTimer -= Time.DeltaTime;

                if (activeTimer <= 0)
                {
                    isActive = false;
                    Debug.Log("岩甲术效果结束");
                    // TODO: 移除防御力加成
                    // TODO: 移除移动速度减速
                    // TODO: 清除岩石护甲特效
                }
            }
        }

        /// <summary>
        /// 获取当前防御加成
        /// </summary>
        public float GetDefenseBonus()
        {
            return isActive ? DefenseBonus : 0f;
        }
    }

    // ============================
    // 地刺术 - 攻击技能
    // ============================
    
    /// <summary>
    /// 地刺术：从地面突刺敌人
    /// 等级要求：10级
    /// 冷却时间：12秒
    /// 内力消耗：100点
    /// </summary>
    public class DiCiShu : SkillBase
    {
        [Tooltip("地刺数量")]
        public int SpikeCount = 5;
        
        [Tooltip("地刺间隔")]
        public float SpikeInterval = 0.2f;
        
        [Tooltip("地刺范围半径")]
        public float SpikeRadius = 8f;
        
        [Tooltip("击飞高度")]
        public float LaunchHeight = 3f;
        
        [Tooltip("地刺特效预制体")]
        public Prefab EarthSpikeEffectPrefab;

        private int currentSpikeIndex = 0;
        private float spikeTimer = 0f;
        private bool isSpawningSpikes = false;

        public override void OnAwake()
        {
            base.OnAwake();
            
            Data = new SkillData
            {
                SkillId = 5002,
                SkillName = "地刺术",
                Description = "从地面召唤岩石尖刺连续攻击敌人",
                Element = WuxingElement.Earth,
                Type = SkillType.ActiveAttack,
                DamageMultiplier = 1.4f,
                EnergyCost = 100f,
                Cooldown = 12f,
                Range = 15f,
                CastTime = 0.5f,
                RequiredLevel = 10
            };
        }

        protected override void ExecuteSkill(Actor target)
        {
            Debug.Log($"地刺术！连续 {SpikeCount} 次地刺攻击");

            // TODO: 播放施法动画
            // TODO: 播放地面震动音效

            isSpawningSpikes = true;
            currentSpikeIndex = 0;
            spikeTimer = 0f;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (isSpawningSpikes)
            {
                spikeTimer += Time.DeltaTime;

                if (spikeTimer >= SpikeInterval)
                {
                    spikeTimer = 0f;
                    SpawnSpike();
                    currentSpikeIndex++;

                    if (currentSpikeIndex >= SpikeCount)
                    {
                        isSpawningSpikes = false;
                        Debug.Log("地刺术结束");
                    }
                }
            }
        }

        private void SpawnSpike()
        {
            float damage = CalculateDamage(null);
            Debug.Log($"第 {currentSpikeIndex + 1} 道地刺，造成 {damage:F1} 点伤害");

            // TODO: 在随机位置生成地刺
            // TODO: 检测地刺位置的敌人
            // TODO: 应用伤害和击飞效果
            // TODO: 生成地刺特效
            // TODO: 播放地刺破土音效
        }
    }

    // ============================
    // 山岳护盾 - 护盾技能
    // ============================
    
    /// <summary>
    /// 山岳护盾：生成护盾吸收伤害
    /// 等级要求：15级
    /// 冷却时间：30秒
    /// 内力消耗：150点
    /// </summary>
    public class ShanYueHuDun : SkillBase
    {
        [Tooltip("护盾值（基于最大生命值百分比）")]
        public float ShieldPercent = 40f;
        
        [Tooltip("护盾持续时间")]
        public float ShieldDuration = 8f;
        
        [Tooltip("护盾破碎时反伤百分比")]
        public float ReflectDamagePercent = 30f;
        
        [Tooltip("护盾特效预制体")]
        public Prefab ShieldEffectPrefab;

        private bool isShieldActive = false;
        private float shieldTimer = 0f;
        private float currentShieldValue = 0f;
        private float maxShieldValue = 0f;

        public override void OnAwake()
        {
            base.OnAwake();
            
            Data = new SkillData
            {
                SkillId = 5003,
                SkillName = "山岳护盾",
                Description = "生成坚固护盾吸收伤害，护盾破碎时反伤周围敌人",
                Element = WuxingElement.Earth,
                Type = SkillType.Support,
                DamageMultiplier = 0f,
                EnergyCost = 150f,
                Cooldown = 30f,
                Range = 0f,
                CastTime = 0.8f,
                RequiredLevel = 15
            };
        }

        protected override void ExecuteSkill(Actor target)
        {
            if (characterAttributes != null)
            {
                maxShieldValue = characterAttributes.MaxHealth * (ShieldPercent / 100f);
                currentShieldValue = maxShieldValue;

                Debug.Log($"山岳护盾生成！护盾值 {currentShieldValue:F1}");

                // TODO: 生成护盾特效（金黄色岩石护盾）
                // TODO: 播放护盾激活音效

                isShieldActive = true;
                shieldTimer = ShieldDuration;
            }
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (isShieldActive)
            {
                shieldTimer -= Time.DeltaTime;

                if (currentShieldValue <= 0)
                {
                    // 护盾破碎
                    ShieldBreak();
                }
                else if (shieldTimer <= 0)
                {
                    // 护盾到期
                    isShieldActive = false;
                    Debug.Log("山岳护盾到期");
                    // TODO: 清除护盾特效
                }
            }
        }

        /// <summary>
        /// 护盾吸收伤害
        /// </summary>
        public float AbsorbDamage(float incomingDamage)
        {
            if (!isShieldActive) return incomingDamage;

            float absorbed = Math.Min(currentShieldValue, incomingDamage);
            currentShieldValue -= absorbed;
            float remainingDamage = incomingDamage - absorbed;

            Debug.Log($"护盾吸收 {absorbed:F1} 点伤害，剩余护盾 {currentShieldValue:F1}");

            return remainingDamage;
        }

        private void ShieldBreak()
        {
            isShieldActive = false;
            Debug.Log("山岳护盾破碎！反伤周围敌人");

            // 计算反伤
            float reflectDamage = maxShieldValue * (ReflectDamagePercent / 100f);

            // TODO: 检测周围敌人
            // TODO: 对周围敌人造成反伤
            // TODO: 生成护盾破碎特效
            // TODO: 播放破碎音效
            // TODO: 相机震动
        }

        /// <summary>
        /// 获取当前护盾值
        /// </summary>
        public float GetCurrentShield()
        {
            return isShieldActive ? currentShieldValue : 0f;
        }
    }

    // ============================
    // 地动山摇 - 范围控制技能
    // ============================
    
    /// <summary>
    /// 地动山摇：范围震荡击飞敌人
    /// 等级要求：20级
    /// 冷却时间：35秒
    /// 内力消耗：250点
    /// </summary>
    public class DiDongShanYao : SkillBase
    {
        [Tooltip("震荡范围半径")]
        public float ShockwaveRadius = 12f;
        
        [Tooltip("击飞高度")]
        public float LaunchHeight = 5f;
        
        [Tooltip("眩晕持续时间")]
        public float StunDuration = 2f;
        
        [Tooltip("震荡波扩散速度")]
        public float ShockwaveSpeed = 15f;
        
        [Tooltip("地震特效预制体")]
        public Prefab EarthquakeEffectPrefab;

        private bool isShockwaveActive = false;
        private float shockwaveDistance = 0f;

        public override void OnAwake()
        {
            base.OnAwake();
            
            Data = new SkillData
            {
                SkillId = 5004,
                SkillName = "地动山摇",
                Description = "猛击地面引发地震，击飞并眩晕范围内所有敌人",
                Element = WuxingElement.Earth,
                Type = SkillType.Control,
                DamageMultiplier = 2.2f,
                EnergyCost = 250f,
                Cooldown = 35f,
                Range = 0f,
                CastTime = 1.5f,
                RequiredLevel = 20
            };
        }

        protected override void ExecuteSkill(Actor target)
        {
            float damage = CalculateDamage(target);
            Debug.Log($"地动山摇！范围 {ShockwaveRadius}米，造成 {damage:F1} 点伤害");

            // TODO: 播放猛击地面动画
            // TODO: 生成地震波特效
            // TODO: 播放地震音效
            // TODO: 强烈相机震动

            isShockwaveActive = true;
            shockwaveDistance = 0f;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (isShockwaveActive)
            {
                shockwaveDistance += ShockwaveSpeed * Time.DeltaTime;

                // TODO: 检测震荡波范围内的敌人
                // TODO: 对敌人应用击飞效果
                // TODO: 应用眩晕效果
                // TODO: 更新震荡波特效

                if (shockwaveDistance >= ShockwaveRadius)
                {
                    isShockwaveActive = false;
                    Debug.Log("地动山摇结束");
                    // TODO: 清除震荡波特效
                }
            }
        }
    }

    // ============================
    // 泰山压顶 - 单体终结技（额外）
    // ============================
    
    /// <summary>
    /// 泰山压顶：召唤巨石从天而降
    /// 等级要求：18级
    /// 冷却时间：28秒
    /// 内力消耗：200点
    /// </summary>
    public class TaiShanYaDing : SkillBase
    {
        [Tooltip("巨石下落延迟")]
        public float FallDelay = 1.5f;
        
        [Tooltip("巨石冲击范围")]
        public float ImpactRadius = 5f;
        
        [Tooltip("压制持续时间")]
        public float SuppressDuration = 3f;
        
        [Tooltip("压制期间减速百分比")]
        public float SlowPercent = 80f;
        
        [Tooltip("巨石特效预制体")]
        public Prefab BoulderEffectPrefab;

        private bool isWarning = false;
        private bool isSuppressing = false;
        private float warningTimer = 0f;
        private float suppressTimer = 0f;
        private Vector3 impactPosition;

        public override void OnAwake()
        {
            base.OnAwake();
            
            Data = new SkillData
            {
                SkillId = 5005,
                SkillName = "泰山压顶",
                Description = "召唤巨大岩石从天而降，压制目标区域的敌人",
                Element = WuxingElement.Earth,
                Type = SkillType.ActiveAttack,
                DamageMultiplier = 2.5f,
                EnergyCost = 200f,
                Cooldown = 28f,
                Range = 20f,
                CastTime = 1.0f,
                RequiredLevel = 18
            };
        }

        protected override void ExecuteSkill(Actor target)
        {
            impactPosition = target != null ? target.Position : Actor.Position + Actor.Direction * 10f;
            
            Debug.Log($"泰山压顶！巨石将在 {FallDelay} 秒后落下");

            // TODO: 在目标位置显示预警圈
            // TODO: 在空中生成巨石特效
            // TODO: 播放预警音效

            isWarning = true;
            warningTimer = FallDelay;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (isWarning)
            {
                warningTimer -= Time.DeltaTime;

                // TODO: 更新巨石下落动画

                if (warningTimer <= 0)
                {
                    isWarning = false;
                    TriggerImpact();
                }
            }

            if (isSuppressing)
            {
                suppressTimer -= Time.DeltaTime;

                if (suppressTimer <= 0)
                {
                    isSuppressing = false;
                    Debug.Log("泰山压顶压制效果结束");
                    // TODO: 移除压制效果
                    // TODO: 移除巨石模型
                }
            }
        }

        private void TriggerImpact()
        {
            float damage = CalculateDamage(null);
            Debug.Log($"巨石砸落！范围 {ImpactRadius}米，造成 {damage:F1} 点伤害");

            // TODO: 检测冲击范围内的敌人
            // TODO: 应用伤害
            // TODO: 应用压制效果（大幅减速）
            // TODO: 生成冲击波特效
            // TODO: 播放砸落音效
            // TODO: 强烈相机震动
            // TODO: 在地面生成巨石模型

            isSuppressing = true;
            suppressTimer = SuppressDuration;
        }
    }

    // ============================
    // 厚土载物 - 终极防御技能（额外）
    // ============================
    
    /// <summary>
    /// 厚土载物：免疫所有控制效果，大幅提升防御
    /// 等级要求：25级
    /// 冷却时间：120秒
    /// 内力消耗：500点
    /// </summary>
    public class HouTuZaiWu : SkillBase
    {
        [Tooltip("持续时间")]
        public float Duration = 10f;
        
        [Tooltip("防御力加成百分比")]
        public float DefenseBonus = 100f;
        
        [Tooltip("伤害减免百分比")]
        public float DamageReduction = 50f;
        
        [Tooltip("生命恢复速率")]
        public float HealthRegenRate = 5f;
        
        [Tooltip("大地之力特效预制体")]
        public Prefab EarthPowerEffectPrefab;

        private bool isActive = false;
        private float activeTimer = 0f;

        public override void OnAwake()
        {
            base.OnAwake();
            
            Data = new SkillData
            {
                SkillId = 5006,
                SkillName = "厚土载物",
                Description = "与大地融为一体，免疫控制，大幅提升防御并持续恢复生命",
                Element = WuxingElement.Earth,
                Type = SkillType.Support,
                DamageMultiplier = 0f,
                EnergyCost = 500f,
                Cooldown = 120f,
                Range = 0f,
                CastTime = 1.5f,
                RequiredLevel = 25
            };
        }

        protected override void ExecuteSkill(Actor target)
        {
            Debug.Log($"厚土载物激活！持续 {Duration} 秒");

            // TODO: 应用控制免疫
            // TODO: 应用防御加成
            // TODO: 应用伤害减免
            // TODO: 生成大地之力特效（金色护体光芒）
            // TODO: 播放大地之力音效

            isActive = true;
            activeTimer = Duration;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (isActive)
            {
                activeTimer -= Time.DeltaTime;

                // 持续恢复生命
                if (characterAttributes != null)
                {
                    float healthRegen = HealthRegenRate * characterAttributes.MaxHealth / 100f * Time.DeltaTime;
                    characterAttributes.Heal(healthRegen);
                }

                if (activeTimer <= 0)
                {
                    isActive = false;
                    Debug.Log("厚土载物效果结束");
                    // TODO: 移除所有增益
                    // TODO: 清除特效
                }
            }
        }

        /// <summary>
        /// 检查是否免疫控制
        /// </summary>
        public bool IsControlImmune()
        {
            return isActive;
        }

        /// <summary>
        /// 获取伤害减免百分比
        /// </summary>
        public float GetDamageReduction()
        {
            return isActive ? DamageReduction : 0f;
        }
    }
}
