using FlaxEngine;

namespace NarrativePro.GAS
{
    /// <summary>
    /// 执行计算基类。对应 UE5 UGameplayEffectExecutionCalculation。
    /// 用于在效果应用时执行自定义计算逻辑（如伤害公式、治疗公式）。
    /// 简化点：移除 UE5 FGameplayEffectCustomExecutionParameters/Output 复杂结构，
    /// 直接传入 ASC 与 Spec，由 Execute 修改属性。
    /// </summary>
    public abstract class GameplayEffectExecutionCalculation
    {
        /// <summary>执行计算。由 ASC 在应用效果时调用。</summary>
        /// <param name="targetASC">目标 ASC。</param>
        /// <param name="spec">效果规格。</param>
        public abstract void Execute(NarrativeAbilitySystemComponent targetASC, GameplayEffectSpec spec);
    }

    /// <summary>
    /// 伤害执行计算。对应 UE5 UNarrativeDamageExecCalc（NarrativeDamageExecCalc.cpp:22-25,52-107）。
    /// 捕捉口径：AttackDamage/AttackRating 来自 SOURCE 侧，Armor 来自 TARGET 侧；
    /// 公式：FinalDamage = Damage * (1 + AR/100) / (1 + Armor/100) * MaterialMultiplier；
    /// 另含友军伤害否决（GetAttitude/ETeamAttitude）与重击标签（Ability.DamageType.Heavy）判定，
    /// 结果写入 Damage 元属性。
    /// </summary>
    public class NarrativeDamageExecCalc : GameplayEffectExecutionCalculation
    {
        public override void Execute(NarrativeAbilitySystemComponent targetASC, GameplayEffectSpec spec)
        {
            if (targetASC?.AttributeSet == null) return;

            var sourceASC = spec?.SourceASC;
            var sourceActor = sourceASC != null ? sourceASC.Actor : spec?.SourceActor;
            var targetActor = targetASC.Actor;

            // 友伤开关（UE5 cpp:53-63 读 UNarrativeCombatDeveloperSettings::bAllowFriendlyFire）。
            // 设置缺失时按"允许友伤"处理——与抽取前"combatSettings == null 就不进友伤门"完全一致。
            var combatSettings = NarrativePro.Settings.NarrativeCombatDeveloperSettings.Instance;
            bool bAllowFriendlyFire = combatSettings == null || combatSettings.bAllowFriendlyFire;

            // AttackDamage / AttackRating：SOURCE 侧捕捉（UE5 FDamageStatics: EGameplayEffectAttributeCaptureSource::Source）
            float damage = 0f;
            float attackRating = 0f;

            // 先取本次 GE 上显式写下的 Damage（含 SetByCaller 覆盖）：
            // 攻击能力已经把"武器基础伤害 + 重击倍率 + 弹药覆盖"算好写在这里，
            // 这是调用方给出的确定值，优先级高于来源属性集上的通用 AttackDamage。
            float explicitDamage = 0f;
            if (spec?.Effect?.Modifiers != null)
            {
                foreach (var mod in spec.Effect.Modifiers)
                {
                    if (mod == null || mod.AttributeName != "Damage") continue;
                    explicitDamage += mod.ResolveMagnitude(spec);
                }
            }

            var sourceAttrs = sourceASC?.AttributeSet;
            if (sourceAttrs != null)
            {
                attackRating = sourceAttrs.AttackRating.CurrentValue;
                // GE 上没有显式伤害时，退回来源属性集的 AttackDamage
                // （UE5 注释 "Capture optional damage value set on the damage GE as a CalculationModifier"）
                damage = explicitDamage > 0f ? explicitDamage : sourceAttrs.AttackDamage.CurrentValue;
            }
            else
            {
                damage = explicitDamage;
            }

            // Armor：TARGET 侧捕捉
            var attrs = targetASC.AttributeSet;
            float armor = attrs.Armor.CurrentValue;

            // 物理材质倍率（UE5: Hit->PhysMaterial->DamageMultiplier）——活路径调用 GetDamageMultiplier
            float materialMultiplier = 1f;
            if (spec?.Hit != null && spec.Hit.PhysicalMaterial != null)
            {
                materialMultiplier = NarrativePro.UnrealFramework.NarrativePhysicalMaterial
                    .GetDamageMultiplier(spec.Hit.PhysicalMaterial, 1f);
            }

            // 友伤否决 + 重击检测 + 公式全部走纯裁决层（进程外 harness 断言的就是这一份实现）
            var outcome = AdjudicateDamage(new DamageAdjudicationInput
            {
                SourceFactionNames = sourceActor != null
                    ? NarrativePro.Core.ArsenalStatics.GetActorFactionNames(sourceActor)
                    : null,
                TargetFactionNames = targetActor != null
                    ? NarrativePro.Core.ArsenalStatics.GetActorFactionNames(targetActor)
                    : null,
                bSameActor = sourceActor != null && targetActor != null && ReferenceEquals(sourceActor, targetActor),
                bAllowFriendlyFire = bAllowFriendlyFire,

                SourceHolderTagNames = sourceASC != null && sourceASC.GameplayTags != null
                    ? sourceASC.GameplayTags.GetTags()
                    : null,
                SpecTagNames = spec != null && spec.DynamicAssetTags != null ? spec.DynamicAssetTags.GetTags() : null,
                HeavyTagName = NarrativePro.Core.NarrativeGameplayTags.Ability_DamageType_Heavy.TagName,

                Damage = damage,
                AttackRating = attackRating,
                Armor = armor,
                MaterialMultiplier = materialMultiplier,
            });

            // 友军伤害否决（UE5 cpp:52-63）：不允许友伤时，同队直接整次执行短路返回
            if (outcome.bVetoedByFriendlyFire)
            {
                NarrativePro.Core.NarrativeLog.LogWarning(
                    $"[DamageExecCalc] {sourceActor?.Name} 命中友军 {targetActor?.Name}，按“禁止友伤”设置取消本次伤害");
                return;
            }

            float finalDamage = outcome.FinalDamage;
            bool bHeavyAttack = outcome.bHeavyAttack;

            if (finalDamage > 0f)
            {
                // 免疫等 Pre 判定（对应 ASC 平坦路径里的 PreGameplayEffectExecute 否决）
                if (attrs.PreGameplayEffectExecute("Damage", finalDamage, spec))
                {
                    float oldDamage = attrs.Damage.CurrentValue;
                    attrs.Damage.SetCurrentValue(oldDamage + finalDamage);

                    NarrativePro.Core.NarrativeLog.Log(
                        $"[DamageExecCalc] {targetASC.Actor?.Name}: base={damage}, AR={attackRating}, armor={armor}, " +
                        $"material={materialMultiplier}, heavy={bHeavyAttack}, final={finalDamage}");
                }
                else
                {
                    // 伤害被否决：与 UE5 早退一致，施毒等后续副作用同样跳过
                    return;
                }
            }

            // 攻击者武器/弹药上涂的毒药在本次命中生效（对应 UE5 UPoisonableFragment::ConsumePoison 施毒）
            ApplySourcePoison(targetASC, spec);
        }

