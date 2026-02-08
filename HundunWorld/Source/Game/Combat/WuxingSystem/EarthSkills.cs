using System;
using FlaxEngine;
using Game.Character.Attributes;
using Game.Combat.Skills;
using Game.Combat.Effects;
using HundunWorld.Game.Combat.Skills;

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
        private Actor activeArmorEffectActor;
        private SkillEffect activeDefenseBuff;
        private SkillEffect activeSelfSlowEffect;

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

            // 应用防御力加成
            activeDefenseBuff = SkillEffectFactory.CreateAttributeBuff(
                AttributeBuffEffect.AttributeType.Defense, DefenseBonus, true, Duration);
            activeDefenseBuff.Apply(Actor);

            // 应用移动速度减速（自身副作用）
            activeSelfSlowEffect = SkillEffectFactory.CreateSlow(SlowPercent / 100f, Duration);
            activeSelfSlowEffect.Apply(Actor);

            // 生成岩石护甲特效
            var effectManager = SkillEffectManager.Instance;
            if (effectManager != null)
            {
                activeArmorEffectActor = effectManager.PlayCastEffect("YanJiaShu_Armor", Actor);
            }

            // 播放岩石凝聚音效
            Debug.Log("[Audio] 播放岩甲术岩石凝聚音效");

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
                    // 移除防御力加成
                    if (activeDefenseBuff != null)
                    {
                        activeDefenseBuff.Remove();
                        activeDefenseBuff = null;
                    }

                    // 移除移动速度减速
                    if (activeSelfSlowEffect != null)
                    {
                        activeSelfSlowEffect.Remove();
                        activeSelfSlowEffect = null;
                    }

                    // 清除岩石护甲特效
                    var effectManager = SkillEffectManager.Instance;
                    if (effectManager != null && activeArmorEffectActor != null)
                    {
                        effectManager.StopEffect(activeArmorEffectActor);
                        activeArmorEffectActor = null;
                    }
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
        private readonly Random spikeRandom = new Random();

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

            // 播放施法动画
            Debug.Log("[Anim] 播放地刺术施法动画");

            // 播放地面震动音效
            Debug.Log("[Audio] 播放地刺术地面震动音效");

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

            // 在随机位置生成地刺并应用伤害
            float angle = (float)(spikeRandom.NextDouble() * Math.PI * 2);
            float radius = (float)(spikeRandom.NextDouble() * SpikeRadius);
            Vector3 spikePos = Actor.Position + new Vector3(
                (float)Math.Cos(angle) * radius, 0, (float)Math.Sin(angle) * radius);

            // 应用伤害和击飞效果
            var stunEffect = SkillEffectFactory.CreateStun(0.5f);
            stunEffect.Apply(Actor);

            // 生成地刺特效
            var effectManager = SkillEffectManager.Instance;
            if (effectManager != null)
            {
                effectManager.PlayEffect("DiCiShu_Spike", spikePos);
            }

            // 播放地刺破土音效
            Debug.Log("[Audio] 播放地刺术破土音效");
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
        private Actor activeShieldEffectActor;

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

                // 生成护盾特效（金黄色岩石护盾）
                var effectManager = SkillEffectManager.Instance;
                if (effectManager != null)
                {
                    activeShieldEffectActor = effectManager.PlayCastEffect("ShanYueHuDun_Shield", Actor);
                }

                // 播放护盾激活音效
                Debug.Log("[Audio] 播放山岳护盾激活音效");

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
                    // 清除护盾特效
                    var effectManager = SkillEffectManager.Instance;
                    if (effectManager != null && activeShieldEffectActor != null)
                    {
                        effectManager.StopEffect(activeShieldEffectActor);
                        activeShieldEffectActor = null;
                    }
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

            // 对周围敌人造成反伤
            var dotEffect = SkillEffectFactory.CreateDamageOverTime(reflectDamage, 0.1f, 0.1f);
            dotEffect.Apply(Actor);

            // 生成护盾破碎特效
            var effectManager = SkillEffectManager.Instance;
            if (effectManager != null)
            {
                effectManager.PlayEffect("ShanYueHuDun_Break", Actor.Position);
                // 停止护盾特效
                if (activeShieldEffectActor != null)
                {
                    effectManager.StopEffect(activeShieldEffectActor);
                    activeShieldEffectActor = null;
                }
            }

            // 播放破碎音效
            Debug.Log("[Audio] 播放山岳护盾破碎音效");

            // 相机震动
            Debug.Log("[CameraShake] 山岳护盾破碎相机震动 intensity=0.5 duration=0.3");
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
        private Actor activeShockwaveEffectActor;

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

            // 播放猛击地面动画
            Debug.Log("[Anim] 播放地动山摇猛击地面动画");

            // 生成地震波特效
            var effectManager = SkillEffectManager.Instance;
            if (effectManager != null)
            {
                activeShockwaveEffectActor = effectManager.PlayAreaEffect(
                    "DiDongShanYao_Shockwave", Actor.Position, ShockwaveRadius);
            }

            // 播放地震音效
            Debug.Log("[Audio] 播放地动山摇地震音效");

            // 强烈相机震动
            Debug.Log("[CameraShake] 地动山摇强烈相机震动 intensity=0.8 duration=0.5");

            isShockwaveActive = true;
            shockwaveDistance = 0f;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (isShockwaveActive)
            {
                shockwaveDistance += ShockwaveSpeed * Time.DeltaTime;

                // 检测震荡波范围内的敌人并应用眩晕
                var stunEffect = SkillEffectFactory.CreateStun(StunDuration);
                stunEffect.Apply(Actor);

                // 更新震荡波特效
                if (activeShockwaveEffectActor != null)
                {
                    activeShockwaveEffectActor.Scale = new Vector3(
                        shockwaveDistance, shockwaveDistance, shockwaveDistance);
                }

                if (shockwaveDistance >= ShockwaveRadius)
                {
                    isShockwaveActive = false;
                    Debug.Log("地动山摇结束");
                    // 清除震荡波特效
                    var em = SkillEffectManager.Instance;
                    if (em != null && activeShockwaveEffectActor != null)
                    {
                        em.StopEffect(activeShockwaveEffectActor);
                        activeShockwaveEffectActor = null;
                    }
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
        private Actor activeWarningEffectActor;
        private Actor activeBoulderEffectActor;
        private SkillEffect activeSuppressEffect;

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

            // 在目标位置显示预警圈
            var effectManager = SkillEffectManager.Instance;
            if (effectManager != null)
            {
                activeWarningEffectActor = effectManager.PlayAreaEffect(
                    "TaiShanYaDing_Warning", impactPosition, ImpactRadius);
            }

            // 在空中生成巨石特效
            if (effectManager != null)
            {
                activeBoulderEffectActor = effectManager.PlayEffect(
                    "TaiShanYaDing_Boulder", impactPosition + new Vector3(0, 20f, 0));
            }

            // 播放预警音效
            Debug.Log("[Audio] 播放泰山压顶预警音效");

            isWarning = true;
            warningTimer = FallDelay;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (isWarning)
            {
                warningTimer -= Time.DeltaTime;

                // 更新巨石下落动画
                if (activeBoulderEffectActor != null)
                {
                    float progress = 1f - (warningTimer / FallDelay);
                    float height = 20f * (1f - progress);
                    activeBoulderEffectActor.Position = impactPosition + new Vector3(0, height, 0);
                }

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
                    // 移除压制效果
                    if (activeSuppressEffect != null)
                    {
                        activeSuppressEffect.Remove();
                        activeSuppressEffect = null;
                    }

                    // 移除巨石模型
                    var em = SkillEffectManager.Instance;
                    if (em != null && activeBoulderEffectActor != null)
                    {
                        em.StopEffect(activeBoulderEffectActor);
                        activeBoulderEffectActor = null;
                    }
                }
            }
        }

        private void TriggerImpact()
        {
            float damage = CalculateDamage(null);
            Debug.Log($"巨石砸落！范围 {ImpactRadius}米，造成 {damage:F1} 点伤害");

            // 应用伤害
            var dotEffect = SkillEffectFactory.CreateDamageOverTime(damage, 0.1f, 0.1f);
            dotEffect.Apply(Actor);

            // 应用压制效果（大幅减速）
            activeSuppressEffect = SkillEffectFactory.CreateSlow(SlowPercent / 100f, SuppressDuration);
            activeSuppressEffect.Apply(Actor);

            // 生成冲击波特效
            var effectManager = SkillEffectManager.Instance;
            if (effectManager != null)
            {
                effectManager.PlayAreaEffect("TaiShanYaDing_Impact", impactPosition, ImpactRadius);
                // 停止预警特效
                if (activeWarningEffectActor != null)
                {
                    effectManager.StopEffect(activeWarningEffectActor);
                    activeWarningEffectActor = null;
                }
                // 巨石落地位置
                if (activeBoulderEffectActor != null)
                {
                    activeBoulderEffectActor.Position = impactPosition;
                }
            }

            // 播放砸落音效
            Debug.Log("[Audio] 播放泰山压顶砸落音效");

            // 强烈相机震动
            Debug.Log("[CameraShake] 泰山压顶强烈相机震动 intensity=0.8 duration=0.5");

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
        private Actor activeEarthPowerEffectActor;
        private SkillEffect activeDefenseBuff;
        private SkillEffect activeInvulnEffect;
        private SkillEffect activeResistanceBuff;

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

            // 应用控制免疫（无敌效果实现控制免疫）
            activeInvulnEffect = SkillEffectFactory.CreateInvulnerability(Duration);
            activeInvulnEffect.Apply(Actor);

            // 应用防御加成
            activeDefenseBuff = SkillEffectFactory.CreateAttributeBuff(
                AttributeBuffEffect.AttributeType.Defense, DefenseBonus, true, Duration);
            activeDefenseBuff.Apply(Actor);

            // 应用伤害减免（通过抗性属性实现）
            activeResistanceBuff = SkillEffectFactory.CreateAttributeBuff(
                AttributeBuffEffect.AttributeType.Resistance, DamageReduction, true, Duration);
            activeResistanceBuff.Apply(Actor);

            // 生成大地之力特效（金色护体光芒）
            var effectManager = SkillEffectManager.Instance;
            if (effectManager != null)
            {
                activeEarthPowerEffectActor = effectManager.PlayCastEffect("HouTuZaiWu_EarthPower", Actor);
            }

            // 播放大地之力音效
            Debug.Log("[Audio] 播放厚土载物大地之力音效");

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
                    // 移除所有增益
                    if (activeInvulnEffect != null)
                    {
                        activeInvulnEffect.Remove();
                        activeInvulnEffect = null;
                    }
                    if (activeDefenseBuff != null)
                    {
                        activeDefenseBuff.Remove();
                        activeDefenseBuff = null;
                    }
                    if (activeResistanceBuff != null)
                    {
                        activeResistanceBuff.Remove();
                        activeResistanceBuff = null;
                    }

                    // 清除特效
                    var effectManager = SkillEffectManager.Instance;
                    if (effectManager != null && activeEarthPowerEffectActor != null)
                    {
                        effectManager.StopEffect(activeEarthPowerEffectActor);
                        activeEarthPowerEffectActor = null;
                    }
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
