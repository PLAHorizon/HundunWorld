using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.GAS
{
    /// <summary>
    /// 近战挥砍能力（轻击）。对应 UE5 由近战武器授予的蓝图攻击能力
    /// （<c>UNarrativeCombatAbility</c> 的子类 + BP 的攻击窗口参数）。
    /// <para>
    /// UE5 里这些值配在蓝图 CDO 上；Flax 端口把"实现"留在 C# 类、把"数值"交给
    /// <c>Content/NarrativePro/GAS/Abilities/*.json</c>（见 <see cref="NarrativeGASAssetRegistry.AbilityDefinition"/>），
    /// 本类的构造值只在数据定义缺失时生效。
    /// </para>
    /// </summary>
    public class NarrativeMeleeSlashAbility : NarrativeCombatAbility
    {
        public NarrativeMeleeSlashAbility()
        {
            AbilityName = "MeleeSlash";

            // 近战：走 WeaponVisual 的动画通知扫描窗口
            IsMelee = true;
            IsHeavyAttack = false;

            // 近战武器不需要弹药（弹药门槛由武器数据推导，见 IsAmmoRequiredFor）
            bRequiresAmmo = false;

            // 伤害定义：JSON 里的 ExecuteCalcTypeId="Damage" 让 ASC 的执行计算派发被真正命中
            DamageEffectPath = "effect_damage_melee";
            DefaultAttackDamage = 15f;

            // 攻击窗口（对应 UE5 AnimNotifyState_AttackAnim 的分支点 TriggerTime/Duration）
            AttackNotifyTriggerTime = 0.12f;
            AttackNotifyDuration = 0.35f;
            AttackPlayRate = 1f;
            MeleeSweepRadius = 80f;

            DefaultBotAttackFrequency = 1.2f;
            DefaultBotAttackRange = 220f;

            // 输入标签：控制器按下 LeftMouseButton 时经 AbilityInputTagPressed 命中本能力
            InputTag = NarrativePro.Core.NarrativeGameplayTags.Narrative_Input_Attack;
        }
    }

    /// <summary>
    /// 近战重击能力。对应 UE5 副攻击（AltAttack）蓝图：
    /// 挂 <c>Ability.DamageType.Heavy</c> 标签，伤害执行计算据此提高倍率
    /// （倍率本身在 <see cref="NarrativeCombatAbility.GetAttackDamage"/> 里按武器
    /// <c>HeavyAttackDamageMultiplier</c> 计算）。
    /// </summary>
    public class NarrativeMeleeHeavySlashAbility : NarrativeCombatAbility
    {
        public NarrativeMeleeHeavySlashAbility()
        {
            AbilityName = "MeleeHeavySlash";

            IsMelee = true;
            IsHeavyAttack = true;
            bRequiresAmmo = false;

            DamageEffectPath = "effect_damage_melee";
            DefaultAttackDamage = 20f;

            // 重击前摇更长、窗口更宽（对应 UE5 重击动画的分支点）
            AttackNotifyTriggerTime = 0.3f;
            AttackNotifyDuration = 0.45f;
            MeleeSweepRadius = 110f;

            DefaultBotAttackFrequency = 0.5f;
            DefaultBotAttackRange = 240f;

            InputTag = NarrativePro.Core.NarrativeGameplayTags.Narrative_Input_AltAttack;
        }
    }

    /// <summary>
    /// 远程射击能力（弓/弩这类需要弹药的武器）。对应 UE5 由 RangedWeaponItem 授予的
    /// <c>GA_Fire</c>：走 hitscan 管线（<see cref="NarrativeCombatAbility.FireRangedAttack"/>），
    /// 弹药消耗与自动装填仍由武器数据决定。
    /// </summary>
    public class NarrativeRangedShotAbility : NarrativeCombatAbility
    {
        public NarrativeRangedShotAbility()
        {
            AbilityName = "RangedShot";

            IsMelee = false;
            IsHeavyAttack = false;

            // 远程默认要弹药；武器不需要时 IsAmmoRequiredFor 会放宽
            bRequiresAmmo = true;

            DamageEffectPath = "effect_damage_ranged";
            DefaultAttackDamage = 12f;

            DefaultBotAttackFrequency = 0.8f;
            DefaultBotAttackRange = 1800f;

            InputTag = NarrativePro.Core.NarrativeGameplayTags.Narrative_Input_Attack;
        }

        /// <summary>射击的追踪数据优先取武器/弹药的配置，散射由武器实时散布提供。</summary>
        protected override CombatTraceData BuildCombatTraceData(NarrativePro.Items.WeaponItem weapon)
        {
            var trace = base.BuildCombatTraceData(weapon);
            if (trace != null && trace.TraceDistance <= 0f)
                trace = new CombatTraceData(DefaultBotAttackRange, 0f);
            return trace;
        }
    }

    /// <summary>
    /// 火矢/火焰投射能力（法杖这类不消耗实体弹药的远程武器）。
    /// 与 <see cref="NarrativeRangedShotAbility"/> 共用 hitscan 管线，
    /// 差别在伤害定义（<c>effect_damage_fire</c>）与不占弹药。
    /// </summary>
    public class NarrativeFireBoltAbility : NarrativeCombatAbility
    {
        public NarrativeFireBoltAbility()
        {
            AbilityName = "FireBolt";

            IsMelee = false;
            IsHeavyAttack = false;
            bRequiresAmmo = false;

            DamageEffectPath = "effect_damage_fire";
            DefaultAttackDamage = 22f;

            // 火焰投射没有精准散布，靠 40 厘米球扫保证近距离命中
            WeaponSpread = 0f;

            DefaultBotAttackFrequency = 0.6f;
            DefaultBotAttackRange = 1500f;

            InputTag = NarrativePro.Core.NarrativeGameplayTags.Narrative_Input_Ability1;
        }

        /// <summary>火焰投射用固定半径球扫（对应 UE5 的 Projectile 命中判定半径）。</summary>
        protected override CombatTraceData BuildCombatTraceData(NarrativePro.Items.WeaponItem weapon)
        {
            var trace = base.BuildCombatTraceData(weapon);
            if (trace != null) trace.TraceRadius = 40f;
            return trace;
        }
    }
}
