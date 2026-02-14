using FlaxEngine;
using Game.Character.Attributes;
using System;
using Horizon.Game.Message.Enums;

namespace Game.Combat.Skills
{
    /// <summary>
    /// 技能数据（ScriptableObject）
    /// </summary>
    public class SkillData
    {
        /// <summary>技能ID</summary>
        public int SkillId;

        /// <summary>技能名称</summary>
        public string SkillName;

        /// <summary>技能描述</summary>
        public string Description;

        /// <summary>五行属性</summary>
        public WuxingElement Element;

        /// <summary>技能类型</summary>
        public SkillType Type;

        /// <summary>基础伤害倍率</summary>
        public float DamageMultiplier = 1.0f;

        /// <summary>能量消耗</summary>
        public float EnergyCost = 50f;

        /// <summary>冷却时间（秒）</summary>
        public float Cooldown = 5f;

        /// <summary>施法范围（米）</summary>
        public float Range = 10f;

        /// <summary>施法时间（秒）</summary>
        public float CastTime = 0.5f;

        /// <summary>技能等级要求</summary>
        public int RequiredLevel = 1;

        /// <summary>技能图标路径</summary>
        public string IconPath;
    }

    /// <summary>
    /// 技能基类
    /// 所有技能的基础实现
    /// </summary>
    public abstract class SkillBase : Script
    {
        #region 技能配置

        [Header("技能数据")]
        [Tooltip("技能配置数据")]
        public SkillData Data;

        #endregion

        #region 运行时状态

        /// <summary>当前冷却剩余时间</summary>
        protected float currentCooldown = 0f;

        /// <summary>是否正在施法</summary>
        protected bool isCasting = false;

        /// <summary>施法进度</summary>
        protected float castProgress = 0f;

        /// <summary>当前施法目标</summary>
        protected Actor castTarget;

        /// <summary>角色属性引用</summary>
        protected CharacterAttributesComponent characterAttributes;

        #endregion

        public override void OnAwake()
        {
            // 获取角色属性组件
            characterAttributes = Actor.GetScript<CharacterAttributesComponent>();
        }

        /// <summary>
        /// 尝试施放技能
        /// </summary>
        public bool TryCast(Actor target = null)
        {
            // 检查冷却
            if (currentCooldown > 0)
            {
                Debug.LogWarning($"技能 {Data.SkillName} 冷却中，剩余 {currentCooldown:F1} 秒");
                return false;
            }

            // 检查能量
            if (characterAttributes != null && !characterAttributes.ConsumeEnergy(Data.EnergyCost))
            {
                Debug.LogWarning($"能量不足，无法释放 {Data.SkillName}");
                return false;
            }

            // 开始施法
            StartCast(target);
            return true;
        }

        /// <summary>
        /// 开始施法
        /// </summary>
        protected virtual void StartCast(Actor target)
        {
            isCasting = true;
            castProgress = 0f;
            castTarget = target;
            
            Debug.Log($"开始施放技能：{Data.SkillName} ({Data.Element})");
            
            // 播放施法动画
            PlayCastAnimation();

            // 显示施法特效
            ShowCastEffect();
        }

        /// <summary>
        /// 完成施法
        /// </summary>
        protected virtual void CompleteCast(Actor target)
        {
            isCasting = false;
            castTarget = null;
            currentCooldown = Data.Cooldown;
            
            // 执行技能效果
            ExecuteSkill(target);
            
            Debug.Log($"技能释放完成：{Data.SkillName}");
        }

        /// <summary>
        /// 执行技能效果（子类实现）
        /// </summary>
        protected abstract void ExecuteSkill(Actor target);

        /// <summary>
        /// 公共接口：执行技能效果
        /// </summary>
        public void ExecuteSkillPublic(Actor target)
        {
            ExecuteSkill(target);
        }

        /// <summary>
        /// 计算技能伤害
        /// </summary>
        protected float CalculateDamage(Actor target)
        {
            if (characterAttributes == null) return 0f;

            // 基础伤害
            float baseDamage = characterAttributes.PhysicalAttack * Data.DamageMultiplier;

            // 五行亲和度加成（每10点+0.5%）
            int affinity = 0;
            if (Data.Element != WuxingElement.None)
            {
                affinity = Data.Element switch
                {
                    WuxingElement.Metal => characterAttributes.MetalAffinity,
                    WuxingElement.Wood => characterAttributes.WoodAffinity,
                    WuxingElement.Water => characterAttributes.WaterAffinity,
                    WuxingElement.Fire => characterAttributes.FireAffinity,
                    WuxingElement.Earth => characterAttributes.EarthAffinity,
                    _ => 0
                };
            }
            float affinityBonus = 1.0f + (affinity / 10) * 0.005f;
            
            float finalDamage = baseDamage * affinityBonus;

            // 应用五行相克系数
            if (target != null)
            {
                var targetWuxing = target.GetScript<Character.Attributes.CharacterAttributesComponent>();
                if (targetWuxing != null)
                {
                    // 获取目标的五行属性（需要在CharacterAttributesComponent中添加五行属性）
                    // 计算相克系数
                    float wuxingMultiplier = CalculateWuxingCounterMultiplier(WuxingElement.Earth, WuxingElement.None);
                    finalDamage *= wuxingMultiplier;
                    
                    Debug.Log($"Wuxing multiplier applied: {wuxingMultiplier}x (Attacker: {Data.Element})");
                }
            }

            // 应用目标防御
            // 简化处理：防御减免 = 防御 / (防御 + 100)
            // 实际需要从target获取防御值

            return finalDamage;
        }

