using FlaxEngine;
using Game.Character.Attributes;
using Game.Combat.Skills;
using Game.Combat.Effects;
using HundunWorld.Game.Combat.Skills;

namespace Game.Combat.WuxingSystem
{
    /// <summary>
    /// 金系技能：金刚掌
    /// 基础金系攻击技能，造成150%物理伤害，附带破甲效果
    /// </summary>
    public class JinGangZhang : SkillBase
    {
        [Header("技能特效")]
        [Tooltip("破甲效果持续时间")]
        public float ArmorBreakDuration = 5f;

        [Tooltip("破甲效果数值（降低防御百分比）")]
        public float ArmorBreakPercent = 15f;

        private Actor activeHitEffectActor;

        protected override void ExecuteSkill(Actor target)
        {
            if (target == null) return;

            // 计算伤害
            float damage = CalculateDamage(target);
            
            Debug.Log($"金刚掌命中目标，造成 {damage:F1} 点伤害");

            // 应用破甲效果
            var armorBreak = SkillEffectFactory.CreateAttributeBuff(
                AttributeBuffEffect.AttributeType.Defense, -ArmorBreakPercent, true, ArmorBreakDuration);
            armorBreak.Apply(target);

            // 播放打击特效
            var effectManager = SkillEffectManager.Instance;
            if (effectManager != null)
            {
                activeHitEffectActor = effectManager.PlayHitEffect("JinGangZhang_Hit", target.Position);
            }

            // 播放音效
            Debug.Log("[Audio] 播放金刚掌打击音效");
        }
    }

    /// <summary>
    /// 金系技能：金蛇剑法
    /// 快速三连斩，每击造成80%伤害
    /// </summary>
    public class JinSheSwordArt : SkillBase
    {
        [Header("连击配置")]
        [Tooltip("连击次数")]
        public int ComboCount = 3;

        [Tooltip("每击伤害系数")]
        public float ComboHitMultiplier = 0.8f;

        [Tooltip("连击间隔（秒）")]
        public float ComboInterval = 0.3f;

        private int currentComboHit = 0;
        private float comboTimer = 0f;
        private Actor activeComboEffectActor;

        protected override void ExecuteSkill(Actor target)
        {
            // 开始连击
            currentComboHit = 0;
            comboTimer = 0f;
            
            Debug.Log("金蛇剑法 - 开始三连斩");
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            // 处理连击
            if (currentComboHit > 0 && currentComboHit < ComboCount)
            {
                comboTimer += Time.DeltaTime;
                
                if (comboTimer >= ComboInterval)
                {
                    PerformComboHit();
                    comboTimer = 0f;
                }
            }
        }

        private void PerformComboHit()
        {
            currentComboHit++;
            
            float damage = CalculateDamage(null) * ComboHitMultiplier;
            Debug.Log($"金蛇剑法 第{currentComboHit}击，伤害: {damage:F1}");

            // 播放连击动画
            Debug.Log($"[Anim] 播放金蛇剑法第{currentComboHit}击动画");

            // 生成剑气特效
            var effectManager = SkillEffectManager.Instance;
            if (effectManager != null)
            {
                activeComboEffectActor = effectManager.PlayEffect(
                    "JinSheSwordArt_Hit", Actor.Position + Actor.Direction * 2f);
            }
        }
    }

    /// <summary>
    /// 金系技能：金钟罩
    /// 5秒内减少50%受到的伤害
    /// </summary>
    public class JinZhongZhao : SkillBase
    {
        [Header("防御配置")]
        [Tooltip("伤害减免百分比")]
        public float DamageReduction = 50f;

        [Tooltip("持续时间（秒）")]
        public float Duration = 5f;

        private float activeTimer = 0f;
        private bool isActive = false;
        private Actor activeShieldEffectActor;
        private SkillEffect activeDefenseBuff;

        protected override void ExecuteSkill(Actor target)
        {
            isActive = true;
            activeTimer = Duration;
            
            Debug.Log($"金钟罩激活，{Duration}秒内减少{DamageReduction}%伤害");

            // 播放金钟罩特效
            var effectManager = SkillEffectManager.Instance;
            if (effectManager != null)
            {
                activeShieldEffectActor = effectManager.PlayCastEffect("JinZhongZhao_Shield", Actor);
            }

            // 应用Buff效果
            activeDefenseBuff = SkillEffectFactory.CreateAttributeBuff(
                AttributeBuffEffect.AttributeType.Defense, DamageReduction, true, Duration);
            activeDefenseBuff.Apply(Actor);
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
                    Debug.Log("金钟罩效果结束");
                    // 移除Buff效果
                    if (activeDefenseBuff != null)
                    {
                        activeDefenseBuff.Remove();
                        activeDefenseBuff = null;
                    }
                    // 停止护盾特效
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
        /// 获取当前是否激活
        /// </summary>
        public bool IsActive()
        {
            return isActive;
        }

        /// <summary>
        /// 获取伤害减免
        /// </summary>
        public float GetDamageReduction()
        {
            return isActive ? DamageReduction / 100f : 0f;
        }
    }
}
