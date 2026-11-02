// 模块关系说明：
// 本文件是 NarrativePro.GAS 的**引擎无关纯规则层**（不引用 FlaxEngine 类型，入参是字符串标签集合），
// 承载两条重击规则：
//   ① "这一下是不是重击"（标签聚合判定）；
//   ② "重击把武器基础伤害放大成多少"（倍率施加）。
//
// 为什么需要它：这两条规则原本长在
//   - GAS/NarrativeExecCalc.cs 的 Execute（sourceASC.HasMatchingGameplayTag / spec.DynamicAssetTags.HasTag），
//   - GAS/NarrativeCombatAbility.cs 的 GetAttackDamage（weaponDamage *= weapon.HeavyAttackDamageMultiplier），
// 而 ASC 与能力都是 FlaxEngine.Script 子类：无原生 FlaxEngine 库时 `new` 直接抛 DllNotFoundException，
// 进程外一条断言都打不到。重击判定的致命点是：
//   ① 判定源是**两处聚合**（持有者标签 + 规格动态标签），只看一处会让"能力挂标签、效果读标签"的链路静默失效；
//   ② UE5 用的是 FGameplayTagContainer::HasTag（父标签可以命中子标签），端口此前只做精确相等，
//      资产写子标签（如 …DamageType.Heavy.Poison）时重击会被读成普通攻击；
//   ③ 倍率必须来自**武器配置**（UE5 WeaponItem.cpp:39 CDO 1.6 / 端口 Items/WeaponItem.cs:106 默认 1.5，
//      资产可覆写：item_sword.json=1.5、item_bow.json=1.8），把它硬编码成常量会让换武器不再改变重击伤害。
//
// 实时通路（NarrativeDamageExecCalc.Execute 的重击检测、NarrativeCombatAbility.GetAttackDamage 的倍率施加）
// 全部经由这里，harness 驱动的是同一份实现。标签授予/回收的账本见 NarrativeGrantedTagLedger.cs
//（重击标签是能力提交时挂的临时 Infinite 效果，能力结束必须撤掉——本工程出过一次"重击永久粘在持有者身上"的缺陷）。

using System;
using System.Collections.Generic;
using NarrativePro.Items;

namespace NarrativePro.GAS
{
    /// <summary>
    /// 重击判定与重击倍率的纯函数层。
    /// </summary>
    public static class NarrativeHeavyAttackRules
    {
        /// <summary>
        /// 重击伤害类型标签名。与 <c>NarrativeGameplayTags.Ability_DamageType_Heavy</c>
        /// （Core/NarrativeGameplayTags.cs:298）同一字面量；纯层用常量以便无引擎环境下也能判定，
        /// 实时侧仍传真实标签名进来。
        /// </summary>
        public const string HeavyTagName = "Narrative.Ability.DamageType.Heavy";

        /// <summary>
        /// 容器是否含有给定标签。
        /// <para>
        /// <paramref name="bParentMatch"/> 对应 UE5 <c>FGameplayTagContainer::HasTag</c>（"存在或作为子标签存在"）：
        /// 真值时，容器里的父标签也能命中子标签查询（held="Narrative.Ability.DamageType"
        /// 命中 query="Narrative.Ability.DamageType.Heavy"）；假值时只认精确相等
        /// （端口 <c>GameplayTagContainer.HasTag</c> 的旧口径）。
        /// </para>
        /// <para>兄弟标签（…DamageType.Melee）与无关标签在两种口径下都不算命中。</para>
        /// </summary>
        public static bool ContainerHasTag(IEnumerable<string> containerTagNames, string tagToCheck, bool bParentMatch)
        {
            if (containerTagNames == null || string.IsNullOrEmpty(tagToCheck)) return false;

            foreach (var held in containerTagNames)
            {
                if (string.IsNullOrEmpty(held)) continue;

                // 精确相等：两种口径都算
                if (string.Equals(held, tagToCheck, StringComparison.Ordinal)) return true;

                if (bParentMatch && IsAncestorTag(held, tagToCheck)) return true;
            }
            return false;
        }

        /// <summary>
        /// 聚合来源标签后的重击判定（UE5 NarrativeDamageExecCalc.cpp:71 的
        /// <c>SourceTags-&gt;HasTag(Ability_DamageType_Heavy)</c> 在本端口的两处来源等价物）：
        /// 先看持有者（ASC）标签，再看效果规格上的动态标签。
        /// </summary>
        /// <param name="sourceHolderTagNames">持有者（来源 ASC）的标签名集合，可为 null。</param>
        /// <param name="specTagNames">规格动态标签名集合，可为 null。</param>
        /// <param name="heavyTagName">重击标签名，缺省取 <see cref="HeavyTagName"/>。</param>
        public static bool IsHeavyAttack(
            IEnumerable<string> sourceHolderTagNames, IEnumerable<string> specTagNames, string heavyTagName = null)
        {
            string heavy = string.IsNullOrEmpty(heavyTagName) ? HeavyTagName : heavyTagName;

            // 两处来源任一命中即算重击：能力提交挂的是持有者标签，SetByCaller/资产涂的是规格动态标签
            if (ContainerHasTag(sourceHolderTagNames, heavy, true)) return true;
            if (ContainerHasTag(specTagNames, heavy, true)) return true;
            return false;
        }

        /// <summary>
        /// 重击倍率施加（对应 UE5 <c>UWeaponItem::HeavyAttackDamageMultiplier</c> 的消费口径：
        /// 只有重击才乘，普通攻击原值返回）。倍率来自武器配置，不硬编码。
        /// </summary>
        public static float ApplyHeavyMultiplier(float baseDamage, bool bHeavyAttack, float heavyAttackDamageMultiplier)
        {
            if (!bHeavyAttack) return baseDamage;
            return baseDamage * heavyAttackDamageMultiplier;
        }

        /// <summary>
        /// 武器来源的攻击伤害（对应 <c>NarrativeCombatAbility.GetAttackDamage</c> 的武器分支：
        /// 武器基础伤害 &lt;= 0 时原样返回，由调用方回退到属性集 AttackDamage / 能力默认值，
        /// 与 UE5"没有武器来源才读属性集"的顺序一致）。
        /// </summary>
        public static float ResolveWeaponAttackDamage(float weaponBaseDamage, bool bHeavyAttack, float heavyAttackDamageMultiplier)
        {
            if (weaponBaseDamage <= 0f) return weaponBaseDamage;
            return ApplyHeavyMultiplier(weaponBaseDamage, bHeavyAttack, heavyAttackDamageMultiplier);
        }

        /// <summary>
        /// <paramref name="candidate"/> 是否为 <paramref name="descendant"/> 的祖先标签
        /// （点分层级前缀，口径同 <see cref="GameplayTag.Matches"/>）。
        /// </summary>
        private static bool IsAncestorTag(string candidate, string descendant)
        {
            if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(descendant)) return false;
            if (candidate.Length >= descendant.Length) return false;
            return descendant.StartsWith(candidate + ".", StringComparison.Ordinal);
        }
    }
}