        /// <summary>
        /// 计算五行相克系数
        /// 金克木、木克土、土克水、水克火、火克金
        /// </summary>
        private float CalculateWuxingCounterMultiplier(WuxingElement attacker, WuxingElement defender)
        {
            if (attacker == WuxingElement.None || defender == WuxingElement.None)
                return 1.0f;

            // 相克关系
            bool counters = (attacker == WuxingElement.Metal && defender == WuxingElement.Wood) ||
                           (attacker == WuxingElement.Wood && defender == WuxingElement.Earth) ||
                           (attacker == WuxingElement.Earth && defender == WuxingElement.Water) ||
                           (attacker == WuxingElement.Water && defender == WuxingElement.Fire) ||
                           (attacker == WuxingElement.Fire && defender == WuxingElement.Metal);

            // 相生关系
            bool generates = (attacker == WuxingElement.Metal && defender == WuxingElement.Water) ||
                            (attacker == WuxingElement.Water && defender == WuxingElement.Wood) ||
                            (attacker == WuxingElement.Wood && defender == WuxingElement.Fire) ||
                            (attacker == WuxingElement.Fire && defender == WuxingElement.Earth) ||
                            (attacker == WuxingElement.Earth && defender == WuxingElement.Metal);

            if (counters)
                return 1.25f; // 相克增伤25%
            else if (generates)
                return 0.8f;  // 相生减伤20%
            else if (attacker == defender)
                return 1.0f;  // 相同无加成
            else
                return 1.0f;  // 其他情况无加成
        }

        /// <summary>
        /// 播放施法动画
        /// </summary>
        private void PlayCastAnimation()
        {
            var animController = Actor.GetScript<SkillAnimationController>();
            if (animController != null)
            {
                // 优先使用技能ID播放动画（自动查找映射配置）
                if (Data.SkillId > 0)
                {
                    animController.PlaySkillAnimationById(Data.SkillId);
                }
                else
                {
                    // 备用方案：使用技能名称
                    string animationName = $"Skill_{Data.SkillName}";
                    animController.PlaySkillAnimation(Data.SkillName, animationName, 
                        Data.CastTime * 0.3f, Data.CastTime * 0.4f, Data.CastTime * 0.3f);
                }
            }
            else
            {
                // 如果没有动画控制器，直接播放基础动画
                var animatedModel = Actor.GetChild<AnimatedModel>();
                if (animatedModel != null)
                {
                    // animatedModel.Play("Cast");
                    Debug.Log($"Playing cast animation for {Data.SkillName}");
                }
            }
        }

        /// <summary>
        /// 显示施法特效
        /// </summary>
        private void ShowCastEffect()
        {
            var effectManager = Scene.FindScript<Combat.Effects.SkillEffectManager>();
            if (effectManager != null)
            {
                // 根据技能属性选择特效
                string effectName = $"Cast_{Data.Element}";
                effectManager.PlayCastEffect(effectName, Actor);
                Debug.Log($"Playing cast effect: {effectName}");
            }
        }


        public override void OnUpdate()
        {
            float deltaTime = Time.DeltaTime;

            // 更新冷却
            if (currentCooldown > 0)
            {
                currentCooldown = Mathf.Max(0, currentCooldown - deltaTime);
            }

            // 更新施法进度
            if (isCasting)
            {
                castProgress += deltaTime;
                
                if (castProgress >= Data.CastTime)
                {
                    CompleteCast(castTarget);
                }
            }
        }

        /// <summary>
        /// 获取冷却进度（0-1）
        /// </summary>
        public float GetCooldownProgress()
        {
            if (Data.Cooldown <= 0) return 1.0f;
            return 1.0f - (currentCooldown / Data.Cooldown);
        }

        /// <summary>
        /// 是否就绪
        /// </summary>
        public bool IsReady()
        {
            return currentCooldown <= 0 && !isCasting;
        }
    }
}