        /// <summary>
        /// 伤害公式纯计算（供 <see cref="Execute"/> 与离线校验共用，无引擎依赖）。
        /// 严格照抄 UE5 UNarrativeDamageExecCalc 口径：
        /// AttackRating / Armor 各自负值夹到 0（FMath::Max），
        /// FinalDamage = Damage * (1 + AR/100) / (1 + Armor/100) * MaterialMultiplier。
        /// </summary>
        public static float ComputeFinalDamage(float damage, float attackRating, float armor, float materialMultiplier)
        {
            if (attackRating < 0f) attackRating = 0f; // FMath::Max(AttackRating, 0.f)
            if (armor < 0f) armor = 0f;
            float attackMultiplier = 1f + attackRating / 100f;
            float defenceMultiplier = 1f + armor / 100f;
            return (damage * attackMultiplier) / defenceMultiplier * materialMultiplier;
        }

        /// <summary>
        /// 一次伤害裁决的输入（纯数据：只有数值与标签/阵营名字集合，无 FlaxEngine 类型，进程外可直接构造）。
        /// <para>实时侧由 <see cref="Execute"/> 从 ASC/规格/命中结果取好后填入；
        /// harness 用同一份 <see cref="AdjudicateDamage"/> 断言友伤否决与重击检测。</para>
        /// </summary>
        public sealed class DamageAdjudicationInput
        {
            /// <summary>来源 Actor 的阵营标签名（可为 null，按空集合处理）。</summary>
            public System.Collections.Generic.IEnumerable<string> SourceFactionNames;

            /// <summary>目标 Actor 的阵营标签名（可为 null，按空集合处理）。</summary>
            public System.Collections.Generic.IEnumerable<string> TargetFactionNames;

            /// <summary>来源与目标是否为同一个 Actor（UE5 cpp:52 的 SourceActor != TargetActor 门）。</summary>
            public bool bSameActor;

            /// <summary>是否允许友军伤害（NarrativeCombatDeveloperSettings.bAllowFriendlyFire）。</summary>
            public bool bAllowFriendlyFire;

            /// <summary>来源 ASC 的持有者标签名（重击判定的第一处来源）。</summary>
            public System.Collections.Generic.IEnumerable<string> SourceHolderTagNames;

            /// <summary>效果规格的动态标签名（重击判定的第二处来源）。</summary>
            public System.Collections.Generic.IEnumerable<string> SpecTagNames;

            /// <summary>重击标签名；为空时用 <see cref="NarrativeHeavyAttackRules.HeavyTagName"/>。</summary>
            public string HeavyTagName;

            /// <summary>捕捉到的基础伤害（GE 显式 Damage 优先，否则来源 AttackDamage）。</summary>
            public float Damage;

            /// <summary>捕捉到的攻击评级（SOURCE 侧）。</summary>
            public float AttackRating;

            /// <summary>捕捉到的护甲（TARGET 侧）。</summary>
            public float Armor;

            /// <summary>物理材质倍率（无命中材质时 1）。</summary>
            public float MaterialMultiplier = 1f;
        }

        /// <summary>伤害裁决结果（态度、是否被友伤否决、是否重击、最终伤害）。</summary>
        public struct DamageAdjudicationResult
        {
            /// <summary>来源对目标的态度（自伤按 Friendly 记录，但不进友伤门）。</summary>
            public NarrativePro.Core.ArsenalStatics.ETeamAttitude Attitude;

            /// <summary>本次执行是否被"禁止友伤"整次取消（UE5 里等价于 Execute 直接 return）。</summary>
            public bool bVetoedByFriendlyFire;

            /// <summary>来源标签聚合里是否检出重击标签。</summary>
            public bool bHeavyAttack;

            /// <summary>最终伤害；被否决时恒为 0。</summary>
            public float FinalDamage;
        }

        /// <summary>
        /// 伤害裁决纯函数：友军伤害否决（UE5 NarrativeDamageExecCalc.cpp:52-67）
        /// + 来源标签聚合的重击检测（同文件 :71）+ 伤害公式（<see cref="ComputeFinalDamage"/>）。
        /// <para>被否决时 <see cref="DamageAdjudicationResult.FinalDamage"/> 为 0，
        /// <see cref="Execute"/> 据此整次早退（连施毒等副作用一起跳过，与 UE5 的 return 语义一致）。</para>
        /// </summary>
        public static DamageAdjudicationResult AdjudicateDamage(DamageAdjudicationInput input)
        {
            var result = new DamageAdjudicationResult
            {
                Attitude = NarrativePro.Core.ArsenalStatics.ETeamAttitude.Neutral,
                bVetoedByFriendlyFire = false,
                bHeavyAttack = false,
                FinalDamage = 0f,
            };
            if (input == null) return result;

            // ① 态度 + 友伤门（同一 Actor 的自伤不进门，UE5 cpp:52）
            result.Attitude = NarrativePro.Core.NarrativeFactionAttitude.ComputeActorAttitude(
                input.SourceFactionNames, input.TargetFactionNames, input.bSameActor);

            result.bVetoedByFriendlyFire = !input.bSameActor &&
                NarrativePro.Core.NarrativeFactionAttitude.ShouldVetoFriendlyFire(result.Attitude, input.bAllowFriendlyFire);

            // ② 重击检测（持有者标签 → 规格动态标签，两处聚合任一命中）
            result.bHeavyAttack = NarrativeHeavyAttackRules.IsHeavyAttack(
                input.SourceHolderTagNames, input.SpecTagNames, input.HeavyTagName);

            if (result.bVetoedByFriendlyFire) return result;

            // ③ 公式（负值夹 0 的口径在 ComputeFinalDamage 里）
            result.FinalDamage = ComputeFinalDamage(
                input.Damage, input.AttackRating, input.Armor, input.MaterialMultiplier);

            return result;
        }

        /// <summary>
        /// 读取并消耗攻击者所持武器（含其弹药源物品）上的 <c>PoisonableFragment</c> 毒药，
        /// 解析成 GameplayEffect 后应用到受击目标。对应 UE5 <c>UNarrativeDamageExecCalc</c> 施毒分支。
        /// </summary>
        private static void ApplySourcePoison(NarrativeAbilitySystemComponent targetASC, GameplayEffectSpec spec)
        {
            if (targetASC == null || spec == null) return;

            var sourceActor = spec.SourceASC != null ? spec.SourceASC.Actor : spec.SourceActor;
            if (sourceActor == null) return;

            string poisonId = "";
            var equipment = sourceActor.GetScript<NarrativePro.Items.EquipmentComponent>();
            if (equipment != null)
            {
                var slots = new[]
                {
                    NarrativePro.Items.NarrativeGameplayTags.Equipment.WieldSlot_Mainhand,
                    NarrativePro.Items.NarrativeGameplayTags.Equipment.WieldSlot_Offhand
                };
                foreach (var slot in slots)
                {
                    if (equipment.GetEquippedItem(slot) is NarrativePro.Items.WeaponItem weapon)
                    {
                        poisonId = ConsumeWeaponPoison(weapon);
                        if (!string.IsNullOrEmpty(poisonId)) break;
                    }
                }
            }
            if (string.IsNullOrEmpty(poisonId)) return;

            var effect = NarrativeGASAssetRegistry.ResolveEffect(poisonId);
            if (effect == null)
            {
                NarrativePro.Core.NarrativeLog.LogWarning($"[DamageExecCalc] 毒药效果 '{poisonId}' 无法解析，已跳过施毒");
                return;
            }

            var poisonSpec = new GameplayEffectSpec(effect, spec.SourceASC, spec.Level);
            targetASC.ApplyGameplayEffectSpecToSelf(new GameplayEffectSpecHandle(poisonSpec));
            NarrativePro.Core.NarrativeLog.Log($"[DamageExecCalc] 对 {targetASC.Actor?.Name} 施加毒药 '{poisonId}'");
        }

        /// <summary>
        /// 取并消耗武器上的毒药 ID；武器自身无毒药时回退到弹药物品（毒箭）。
        /// 对应 UE5 UPoisonableFragment 可挂在武器或箭矢弹药上。
        /// </summary>
        private static string ConsumeWeaponPoison(NarrativePro.Items.WeaponItem weapon)
        {
            if (weapon?.WeaponClipState == null) return "";

            var poisonable = weapon.GetFragment<NarrativePro.Items.PoisonableFragment>();
            if (poisonable != null)
            {
                string id = poisonable.ConsumePoison();
                if (!string.IsNullOrEmpty(id)) return id;
            }

            var ammoPoisonable = weapon.WeaponClipState.AmmoItemSource?.GetFragment<NarrativePro.Items.PoisonableFragment>();
            return ammoPoisonable != null ? ammoPoisonable.ConsumePoison() : "";
        }
    }

    /// <summary>
    /// 治疗执行计算。对应 UE5 UNarrativeHealExecCalc。
    /// 将效果中指定的治疗量写入 Heal 元属性。
    /// </summary>
    public class NarrativeHealExecCalc : GameplayEffectExecutionCalculation
    {
        public override void Execute(NarrativeAbilitySystemComponent targetASC, GameplayEffectSpec spec)
        {
            if (targetASC?.AttributeSet == null || spec?.Effect == null) return;

            // 从效果修饰器中提取 Heal 量（走 ResolveMagnitude，SetByCaller 覆盖才生效——
            // 对应 UE5 效果上 CalculationModifier 的 SetByCaller 口径）
            float healAmount = 0f;
            foreach (var mod in spec.Effect.Modifiers)
            {
                if (mod.AttributeName == "Heal")
                {
                    healAmount += mod.ResolveMagnitude(spec);
                }
            }

            if (healAmount > 0f)
            {
                var attrs = targetASC.AttributeSet;
                float oldHeal = attrs.Heal.CurrentValue;
                attrs.Heal.SetCurrentValue(oldHeal + healAmount);

                NarrativePro.Core.NarrativeLog.Log($"[HealExecCalc] {targetASC.Actor?.Name}: heal={healAmount}");
            }
        }
    }

    /// <summary>
    /// 执行计算注册表。由 TypeId 字符串查找对应的 ExecCalc 实例。
    /// </summary>
    public static class ExecutionCalculationRegistry
    {
        private static readonly System.Collections.Generic.Dictionary<string, GameplayEffectExecutionCalculation> _calculations =
            new System.Collections.Generic.Dictionary<string, GameplayEffectExecutionCalculation>
            {
                { "Damage", new NarrativeDamageExecCalc() },
                { "Heal", new NarrativeHealExecCalc() }
            };

        /// <summary>注册执行计算。</summary>
        public static void Register(string typeId, GameplayEffectExecutionCalculation calc)
        {
            if (string.IsNullOrEmpty(typeId) || calc == null) return;
            _calculations[typeId] = calc;
        }

        /// <summary>查找执行计算。</summary>
        public static GameplayEffectExecutionCalculation Find(string typeId)
        {
            if (string.IsNullOrEmpty(typeId)) return null;
            return _calculations.TryGetValue(typeId, out var calc) ? calc : null;
        }
    }
}
